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
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Historian;
using Opc.Ua.Samples.Client;

namespace Quickstarts.HistoricalEvents.Client.Model
{
    /// <summary>
    /// One event, live or from history, the way the event list shows it.
    /// </summary>
    /// <param name="Fields">The fields the filter selected. The first one is the node id of the event, the rest line up with the fields of the filter.</param>
    /// <param name="DisplayTexts">The text of each field the filter shows in the list, in column order.</param>
    public sealed record EventRecord(IReadOnlyList<Variant> Fields, IReadOnlyList<string> DisplayTexts);

    /// <summary>
    /// What a read of the event history of an area asks for.
    /// </summary>
    /// <param name="StartTime">The start of the range, or <see cref="DateTime.MinValue"/> for no bound.</param>
    /// <param name="EndTime">The end of the range, or <see cref="DateTime.MinValue"/> for no bound.</param>
    /// <param name="MaxEvents">The most events one page holds, 0 for no limit.</param>
    public sealed record EventHistoryRequest(DateTime StartTime, DateTime EndTime, uint MaxEvents);

    /// <summary>
    /// What the next page of a paged history read needs.
    /// </summary>
    /// <remarks>
    /// The history client of the SDK hands the answer of a read out as one sequence
    /// which spans the whole time range: it issues the requests, carries the
    /// continuation point of one into the next, and releases the one still open
    /// when the sequence is abandoned. A continuation holds the enumerator of that
    /// sequence between two pages; disposing it is what tells the server the rest
    /// is not wanted.
    /// </remarks>
    public sealed class EventHistoryContinuation : IAsyncDisposable
    {
        internal EventHistoryContinuation(
            NodeId areaId,
            FilterDeclaration filter,
            IAsyncEnumerator<HistoryEventFieldList> reader,
            uint pageSize)
        {
            AreaId = areaId;
            Filter = filter;
            Reader = reader;
            PageSize = pageSize;
        }

        internal NodeId AreaId { get; }

        internal FilterDeclaration Filter { get; }

        internal IAsyncEnumerator<HistoryEventFieldList> Reader { get; }

        internal uint PageSize { get; }

        /// <summary>
        /// Abandons the read, which releases the continuation point the server holds.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return Reader.DisposeAsync();
        }
    }

    /// <summary>
    /// One page of the event history of an area.
    /// </summary>
    /// <param name="Events">The events of the page, oldest first.</param>
    /// <param name="Continuation">What the next page needs, null after the last one.</param>
    public sealed record EventHistoryPage(IReadOnlyList<EventRecord> Events, EventHistoryContinuation Continuation)
    {
        /// <summary>
        /// True while the server holds more events for this read.
        /// </summary>
        public bool HasMore => Continuation != null;
    }

    /// <summary>
    /// The payload of <see cref="HistoricalEventsClientModel.EventReceived"/>.
    /// </summary>
    public sealed class EventReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public EventReceivedEventArgs(EventRecord record, bool isLive)
        {
            Record = record;
            IsLive = isLive;
        }

        /// <summary>
        /// The event.
        /// </summary>
        public EventRecord Record { get; }

        /// <summary>
        /// True for an event which just happened, false for one read from history.
        /// </summary>
        public bool IsLive { get; }
    }

    /// <summary>
    /// The payload of <see cref="HistoricalEventsClientModel.FilterChanged"/>.
    /// </summary>
    public sealed class FilterChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public FilterChangedEventArgs(FilterDeclaration filter, IReadOnlyList<string> columnNames)
        {
            Filter = filter;
            ColumnNames = columnNames;
        }

        /// <summary>
        /// The filter which is now in effect.
        /// </summary>
        public FilterDeclaration Filter { get; }

        /// <summary>
        /// The names of the columns the list shows for it, in order.
        /// </summary>
        public IReadOnlyList<string> ColumnNames { get; }
    }

    /// <summary>
    /// The client model of the Historical Events client: shows the events of one area,
    /// from history and - while subscribed - live, and lets the user page through,
    /// filter, rewrite and delete the history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The area and the filter are what the user chose; they outlive the session, the way
    /// the window remembers them across a disconnect. The first session picks the defaults
    /// of the sample: the platforms of the server and the well test reports they raise.
    /// </para>
    /// <para>
    /// Live events come off an <see cref="EventStream"/> and are reported through
    /// <see cref="EventReceived"/> one at a time; events read from history are reported
    /// through the same event with <see cref="EventReceivedEventArgs.IsLive"/> false. The
    /// texts the list shows are computed here, before the event is raised, so the window
    /// only writes them into a row.
    /// </para>
    /// <para>
    /// The history itself is read and written through the <see cref="HistoryClient"/> of
    /// the SDK - <c>session.Historian()</c> - which builds the details of every request
    /// from the filter, follows the continuation points of a read, and lines the fields
    /// of an event up with the select clauses of the filter in both directions: what a
    /// read hands out and what an insert, replace or update sends are the same shape.
    /// </para>
    /// </remarks>
    public sealed class HistoricalEventsClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the historical events model, for a caller which cannot name
        /// the generated constants (they exist in the server assembly as well).
        /// </summary>
        public const string HistoricalEventsNamespaceUri = Namespaces.HistoricalEvents;

        /// <summary>
        /// How many events one page holds when the request puts no number on it.
        /// </summary>
        private const uint kDefaultPageSize = 1000;

        private EventStream m_stream;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public HistoricalEventsClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The area whose events are shown, <see cref="NodeId.Null"/> before the first session picked the default.
        /// </summary>
        public NodeId AreaId { get; private set; }

        /// <summary>
        /// The filter which selects the events and their fields, null before the first
        /// session picked the default.
        /// </summary>
        public FilterDeclaration Filter { get; private set; }

        /// <summary>
        /// Whether live events are streamed while a session is attached.
        /// </summary>
        public bool IsSubscribed { get; private set; }

        /// <summary>
        /// The names of the columns the list shows for the current filter, in order.
        /// </summary>
        public IReadOnlyList<string> ColumnNames => ColumnNamesOf(Filter);

        /// <summary>
        /// Raised for every event, live or read from history.
        /// </summary>
        public event EventHandler<EventReceivedEventArgs> EventReceived;

        /// <summary>
        /// Raised when the events shown so far no longer apply, because the area or the
        /// filter changed.
        /// </summary>
        public event EventHandler<EventArgs> EventsCleared;

        /// <summary>
        /// Raised when the filter changed, with the columns the list shows for it.
        /// </summary>
        public event EventHandler<FilterChangedEventArgs> FilterChanged;

        #region Area, filter and subscription
        /// <summary>
        /// Starts or stops streaming live events.
        /// </summary>
        /// <remarks>
        /// The choice is remembered while detached and applied when a session is attached.
        /// </remarks>
        /// <param name="subscribed">Whether to stream live events.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task SetSubscribedAsync(bool subscribed, CancellationToken ct = default)
        {
            if (IsSubscribed == subscribed)
            {
                return;
            }

            IsSubscribed = subscribed;

            if (!IsConnected)
            {
                return;
            }

            if (subscribed)
            {
                await StartStreamAsync().ConfigureAwait(false);
            }
            else
            {
                await StopStreamAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Changes the area whose events are shown.
        /// </summary>
        /// <param name="areaId">The area.</param>
        /// <param name="fetchRecent">Whether to read the recent history of the area.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ChangeAreaAsync(NodeId areaId, bool fetchRecent, CancellationToken ct = default)
        {
            AreaId = areaId;

            Raise(EventsCleared, EventArgs.Empty);

            // the choice is remembered while detached; the history is read once there is
            // a session to read it from.
            if (fetchRecent && IsConnected)
            {
                await ReadRecentHistoryAsync(ct).ConfigureAwait(false);
            }

            // the node an item monitors cannot be changed, so the stream is restarted:
            // that removes the old monitored item and creates one for the new area.
            await RestartStreamAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the filter which selects the events and their fields.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="fetchRecent">Whether to read the recent history of the area with the new filter.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ChangeFilterAsync(FilterDeclaration filter, bool fetchRecent, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filter);

            Filter = filter;

            Raise(EventsCleared, EventArgs.Empty);
            Raise(FilterChanged, new FilterChangedEventArgs(filter, ColumnNamesOf(filter)));

            if (fetchRecent && IsConnected)
            {
                await ReadRecentHistoryAsync(ct).ConfigureAwait(false);
            }

            // the event filter of an item cannot be changed, so the stream is restarted:
            // that removes the old monitored item and creates one with the new filter.
            await RestartStreamAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the last hour, or the last ten, of the events of the area and reports
        /// them through <see cref="EventReceived"/>.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task ReadRecentHistoryAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (AreaId.IsNull || Filter == null)
            {
                return;
            }

            // an area which does not historize its events has nothing to read.
            if (await session.NodeCache.FindAsync(AreaId, ct).ConfigureAwait(false) is not IObject area
                || (area.EventNotifier & EventNotifiers.HistoryRead) == 0)
            {
                return;
            }

            // a start time after the end time reads backwards, so this asks for the newest
            // ten events of the last hour.
            DateTime start = DateTime.UtcNow.AddSeconds(30);

            EventHistoryPage page = await ReadHistoryAsync(
                AreaId,
                Filter,
                new EventHistoryRequest(start, start.AddHours(-1), 10),
                ct).ConfigureAwait(false);

            foreach (EventRecord record in page.Events)
            {
                Raise(EventReceived, new EventReceivedEventArgs(record, false));
            }

            // only the first page is wanted; the server is told so.
            if (page.HasMore)
            {
                await ReleaseContinuationPointAsync(page.Continuation, ct).ConfigureAwait(false);
            }
        }
        #endregion

        #region History
        /// <summary>
        /// Reads the first page of the event history of an area.
        /// </summary>
        /// <remarks>
        /// The history client walks the whole range as one sequence; the model pulls
        /// one page of it at a time so that Go, Next and Stop keep meaning what they
        /// always did in the window.
        /// </remarks>
        /// <param name="areaId">The area.</param>
        /// <param name="filter">The filter which selects the events and their fields.</param>
        /// <param name="request">The range and the page size.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<EventHistoryPage> ReadHistoryAsync(
            NodeId areaId,
            FilterDeclaration filter,
            EventHistoryRequest request,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(request);

            ISession session = RequireSession();

            IAsyncEnumerator<HistoryEventFieldList> reader = session.Historian().ReadEventsAsync(
                areaId,
                request.StartTime,
                request.EndTime,
                filter.GetFilter(),
                request.MaxEvents,
                TimestampsToReturn.Source,
                ct).GetAsyncEnumerator(ct);

            return ReadPageAsync(
                new EventHistoryContinuation(areaId, filter, reader, request.MaxEvents != 0 ? request.MaxEvents : kDefaultPageSize),
                ct);
        }

        /// <summary>
        /// Reads the next page of a history read.
        /// </summary>
        /// <param name="continuation">What the previous page handed back.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<EventHistoryPage> ReadNextAsync(EventHistoryContinuation continuation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(continuation);

            RequireSession();

            return ReadPageAsync(continuation, ct);
        }

        /// <summary>
        /// Tells the server that the rest of a paged read is not wanted.
        /// </summary>
        /// <remarks>
        /// Abandoning the sequence of the history client is what releases the
        /// continuation point the server is holding for it.
        /// </remarks>
        /// <param name="continuation">What the last page handed back.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ReleaseContinuationPointAsync(EventHistoryContinuation continuation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(continuation);

            await continuation.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the time of the oldest event in the history of an area.
        /// </summary>
        /// <param name="areaId">The area.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The time, in UTC.</returns>
        /// <exception cref="ServiceResultException">The area has no events in its history.</exception>
        public async Task<DateTime> ReadFirstEventTimeAsync(NodeId areaId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            // read the time of the first event in the archive: one event, forwards from
            // the beginning of time, with only its Time field selected. Both bounds are
            // given: the sample server applies the window [start, end) as it is and does
            // not treat a missing end as "up to now".
            var filter = new EventFilter();
            filter.AddSelectClause(Opc.Ua.ObjectTypeIds.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.Time));

            // leaving the enumeration after the first event is what releases the
            // continuation point the server opened for the rest.
            await foreach (HistoryEventFieldList e in session.Historian().ReadEventsAsync(
                areaId,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DateTime.UtcNow.AddDays(1),
                filter,
                maxValuesPerNode: 1,
                TimestampsToReturn.Source,
                ct).ConfigureAwait(false))
            {
                // the Time field is a DateTimeUtc, which the Variant hands out as such, not
                // as a DateTime.
                if (e.EventFields.Count == 0 || !e.EventFields[0].TryGetValue(out DateTimeUtc eventTime))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                }

                return (DateTime)eventTime;
            }

            throw new ServiceResultException(StatusCodes.BadNoDataAvailable);
        }

        /// <summary>
        /// Writes events into the history of an area.
        /// </summary>
        /// <remarks>
        /// The fields of a record are the node id of the event followed by the fields
        /// of the filter, which is how a read hands them out; the write sends the same
        /// shape back with the same filter, so the server lines the fields up with the
        /// select clauses the way it did when it produced them. Insert refuses an event
        /// id the history already holds, Replace one it does not, and Update takes
        /// either.
        /// </remarks>
        /// <param name="areaId">The area.</param>
        /// <param name="filter">The filter the events were read with, or built for.</param>
        /// <param name="events">The events.</param>
        /// <param name="updateType">Whether to insert, replace or update.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>What the server answered for each event.</returns>
        public async Task<IReadOnlyList<StatusCode>> WriteEventsAsync(
            NodeId areaId,
            FilterDeclaration filter,
            IReadOnlyList<EventRecord> events,
            PerformUpdateType updateType,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(events);

            HistoryClient historian = RequireSession().Historian();

            // a write carries the select clauses of the filter and nothing else: they
            // are what lines the fields up, and a where clause has no meaning for an
            // update - the history client refuses one. It refuses a standard field
            // selected twice as well, and a filter built from a type declaration
            // selects the fields of the base event type once for the base type and
            // once more for the report type which inherits them, so a clause which
            // repeats an earlier one is left out together with its field.
            IList<SimpleAttributeOperand> selectClauses = filter.GetSelectClause();
            List<int> kept = DistinctClauses(selectClauses);

            var eventFilter = new EventFilter {
                SelectClauses = kept.Select(index => selectClauses[index]).ToArray().ToArrayOf(),
            };

            var fieldLists = new HistoryEventFieldList[events.Count];

            for (int ii = 0; ii < events.Count; ii++)
            {
                IReadOnlyList<Variant> fields = events[ii].Fields;

                if (fields.Count != selectClauses.Count)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "The event carries {0} fields, the filter selects {1}.",
                        fields.Count,
                        selectClauses.Count);
                }

                fieldLists[ii] = new HistoryEventFieldList {
                    EventFields = kept.Select(index => fields[index]).ToArray().ToArrayOf(),
                };
            }

            ArrayOf<StatusCode> results = updateType switch {
                PerformUpdateType.Insert => await historian.InsertEventsAsync(areaId, eventFilter, fieldLists, ct).ConfigureAwait(false),
                PerformUpdateType.Replace => await historian.ReplaceEventsAsync(areaId, eventFilter, fieldLists, ct).ConfigureAwait(false),
                PerformUpdateType.Update => await historian.UpdateEventsAsync(areaId, eventFilter, fieldLists, ct).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(updateType), updateType, "Events are inserted, replaced or updated; Remove is what DeleteEventsAsync does."),
            };

            return results.ToArray();
        }

        /// <summary>
        /// The indexes of the select clauses which do not repeat an earlier one: the
        /// first clause for each attribute and browse path, whichever type declared it.
        /// </summary>
        private static List<int> DistinctClauses(IList<SimpleAttributeOperand> selectClauses)
        {
            var kept = new List<int>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int ii = 0; ii < selectClauses.Count; ii++)
            {
                SimpleAttributeOperand clause = selectClauses[ii];

                string identity = clause.AttributeId + "|" +
                    string.Join("/", clause.BrowsePath.ToArray().Select(name => name.ToString())) + "|" +
                    clause.IndexRange;

                if (seen.Add(identity))
                {
                    kept.Add(ii);
                }
            }

            return kept;
        }

        /// <summary>
        /// Replaces one field of an event in the history of an area.
        /// </summary>
        /// <remarks>
        /// The record is rewritten with the new value in place of the old one and sent
        /// back as a replace, which keeps its event id and everything else about it.
        /// </remarks>
        /// <param name="areaId">The area.</param>
        /// <param name="filter">The filter the event was read with.</param>
        /// <param name="record">The event.</param>
        /// <param name="browseName">The field to change.</param>
        /// <param name="value">The new value of the field.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The record as it was written, with the texts the list shows for it.</returns>
        public async Task<EventRecord> ReplaceEventFieldAsync(
            NodeId areaId,
            FilterDeclaration filter,
            EventRecord record,
            QualifiedName browseName,
            Variant value,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(record);

            ISession session = RequireSession();

            int index = IndexOfField(filter, browseName);

            if (index < 0 || index >= record.Fields.Count)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "The filter does not select the field {0}.", browseName);
            }

            var fields = new List<Variant>(record.Fields) {
                [index] = value,
            };

            EventRecord replacement = await CreateRecordAsync(session, filter, fields, ct).ConfigureAwait(false);

            IReadOnlyList<StatusCode> results = await WriteEventsAsync(areaId, filter, new[] { replacement }, PerformUpdateType.Replace, ct).ConfigureAwait(false);

            if (results.Count == 0 || StatusCode.IsBad(results[0]))
            {
                throw new ServiceResultException(results.Count == 0 ? StatusCodes.BadUnexpectedError : results[0]);
            }

            return replacement;
        }

        /// <summary>
        /// Deletes events from the history of an area.
        /// </summary>
        /// <param name="areaId">The area.</param>
        /// <param name="filter">The filter the events were read with, which says where their EventId is.</param>
        /// <param name="events">The events.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task DeleteEventsAsync(
            NodeId areaId,
            FilterDeclaration filter,
            IReadOnlyList<EventRecord> events,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(events);

            HistoryClient historian = RequireSession().Historian();

            // can't delete events if no event id.
            if (!filter.Fields.Any(field => field.InstanceDeclaration.BrowseName == Opc.Ua.BrowseNames.EventId))
            {
                throw ServiceResultException.Create(StatusCodes.BadEventIdUnknown, "Cannot delete events if EventId was not selected.");
            }

            // build list of events to delete.
            var eventIds = new List<ByteString>();

            foreach (EventRecord record in events)
            {
                filter.GetValue(new QualifiedName(Opc.Ua.BrowseNames.EventId), new List<Variant>(record.Fields)).TryGetValue(out ByteString eventId);

                eventIds.Add(eventId);
            }

            // delete the events; the client unpacks what the server answered per event.
            ArrayOf<StatusCode> results = await historian.DeleteEventsAsync(areaId, eventIds.ToArray(), ct).ConfigureAwait(false);

            // check for item level errors.
            int failed = results.ToArray().Count(StatusCode.IsBad);

            if (failed > 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEventIdUnknown,
                    "Error deleting events. Only {0} of {1} deletes succeeded.",
                    events.Count - failed,
                    events.Count);
            }
        }

        /// <summary>
        /// Pulls one page of events off the sequence of a read.
        /// </summary>
        /// <remarks>
        /// The sequence is exhausted when it hands out fewer events than the page
        /// holds, and the continuation is disposed then - there is no continuation
        /// point left to release. A full page keeps the continuation, and with it the
        /// continuation point, for the next page.
        /// </remarks>
        private async Task<EventHistoryPage> ReadPageAsync(EventHistoryContinuation continuation, CancellationToken ct)
        {
            ISession session = RequireSession();

            var events = new List<EventRecord>();
            bool exhausted = false;

            try
            {
                for (uint ii = 0; ii < continuation.PageSize; ii++)
                {
                    if (!await continuation.Reader.MoveNextAsync().ConfigureAwait(false))
                    {
                        exhausted = true;
                        break;
                    }

                    events.Add(await CreateRecordAsync(
                        session,
                        continuation.Filter,
                        continuation.Reader.Current.EventFields.ToList(),
                        ct).ConfigureAwait(false));
                }
            }
            catch
            {
                await continuation.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            if (exhausted)
            {
                await continuation.DisposeAsync().ConfigureAwait(false);
                return new EventHistoryPage(events, null);
            }

            return new EventHistoryPage(events, continuation);
        }
        #endregion

        #region Types and names
        /// <summary>
        /// Collects the fields an event type declares, which is what a filter is built from.
        /// </summary>
        /// <param name="typeId">The event type.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<TypeDeclaration> DescribeEventTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            return new TypeDeclaration {
                NodeId = typeId,
                Declarations = await SampleTypeModel.CollectInstanceDeclarationsForTypeAsync(session, typeId, ct).ConfigureAwait(false),
            };
        }

        /// <summary>
        /// Finds the direct subtypes of a type, which is how the event type tree is expanded.
        /// </summary>
        /// <param name="typeId">The type.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<ReferenceDescription>> BrowseSubtypesAsync(NodeId typeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodeToBrowse = new BrowseDescription {
                NodeId = typeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, nodeToBrowse, false, ct)
                .ConfigureAwait(false);

            // a type on another server cannot be described from this session.
            return references?.Where(reference => !reference.NodeId.IsAbsolute).ToList()
                ?? new List<ReferenceDescription>();
        }

        /// <summary>
        /// The text the server displays for a node.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<string> GetDisplayTextAsync(NodeId nodeId, CancellationToken ct = default)
        {
            return await RequireSession().NodeCache.GetDisplayTextAsync(nodeId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the locale the server answers in.
        /// </summary>
        /// <param name="locale">The locale.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task SetLocaleAsync(string locale, CancellationToken ct = default)
        {
            return RequireSession().ChangePreferredLocalesAsync(new List<string> { locale }, ct);
        }

        /// <summary>
        /// Where a field sits in the fields of a record: the node id of the event comes
        /// first, the fields of the filter after it.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="browseName">The field.</param>
        /// <returns>The index into <see cref="EventRecord.Fields"/>, or -1 when the filter does not select the field.</returns>
        public static int IndexOfField(FilterDeclaration filter, QualifiedName browseName)
        {
            ArgumentNullException.ThrowIfNull(filter);

            for (int ii = 0; ii < filter.Fields.Count; ii++)
            {
                if (filter.Fields[ii].InstanceDeclaration.BrowseName == browseName)
                {
                    return ii + 1;
                }
            }

            return -1;
        }
        #endregion

        #region Lifecycle
        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // the first session picks the defaults of the sample; a later one keeps what
            // the user chose in the meantime.
            if (Filter == null)
            {
                AreaId = ExpandedNodeId.ToNodeId(ObjectIds.Plaforms, session.NamespaceUris);

                TypeDeclaration type = await DescribeEventTypeAsync(
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.WellTestReportType, session.NamespaceUris),
                    ct).ConfigureAwait(false);

                Filter = new FilterDeclaration(type, null);
            }

            Raise(EventsCleared, EventArgs.Empty);
            Raise(FilterChanged, new FilterChangedEventArgs(Filter, ColumnNamesOf(Filter)));

            if (IsSubscribed)
            {
                await StartStreamAsync().ConfigureAwait(false);
                await ReadRecentHistoryAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            // done before the session is closed: the stream is ended and its subscription
            // deleted while the session can still do that.
            return StopStreamAsync();
        }

        // the streaming subscription belongs to the subscription manager of the session and
        // survives a reconnect together with its monitored item, so the enumeration keeps
        // running: the reconnect hooks of the base class are not overridden.

        /// <summary>
        /// Starts streaming the events of the area, creating the stream on first use.
        /// </summary>
        private async Task StartStreamAsync()
        {
            m_stream ??= new EventStream(RequireSession(), OnLiveEventAsync, ReportError);

            await m_stream.StartAsync(AreaId, Filter).ConfigureAwait(false);
        }

        /// <summary>
        /// Restarts the stream for the current area and filter, if there is one.
        /// </summary>
        private async Task RestartStreamAsync()
        {
            EventStream stream = m_stream;

            if (stream != null)
            {
                await stream.StartAsync(AreaId, Filter).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Ends the stream and deletes its subscription.
        /// </summary>
        private async Task StopStreamAsync()
        {
            EventStream stream = m_stream;

            m_stream = null;

            if (stream != null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        #endregion

        #region Display texts
        /// <summary>
        /// Reports a live event. Runs on the pump, one event at a time.
        /// </summary>
        private async Task OnLiveEventAsync(FilterDeclaration filter, IReadOnlyList<Variant> fields, CancellationToken ct)
        {
            // check if the filter has changed while this event was on its way.
            if (fields.Count != filter.Fields.Count + 1)
            {
                return;
            }

            EventRecord record = await CreateRecordAsync(RequireSession(), filter, fields, ct).ConfigureAwait(false);

            Raise(EventReceived, new EventReceivedEventArgs(record, true));
        }

        /// <summary>
        /// Computes the texts the list shows for an event.
        /// </summary>
        private static async Task<EventRecord> CreateRecordAsync(
            ISession session,
            FilterDeclaration filter,
            IReadOnlyList<Variant> fields,
            CancellationToken ct)
        {
            var texts = new List<string>();

            // the first field is the node id of the event, which the select clause asks
            // for ahead of the fields of the filter.
            for (int ii = 1; ii < fields.Count && ii - 1 < filter.Fields.Count; ii++)
            {
                FilterDeclarationField field = filter.Fields[ii - 1];

                if (!field.DisplayInList)
                {
                    continue;
                }

                texts.Add(await DisplayTextAsync(session, field, fields[ii], ct).ConfigureAwait(false));
            }

            return new EventRecord(fields, texts);
        }

        /// <summary>
        /// The text the list shows for one field of an event.
        /// </summary>
        private static async Task<string> DisplayTextAsync(
            ISession session,
            FilterDeclarationField field,
            Variant value,
            CancellationToken ct)
        {
            // check for missing fields.
            if (value.IsNull)
            {
                return string.Empty;
            }

            // display the name of a node instead of the node id.
            if (value.TryGetValue(out NodeId nodeId))
            {
                INode node = await session.NodeCache.FindAsync(nodeId, ct).ConfigureAwait(false);

                return node?.ToString() ?? string.Empty;
            }

            // display local time for any time fields. The value is a DateTimeUtc, which
            // the Variant hands out as such and not as a DateTime.
            if (value.TryGetValue(out DateTimeUtc fieldTime))
            {
                DateTime local = fieldTime.ToLocalTime();

                return field.InstanceDeclaration.DisplayName.Contains("Time", StringComparison.Ordinal)
                    ? local.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture)
                    : local.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
            }

            // use default string format.
            return value.ToString();
        }

        /// <summary>
        /// The names of the columns the list shows for a filter: the display name of every
        /// field the filter marks for the list, in the order of the filter.
        /// </summary>
        /// <param name="filter">The filter, which may be null.</param>
        public static IReadOnlyList<string> ColumnNamesOf(FilterDeclaration filter)
        {
            if (filter == null)
            {
                return Array.Empty<string>();
            }

            return filter.Fields
                .Where(field => field.DisplayInList)
                .Select(field => field.InstanceDeclaration.DisplayName)
                .ToList();
        }
        #endregion
    }
}
