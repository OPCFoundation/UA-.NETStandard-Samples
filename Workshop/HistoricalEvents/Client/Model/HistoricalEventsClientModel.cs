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
    /// What the next page of a paged history read needs: the server keeps the state of
    /// the read behind a continuation point, and the client has to repeat the request.
    /// </summary>
    public sealed class EventHistoryContinuation
    {
        internal EventHistoryContinuation(
            NodeId areaId,
            FilterDeclaration filter,
            ReadEventDetails details,
            ByteString continuationPoint)
        {
            AreaId = areaId;
            Filter = filter;
            Details = details;
            ContinuationPoint = continuationPoint;
        }

        internal NodeId AreaId { get; }

        internal FilterDeclaration Filter { get; }

        internal ReadEventDetails Details { get; }

        internal ByteString ContinuationPoint { get; }
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
    /// from history and - while subscribed - live, and lets the user page through, filter
    /// and delete the history.
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
    /// </remarks>
    public sealed class HistoricalEventsClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the historical events model, for a caller which cannot name
        /// the generated constants (they exist in the server assembly as well).
        /// </summary>
        public const string HistoricalEventsNamespaceUri = Namespaces.HistoricalEvents;

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

            var details = new ReadEventDetails {
                StartTime = start,
                EndTime = start.AddHours(-1),
                NumValuesPerNode = 10,
                Filter = Filter.GetFilter(),
            };

            EventHistoryPage page = await ReadPageAsync(AreaId, Filter, details, default, ct).ConfigureAwait(false);

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

            var details = new ReadEventDetails {
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                NumValuesPerNode = request.MaxEvents,
                Filter = filter.GetFilter(),
            };

            return ReadPageAsync(areaId, filter, details, default, ct);
        }

        /// <summary>
        /// Reads the next page of a history read.
        /// </summary>
        /// <param name="continuation">What the previous page handed back.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<EventHistoryPage> ReadNextAsync(EventHistoryContinuation continuation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(continuation);

            return ReadPageAsync(
                continuation.AreaId,
                continuation.Filter,
                continuation.Details,
                continuation.ContinuationPoint,
                ct);
        }

        /// <summary>
        /// Tells the server that the rest of a paged read is not wanted.
        /// </summary>
        /// <param name="continuation">What the last page handed back.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ReleaseContinuationPointAsync(EventHistoryContinuation continuation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(continuation);

            ISession session = RequireSession();

            var nodesToRead = new List<HistoryReadValueId> {
                new HistoryReadValueId {
                    NodeId = continuation.AreaId,
                    ContinuationPoint = continuation.ContinuationPoint,
                },
            };

            await HistoryReadAsync(session, continuation.Details, true, nodesToRead, ct).ConfigureAwait(false);
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
            var details = new ReadEventDetails {
                StartTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTime = DateTime.UtcNow.AddDays(1),
                NumValuesPerNode = 1,
                Filter = new EventFilter(),
            };

            details.Filter.AddSelectClause(Opc.Ua.ObjectTypeIds.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.Time));

            var nodeToRead = new HistoryReadValueId { NodeId = areaId };
            var nodesToRead = new List<HistoryReadValueId> { nodeToRead };

            List<HistoryReadResult> results = await HistoryReadAsync(session, details, false, nodesToRead, ct).ConfigureAwait(false);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            var data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;

            // release the continuation point.
            if (!results[0].ContinuationPoint.IsNull)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                await HistoryReadAsync(session, details, true, nodesToRead, ct).ConfigureAwait(false);
            }

            // check if an event found.
            if (data == null || data.Events.Count == 0 || data.Events[0].EventFields.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNoDataAvailable);
            }

            // the Time field is a DateTimeUtc, which the Variant hands out as such, not as
            // a DateTime.
            if (!data.Events[0].EventFields[0].TryGetValue(out DateTimeUtc eventTime))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }

            return (DateTime)eventTime;
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

            ISession session = RequireSession();

            // can't delete events if no event id.
            if (!filter.Fields.Any(field => field.InstanceDeclaration.BrowseName == Opc.Ua.BrowseNames.EventId))
            {
                throw ServiceResultException.Create(StatusCodes.BadEventIdUnknown, "Cannot delete events if EventId was not selected.");
            }

            // build list of events to delete.
            var details = new DeleteEventDetails { NodeId = areaId };

            foreach (EventRecord record in events)
            {
                filter.GetValue(new QualifiedName(Opc.Ua.BrowseNames.EventId), new List<Variant>(record.Fields)).TryGetValue(out ByteString eventId);

                details.EventIds = details.EventIds.AddItem(eventId);
            }

            // delete the events.
            var nodesToUpdate = new List<ExtensionObject> { new ExtensionObject(details) };

            HistoryUpdateResponse response = await session.HistoryUpdateAsync(null, nodesToUpdate, ct).ConfigureAwait(false);

            List<HistoryUpdateResult> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToUpdate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToUpdate);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            // check for item level errors.
            int failed = results[0].OperationResults.ToArray().Count(StatusCode.IsBad);

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
        /// Reads one page of the event history of an area.
        /// </summary>
        private async Task<EventHistoryPage> ReadPageAsync(
            NodeId areaId,
            FilterDeclaration filter,
            ReadEventDetails details,
            ByteString continuationPoint,
            CancellationToken ct)
        {
            ISession session = RequireSession();

            var nodesToRead = new List<HistoryReadValueId> {
                new HistoryReadValueId { NodeId = areaId, ContinuationPoint = continuationPoint },
            };

            List<HistoryReadResult> results = await HistoryReadAsync(session, details, false, nodesToRead, ct).ConfigureAwait(false);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            var events = new List<EventRecord>();

            if (ExtensionObject.ToEncodeable(results[0].HistoryData) is HistoryEvent data)
            {
                foreach (HistoryEventFieldList e in data.Events.ToArray())
                {
                    events.Add(await CreateRecordAsync(session, filter, e.EventFields.ToList(), ct).ConfigureAwait(false));
                }
            }

            EventHistoryContinuation continuation = null;

            if (!results[0].ContinuationPoint.IsNull && results[0].ContinuationPoint.Length > 0)
            {
                continuation = new EventHistoryContinuation(areaId, filter, details, results[0].ContinuationPoint);
            }

            return new EventHistoryPage(events, continuation);
        }

        /// <summary>
        /// Sends one HistoryRead request and validates the response.
        /// </summary>
        private static async Task<List<HistoryReadResult>> HistoryReadAsync(
            ISession session,
            ReadEventDetails details,
            bool releaseContinuationPoints,
            List<HistoryReadValueId> nodesToRead,
            CancellationToken ct)
        {
            HistoryReadResponse response = await session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                releaseContinuationPoints,
                nodesToRead,
                ct).ConfigureAwait(false);

            List<HistoryReadResult> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            return results;
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
                Declarations = await ModelUtils.CollectInstanceDeclarationsForTypeAsync(session, typeId, ct).ConfigureAwait(false),
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
