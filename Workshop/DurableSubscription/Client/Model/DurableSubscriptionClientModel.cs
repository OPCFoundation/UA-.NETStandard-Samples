/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Quickstarts.DurableSubscriptionClient.Model
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, so
    // the client types of the engine are aliased to win over the using directives above.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// One data change the client received for the watched value.
    /// </summary>
    public sealed class DurableValueEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public DurableValueEventArgs(DataValue value, uint sequenceNumber, bool recovered)
        {
            Value = value;
            SequenceNumber = sequenceNumber;
            Recovered = recovered;
        }

        /// <summary>
        /// The value the server sent.
        /// </summary>
        public DataValue Value { get; }

        /// <summary>
        /// The sequence number of the notification message the value arrived in.
        /// </summary>
        public uint SequenceNumber { get; }

        /// <summary>
        /// True if the value was queued by the durable subscription while the client was
        /// gone and delivered after the subscription was transferred back, false for a
        /// value received live.
        /// </summary>
        public bool Recovered { get; }
    }

    /// <summary>
    /// A status message the model reports to the window.
    /// </summary>
    public sealed class DurableStatusEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public DurableStatusEventArgs(string message)
        {
            Message = message;
        }

        /// <summary>
        /// The message.
        /// </summary>
        public string Message { get; }
    }

    /// <summary>
    /// The client model of the durable subscription sample: it opens a session, creates a
    /// durable subscription on the server's <c>CurrentTime</c>, and can persist the
    /// subscription to disk, drop the session without deleting the subscription on the
    /// server, and - after a restart of the client - transfer the subscription back and
    /// recover the values which the server queued while the client was gone.
    /// </summary>
    /// <remarks>
    /// The four client use cases of <c>Docs/DurableSubscription.md</c> and
    /// <c>Docs/TransferSubscription.md</c> all reduce to a small set of calls of the V2
    /// subscription engine (<c>Opc.Ua.Client.Subscriptions</c>):
    /// <see cref="Opc.Ua.Client.Subscriptions.ISubscription.SetAsDurableAsync(TimeSpan, CancellationToken)"/>
    /// makes a subscription durable, and
    /// <see cref="Opc.Ua.Client.Subscriptions.ISubscriptionManager.LoadAsync"/> with
    /// <c>transferSubscriptions: true</c> takes a persisted subscription over into a new
    /// session (which drives the GetMonitoredItems/TransferSubscriptions services under the
    /// hood):
    /// <list type="number">
    /// <item>from an active session - transfer a subscription owned by another live session;</item>
    /// <item>from a closed session with <c>DeleteSubscriptionsOnClose = false</c> - the
    /// close leaves the subscription on the server, a new session transfers it back;</item>
    /// <item>from persisted storage across a client restart - what this sample does with
    /// <see cref="Opc.Ua.Client.Subscriptions.ISubscriptionManager.SaveAsync"/> and
    /// <see cref="Opc.Ua.Client.Subscriptions.ISubscriptionManager.LoadAsync"/>;</item>
    /// <item>after a failed reconnect - the managed session of the V2 engine transfers the
    /// subscription back when the session cannot be reactivated.</item>
    /// </list>
    /// The window creates the model on its own thread, so the model captures that thread's
    /// <see cref="SynchronizationContext"/> and raises its events there; the window can
    /// therefore touch its controls from the handlers directly.
    /// </remarks>
    public sealed class DurableSubscriptionClientModel : IDisposable
    {
        // one hour, the smallest lifetime the server is asked to keep the durable
        // subscription alive without a publish from the client.
        private static readonly TimeSpan s_durableLifetime = TimeSpan.FromHours(1);

        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly SynchronizationContext m_syncContext;

        // the V2 engine takes the notification handler when the subscription is created, so
        // the model owns one for its whole lifetime and points it at its own method. The
        // same handler is handed back for a subscription which is transferred back on load.
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        private ISession m_session;
        private ISubscription m_subscription;

        // the moment a transfer was requested. A value whose source timestamp is older
        // was queued by the server while the client was gone (recovered); a newer value is
        // live. This is robust even when the watched node changes on every publish, which
        // would keep a keep-alive from ever marking the end of the recovery.
        private DateTime m_recoverThresholdUtc = DateTime.MinValue;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public DurableSubscriptionClientModel(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<DurableSubscriptionClientModel>();
            m_syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            m_callbacks.DataChangeCallback = OnDataChanges;
        }

        /// <summary>
        /// Raised for every value the watched node reports, live or recovered.
        /// </summary>
        public event EventHandler<DurableValueEventArgs> ValueReceived;

        /// <summary>
        /// Raised with a human readable status message for the window to show.
        /// </summary>
        public event EventHandler<DurableStatusEventArgs> StatusChanged;

        /// <summary>
        /// The file the subscription is persisted to across a restart of the client.
        /// </summary>
        public string PersistedSubscriptionFile { get; } = Path.Combine(
            Path.GetTempPath(),
            "Quickstarts.DurableSubscriptionClient.subscriptions.xml");

        /// <summary>
        /// True while a session is open.
        /// </summary>
        public bool IsConnected => m_session != null;

        /// <summary>
        /// True once a durable subscription is being watched.
        /// </summary>
        public bool HasSubscription => m_subscription != null;

        /// <summary>
        /// True if a subscription was persisted by a previous run and is waiting to be
        /// transferred back.
        /// </summary>
        public bool HasPersistedSubscription => File.Exists(PersistedSubscriptionFile);

        /// <summary>
        /// Opens a session to the server. If a subscription was persisted by a previous
        /// run it is loaded and transferred back into the new session, and the values the
        /// server queued while the client was gone are recovered; otherwise the caller
        /// creates a fresh durable subscription with <see cref="CreateDurableSubscriptionAsync"/>.
        /// </summary>
        /// <param name="endpointUrl">The opc.tcp endpoint of the server.</param>
        /// <param name="configuration">The application configuration of the client.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ConnectAsync(
            string endpointUrl,
            ApplicationConfiguration configuration,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(endpointUrl);
            ArgumentNullException.ThrowIfNull(configuration);

            if (m_session != null)
            {
                return;
            }

            Report("Creating the session...");

            // the managed session of the V2 client brings its own reconnect policy and, in
            // particular, the V2 subscription engine (its subscription manager) the durable
            // subscription flow relies on. A DefaultSessionFactory session would not carry
            // that engine, so SampleSessionFactory (a ManagedSessionFactory) is used.
            ISession session = await SampleSessionFactory.ConnectAsync(
                configuration,
                endpointUrl,
                useSecurity: false,
                identity: new UserIdentity(),
                sessionName: "Durable Subscription Client",
                telemetry: m_telemetry,
                sessionTimeout: 60000,
                ct: ct).ConfigureAwait(false);

            // the subscription has to survive the close of the session which owns it, so
            // the server keeps it for the next session to transfer back. This is the
            // second use case: a closed session with DeleteSubscriptionsOnClose = false.
            session.DeleteSubscriptionsOnClose = false;

            m_session = session;

            if (HasPersistedSubscription)
            {
                await RecoverPersistedSubscriptionAsync(ct).ConfigureAwait(false);
            }
            else
            {
                Report("Connected. Create a durable subscription to begin.");
            }
        }

        /// <summary>
        /// Creates a durable subscription which watches the server's <c>CurrentTime</c>.
        /// </summary>
        /// <remarks>
        /// The subscription is created on the V2 subscription engine, one monitored item is
        /// added (the engine applies it on its own worker, so
        /// <see cref="SampleSession.WaitForPendingChangesAsync"/> waits for that to settle),
        /// and then <see cref="Opc.Ua.Client.Subscriptions.ISubscription.SetAsDurableAsync(TimeSpan, CancellationToken)"/>
        /// asks the server to keep the subscription and its queued notifications alive
        /// without the client for <see cref="s_durableLifetime"/>. The server side monitored
        /// item handles a later transfer relies on are read for the caller by the engine
        /// itself during transfer-on-load.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task CreateDurableSubscriptionAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (m_subscription != null)
            {
                return;
            }

            Report("Creating the subscription...");

            var options = new OptionsMonitor<SubscriptionOptions>(
                SampleSession.DefaultSubscriptionOptions with {
                    PublishingInterval = TimeSpan.FromSeconds(1),
                    KeepAliveCount = 10,
                    LifetimeCount = 1000,
                    MaxNotificationsPerPublish = 1000,
                    PublishingEnabled = true,
                    Priority = 1,
                });

            ISubscription subscription = SampleSession.AddSubscription(session, m_callbacks, options);

            // adding the item to the collection is the create request; the engine applies
            // it on its own worker, there is no ApplyChanges to call.
            subscription.MonitoredItems.TryAdd(
                "CurrentTime",
                new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = TimeSpan.FromMilliseconds(500),
                    QueueSize = 1000,
                    DiscardOldest = false,
                }),
                out IMonitoredItem _);

            await SampleSession
                .WaitForPendingChangesAsync(subscription, SampleSubscription.DefaultApplyTimeout, ct)
                .ConfigureAwait(false);

            // ask the server to make the subscription durable; the server may revise the
            // lifetime down to what its configuration allows.
            try
            {
                TimeSpan revisedLifetime = await subscription
                    .SetAsDurableAsync(s_durableLifetime, ct)
                    .ConfigureAwait(false);

                Report($"Durable subscription created, kept alive for {revisedLifetime.TotalHours:0.#} hour(s) without the client.");
            }
            catch (ServiceResultException exception)
            {
                m_logger.LogWarning(exception, "The server did not make the subscription durable.");
                Report("The server did not make the subscription durable. Is DurableSubscriptionsEnabled set?");
            }

            m_subscription = subscription;
        }

        /// <summary>
        /// Persists the durable subscription to disk and drops the session without deleting
        /// the subscription on the server, so a restart of the client can transfer it back.
        /// </summary>
        /// <remarks>
        /// This is the client side of the third use case: persisted storage across a client
        /// restart. The subscription structure is snapshotted with
        /// <see cref="Opc.Ua.Client.Subscriptions.ISubscriptionManager.SaveAsync"/>; the
        /// server keeps the live subscription because the session was told not to delete it
        /// on close.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task PersistAndDropSessionAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (m_subscription == null)
            {
                throw new InvalidOperationException("There is no durable subscription to persist.");
            }

            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new InvalidOperationException("The session does not run the V2 subscription engine.");
            }

            Report($"Saving the subscription to {PersistedSubscriptionFile}...");

            using (FileStream stream = File.Create(PersistedSubscriptionFile))
            {
                await manager
                    .SaveAsync(stream, session.MessageContext, new[] { m_subscription }, ct)
                    .ConfigureAwait(false);
            }

            // stop tracking the subscription; the engine tears down its client side when
            // the session closes, and the server keeps the durable subscription itself.
            m_subscription = null;

            // leave the subscription on the server for the next session to transfer back.
            session.DeleteSubscriptionsOnClose = false;

            await SampleSession.CloseAndDisposeAsync(session, ct).ConfigureAwait(false);
            m_session = null;

            Report("Subscription persisted. Restart the client to transfer it back.");
        }

        /// <summary>
        /// Loads the persisted subscription into the current session and transfers it back
        /// from the server, recovering the values queued while the client was gone.
        /// </summary>
        private async Task RecoverPersistedSubscriptionAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                Report("The session does not run the V2 subscription engine; cannot transfer back.");
                return;
            }

            Report("Loading the persisted subscription and transferring it back...");

            // any value the server queued was sampled before now, while the client was
            // gone; a value sampled after this point is live. Set the threshold before the
            // transfer so the classification in OnDataChanges is correct for the first
            // notification that arrives.
            m_recoverThresholdUtc = DateTime.UtcNow;

            // load rebuilds the subscription on this session and, because transfer is
            // requested, takes it back from the server (GetMonitoredItems and
            // TransferSubscriptions under the hood). The same callbacks handle its
            // notifications, matched to the restored subscription by its saved name.
            using (FileStream stream = File.OpenRead(PersistedSubscriptionFile))
            {
                await manager
                    .LoadAsync(stream, session.MessageContext, _ => m_callbacks, transferSubscriptions: true, ct)
                    .ConfigureAwait(false);
            }

            ISubscription subscription = manager.Items.FirstOrDefault();

            if (subscription == null)
            {
                Report("The persisted file held no subscription.");
                m_recoverThresholdUtc = DateTime.MinValue;
                TryDeletePersistedFile();
                return;
            }

            m_subscription = subscription;

            // the file has served its purpose; the live subscription now belongs to this
            // session and will be persisted again on the next drop.
            TryDeletePersistedFile();

            Report("Subscription transferred. Values queued while the client was gone are marked recovered.");
        }

        /// <summary>
        /// Closes the session. The subscription is deleted on the server as well when
        /// <paramref name="deleteSubscription"/> is true, which ends the durable
        /// subscription for good.
        /// </summary>
        /// <param name="deleteSubscription">True to delete the subscription on the server.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task DisconnectAsync(bool deleteSubscription, CancellationToken ct = default)
        {
            ISession session = m_session;

            if (session == null)
            {
                return;
            }

            m_recoverThresholdUtc = DateTime.MinValue;

            ISubscription subscription = m_subscription;
            m_subscription = null;

            // disposing a V2 subscription deletes it on the server and removes it from the
            // manager, which is what ends the durable subscription for good.
            if (deleteSubscription && subscription != null)
            {
                try
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    m_logger.LogError(exception, "Error deleting the subscription.");
                }
            }

            session.DeleteSubscriptionsOnClose = deleteSubscription;

            try
            {
                await SampleSession.CloseAndDisposeAsync(session, ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Error closing the session.");
            }

            m_session = null;

            Report(deleteSubscription
                ? "Disconnected and deleted the subscription on the server."
                : "Disconnected. The durable subscription is still alive on the server.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // let the durable subscription go without deleting it: it stays on the server so
            // it can still be transferred back on the next run. The client side object is
            // torn down with the session.
            m_subscription = null;

            ISession session = m_session;

            if (session != null)
            {
                session.DeleteSubscriptionsOnClose = false;

                try
                {
                    SampleSession.CloseAndDisposeAsync(session, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    m_logger.LogError(exception, "Error closing the session on dispose.");
                }

                m_session = null;
            }
        }

        private void OnDataChanges(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] notifications,
            PublishState publishState)
        {
            if (notifications == null)
            {
                return;
            }

            DateTime thresholdUtc = m_recoverThresholdUtc;

            foreach (DataValueChange change in notifications)
            {
                DataValue value = change.Value;

                // a value sampled before the transfer was queued while the client was gone.
                bool recovered = thresholdUtc > DateTime.MinValue
                    && value.SourceTimestamp.ToDateTime() < thresholdUtc;

                m_syncContext.Post(
                    _ => Raise(ValueReceived, new DurableValueEventArgs(value, sequenceNumber, recovered)),
                    null);
            }
        }

        private ISession RequireSession()
        {
            return m_session ?? throw new InvalidOperationException("The client is not connected.");
        }

        private void TryDeletePersistedFile()
        {
            try
            {
                if (File.Exists(PersistedSubscriptionFile))
                {
                    File.Delete(PersistedSubscriptionFile);
                }
            }
            catch (IOException exception)
            {
                m_logger.LogWarning(exception, "Could not delete the persisted subscription file.");
            }
        }

        private void Report(string message)
        {
            m_syncContext.Post(_ => Raise(StatusChanged, new DurableStatusEventArgs(message)), null);
        }

        private void Raise<TArgs>(EventHandler<TArgs> handler, TArgs args)
        {
            handler?.Invoke(this, args);
        }
    }
}
