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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// One event as a client sees it, with its fields addressable by name.
    /// </summary>
    public sealed class CapturedEvent
    {
        private readonly IReadOnlyDictionary<string, Variant> m_fields;

        internal CapturedEvent(IReadOnlyDictionary<string, Variant> fields, Variant[] rawFields)
        {
            m_fields = fields;
            RawFields = rawFields;
        }

        /// <summary>
        /// The fields exactly as the server sent them, in the order of the select clauses.
        /// </summary>
        /// <remarks>
        /// This is what a source-generated event-record decoder consumes, so a test which
        /// subscribed with a generated filter can decode the event from here.
        /// </remarks>
        public IReadOnlyList<Variant> RawFields { get; }

        /// <summary>
        /// The type of the event.
        /// </summary>
        public NodeId EventType
            => Field(Opc.Ua.BrowseNames.EventType).TryGetValue(out NodeId eventType) ? eventType : NodeId.Null;

        /// <summary>
        /// The name of the source which reported the event.
        /// </summary>
        public string SourceName
            => Field(Opc.Ua.BrowseNames.SourceName).TryGetValue(out string sourceName) ? sourceName : null;

        /// <summary>
        /// The message of the event.
        /// </summary>
        public string Message
            => Field(Opc.Ua.BrowseNames.Message).TryGetValue(out LocalizedText message) ? message.Text : null;

        /// <summary>
        /// The value of one of the selected fields, by the name it was selected under.
        /// </summary>
        public Variant Field(string name)
            => m_fields.TryGetValue(name, out Variant value) ? value : Variant.Null;

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Join(
                ", ",
                m_fields.Select(field => $"{field.Key}={field.Value}"));
        }
    }

    /// <summary>
    /// Subscribes to the events of a notifier and collects what the server reports.
    /// </summary>
    /// <remarks>
    /// Several of the samples exist to report events: simple events raises one on a timer,
    /// the alarm sample routes events from a source up to the area it belongs to. Neither
    /// can be observed any other way, because an event is not a node that could be read.
    ///
    /// The filter is built here rather than taken from the client library helpers, so that
    /// a test can select a field a sample declares itself and then assert its value.
    /// </remarks>
    public sealed class EventCapture : IAsyncDisposable
    {
        private readonly ISession m_session;
        private readonly Subscription m_subscription;
        private readonly MonitoredItem m_item;
        private readonly Channel<CapturedEvent> m_events;
        private readonly MonitoredItemNotificationEventHandler m_handler;
        private readonly string[] m_fieldNames;
        private bool m_disposed;

        private EventCapture(
            ISession session,
            Subscription subscription,
            MonitoredItem item,
            string[] fieldNames)
        {
            m_session = session;
            m_subscription = subscription;
            m_item = item;
            m_fieldNames = fieldNames;
            m_events = Channel.CreateUnbounded<CapturedEvent>();
            m_handler = OnNotification;
            m_item.Notification += m_handler;
        }

        /// <summary>
        /// The id the server gave the subscription, as needed by a ConditionRefresh call.
        /// </summary>
        public uint SubscriptionId => m_subscription.Id;

        /// <summary>
        /// Subscribes to the events of a notifier.
        /// </summary>
        /// <param name="session">The session to subscribe on.</param>
        /// <param name="notifier">The node which reports the events, usually the server.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <param name="eventTypeId">
        /// Restricts the subscription to one type of event and its subtypes. Null accepts
        /// every event the notifier reports.
        /// </param>
        /// <param name="extraFields">
        /// Fields beyond the standard ones, given as the browse path to reach them. They
        /// are addressable afterwards by the name of their last element, or by the whole
        /// path joined with slashes where the path has more than one element - the two
        /// state variables of a condition all end in "Id", so the last element alone would
        /// not tell them apart.
        /// </param>
        public static async Task<EventCapture> CreateAsync(
            ISession session,
            NodeId notifier,
            CancellationToken ct,
            NodeId eventTypeId = default,
            params QualifiedName[][] extraFields)
        {
            ArgumentNullException.ThrowIfNull(session);

            var selectClauses = new List<SimpleAttributeOperand>();
            var fieldNames = new List<string>();

            foreach (string standard in new[] {
                Opc.Ua.BrowseNames.EventId,
                Opc.Ua.BrowseNames.EventType,
                Opc.Ua.BrowseNames.SourceName,
                Opc.Ua.BrowseNames.SourceNode,
                Opc.Ua.BrowseNames.Time,
                Opc.Ua.BrowseNames.Message,
                Opc.Ua.BrowseNames.Severity,
            })
            {
                selectClauses.Add(Operand(ObjectTypeIds.BaseEventType, [new QualifiedName(standard)]));
                fieldNames.Add(standard);
            }

            foreach (QualifiedName[] path in extraFields ?? [])
            {
                if (path == null || path.Length == 0)
                {
                    continue;
                }

                // the operand names the base event type rather than the type which declares
                // the field: a server resolves the browse path against the event instance,
                // and naming the concrete type makes the clause miss on some of them
                selectClauses.Add(Operand(ObjectTypeIds.BaseEventType, path));

                fieldNames.Add(NameOf(path));
            }

            var filter = new EventFilter { SelectClauses = selectClauses.ToArrayOf() };

            if (!eventTypeId.IsNull)
            {
                filter.WhereClause = OfType(eventTypeId);
            }

            return await CreateAsync(session, notifier, filter, fieldNames.ToArray(), ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Subscribes to the events of a notifier with a filter the caller built.
        /// </summary>
        /// <remarks>
        /// The source generator emits an event filter per event type, whose select clauses
        /// line up with the decoder of the matching event record. A test which wants to go
        /// through that path hands the generated filter in here and decodes
        /// <see cref="CapturedEvent.RawFields"/> afterwards.
        /// </remarks>
        /// <param name="session">The session to subscribe on.</param>
        /// <param name="notifier">The node which reports the events, usually the server.</param>
        /// <param name="filter">The event filter to apply.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task<EventCapture> CreateAsync(
            ISession session,
            NodeId notifier,
            EventFilter filter,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(filter);

            var fieldNames = new List<string>();

            foreach (SimpleAttributeOperand clause in filter.SelectClauses)
            {
                fieldNames.Add(clause.BrowsePath.IsNull || clause.BrowsePath.Count == 0
                    ? string.Empty
                    : NameOf([.. clause.BrowsePath]));
            }

            return CreateAsync(session, notifier, filter, fieldNames.ToArray(), ct);
        }

        private static async Task<EventCapture> CreateAsync(
            ISession session,
            NodeId notifier,
            EventFilter filter,
            string[] fieldNames,
            CancellationToken ct)
        {
            var subscription = new Subscription(NullTelemetry.Instance) {
                DisplayName = null,
                PublishingInterval = 250,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                PublishingEnabled = true,
                TimestampsToReturn = TimestampsToReturn.Both,
            };

            EventCapture capture = null;

            try
            {
                session.AddSubscription(subscription);
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                var item = new MonitoredItem(NullTelemetry.Instance) {
                    StartNodeId = notifier,
                    AttributeId = Attributes.EventNotifier,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = 0,
                    QueueSize = 1000,
                    DiscardOldest = true,
                    Filter = filter,
                };

                capture = new EventCapture(session, subscription, item, fieldNames);

                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

                if (item.Status?.Error != null && ServiceResult.IsBad(item.Status.Error))
                {
                    throw new ServiceResultException(item.Status.Error);
                }

                return capture;
            }
            catch
            {
                if (capture != null)
                {
                    await capture.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await SafeRemoveAsync(session, subscription).ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>
        /// Waits for an event which satisfies the condition.
        /// </summary>
        /// <remarks>
        /// The timers of the samples run from the moment the server starts, so a test can
        /// never assume it sees the first event of a run. Waiting for a matching one rather
        /// than for the next one is what keeps that from mattering.
        /// </remarks>
        public async Task<CapturedEvent> WaitAsync(
            Func<CapturedEvent, bool> matches,
            TimeSpan timeout,
            string because,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(matches);

            var seen = new List<CapturedEvent>();
            long started = Environment.TickCount64;

            while (true)
            {
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
                TimeSpan left = timeout - elapsed;

                if (left <= TimeSpan.Zero)
                {
                    throw new TimeoutException(string.Format(
                        CultureInfo.InvariantCulture,
                        "No event matching {0} arrived within {1:0.#} s. {2}",
                        because,
                        timeout.TotalSeconds,
                        seen.Count == 0
                            ? "No event arrived at all."
                            : $"Seen instead: {string.Join(" | ", seen.Select(e => e.ToString()))}"));
                }

                CapturedEvent next = await NextAsync(left, ct).ConfigureAwait(false);

                if (matches(next))
                {
                    return next;
                }

                seen.Add(next);
            }
        }

        /// <summary>
        /// Waits for the next event, whatever it is.
        /// </summary>
        public async Task<CapturedEvent> NextAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            using var timer = new CancellationTokenSource(timeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

            try
            {
                return await m_events.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested)
            {
                throw new TimeoutException(string.Format(
                    CultureInfo.InvariantCulture,
                    "No event from {0} arrived within {1:0.#} s.",
                    m_item.StartNodeId,
                    timeout.TotalSeconds));
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_item.Notification -= m_handler;
            m_events.Writer.TryComplete();

            await SafeRemoveAsync(m_session, m_subscription).ConfigureAwait(false);
        }

        /// <summary>
        /// The name a field is addressable by: the last element of its browse path, or the
        /// whole path where that would be ambiguous.
        /// </summary>
        private static string NameOf(QualifiedName[] path)
        {
            return path.Length == 1
                ? path[0].Name
                : string.Join("/", path.Select(element => element.Name));
        }

        private static SimpleAttributeOperand Operand(NodeId typeId, QualifiedName[] path)
        {
            return new SimpleAttributeOperand {
                TypeDefinitionId = typeId,
                AttributeId = Attributes.Value,
                BrowsePath = path.ToArrayOf(),
            };
        }

        /// <summary>
        /// A where clause which accepts only events of the given type and its subtypes.
        /// </summary>
        private static ContentFilter OfType(NodeId eventTypeId)
        {
            var filter = new ContentFilter();
            filter.Push(FilterOperator.OfType, Variant.From(eventTypeId));
            return filter;
        }

        private void OnNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (e.NotificationValue is not EventFieldList notification)
            {
                return;
            }

            Variant[] values = notification.EventFields.ToArray();
            var fields = new Dictionary<string, Variant>(StringComparer.Ordinal);

            for (int ii = 0; ii < m_fieldNames.Length && ii < values.Length; ii++)
            {
                // a select clause the event type does not have comes back as a null, and
                // the last name wins if a sample selects the same field twice
                fields[m_fieldNames[ii]] = values[ii];
            }

            m_events.Writer.TryWrite(new CapturedEvent(fields, values));
        }

        private static async Task SafeRemoveAsync(ISession session, Subscription subscription)
        {
            try
            {
                if (subscription.Created)
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }

                await session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // tearing down a subscription on a server which is already gone, or which
                // dropped it itself, must not fail the test that was using it
            }

            subscription.Dispose();
        }
    }
}
