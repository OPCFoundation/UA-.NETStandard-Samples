/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Configuration;

// the V2 engine reuses names the classic engine already has in Opc.Ua.Client
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: the Reference Client has to survive its server going away and coming back,
    /// without doing anything about it itself.
    /// </summary>
    /// <remarks>
    /// The client smoke tests prove that a sample connects, and the subscribe tests prove that
    /// notifications arrive. Neither drops the server, which is the one thing the move to the
    /// managed session changed: the reconnect is no longer driven by the sample or by the
    /// shared connect control, and the session the sample holds is the same instance before and
    /// after. This test is what would notice if that stopped being true - either because the
    /// reconnect does not happen at all, or because it happens by handing the sample a new
    /// session it never picks up.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class ClientReconnectTests
    {
        /// <summary>
        /// How long the whole test may take. A reconnect is a wait for a keep alive to fail,
        /// a backoff and a session recreate, so this is well above the client smoke tests.
        /// </summary>
        private const int kTimeout = 180_000;

        /// <summary>
        /// How long a single service call of the sample may take.
        /// </summary>
        private const int kOperationTimeout = 30_000;

        /// <summary>
        /// The sample this drives. Its client is the one #771 modernized, and its server is
        /// the Quickstart Reference Server the issue asks the samples to be verified against.
        /// </summary>
        private const string kSample = "Reference";

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReferenceClientReconnectsAfterAServerRestart(CancellationToken ct)
        {
            SampleClientUnderTest client = SampleClientFactories.All.Single(entry => entry.Sample.Name == kSample);
            SampleServerUnderTest server = SampleServerFactories.All.Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            var phase = new ClientPhase();

            try
            {
                await WinFormsHarness.RunAsync(
                    async _ => await DriveReconnectAsync(client, host, phase, ct).ConfigureAwait(true),
                    TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException expired)
            {
                throw phase.Explain(expired);
            }
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveReconnectAsync(
            SampleClientUnderTest client,
            SampleServerHost host,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("loading the configuration of the sample");

            using var pki = new TemporaryPki("client-reconnect");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(client.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            configuration.TransportQuotas.OperationTimeout = kOperationTimeout;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            using Form form = client.CreateMainForm(configuration, NullTelemetry.Instance);

            // create the window handle without ever showing the form: the sample marshals its
            // connect callbacks back to the UI thread and needs a handle to do so
            _ = form.Handle;

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            phase.Enter($"connecting to {host.EndpointUrl}");

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, host.EndpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            try
            {
                Assert.That(
                    session,
                    Is.InstanceOf<ManagedSession>(),
                    "The sample no longer connects with a managed session, so nothing drives its reconnect.");

                using var states = new ConnectionStateLog((ManagedSession)session);
                var changes = new DataChangeCounter();

                phase.Enter("subscribing through the V2 engine");

                ISubscription subscription = Subscribe(session, changes);

                Assert.That(
                    await changes.WaitForMoreAsync(0, TimeSpan.FromSeconds(30), ct).ConfigureAwait(true),
                    Is.True,
                    "No data change arrived before the server was restarted, so the test could not " +
                    "have told a broken reconnect from a subscription which never worked.");

                // drop the server the sample is talking to, and wait until the session has
                // noticed. Restarting it right away would let the client reconnect over a
                // channel which never broke, which proves nothing.
                phase.Enter("stopping the server");

                // off the message loop: the server counts its shutdown down and closes its
                // channels while the client is still on them, and doing that on the thread
                // which also pumps the callbacks of the client deadlocks the two
                await Task.Run(() => host.StopAsync(), ct).ConfigureAwait(true);

                phase.Enter("waiting for the session to notice that the server is gone");

                await states
                    .WaitForAsync(
                        state => state is ConnectionState.Reconnecting or ConnectionState.Disconnected
                            or ConnectionState.Failover,
                        "the managed session to notice that the server is gone",
                        TimeSpan.FromSeconds(45),
                        ct)
                    .ConfigureAwait(true);

                phase.Enter("starting the server again");

                await Task.Run(() => host.StartAgainAsync(ct), ct).ConfigureAwait(true);

                phase.Enter("waiting for the session to reconnect");

                await states
                    .WaitForAsync(
                        state => state == ConnectionState.Connected,
                        "the managed session to reconnect to the restarted server",
                        TimeSpan.FromSeconds(60),
                        ct)
                    .ConfigureAwait(true);

                phase.Enter("checking the reconnected session");

                Assert.That(
                    connect.Session,
                    Is.SameAs(session),
                    "The reconnect replaced the session of the connect control. The sample relies on " +
                    "the managed session keeping the same instance and no longer rebinds its browse tree.");

                Assert.That(session.Connected, Is.True, "The session did not come back connected.");

                // the session works again, which is what the browse and read paths of the
                // sample do with it
                DataValue serverState = await SessionOps
                    .ReadValueAsync(session, VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(true);

                Assert.That(
                    ServiceResult.IsGood(serverState.StatusCode),
                    Is.True,
                    $"Reading over the reconnected session failed: {serverState.StatusCode}");

                // and the V2 subscription is serving the sample again
                phase.Enter("waiting for the subscription to deliver again");

                int seen = changes.Count;

                Assert.That(
                    await changes.WaitForMoreAsync(seen, TimeSpan.FromSeconds(45), ct).ConfigureAwait(true),
                    Is.True,
                    "No data change arrived after the reconnect, so the V2 subscription was not " +
                    $"restored. The subscription reports Created={subscription.Created}.");
            }
            finally
            {
                phase.Enter("disconnecting");

                await connect.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Adds a V2 subscription with one fast changing item, the way the shared controls do.
        /// </summary>
        private static ISubscription Subscribe(ISession session, DataChangeCounter changes)
        {
            ISubscription subscription = ClientUtils.AddSubscription(
                session,
                changes.Handler,
                new OptionsMonitor<SubscriptionOptions>(ClientUtils.DefaultSubscriptionOptions));

            bool added = subscription.MonitoredItems.TryAdd(
                "CurrentTime",
                new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = TimeSpan.FromMilliseconds(250),
                }),
                out _);

            Assert.That(added, Is.True, "The V2 engine refused the monitored item.");

            return subscription;
        }

        /// <summary>
        /// Counts the data changes the V2 engine delivers.
        /// </summary>
        /// <remarks>
        /// The callbacks run on a publish worker, not on the message loop the test body runs
        /// on, so the count is read across threads and the waits are interlocked rather than
        /// plain field reads.
        /// </remarks>
        private sealed class DataChangeCounter
        {
            private int m_count;

            public DataChangeCounter()
            {
                Handler = new SubscriptionCallbacks {
                    DataChangeCallback = (_, _, _, notifications, _) =>
                        Interlocked.Add(ref m_count, notifications.Length),
                };
            }

            /// <summary>
            /// The handler to create the subscription with.
            /// </summary>
            public SubscriptionCallbacks Handler { get; }

            /// <summary>
            /// How many data changes have arrived so far.
            /// </summary>
            public int Count => Volatile.Read(ref m_count);

            /// <summary>
            /// Waits until more data changes than <paramref name="seen"/> have arrived.
            /// </summary>
            public async Task<bool> WaitForMoreAsync(int seen, TimeSpan timeout, CancellationToken ct)
            {
                DateTime deadline = DateTime.UtcNow.Add(timeout);

                while (DateTime.UtcNow < deadline)
                {
                    if (Count > seen)
                    {
                        return true;
                    }

                    await Task.Delay(100, ct).ConfigureAwait(true);
                }

                return false;
            }
        }

        /// <summary>
        /// Records the connection state transitions of a managed session.
        /// </summary>
        /// <remarks>
        /// Polling <c>StateMachine.State</c> would miss a transition which is over before the
        /// next poll, and a reconnect which is fast enough would then look like a session that
        /// never noticed anything. The states are collected from the event instead, and the
        /// waits below consume them in order.
        /// </remarks>
        private sealed class ConnectionStateLog : IDisposable
        {
            private readonly ManagedSession m_session;
            private readonly List<ConnectionState> m_states = [];
            private readonly Lock m_lock = new();
            private int m_consumed;

            public ConnectionStateLog(ManagedSession session)
            {
                m_session = session;
                m_session.ConnectionStateChanged += OnStateChanged;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_session.ConnectionStateChanged -= OnStateChanged;
            }

            /// <summary>
            /// Waits until a state the test has not consumed yet is accepted, and consumes
            /// everything up to and including it.
            /// </summary>
            public async Task WaitForAsync(
                Func<ConnectionState, bool> accept,
                string because,
                TimeSpan timeout,
                CancellationToken ct)
            {
                DateTime deadline = DateTime.UtcNow.Add(timeout);

                while (true)
                {
                    if (TryConsume(accept))
                    {
                        return;
                    }

                    if (DateTime.UtcNow >= deadline)
                    {
                        Assert.Fail(
                            $"Timed out after {timeout.TotalSeconds:F0} s waiting for {because}. " +
                            $"The states seen so far were: {Describe()}.");
                    }

                    await Task.Delay(200, ct).ConfigureAwait(true);
                }
            }

            private bool TryConsume(Func<ConnectionState, bool> accept)
            {
                lock (m_lock)
                {
                    for (int index = m_consumed; index < m_states.Count; index++)
                    {
                        if (accept(m_states[index]))
                        {
                            m_consumed = index + 1;
                            return true;
                        }
                    }

                    return false;
                }
            }

            private string Describe()
            {
                lock (m_lock)
                {
                    return m_states.Count == 0
                        ? "none, the session never reported a state change"
                        : string.Join(" -> ", m_states);
                }
            }

            private void OnStateChanged(object sender, ConnectionStateChangedEventArgs e)
            {
                lock (m_lock)
                {
                    m_states.Add(e.NewState);
                }
            }
        }
    }
}
