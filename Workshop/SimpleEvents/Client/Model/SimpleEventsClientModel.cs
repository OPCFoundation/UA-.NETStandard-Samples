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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Samples.Client;

namespace Quickstarts.SimpleEvents.Client.Model
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// One system cycle status event of the server, decoded into the fields the client
    /// displays.
    /// </summary>
    /// <param name="SourceName">The name of the source which raised the event.</param>
    /// <param name="EventTypeName">The display name of the event type, or its node id when the type is not in the cache.</param>
    /// <param name="CycleId">The identifier of the cycle the event belongs to.</param>
    /// <param name="CurrentStep">The name of the step the cycle is in, or null.</param>
    /// <param name="TimeUtc">When the event occurred, or null when the server did not say.</param>
    /// <param name="Message">The message of the event.</param>
    public sealed record SimpleEventRecord(
        string SourceName,
        string EventTypeName,
        string CycleId,
        string CurrentStep,
        DateTime? TimeUtc,
        string Message);

    /// <summary>
    /// The payload of <see cref="SimpleEventsClientModel.EventReceived"/>.
    /// </summary>
    public sealed class SimpleEventReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public SimpleEventReceivedEventArgs(SimpleEventRecord evt)
        {
            Event = evt;
        }

        /// <summary>
        /// The event which arrived.
        /// </summary>
        public SimpleEventRecord Event { get; }
    }

    /// <summary>
    /// The client model of the SimpleEvents client: streams the events of the server for
    /// as long as it is attached.
    /// </summary>
    /// <remarks>
    /// This sample exists to show one thing - the events of a server arriving in a list -
    /// and it watches them for exactly as long as it is connected. That is what the
    /// streaming API of the V2 subscription engine is for: <see cref="IStreamingSubscription"/>
    /// hands the notifications out as an <see cref="IAsyncEnumerable{T}"/>, creates the
    /// monitored item when the enumeration starts and removes it again when it ends, so
    /// the model reads events in a plain <c>await foreach</c> instead of wiring up a
    /// notification handler. The enumeration runs on a publish worker of the engine, which
    /// is where the decoding and the type lookup happen; each event is then reported
    /// through <see cref="EventReceived"/> on the thread the model was created on. The
    /// AlarmCondition sample shows the callback based
    /// <see cref="ISubscriptionNotificationHandler"/>, which is the better fit for a
    /// subscription that outlives one screen and has to serve condition refreshes.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The handles below are taken, cleared and released by OnDetachingAsync, which the detach of the base class runs - on a detach as well as on a dispose. The analyzer does not follow an asynchronous release through a virtual hook.")]
    public sealed class SimpleEventsClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the event model, for a caller which cannot name the generated
        /// constants (they exist in the server assembly as well).
        /// </summary>
        public const string SimpleEventsNamespaceUri = Namespaces.SimpleEvents;

        private StreamingSubscription m_streaming;
        private SubscriptionPump m_pump;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public SimpleEventsClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// Raised for every system cycle status event the server reports.
        /// </summary>
        public event EventHandler<SimpleEventReceivedEventArgs> EventReceived;

        /// <summary>
        /// Changes the locale the server renders its localized texts in.
        /// </summary>
        /// <param name="locales">The locales, most preferred first.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task ChangePreferredLocalesAsync(IEnumerable<string> locales, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(locales);

            return RequireSession().ChangePreferredLocalesAsync(new List<string>(locales), ct);
        }

        /// <inheritdoc/>
        protected override Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // the streaming subscription lives as long as the model is attached: the
            // underlying OPC UA subscription is created when the first SubscribeXxxAsync
            // enumeration starts.
            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            // Register the generated activators for the sample's own data types, so that
            // the CurrentStep of an event arrives as a CycleStepDataType instead of as the
            // raw body of an extension object. Registering them again on a later attach
            // to the same session is harmless.
            session.Factory.Builder.AddQuickstartsSimpleEvents().Commit();

            // The source generator emitted one event record - and a positional decoder for
            // it - per event type in the server's information model. Registering those
            // decoders yields both the select clauses and the decode step, so the client
            // does not have to browse the type model to find out which fields a
            // SystemCycleStatusEventType carries.
            EventRecordDecoderRegistry decoders = new EventRecordDecoderRegistry()
                .RegisterSimpleEventsDecoders(session.NamespaceUris);

            // the filter to use. The fields of a notification line up with its select
            // clauses, so the model keeps it: the engine does not report the filter of an
            // item back.
            EventFilter filter = SystemCycleStatusEventTypeRecord.EventFilters.Build(
                session.NamespaceUris,
                decoders);

            var streaming = new StreamingSubscription(manager, SampleSession.DefaultSubscriptionOptions);
            var pump = new SubscriptionPump();

            m_streaming = streaming;
            m_pump = pump;

            // start reading the events. The pump keeps the task: the enumeration runs for
            // as long as the model is attached, and detaching waits for it to end.
            pump.Run(token => PumpEventsAsync(session, streaming, decoders, filter, token));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            StreamingSubscription streaming = m_streaming;
            SubscriptionPump pump = m_pump;

            m_streaming = null;
            m_pump = null;

            // the enumeration first: cancelling it removes the monitored item, and waiting
            // for it guarantees that nothing is raised after the detach returns.
            if (pump != null)
            {
                await pump.DisposeAsync().ConfigureAwait(false);
            }

            // then the subscription on the server. Done before the session is closed:
            // closing a session which still carries a subscription waits for the publish
            // pipeline to drain.
            if (streaming != null)
            {
                await streaming.DisposeAsync().ConfigureAwait(false);
            }
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so the stream keeps running and
        // the reconnect hooks of the base class are not overridden.

        /// <summary>
        /// Reads the events off the streaming subscription until the model detaches.
        /// </summary>
        /// <remarks>
        /// Each notification arrives on the enumeration instead of on a callback, so the
        /// model drives the loop, and cancelling the token both ends the loop and removes
        /// the monitored item again. The loop runs on a publish worker of the engine: the
        /// decode and the lookup of the event type happen there, and only the finished
        /// record is handed to the base class, which posts it to the thread the model was
        /// created on.
        /// </remarks>
        private async Task PumpEventsAsync(
            ISession session,
            IStreamingSubscription streaming,
            EventRecordDecoderRegistry decoders,
            EventFilter filter,
            CancellationToken ct)
        {
            var options = new MonitoredItemOptions {
                StartNodeId = Opc.Ua.ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(Opc.Ua.ObjectIds.Server, filter, options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    // The engine reports the fields of an event, which line up with the
                    // select clauses of the generated filter the item was created with.
                    // The registry routes on the EventType field and remaps the server's
                    // field order to the layout the generated decoder expects.
                    if (decoders.Decode(notification.Fields.ToArray())
                        is not SystemCycleStatusEventTypeRecord status)
                    {
                        // ignore events with no registered decoder.
                        continue;
                    }

                    Raise(EventReceived, new SimpleEventReceivedEventArgs(
                        await ToRecordAsync(session, status, ct).ConfigureAwait(false)));
                }
            }
            catch (OperationCanceledException)
            {
                // the model detached.
            }
            catch (Exception exception)
            {
                // the pump has no caller to throw to, so the failure goes through the
                // error channel of the model.
                ReportError("Reading the events", exception);
            }
        }

        /// <summary>
        /// Turns a decoded event into the record the client displays.
        /// </summary>
        private static async Task<SimpleEventRecord> ToRecordAsync(
            ISession session,
            SystemCycleStatusEventTypeRecord status,
            CancellationToken ct)
        {
            // look up the event type metadata in the local cache. A type the cache does
            // not know is shown by its node id, so the row is never blank.
            INode type = await session.NodeCache.FindAsync(status.EventType, ct).ConfigureAwait(false);

            string typeName = type != null
                ? Utils.Format("{0}", type)
                : Utils.Format("{0}", status.EventType);

            return new SimpleEventRecord(
                status.SourceName,
                typeName,
                status.CycleId,
                status.CurrentStep?.Name,
                status.Time,
                status.Message.Text);
        }
    }
}
