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
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The result of one history read, with the continuation point it left behind.
    /// </summary>
    public sealed class HistoryReadOutcome
    {
        /// <summary>
        /// The status of the read.
        /// </summary>
        public StatusCode StatusCode { get; init; }

        /// <summary>
        /// The values which were read, empty for an event read.
        /// </summary>
        public IReadOnlyList<DataValue> Values { get; init; } = [];

        /// <summary>
        /// The events which were read, empty for a data read.
        /// </summary>
        public IReadOnlyList<HistoryEventFieldList> Events { get; init; } = [];

        /// <summary>
        /// The continuation point, empty when the server is done.
        /// </summary>
        public ByteString ContinuationPoint { get; init; }

        /// <summary>
        /// True when the server has more to give.
        /// </summary>
        public bool HasMore => !ContinuationPoint.IsNull && ContinuationPoint.Length > 0;
    }

    /// <summary>
    /// The history services, as one call each.
    /// </summary>
    /// <remarks>
    /// The historian samples implement most of their behaviour in the history services,
    /// and none of it can be reached through the read and write services a client would
    /// otherwise use. The results are returned rather than thrown, because a test which
    /// pins down what a server answers for a bad request needs the status code as a value.
    /// </remarks>
    public static class HistoryOps
    {
        /// <summary>
        /// Reads raw history between two points in time.
        /// </summary>
        public static Task<HistoryReadOutcome> ReadRawAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint numValuesPerNode,
            CancellationToken ct,
            bool returnBounds = false,
            ByteString continuationPoint = default)
        {
            var details = new ReadRawModifiedDetails {
                IsReadModified = false,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = numValuesPerNode,
                ReturnBounds = returnBounds,
            };

            return ReadAsync(session, nodeId, details, ct, continuationPoint);
        }

        /// <summary>
        /// Reads the modified history - the replaced and deleted values - between
        /// two points in time.
        /// </summary>
        public static Task<HistoryReadOutcome> ReadModifiedAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint numValuesPerNode,
            CancellationToken ct,
            ByteString continuationPoint = default)
        {
            var details = new ReadRawModifiedDetails {
                IsReadModified = true,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = numValuesPerNode,
            };

            return ReadAsync(session, nodeId, details, ct, continuationPoint);
        }

        /// <summary>
        /// Follows the continuation points of a raw read until the server is done.
        /// </summary>
        /// <remarks>
        /// The cap is a safety net: a node manager which hands out a continuation point it
        /// never retires would otherwise hang the test run rather than fail it.
        /// </remarks>
        public static async Task<IReadOnlyList<DataValue>> ReadAllRawAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint numValuesPerNode,
            CancellationToken ct,
            int maxReads = 100)
        {
            var all = new List<DataValue>();
            ByteString continuationPoint = default;

            for (int reads = 0; reads < maxReads; reads++)
            {
                HistoryReadOutcome outcome = await ReadRawAsync(
                    session,
                    nodeId,
                    startTime,
                    endTime,
                    numValuesPerNode,
                    ct,
                    continuationPoint: continuationPoint).ConfigureAwait(false);

                if (StatusCode.IsBad(outcome.StatusCode))
                {
                    throw new ServiceResultException(outcome.StatusCode);
                }

                all.AddRange(outcome.Values);

                if (!outcome.HasMore)
                {
                    return all;
                }

                continuationPoint = outcome.ContinuationPoint;
            }

            throw new InvalidOperationException(
                $"The server kept handing out continuation points for {nodeId} after {maxReads} reads.");
        }

        /// <summary>
        /// Reads history aggregated over intervals.
        /// </summary>
        public static Task<HistoryReadOutcome> ReadProcessedAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            NodeId aggregateType,
            CancellationToken ct)
        {
            var details = new ReadProcessedDetails {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = processingInterval,
                AggregateType = new[] { aggregateType }.ToArrayOf(),
            };

            return ReadAsync(session, nodeId, details, ct);
        }

        /// <summary>
        /// Reads the history at the given points in time.
        /// </summary>
        public static Task<HistoryReadOutcome> ReadAtTimeAsync(
            ISession session,
            NodeId nodeId,
            IEnumerable<DateTime> times,
            CancellationToken ct,
            bool useSimpleBounds = true)
        {
            var details = new ReadAtTimeDetails {
                ReqTimes = times.Select(time => (DateTimeUtc)time).ToArray().ToArrayOf(),
                UseSimpleBounds = useSimpleBounds,
            };

            return ReadAsync(session, nodeId, details, ct);
        }

        /// <summary>
        /// Reads the events of a notifier from history.
        /// </summary>
        public static Task<HistoryReadOutcome> ReadEventsAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint numValuesPerNode,
            EventFilter filter,
            CancellationToken ct,
            ByteString continuationPoint = default)
        {
            var details = new ReadEventDetails {
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = numValuesPerNode,
                Filter = filter,
            };

            return ReadAsync(session, nodeId, details, ct, continuationPoint);
        }

        /// <summary>
        /// Releases a continuation point without reading anything more.
        /// </summary>
        public static async Task<StatusCode> ReleaseAsync(
            ISession session,
            NodeId nodeId,
            ByteString continuationPoint,
            CancellationToken ct)
        {
            var nodesToRead = new List<HistoryReadValueId> {
                new() { NodeId = nodeId, ContinuationPoint = continuationPoint },
            };

            HistoryReadResponse response = await session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Both,
                true,
                nodesToRead,
                ct).ConfigureAwait(false);

            return response.Results.ToList()[0].StatusCode;
        }

        /// <summary>
        /// Inserts, replaces or updates values in the history of a node.
        /// </summary>
        public static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> UpdateDataAsync(
            ISession session,
            NodeId nodeId,
            PerformUpdateType updateType,
            IEnumerable<DataValue> values,
            CancellationToken ct)
        {
            var details = new UpdateDataDetails {
                NodeId = nodeId,
                PerformInsertReplace = updateType,
                UpdateValues = values.ToArray().ToArrayOf(),
            };

            return await UpdateAsync(session, details, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Inserts, replaces or updates structured values - annotations - in the
        /// history of a node.
        /// </summary>
        public static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> UpdateStructureDataAsync(
            ISession session,
            NodeId nodeId,
            PerformUpdateType updateType,
            IEnumerable<DataValue> values,
            CancellationToken ct)
        {
            var details = new UpdateStructureDataDetails {
                NodeId = nodeId,
                PerformInsertReplace = updateType,
                UpdateValues = values.ToArray().ToArrayOf(),
            };

            return await UpdateAsync(session, details, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes everything in the history of a node between two points in time.
        /// </summary>
        public static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> DeleteRawAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            CancellationToken ct)
        {
            var details = new DeleteRawModifiedDetails {
                NodeId = nodeId,
                IsDeleteModified = false,
                StartTime = startTime,
                EndTime = endTime,
            };

            return await UpdateAsync(session, details, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the values at the given points in time.
        /// </summary>
        public static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> DeleteAtTimeAsync(
            ISession session,
            NodeId nodeId,
            IEnumerable<DateTime> times,
            CancellationToken ct)
        {
            var details = new DeleteAtTimeDetails {
                NodeId = nodeId,
                ReqTimes = times.Select(time => (DateTimeUtc)time).ToArray().ToArrayOf(),
            };

            return await UpdateAsync(session, details, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Inserts, replaces or updates events in the history of a notifier.
        /// </summary>
        /// <remarks>
        /// The fields of an event travel in the order the select clauses of the filter
        /// name them, which is also how the server reads them back, so both sides of
        /// one call have to be built from the same filter.
        /// </remarks>
        public static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> UpdateEventsAsync(
            ISession session,
            NodeId nodeId,
            PerformUpdateType updateType,
            EventFilter filter,
            IEnumerable<HistoryEventFieldList> events,
            CancellationToken ct)
        {
            var details = new UpdateEventDetails {
                NodeId = nodeId,
                PerformInsertReplace = updateType,
                Filter = filter,
                EventData = events.ToArray().ToArrayOf(),
            };

            return await UpdateAsync(session, details, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes events from the history of a notifier.
        /// </summary>
        public static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> DeleteEventsAsync(
            ISession session,
            NodeId nodeId,
            IEnumerable<ByteString> eventIds,
            CancellationToken ct)
        {
            var details = new DeleteEventDetails {
                NodeId = nodeId,
                EventIds = eventIds.ToArray().ToArrayOf(),
            };

            return await UpdateAsync(session, details, ct).ConfigureAwait(false);
        }

        private static async Task<HistoryReadOutcome> ReadAsync(
            ISession session,
            NodeId nodeId,
            IEncodeable details,
            CancellationToken ct,
            ByteString continuationPoint = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            var nodesToRead = new List<HistoryReadValueId> {
                new() { NodeId = nodeId, ContinuationPoint = continuationPoint },
            };

            HistoryReadResponse response = await session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
                false,
                nodesToRead,
                ct).ConfigureAwait(false);

            List<HistoryReadResult> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, nodesToRead);

            HistoryReadResult result = results[0];

            object payload = result.HistoryData.IsNull
                ? null
                : ExtensionObject.ToEncodeable(result.HistoryData);

            return new HistoryReadOutcome {
                StatusCode = result.StatusCode,
                ContinuationPoint = result.ContinuationPoint,
                Values = payload is HistoryData data ? data.DataValues.ToArray() : [],
                Events = payload is HistoryEvent events ? events.Events.ToArray() : [],
            };
        }

        private static async Task<(StatusCode Result, IReadOnlyList<StatusCode> PerValue)> UpdateAsync(
            ISession session,
            IEncodeable details,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(session);

            var updates = new List<ExtensionObject> { new(details) };

            HistoryUpdateResponse response = await session
                .HistoryUpdateAsync(null, updates, ct)
                .ConfigureAwait(false);

            List<HistoryUpdateResult> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, updates);

            return (results[0].StatusCode, results[0].OperationResults.ToArray());
        }
    }
}
