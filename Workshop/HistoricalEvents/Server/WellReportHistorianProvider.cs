/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Quickstarts.HistoricalEvents.Server
{
    /// <summary>
    /// Serves the well test reports through the SDK's native historian interfaces.
    /// </summary>
    /// <remarks>
    /// The node manager registers one instance of this provider for the sample
    /// namespace. <see cref="Opc.Ua.Server.AsyncCustomNodeManager"/> then routes every
    /// HistoryRead and HistoryUpdate service call for an event notifier through the
    /// <see cref="HistorianDispatcher"/>, which owns the protocol plumbing - the
    /// continuation points, the projection of a record onto the select clauses of the
    /// request, the decoding of an incoming update back into a record, and the audit
    /// events - and calls the methods below with validated, normalised requests. The
    /// node manager needs no history overrides at all.
    ///
    /// A record is the flat form the framework works in: the fields of an event keyed
    /// by the browse path which addresses them, with the segments joined by a slash.
    /// The reports live in the tables of the <see cref="ReportGenerator"/> as rows, so
    /// a read here materialises a row as the event state the sample already knows how
    /// to build and then asks it for exactly the fields the request refers to.
    ///
    /// The where clause is evaluated twice: here against the full event, and again by
    /// the framework against the record. That is deliberate. Evaluating it here is
    /// what the documentation calls push-down, and it is what keeps the requested
    /// number of events per page a count of matching events rather than of candidates.
    /// The framework re-evaluates for correctness, so a record has to carry every
    /// field the where clause reads as well as every field the select clauses ask for,
    /// or the second pass would discard what the first one kept.
    ///
    /// A provider read has no per-operation error channel, and nothing between here
    /// and the transport catches an exception, so every operation contains its own
    /// failures: reads answer with an empty page, updates with a bad status per event,
    /// and the error goes to the log.
    /// </remarks>
    public sealed class WellReportHistorianProvider : HistorianProviderBase, IHistorianEventProvider
    {
        /// <summary>
        /// Creates a provider serving the reports of the generator.
        /// </summary>
        public WellReportHistorianProvider(IServerInternal server, ReportGenerator generator, ushort namespaceIndex)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_generator = generator ?? throw new ArgumentNullException(nameof(generator));
            m_namespaceIndex = namespaceIndex;
            m_logger = server.Telemetry.CreateLogger<WellReportHistorianProvider>();
        }

        #region HistorianProviderBase Members
        /// <summary>
        /// The capabilities every notifier of this archive has.
        /// </summary>
        /// <remarks>
        /// Only the data half of these flags rolls up into the
        /// HistoryServerCapabilities node, and this archive holds events rather than
        /// values, so the read flags - which default to true - are cleared here and
        /// the node manager sets the event flags of that node by hand.
        /// </remarks>
        public override ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct)
        {
            return new ValueTask<HistorianNodeCapabilities>(s_capabilities);
        }
        #endregion

        #region IHistorianEventProvider Members
        /// <inheritdoc/>
        public ValueTask<HistorianPage<HistorianEventRecord>> ReadEventsAsync(
            HistorianOperationContext context,
            HistorianEventReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            try
            {
                List<HistorianEventRecord> matching = ReadMatchingRecords(context, request);

                return new ValueTask<HistorianPage<HistorianEventRecord>>(
                    Paginate(matching, request.MaxValues, request.IsForward, resumeToken));
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reading the event history of {NodeId}.", request.NodeId);
                return new ValueTask<HistorianPage<HistorianEventRecord>>(HistorianPage<HistorianEventRecord>.Empty);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return WriteEventsAsync(context, nodeId, events, PerformUpdateType.Insert);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return WriteEventsAsync(context, nodeId, events, PerformUpdateType.Replace);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return WriteEventsAsync(context, nodeId, events, PerformUpdateType.Update);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<ByteString> eventIds,
            CancellationToken ct)
        {
            if (eventIds == null)
            {
                throw new ArgumentNullException(nameof(eventIds));
            }

            var results = new StatusCode[eventIds.Count];

            try
            {
                lock (m_generator.SyncRoot)
                {
                    for (int ii = 0; ii < eventIds.Count; ii++)
                    {
                        results[ii] = TryGetEventId(eventIds[ii], out string eventId) && m_generator.DeleteEvent(eventId)
                            ? StatusCodes.Good
                            : StatusCodes.BadEventIdUnknown;
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error deleting from the event history of {NodeId}.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, eventIds.Count));
            }

            return new ValueTask<IList<StatusCode>>(results);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the reports of the notifier which fall inside the window of the
        /// request and pass its where clause, oldest first, as the flat records the
        /// framework works in.
        /// </summary>
        /// <remarks>
        /// The whole window is materialised on every page rather than carried across
        /// them. A continuation point travels to the client and back and may outlive
        /// the task which issued it, so it cannot hold a cursor; the resume token
        /// below therefore only records where the last page ended, and the page after
        /// it re-reads the window. The sample archive is small enough for that to be
        /// the clearer trade.
        /// </remarks>
        private List<HistorianEventRecord> ReadMatchingRecords(
            HistorianOperationContext context,
            HistorianEventReadRequest request)
        {
            ServerSystemContext systemContext = context.SystemContext;

            var filterContext = new FilterContext(
                systemContext.NamespaceUris,
                systemContext.TypeTable,
                systemContext.PreferredLocales,
                m_server.Telemetry);

            IReadOnlyList<SimpleAttributeOperand> operands = CollectOperands(request.Filter);

            var records = new List<HistorianEventRecord>();

            lock (m_generator.SyncRoot)
            {
                for (ReportType reportType = ReportType.FluidLevelTest; reportType <= ReportType.InjectionTest; reportType++)
                {
                    using DataView view = ReadWindow(context.Node, reportType, request);

                    foreach (DataRowView rowView in view)
                    {
                        BaseEventState e = m_generator.GetReport(
                            systemContext,
                            m_namespaceIndex,
                            reportType,
                            rowView.Row);

                        if (e == null)
                        {
                            continue;
                        }

                        // push the where clause down onto the full event: the framework
                        // evaluates it again on the record built below, but only what
                        // passes here is counted against the requested page size.
                        if (request.Filter.WhereClause != null &&
                            request.Filter.WhereClause.Elements.Count > 0 &&
                            !request.Filter.WhereClause.Evaluate(filterContext, e))
                        {
                            continue;
                        }

                        records.Add(CreateRecord(e, filterContext, operands));
                    }
                }
            }

            // the two report tables are each sorted by time, the merge of them is not.
            records.Sort(CompareRecords);

            return records;
        }

        /// <summary>
        /// Queries the reports of one kind which the notifier of the request covers,
        /// inside its time window.
        /// </summary>
        /// <remarks>
        /// A well reports its own tests, anything above it - an area, or the folder
        /// the areas hang under - reports the tests of the wells below it. An area
        /// carries its name as the identifier of its node id, the folder comes from
        /// the model and carries a number, and a query without an area name matches
        /// every well.
        ///
        /// The window the archive applies is half open - [start, end) whichever way
        /// the read runs - which is what the in memory historian of the SDK does for
        /// events as well.
        /// </remarks>
        private DataView ReadWindow(NodeState node, ReportType reportType, HistorianEventReadRequest request)
        {
            var startTime = (DateTime)request.StartTime;
            var endTime = (DateTime)request.EndTime;

            if (node is WellState)
            {
                return m_generator.ReadHistoryForWellId(
                    reportType,
                    node.NodeId.TryGetValue(out string wellId) ? wellId : null,
                    startTime,
                    endTime);
            }

            return m_generator.ReadHistoryForArea(
                reportType,
                node != null && node.NodeId.TryGetValue(out string areaName) ? areaName : null,
                startTime,
                endTime);
        }

        /// <summary>
        /// Flattens an event into the record the framework works in: every field the
        /// request refers to, keyed by the browse path which addresses it.
        /// </summary>
        private HistorianEventRecord CreateRecord(
            BaseEventState e,
            FilterContext filterContext,
            IReadOnlyList<SimpleAttributeOperand> operands)
        {
            var fields = new Dictionary<string, Variant>(StringComparer.Ordinal);

            foreach (SimpleAttributeOperand operand in operands)
            {
                Variant value = e.GetAttributeValue(
                    filterContext,
                    operand.TypeDefinitionId,
                    operand.BrowsePath,
                    operand.AttributeId,
                    operand.ParsedIndexRange);

                if (value.TryGetValue(out LocalizedText text) && !text.IsNullOrEmpty)
                {
                    value = Variant.From(m_server.ResourceManager.Translate(filterContext.PreferredLocales, text));
                }

                fields[BuildOperandKey(operand.BrowsePath)] = value;
            }

            return new HistorianEventRecord(
                e.EventId?.Value ?? ByteString.Empty,
                e.TypeDefinitionId,
                new DateTimeUtc(e.Time?.Value ?? DateTime.MinValue),
                fields);
        }

        /// <summary>
        /// Returns every field of the request which has to end up in a record: the
        /// select clauses, which the framework projects the answer from, and the
        /// operands of the where clause, which it evaluates the record against.
        /// </summary>
        private static IReadOnlyList<SimpleAttributeOperand> CollectOperands(EventFilter filter)
        {
            var operands = new List<SimpleAttributeOperand>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(SimpleAttributeOperand operand)
            {
                if (operand != null && operand.BrowsePath.Count > 0 && seen.Add(BuildOperandKey(operand.BrowsePath)))
                {
                    operands.Add(operand);
                }
            }

            foreach (SimpleAttributeOperand operand in filter.SelectClauses)
            {
                Add(operand);
            }

            if (filter.WhereClause != null)
            {
                foreach (ContentFilterElement element in filter.WhereClause.Elements)
                {
                    foreach (FilterOperand operand in element.GetOperands())
                    {
                        Add(operand as SimpleAttributeOperand);
                    }
                }
            }

            return operands;
        }

        /// <summary>
        /// The key a browse path is stored and looked up under: the names of its
        /// segments joined by a slash, the way the framework builds it.
        /// </summary>
        private static string BuildOperandKey(ArrayOf<QualifiedName> browsePath)
        {
            if (browsePath.Count == 1)
            {
                return browsePath[0].Name ?? String.Empty;
            }

            var key = new StringBuilder();

            for (int ii = 0; ii < browsePath.Count; ii++)
            {
                if (ii > 0)
                {
                    key.Append('/');
                }

                key.Append(browsePath[ii].Name);
            }

            return key.ToString();
        }

        /// <summary>
        /// Orders the records of both report tables into the single sequence the
        /// pages walk. The event id breaks the tie so two reports written in the same
        /// instant keep a stable order across the pages of one read.
        /// </summary>
        private static int CompareRecords(HistorianEventRecord x, HistorianEventRecord y)
        {
            int byTime = x.SourceTimestamp.CompareTo(y.SourceTimestamp);

            if (byTime != 0)
            {
                return byTime;
            }

            return String.CompareOrdinal(
                Convert.ToBase64String(x.EventId.ToArray()),
                Convert.ToBase64String(y.EventId.ToArray()));
        }

        /// <summary>
        /// Returns one page of the ordered records, resuming after the page which
        /// came before.
        /// </summary>
        /// <remarks>
        /// Several reports can share a source timestamp, so a token of only the
        /// timestamp would drop the rest of a group a page boundary lands in: the
        /// token carries how many records at the boundary timestamp the pages so far
        /// returned along with the timestamp itself.
        /// </remarks>
        private static HistorianPage<HistorianEventRecord> Paginate(
            List<HistorianEventRecord> records,
            uint maxValues,
            bool isForward,
            HistorianResumeToken resumeToken)
        {
            uint pageSize = maxValues != 0 ? maxValues : kDefaultPageSize;
            (DateTime resumeAt, int resumeSkip) = DecodeGroupPosition(resumeToken);
            bool resuming = resumeAt != DateTime.MinValue;

            var page = new List<HistorianEventRecord>();
            DateTime lastReturned = DateTime.MinValue;
            int returnedAtLast = 0;
            int skipped = 0;

            int ii = isForward ? 0 : records.Count - 1;

            while (ii >= 0 && ii < records.Count)
            {
                HistorianEventRecord record = records[ii];
                var timestamp = (DateTime)record.SourceTimestamp;
                ii += isForward ? 1 : -1;

                if (resuming)
                {
                    if (isForward ? timestamp < resumeAt : timestamp > resumeAt)
                    {
                        continue;
                    }

                    if (timestamp == resumeAt && skipped < resumeSkip)
                    {
                        skipped++;
                        continue;
                    }
                }

                page.Add(record);

                if (timestamp == lastReturned)
                {
                    returnedAtLast++;
                }
                else
                {
                    lastReturned = timestamp;
                    returnedAtLast = 1;
                }

                if (page.Count >= pageSize)
                {
                    // nothing behind the page means it is the last one, and a token
                    // would only buy the client an empty read.
                    if (ii < 0 || ii >= records.Count)
                    {
                        break;
                    }

                    int carriedOver = resuming && lastReturned == resumeAt ? resumeSkip : 0;

                    return new HistorianPage<HistorianEventRecord>(
                        page,
                        EncodeGroupPosition(lastReturned, carriedOver + returnedAtLast));
                }
            }

            return new HistorianPage<HistorianEventRecord>(page);
        }

        /// <summary>
        /// Applies the per event insert, replace or update to the archive.
        /// </summary>
        private ValueTask<IList<StatusCode>> WriteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            PerformUpdateType performUpdateType)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            // a client which writes through a well need not repeat which well it means.
            string defaultWellId = context.Node is WellState && nodeId.TryGetValue(out string wellId)
                ? wellId
                : null;

            var results = new StatusCode[events.Count];

            try
            {
                lock (m_generator.SyncRoot)
                {
                    for (int ii = 0; ii < events.Count; ii++)
                    {
                        results[ii] = WriteEvent(events[ii], defaultWellId, performUpdateType);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error writing to the event history of {NodeId}.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, events.Count));
            }

            return new ValueTask<IList<StatusCode>>(results);
        }

        /// <summary>
        /// Applies one event to the report table its type belongs to.
        /// </summary>
        /// <remarks>
        /// The archive stores a report of a known kind, not an arbitrary event, so a
        /// record which does not name one of the two report types has nowhere to go.
        /// The event type only reaches the provider if the client selected it, which
        /// is why an update whose filter leaves it out is refused rather than guessed
        /// at.
        /// </remarks>
        private StatusCode WriteEvent(HistorianEventRecord record, string defaultWellId, PerformUpdateType performUpdateType)
        {
            if (record == null || !TryGetReportType(record.EventType, out ReportType reportType))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!TryGetEventId(record.EventId, out string eventId))
            {
                // an insert may leave the event id to the archive, anything else has
                // to name the event it means.
                if (performUpdateType != PerformUpdateType.Insert)
                {
                    return StatusCodes.BadEventIdUnknown;
                }

                eventId = Guid.NewGuid().ToString();
            }

            return m_generator.WriteEvent(
                reportType,
                eventId,
                (DateTime)record.SourceTimestamp,
                record.Fields,
                defaultWellId,
                performUpdateType);
        }

        /// <summary>
        /// Returns the report table an event type belongs to.
        /// </summary>
        private bool TryGetReportType(NodeId eventType, out ReportType reportType)
        {
            if (eventType == new NodeId(ObjectTypes.FluidLevelTestReportType, m_namespaceIndex))
            {
                reportType = ReportType.FluidLevelTest;
                return true;
            }

            if (eventType == new NodeId(ObjectTypes.InjectionTestReportType, m_namespaceIndex))
            {
                reportType = ReportType.InjectionTest;
                return true;
            }

            reportType = default;
            return false;
        }

        /// <summary>
        /// Returns the event id in the form the archive keys its rows by.
        /// </summary>
        /// <remarks>
        /// The archive writes the event id of a report as the sixteen bytes of a
        /// guid, so anything else cannot name a row of it.
        /// </remarks>
        private static bool TryGetEventId(ByteString eventId, out string result)
        {
            if (eventId.IsEmpty || eventId.Length != 16)
            {
                result = null;
                return false;
            }

            result = new Guid(eventId.ToArray()).ToString();
            return true;
        }

        /// <summary>
        /// Encodes where a page ended: the boundary timestamp and how many records at
        /// that timestamp the pages so far returned.
        /// </summary>
        private static HistorianResumeToken EncodeGroupPosition(DateTime timestamp, int returnedAtTimestamp)
        {
            byte[] state = new byte[12];
            BinaryPrimitives.WriteInt64BigEndian(state, timestamp.Ticks);
            BinaryPrimitives.WriteInt32BigEndian(state.AsSpan(8), returnedAtTimestamp);
            return new HistorianResumeToken(state);
        }

        /// <summary>
        /// Decodes where the previous page ended; MinValue and zero on the first page.
        /// </summary>
        private static (DateTime Timestamp, int Count) DecodeGroupPosition(HistorianResumeToken token)
        {
            if (token.IsEmpty || token.State.Length != 12)
            {
                return (DateTime.MinValue, 0);
            }

            return (
                new DateTime(BinaryPrimitives.ReadInt64BigEndian(token.State.Span), DateTimeKind.Utc),
                BinaryPrimitives.ReadInt32BigEndian(token.State.Span.Slice(8)));
        }
        #endregion

        #region Private Fields
        private const uint kDefaultPageSize = 1000;

        /// <summary>
        /// What this archive supports.
        /// </summary>
        private static readonly HistorianNodeCapabilities s_capabilities = new HistorianNodeCapabilities {
            ReadRawData = false,
            ReadModifiedData = false,
            ReadAtTime = false,
            ReadProcessedData = false
        };

        private readonly IServerInternal m_server;
        private readonly ReportGenerator m_generator;
        private readonly ushort m_namespaceIndex;
        private readonly ILogger m_logger;
        #endregion
    }
}
