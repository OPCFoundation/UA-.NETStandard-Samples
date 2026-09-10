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

namespace Quickstarts.HistoricalAccess.Client.Model
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// What the server reported about one history update: the audit event of Part 11,
    /// as a client sees it.
    /// </summary>
    /// <param name="Time">When the update happened.</param>
    /// <param name="EventType">The audit event type, which says what kind of update it was.</param>
    /// <param name="UpdatedNode">The node whose history was updated, where the event type carries it.</param>
    /// <param name="PerformInsertReplace">Whether the update inserted, replaced, updated or removed, where the event type carries it.</param>
    /// <param name="NewValueCount">How many values the update carried.</param>
    /// <param name="OldValueCount">How many values the update displaced - what a replace overwrote or a delete removed.</param>
    /// <param name="Succeeded">Whether the server accepted the update.</param>
    /// <param name="ClientUserId">Who asked for it.</param>
    /// <param name="Message">The message of the event.</param>
    public sealed record HistoryAuditRecord(
        DateTime Time,
        NodeId EventType,
        NodeId UpdatedNode,
        PerformUpdateType? PerformInsertReplace,
        int NewValueCount,
        int OldValueCount,
        bool Succeeded,
        string ClientUserId,
        string Message);

    /// <summary>
    /// Streams the audit events the server raises for history updates off the V2
    /// subscription engine and hands them to the model one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The historian dispatcher of the stack reports an audit event after every
    /// HistoryUpdate - one type per kind of update, all below
    /// <c>AuditHistoryUpdateEventType</c> - and the server object is the notifier
    /// they are reported through. An update which should be audited looks exactly
    /// like one which is not until something subscribes to them, which is what this
    /// does. The events carry the values the update displaced, so a replace or a
    /// delete can be accounted for.
    /// </para>
    /// <para>
    /// A server only reports audit events while auditing is switched on in its
    /// configuration, and only delivers them to a session on an encrypted channel;
    /// a client on an unsecured endpoint subscribes successfully and sees nothing.
    /// </para>
    /// </remarks>
    internal sealed class AuditEventStream : IAsyncDisposable
    {
        private readonly ISession m_session;
        private readonly Func<HistoryAuditRecord, CancellationToken, Task> m_onEvent;
        private readonly Action<string, Exception> m_onError;
        private StreamingSubscription m_streaming;
        private SubscriptionPump m_pump;

        /// <summary>
        /// The fields the stream selects, in the order they arrive.
        /// </summary>
        private static readonly string[] s_fields = new[] {
            Opc.Ua.BrowseNames.EventType,
            Opc.Ua.BrowseNames.Time,
            Opc.Ua.BrowseNames.Message,
            Opc.Ua.BrowseNames.Status,
            Opc.Ua.BrowseNames.ClientUserId,
            Opc.Ua.BrowseNames.UpdatedNode,
            Opc.Ua.BrowseNames.PerformInsertReplace,
            Opc.Ua.BrowseNames.NewValues,
            Opc.Ua.BrowseNames.OldValues,
        };

        /// <summary>
        /// Creates the stream.
        /// </summary>
        /// <param name="session">The session, which has to run the V2 subscription engine.</param>
        /// <param name="onEvent">Handles one audit event. Awaited before the next one is read.</param>
        /// <param name="onError">Reports a failure of the enumeration, which has no caller to throw to.</param>
        public AuditEventStream(
            ISession session,
            Func<HistoryAuditRecord, CancellationToken, Task> onEvent,
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
        /// Starts streaming the audit events of the server.
        /// </summary>
        public async Task StartAsync()
        {
            await StopAsync().ConfigureAwait(false);

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

            StreamingSubscription streaming = m_streaming;

            var pump = new SubscriptionPump();
            m_pump = pump;

            // nothing is awaited here on purpose: the enumeration runs until the stream
            // is stopped, and the pump is what ends it then.
            pump.Run(ct => PumpEventsAsync(streaming, ct));
        }

        /// <summary>
        /// Ends the current enumeration and waits for the event which is being handled.
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
        /// The filter which selects the history audit events and their fields.
        /// </summary>
        /// <remarks>
        /// Every field is addressed from the base event type by its browse name. A
        /// server resolves the path against the instance it reports, so a field an
        /// event type does not have - a delete carries no PerformInsertReplace, an
        /// annotation update no UpdatedNode - comes back as a null, and the record
        /// says so.
        /// </remarks>
        public static EventFilter CreateFilter()
        {
            var filter = new EventFilter();

            foreach (string field in s_fields)
            {
                filter.SelectClauses = filter.SelectClauses.AddItem(new SimpleAttributeOperand {
                    TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.Value,
                    BrowsePath = new[] { new QualifiedName(field) }.ToArrayOf(),
                });
            }

            var whereClause = new ContentFilter();
            whereClause.Push(FilterOperator.OfType, Variant.From(Opc.Ua.ObjectTypeIds.AuditHistoryUpdateEventType));
            filter.WhereClause = whereClause;

            return filter;
        }

        /// <summary>
        /// Turns the fields of a notification into the record the model hands out.
        /// </summary>
        public static HistoryAuditRecord CreateRecord(IReadOnlyList<Variant> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);

            if (fields.Count < s_fields.Length)
            {
                throw new ArgumentException("The notification carries fewer fields than the filter selects.", nameof(fields));
            }

            return new HistoryAuditRecord(
                fields[1].TryGetValue(out DateTimeUtc time) ? (DateTime)time : DateTime.MinValue,
                fields[0].TryGetValue(out NodeId eventType) ? eventType : NodeId.Null,
                fields[5].TryGetValue(out NodeId updatedNode) ? updatedNode : NodeId.Null,
                fields[6].TryGetValue(out int performInsertReplace) ? (PerformUpdateType)performInsertReplace : null,
                CountOf(fields[7]),
                CountOf(fields[8]),
                fields[3].TryGetValue(out bool status) && status,
                fields[4].TryGetValue(out string clientUserId) ? clientUserId : null,
                fields[2].TryGetValue(out LocalizedText message) ? message.Text : null);
        }

        /// <summary>
        /// How many values an array field carries; zero for a field the event does
        /// not have.
        /// </summary>
        private static int CountOf(Variant field)
        {
            if (field.IsNull)
            {
                return 0;
            }

            // the values of a data update travel as DataValues, the annotations of an
            // annotation update and the fields of an event update as structures.
            if (field.TryGetValue(out ArrayOf<DataValue> values))
            {
                return values.Count;
            }

            if (field.TryGetValue(out ArrayOf<ExtensionObject> structures))
            {
                return structures.Count;
            }

            if (field.Value is System.Collections.ICollection collection)
            {
                return collection.Count;
            }

            return 1;
        }

        /// <summary>
        /// Reads the audit events off the streaming subscription.
        /// </summary>
        private async Task PumpEventsAsync(IStreamingSubscription streaming, CancellationToken ct)
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
                    .SubscribeEventsAsync(Opc.Ua.ObjectIds.Server, CreateFilter(), options, ct)
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
                        await m_onEvent(CreateRecord(notification.Fields.ToList()), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        m_onError("Handling an audit event", exception);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // the stream was stopped.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is reported instead of thrown.
                m_onError("Reading the audit events", exception);
            }
        }
    }
}
