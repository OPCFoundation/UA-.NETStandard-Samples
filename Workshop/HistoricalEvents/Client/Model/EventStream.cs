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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Samples.Client;

namespace Quickstarts.HistoricalEvents.Client.Model
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Streams the live events of one area off the V2 subscription engine and hands them
    /// to the model one at a time, in the order they arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The streaming API of the engine hands the notifications out as an
    /// <see cref="IAsyncEnumerable{T}"/>: <see cref="IStreamingSubscription"/> creates the
    /// monitored item when the enumeration starts and removes it again when it ends. That
    /// suits this client, because neither the node an item monitors nor its event filter
    /// can be modified afterwards - picking another area or another filter simply restarts
    /// the enumeration.
    /// </para>
    /// <para>
    /// The enumeration runs on a <see cref="SubscriptionPump"/>, and the handler which
    /// turns a notification into what the window shows is awaited inside the loop. So
    /// there is exactly one event in flight at any time, and stopping the pump waits for
    /// it: nothing is reported after the stream was stopped, and two events can never
    /// overtake each other on their way to the list.
    /// </para>
    /// </remarks>
    internal sealed class EventStream : IAsyncDisposable
    {
        private readonly ISession m_session;
        private readonly Func<FilterDeclaration, IReadOnlyList<Variant>, CancellationToken, Task> m_onEvent;
        private readonly Action<string, Exception> m_onError;
        private StreamingSubscription m_streaming;
        private SubscriptionPump m_pump;

        /// <summary>
        /// Creates the stream.
        /// </summary>
        /// <param name="session">The session, which has to run the V2 subscription engine.</param>
        /// <param name="onEvent">Handles one event: the filter the enumeration runs with and the fields of the notification. Awaited before the next event is read.</param>
        /// <param name="onError">Reports a failure of the enumeration, which has no caller to throw to.</param>
        public EventStream(
            ISession session,
            Func<FilterDeclaration, IReadOnlyList<Variant>, CancellationToken, Task> onEvent,
            Action<string, Exception> onError)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            m_onError = onError ?? throw new ArgumentNullException(nameof(onError));
        }

        /// <summary>
        /// True while an enumeration is running.
        /// </summary>
        public bool IsRunning => m_pump != null && m_pump.IsRunning;

        /// <summary>
        /// Starts streaming the events of an area, ending the enumeration of the previous
        /// area or filter first.
        /// </summary>
        /// <remarks>
        /// The underlying OPC UA subscription is created once, when the first enumeration
        /// starts, and kept across restarts: only the monitored item changes.
        /// </remarks>
        /// <param name="areaId">The area whose events are streamed.</param>
        /// <param name="filter">The filter which selects the events and their fields.</param>
        public async Task StartAsync(NodeId areaId, FilterDeclaration filter)
        {
            await StopAsync().ConfigureAwait(false);

            if (areaId.IsNull || filter == null)
            {
                return;
            }

            if (m_streaming == null)
            {
                if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The session does not use the V2 subscription engine.");
                }

                m_streaming = new StreamingSubscription(manager, SampleSession.DefaultSubscriptionOptions);
            }

            // the fields of a notification line up with the select clauses of this filter, so
            // the enumeration keeps it: the engine does not report the filter of an item back.
            EventFilter eventFilter = filter.GetFilter();
            StreamingSubscription streaming = m_streaming;

            var pump = new SubscriptionPump();
            m_pump = pump;

            // nothing is awaited here on purpose: the enumeration runs until the area, the
            // filter or the session changes, and the pump is what ends it then.
            pump.Run(ct => PumpEventsAsync(streaming, areaId, filter, eventFilter, ct));
        }

        /// <summary>
        /// Ends the current enumeration, which removes the monitored item it created, and
        /// waits for the event which is being handled.
        /// </summary>
        public async Task StopAsync()
        {
            SubscriptionPump pump = m_pump;

            m_pump = null;

            if (pump != null)
            {
                await pump.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Ends the enumeration and deletes the subscription on the server.
        /// </summary>
        /// <remarks>
        /// This also runs when the session has already gone away, and then the subscription
        /// cannot be deleted on the server any more: that exception reaches the caller, and
        /// the model lets its base class log it.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);

            StreamingSubscription streaming = m_streaming;

            m_streaming = null;

            if (streaming != null)
            {
                await streaming.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the events of one area off the streaming subscription.
        /// </summary>
        private async Task PumpEventsAsync(
            IStreamingSubscription streaming,
            NodeId areaId,
            FilterDeclaration filter,
            EventFilter eventFilter,
            CancellationToken ct)
        {
            var options = new MonitoredItemOptions {
                StartNodeId = areaId,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(areaId, eventFilter, options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    // the handler is awaited here, on the pump, so the events reach the
                    // model one at a time and in order. A handler which fails for one event
                    // does not end the stream for the others.
                    try
                    {
                        await m_onEvent(filter, notification.Fields.ToList(), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        m_onError("Handling an event", exception);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // the area, the filter or the session changed.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is reported instead of thrown.
                m_onError("Reading the events", exception);
            }
        }
    }
}
