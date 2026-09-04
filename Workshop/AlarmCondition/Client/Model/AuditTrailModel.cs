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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Samples.Client;

namespace Quickstarts.AlarmConditionClient.Model
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// What the audit trail shows for one audit event: one field per column of the list.
    /// </summary>
    /// <param name="SourceName">The name of the source of the event.</param>
    /// <param name="TypeName">The name of the event type.</param>
    /// <param name="MethodId">The Method whose call was audited.</param>
    /// <param name="MethodName">The name of that Method, as the node cache knows it.</param>
    /// <param name="StatusText">Whether the call succeeded, as text.</param>
    /// <param name="Time">The time of the event, in UTC.</param>
    /// <param name="Message">The message of the event.</param>
    /// <param name="ArgumentsText">The input arguments of the call, as text.</param>
    /// <param name="Details">The raw fields of the event, for the details dialog.</param>
    public sealed record AuditEventSnapshot(
        string SourceName,
        string TypeName,
        NodeId MethodId,
        string MethodName,
        string StatusText,
        DateTimeUtc? Time,
        string Message,
        string ArgumentsText,
        ConditionDetails Details)
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            return Utils.Format("{0} {1} {2}", SourceName, MethodName, StatusText);
        }
    }

    /// <summary>
    /// The payload of <see cref="AuditTrailModel.AuditEventReceived"/>.
    /// </summary>
    public sealed class AuditEventReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public AuditEventReceivedEventArgs(AuditEventSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>
        /// The audit event.
        /// </summary>
        public AuditEventSnapshot Snapshot { get; }
    }

    /// <summary>
    /// Watches the audit trail of the server: every Method call the server audits is
    /// reported as an <see cref="AuditEventSnapshot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the model behind the audit window, which watches the trail for as long as
    /// it is open. That is what the streaming API of the V2 subscription engine is for:
    /// <see cref="IStreamingSubscription"/> hands the notifications out as an
    /// <see cref="IAsyncEnumerable{T}"/>, creates the monitored item when the enumeration
    /// starts and removes it again when it ends. The callback based subscription of the
    /// <see cref="AlarmConditionClientModel"/> is the better fit for the condition list,
    /// because that subscription lives as long as the session and has to serve condition
    /// refreshes.
    /// </para>
    /// <para>
    /// The decoding of an event happens inside the <c>await foreach</c>, so the events
    /// are delivered one at a time, in order. Events are raised on the thread the model
    /// was created on, the way the client models do it; a failure on the pump is logged,
    /// never shown, because the pump has no caller to throw to.
    /// </para>
    /// </remarks>
    public sealed class AuditTrailModel : IAsyncDisposable, IDisposable
    {
        private readonly ISession m_session;
        private readonly ILogger m_logger;
        private readonly SynchronizationContext m_context;
        private readonly FilterDefinition m_filter;
        private readonly Dictionary<NodeId, Type> m_knownEventTypes = ConditionEventTypes.CreateKnownTypes();
        private readonly Dictionary<NodeId, NodeId> m_eventTypeMappings = new Dictionary<NodeId, NodeId>();
        private StreamingSubscription m_streaming;
        private SubscriptionPump m_pump;
        private bool m_disposed;

        /// <summary>
        /// Creates the model on a session, capturing the synchronization context of the
        /// calling thread for its events. <see cref="StartAsync"/> starts the trail.
        /// </summary>
        /// <param name="session">The session, which has to run the V2 subscription engine.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public AuditTrailModel(ISession session, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            m_session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry;
            m_logger = telemetry.CreateLogger<AuditTrailModel>();
            m_context = SynchronizationContext.Current;

            m_filter = new FilterDefinition {
                AreaId = ObjectIds.Server,
                Severity = EventSeverity.Min,
                IgnoreSuppressedOrShelved = true,
                EventTypes = new NodeId[] { ObjectTypeIds.AuditUpdateMethodEventType },
            };
        }

        /// <summary>
        /// The telemetry context of the client.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// True while the trail is being read.
        /// </summary>
        public bool IsRunning => m_pump != null && m_pump.IsRunning;

        /// <summary>
        /// Raised for every audit event which was decoded.
        /// </summary>
        public event EventHandler<AuditEventReceivedEventArgs> AuditEventReceived;

        /// <summary>
        /// Starts reading the trail. The subscription and its monitored item are created
        /// when the enumeration starts, on its own worker.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task StartAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            if (m_pump != null)
            {
                return;
            }

            // find the fields of interest.
            m_filter.SelectClauses = await m_filter
                .ConstructSelectClausesAsync(m_session, ct, ObjectTypeIds.AuditUpdateMethodEventType)
                .ConfigureAwait(false);

            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            MonitoredItemOptions options = m_filter.CreateMonitoredItemOptions(m_session);

            // the fields of a notification line up with the select clauses of this filter,
            // so the model keeps it: the engine does not report the filter of an item back.
            var filter = (EventFilter)options.Filter;

            m_streaming = new StreamingSubscription(manager, SampleSession.DefaultSubscriptionOptions);
            m_pump = new SubscriptionPump();
            m_pump.Run(token => PumpAsync(m_streaming, filter, options, token));
        }

        /// <summary>
        /// Ends the enumeration, which removes the monitored item, and deletes the
        /// subscription on the server.
        /// </summary>
        /// <remarks>
        /// The main window closes the audit window when the session goes away, and then
        /// the subscription cannot be deleted on the server any more. That is not worth a
        /// dialog on the way out, so a failure is logged.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;

            SubscriptionPump pump = m_pump;
            StreamingSubscription streaming = m_streaming;

            m_pump = null;
            m_streaming = null;

            try
            {
                if (pump != null)
                {
                    await pump.DisposeAsync().ConfigureAwait(false);
                }

                if (streaming != null)
                {
                    await streaming.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Failed to delete the audit event subscription.");
            }
        }

        /// <summary>
        /// Ends the trail for a caller which cannot await, the window's Dispose.
        /// </summary>
        /// <remarks>
        /// The teardown runs on a thread pool thread, where there is no synchronization
        /// context for its continuations to be posted back to; awaiting it on the user
        /// interface thread would deadlock against the message loop the wait is blocking.
        /// Nothing in it touches a control.
        /// </remarks>
        public void Dispose()
        {
            SampleSession.WaitForTeardown(() => DisposeAsync().AsTask());
        }

        /// <summary>
        /// Reads the audit events off the streaming subscription until the pump is stopped.
        /// </summary>
        /// <remarks>
        /// Each notification arrives on the enumeration instead of on a callback, so the
        /// model drives the loop, and cancelling the token both ends the loop and removes
        /// the monitored item. An event which cannot be decoded is logged and skipped;
        /// the trail goes on.
        /// </remarks>
        private async Task PumpAsync(
            StreamingSubscription streaming,
            EventFilter filter,
            MonitoredItemOptions options,
            CancellationToken ct)
        {
            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(m_filter.AreaId, filter, options, ct)
                    .ConfigureAwait(false))
                {
                    try
                    {
                        await ProcessAsync(filter, notification, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        m_logger.LogError(exception, "Failed to decode an audit event.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // the trail was closed.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown.
                m_logger.LogError(exception, "Failed to read the audit events.");
            }
        }

        /// <summary>
        /// Decodes one audit event and reports it.
        /// </summary>
        private async Task ProcessAsync(EventFilter filter, EventNotification notification, CancellationToken ct)
        {
            EventFieldList fields = ConditionEventTypes.ToFieldList(notification);

            // check the type of event.
            NodeId eventTypeId = SampleSession.FindEventType(filter, fields);

            // ignore unknown events.
            if (eventTypeId.IsNull)
            {
                return;
            }

            // construct the audit object.
            AuditUpdateMethodEventState audit = await SampleSession.ConstructEventAsync(
                m_session,
                filter,
                fields,
                m_knownEventTypes,
                m_eventTypeMappings,
                ct).ConfigureAwait(false) as AuditUpdateMethodEventState;

            if (audit == null)
            {
                return;
            }

            // look up the event type and the Method in the local cache.
            INode type = await m_session.NodeCache.FindAsync(audit.TypeDefinitionId, ct).ConfigureAwait(false);

            NodeId methodId = audit.MethodId?.Value ?? NodeId.Null;
            INode method = methodId.IsNull ? null : await m_session.NodeCache.FindAsync(methodId, ct).ConfigureAwait(false);

            var snapshot = new AuditEventSnapshot(
                Text(audit.SourceName?.Value),
                type != null ? Utils.Format("{0}", type) : null,
                methodId,
                method != null ? Utils.Format("{0}", method) : Text(methodId.IsNull ? null : methodId),
                Text(audit.Status?.Value),
                audit.Time?.Value,
                Text(audit.Message?.Value),
                audit.InputArguments != null ? Utils.Format("{0}", new Variant(audit.InputArguments.Value)) : null,
                new ConditionDetails(filter, fields));

            Raise(AuditEventReceived, new AuditEventReceivedEventArgs(snapshot));
        }

        /// <summary>
        /// Raises an event on the context the model was created on, or inline when there
        /// was none. Posted, never sent: the window may be blocking its thread on
        /// <see cref="DisposeAsync"/> while it closes.
        /// </summary>
        private void Raise<TArgs>(EventHandler<TArgs> handler, TArgs args)
        {
            if (handler == null)
            {
                return;
            }

            if (m_context != null)
            {
                m_context.Post(_ => handler(this, args), null);
                return;
            }

            handler(this, args);
        }

        /// <summary>
        /// A value the way the list shows it, or null when the event did not carry it.
        /// </summary>
        private static string Text(object value)
        {
            return value == null ? null : Utils.Format("{0}", value);
        }
    }
}
