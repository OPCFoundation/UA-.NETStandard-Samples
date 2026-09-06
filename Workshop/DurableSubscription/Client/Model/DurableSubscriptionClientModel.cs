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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.DurableSubscriptionClient.Model
{
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
    /// <c>Docs/TransferSubscription.md</c> all reduce to the same two calls of the client
    /// SDK: <see cref="Opc.Ua.Client.Subscription.SetSubscriptionDurableAsync"/> makes a
    /// subscription durable, and <see cref="Opc.Ua.Client.ISession.TransferSubscriptionsAsync"/>
    /// takes it over into a session:
    /// <list type="number">
    /// <item>from an active session - transfer a subscription owned by another live session;</item>
    /// <item>from a closed session with <c>DeleteSubscriptionsOnClose = false</c> - the
    /// close leaves the subscription on the server, a new session transfers it back;</item>
    /// <item>from persisted storage across a client restart - what this sample does with
    /// <see cref="Opc.Ua.Client.SessionExtensions.Save(Opc.Ua.Client.ISession, string, IEnumerable{Opc.Ua.Client.Subscription}, IEnumerable{Type})"/>
    /// and <see cref="Opc.Ua.Client.SessionExtensions.Load(Opc.Ua.Client.ISession, string, bool, IEnumerable{Type})"/>;</item>
    /// <item>after a failed reconnect - the reconnect handler of the SDK transfers the
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
        private const uint DurableLifetimeHours = 1;

        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly SynchronizationContext m_syncContext;

        private ISession m_session;
        private Subscription m_subscription;

        // set while the queued values of a transferred subscription are being delivered,
        // so the window can tell recovered values from live ones.
        private volatile bool m_recovering;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public DurableSubscriptionClientModel(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<DurableSubscriptionClientModel>();
            m_syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
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

            Report("Selecting the server endpoint (no security, for the sample)...");

            // no security keeps the sample self contained: the server offers a None
            // policy, so the client and the server do not have to trust each other's
            // certificate for the durable subscription flow to be shown.
            EndpointDescription endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(configuration, endpointUrl, useSecurity: false, m_telemetry, ct)
                .ConfigureAwait(false);

            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            Report("Creating the session...");

            var sessionFactory = new DefaultSessionFactory(m_telemetry);

            ISession session = await sessionFactory.CreateAsync(
                configuration,
                endpoint,
                updateBeforeConnect: false,
                sessionName: "Durable Subscription Client",
                sessionTimeout: 60000,
                identity: new UserIdentity(),
                preferredLocales: default,
                ct).ConfigureAwait(false);

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
        /// The subscription is created, one monitored item is added, and then
        /// <see cref="Opc.Ua.Client.Subscription.SetSubscriptionDurableAsync"/> asks the
        /// server to keep it and its queued notifications alive without the client for
        /// <see cref="DurableLifetimeHours"/> hours.
        /// <see cref="Opc.Ua.Client.Subscription.GetMonitoredItemsAsync"/> reads back the
        /// server side handles, which is what a transfer into another session needs.
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

            var subscription = new Subscription(
                m_telemetry,
                new SubscriptionOptions {
                    DisplayName = "Durable Subscription",
                    PublishingInterval = 1000,
                    KeepAliveCount = 10,
                    LifetimeCount = 1000,
                    MaxNotificationsPerPublish = 1000,
                    PublishingEnabled = true,
                    Priority = 1,
                });

            session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            var monitoredItem = new MonitoredItem(
                m_telemetry,
                new MonitoredItemOptions {
                    DisplayName = "CurrentTime",
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = 500,
                    QueueSize = 1000,
                    DiscardOldest = false,
                });

            subscription.AddItem(monitoredItem);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

            subscription.FastDataChangeCallback = OnDataChange;

            // ask the server to make the subscription durable; the server may revise the
            // lifetime down to what its configuration allows.
            (bool durable, uint revisedLifetimeHours) = await subscription
                .SetSubscriptionDurableAsync(DurableLifetimeHours, ct)
                .ConfigureAwait(false);

            if (!durable)
            {
                Report("The server did not make the subscription durable. Is DurableSubscriptionsEnabled set?");
            }
            else
            {
                Report($"Durable subscription created, kept alive for {revisedLifetimeHours} hour(s) without the client.");
            }

            // the server side monitored item handles, which a transfer into another
            // session relies on (GetMonitoredItems, OPC UA Part 4).
            (bool ok, ArrayOf<uint> serverHandles, ArrayOf<uint> clientHandles) = await subscription
                .GetMonitoredItemsAsync(ct)
                .ConfigureAwait(false);

            if (ok && m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation(
                    "GetMonitoredItems returned {Count} item(s) for subscription {SubscriptionId}.",
                    serverHandles.Count,
                    subscription.Id);
            }

            m_subscription = subscription;
        }

        /// <summary>
        /// Persists the durable subscription to disk and drops the session without deleting
        /// the subscription on the server, so a restart of the client can transfer it back.
        /// </summary>
        /// <remarks>
        /// This is the client side of the third use case: persisted storage across a client
        /// restart. The subscription structure is saved with
        /// <see cref="Opc.Ua.Client.SessionExtensions.Save(Opc.Ua.Client.ISession, string, IEnumerable{Opc.Ua.Client.Subscription}, IEnumerable{Type})"/>;
        /// the server keeps the live subscription because the session was told not to delete
        /// it on close.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task PersistAndDropSessionAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (m_subscription == null)
            {
                throw new InvalidOperationException("There is no durable subscription to persist.");
            }

            Report($"Saving the subscription to {PersistedSubscriptionFile}...");

            session.Save(PersistedSubscriptionFile, new[] { m_subscription }, null);

            // stop delivering while the session goes away.
            m_subscription.FastDataChangeCallback = null;
            m_subscription = null;

            // leave the subscription on the server for the next session to transfer back.
            session.DeleteSubscriptionsOnClose = false;

            await session.CloseAsync(10000, closeChannel: true, ct).ConfigureAwait(false);
            session.Dispose();
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

            Report("Loading the persisted subscription and transferring it back...");

            // load rebuilds the subscription objects from the file and, because transfer
            // is requested, transfers them back from the server into this session.
            List<Subscription> loaded = (await Task
                .FromResult(session.Load(PersistedSubscriptionFile, transferSubscriptions: true, null))
                .ConfigureAwait(false))
                .ToList();

            Subscription subscription = loaded.FirstOrDefault();

            if (subscription == null)
            {
                Report("The persisted file held no subscription.");
                TryDeletePersistedFile();
                return;
            }

            // deliver the queued values as recovered, then flip to live once the burst is
            // drained by the first keep alive.
            m_recovering = true;
            subscription.FastDataChangeCallback = OnDataChange;
            subscription.PublishStatusChanged += OnPublishStatusChanged;

            m_subscription = subscription;

            // the file has served its purpose; the live subscription now belongs to this
            // session and will be persisted again on the next drop.
            TryDeletePersistedFile();

            Report("Subscription transferred. Recovering the values queued while the client was gone...");
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

            if (m_subscription != null)
            {
                m_subscription.FastDataChangeCallback = null;
                m_subscription.PublishStatusChanged -= OnPublishStatusChanged;
                m_subscription = null;
            }

            session.DeleteSubscriptionsOnClose = deleteSubscription;

            try
            {
                await session.CloseAsync(10000, closeChannel: true, ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Error closing the session.");
            }

            session.Dispose();
            m_session = null;

            Report(deleteSubscription
                ? "Disconnected and deleted the subscription on the server."
                : "Disconnected. The durable subscription is still alive on the server.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Subscription subscription = m_subscription;

            if (subscription != null)
            {
                subscription.FastDataChangeCallback = null;
                subscription.Dispose();
                m_subscription = null;
            }

            ISession session = m_session;

            if (session != null)
            {
                // leave the durable subscription on the server when the window closes, so
                // it can still be transferred back on the next run.
                session.DeleteSubscriptionsOnClose = false;

                try
                {
                    session.CloseAsync(5000, closeChannel: true, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    m_logger.LogError(exception, "Error closing the session on dispose.");
                }

                session.Dispose();
                m_session = null;
            }
        }

        private void OnDataChange(
            Subscription subscription,
            DataChangeNotification notification,
            ArrayOf<string> stringTable)
        {
            if (notification?.MonitoredItems == null)
            {
                return;
            }

            bool recovered = m_recovering;

            foreach (MonitoredItemNotification item in notification.MonitoredItems)
            {
                if (item?.Value == null)
                {
                    continue;
                }

                uint sequenceNumber = item.Message?.SequenceNumber ?? 0;
                DataValue value = item.Value;

                m_syncContext.Post(
                    _ => Raise(ValueReceived, new DurableValueEventArgs(value, sequenceNumber, recovered)),
                    null);
            }
        }

        private void OnPublishStatusChanged(Subscription subscription, PublishStateChangedEventArgs e)
        {
            // the first keep alive after the queued burst marks the end of the recovery:
            // everything after it is live.
            if (m_recovering && (e.Status & PublishStateChangedMask.KeepAlive) != 0)
            {
                m_recovering = false;
                m_syncContext.Post(
                    _ => Report("Recovery complete. Now receiving live values."),
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
