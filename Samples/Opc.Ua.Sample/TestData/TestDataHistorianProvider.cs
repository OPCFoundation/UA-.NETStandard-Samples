/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace TestData
{
    /// <summary>
    /// Serves the in-memory archive of the test system through the SDK's native
    /// historian interfaces.
    /// </summary>
    /// <remarks>
    /// The node manager registers one instance of this provider for the test data
    /// namespace. <see cref="Opc.Ua.Server.AsyncCustomNodeManager"/> then routes
    /// every HistoryRead through the <see cref="HistorianDispatcher"/>, which owns
    /// the protocol plumbing - continuation points, timestamps to return, index
    /// ranges and data encodings - and calls the provider with validated,
    /// normalised requests. Processed and at-time reads run on the framework
    /// fallbacks built on <see cref="ReadRawAsync"/>.
    ///
    /// The archive is read only: it is filled by the simulation, so every update
    /// operation answers with BadHistoryOperationUnsupported, which is what the
    /// sample always did.
    ///
    /// A provider read has no per-operation error channel, and nothing between
    /// here and the transport catches an exception, so every operation contains
    /// its own failures: reads answer with an empty page and the error goes to
    /// the log.
    /// </remarks>
    public sealed class TestDataHistorianProvider : HistorianProviderBase, IHistorianDataProvider
    {
        /// <summary>
        /// Creates a provider serving the archive of the test system.
        /// </summary>
        public TestDataHistorianProvider(TestDataSystem system, IServerInternal server)
        {
            m_system = system ?? throw new ArgumentNullException(nameof(system));

            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            m_logger = server.Telemetry.CreateLogger<TestDataHistorianProvider>();
        }

        #region HistorianProviderBase Members
        /// <inheritdoc/>
        public override ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
        {
            return new ValueTask<bool>(m_system.IsHistoryArchived(nodeId));
        }
        #endregion

        #region IHistorianDataProvider Members
        /// <inheritdoc/>
        public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
            HistorianOperationContext context,
            HistorianRawReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            try
            {
                IReadOnlyList<DataValue> values = m_system.ReadHistoryValues(request.NodeId);

                if (values == null)
                {
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(HistorianPage<HistoricalDataValue>.Empty);
                }

                return new ValueTask<HistorianPage<HistoricalDataValue>>(
                    ReadRawPage(values, request, DecodeTimestamp(resumeToken)));
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reading the raw history of {NodeId}.", request.NodeId);
                return new ValueTask<HistorianPage<HistoricalDataValue>>(HistorianPage<HistoricalDataValue>.Empty);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAsync(HistorianOperationContext context, NodeId nodeId, IList<DataValue> values, CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadHistoryOperationUnsupported, values.Count));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAsync(HistorianOperationContext context, NodeId nodeId, IList<DataValue> values, CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadHistoryOperationUnsupported, values.Count));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAsync(HistorianOperationContext context, NodeId nodeId, IList<DataValue> values, CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadHistoryOperationUnsupported, values.Count));
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> DeleteRawAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            bool isDeleteModified,
            CancellationToken ct)
        {
            return new ValueTask<StatusCode>((StatusCode)StatusCodes.BadHistoryOperationUnsupported);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> timestamps,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadHistoryOperationUnsupported, timestamps.Count));
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Builds one page of a raw read over the archived samples, which are
        /// sorted by source timestamp.
        /// </summary>
        /// <remarks>
        /// The window is normalised by the framework: the effective start is the
        /// earlier time, one-sided requests carry a MinValue or MaxValue sentinel,
        /// and <see cref="HistorianRawReadRequest.IsForward"/> holds the direction.
        /// A forward read returns the samples in [start, end) so the sample at the
        /// end becomes the bound; a reverse read mirrors that to (start, end].
        /// The source timestamp is the storage key, so the resume token is simply
        /// the timestamp the previous page ended at.
        /// </remarks>
        private static HistorianPage<HistoricalDataValue> ReadRawPage(
            IReadOnlyList<DataValue> view,
            HistorianRawReadRequest request,
            DateTime resumeAt)
        {
            DateTime windowStart = (DateTime)request.StartTime;
            DateTime windowEnd = (DateTime)request.EndTime;
            bool hasStart = request.StartTime != DateTimeUtc.MinValue;
            bool hasEnd = request.EndTime != DateTimeUtc.MaxValue;
            bool firstPage = resumeAt == DateTime.MinValue;

            // a one-sided request is capped by the requested count alone, so a full
            // page is the end of it rather than a reason to continue.
            bool sizeLimited = request.MaxValues != 0 && (!hasStart || !hasEnd);
            uint pageSize = request.MaxValues != 0 ? request.MaxValues : kDefaultPageSize;

            List<HistoricalDataValue> values = new List<HistoricalDataValue>();

            // a read at a single point in time returns the sample recorded there,
            // and with bounds requested the samples on each side of the instant.
            if (hasStart && hasEnd && windowStart == windowEnd)
            {
                int exact = Find(view, windowStart);

                if (exact >= 0)
                {
                    if (firstPage)
                    {
                        values.Add(new HistoricalDataValue(view[exact], request.ReturnBounds));

                        if (request.ReturnBounds && request.MaxValues != 1)
                        {
                            values.Add(FindBoundAfter(view, windowStart));
                        }
                    }

                    return new HistorianPage<HistoricalDataValue>(values);
                }

                if (!request.ReturnBounds)
                {
                    return new HistorianPage<HistoricalDataValue>(values);
                }

                // no sample at the instant: fall through, so the read answers with
                // the bound on each side of it.
            }

            DateTime lastReturned = DateTime.MinValue;
            bool full = false;

            if (request.IsForward)
            {
                // the bound before the window, unless a sample sits exactly on its edge.
                if (firstPage && request.ReturnBounds && hasStart && Find(view, windowStart) < 0)
                {
                    HistoricalDataValue bound = FindBoundBefore(view, windowStart);
                    values.Add(bound);
                    lastReturned = (DateTime)bound.Value.SourceTimestamp;
                    full = values.Count >= pageSize;
                }

                for (int ii = 0; ii < view.Count; ii++)
                {
                    DateTime timestamp = (DateTime)view[ii].SourceTimestamp;

                    if (timestamp < windowStart || (!firstPage && timestamp <= resumeAt))
                    {
                        continue;
                    }

                    if (hasEnd && timestamp >= windowEnd)
                    {
                        if (request.ReturnBounds)
                        {
                            if (full)
                            {
                                return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                            }

                            values.Add(new HistoricalDataValue(view[ii], true));
                        }

                        return new HistorianPage<HistoricalDataValue>(values);
                    }

                    if (full)
                    {
                        return sizeLimited
                            ? new HistorianPage<HistoricalDataValue>(values)
                            : new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                    }

                    values.Add(new HistoricalDataValue(view[ii]));
                    lastReturned = timestamp;
                    full = values.Count >= pageSize;
                }

                // the data ran out inside the window, so the bound at the end is missing.
                if (request.ReturnBounds && hasEnd)
                {
                    if (full)
                    {
                        return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                    }

                    values.Add(CreateMissingBound(windowEnd));
                }

                return new HistorianPage<HistoricalDataValue>(values);
            }

            // reverse: iterate the sorted samples from the back.
            if (firstPage && request.ReturnBounds && hasEnd && Find(view, windowEnd) < 0)
            {
                HistoricalDataValue bound = FindBoundAfter(view, windowEnd);
                values.Add(bound);
                lastReturned = (DateTime)bound.Value.SourceTimestamp;
                full = values.Count >= pageSize;
            }

            for (int ii = view.Count - 1; ii >= 0; ii--)
            {
                DateTime timestamp = (DateTime)view[ii].SourceTimestamp;

                if (timestamp > windowEnd || (!firstPage && timestamp >= resumeAt))
                {
                    continue;
                }

                if (hasStart && timestamp <= windowStart)
                {
                    if (request.ReturnBounds)
                    {
                        if (full)
                        {
                            return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                        }

                        values.Add(new HistoricalDataValue(view[ii], true));
                    }

                    return new HistorianPage<HistoricalDataValue>(values);
                }

                if (full)
                {
                    return sizeLimited
                        ? new HistorianPage<HistoricalDataValue>(values)
                        : new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                }

                values.Add(new HistoricalDataValue(view[ii]));
                lastReturned = timestamp;
                full = values.Count >= pageSize;
            }

            if (request.ReturnBounds && hasStart)
            {
                if (full)
                {
                    return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                }

                values.Add(CreateMissingBound(windowStart));
            }

            return new HistorianPage<HistoricalDataValue>(values);
        }

        /// <summary>
        /// Returns the index of the sample recorded exactly at the timestamp, or -1.
        /// </summary>
        private static int Find(IReadOnlyList<DataValue> view, DateTime timestamp)
        {
            for (int ii = 0; ii < view.Count; ii++)
            {
                if ((DateTime)view[ii].SourceTimestamp == timestamp)
                {
                    return ii;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns the last sample before the timestamp as a bound, or the marker
        /// for a bound the archive cannot supply.
        /// </summary>
        private static HistoricalDataValue FindBoundBefore(IReadOnlyList<DataValue> view, DateTime timestamp)
        {
            for (int ii = view.Count - 1; ii >= 0; ii--)
            {
                if ((DateTime)view[ii].SourceTimestamp < timestamp)
                {
                    return new HistoricalDataValue(view[ii], true);
                }
            }

            return CreateMissingBound(timestamp);
        }

        /// <summary>
        /// Returns the first sample after the timestamp as a bound, or the marker
        /// for a bound the archive cannot supply.
        /// </summary>
        private static HistoricalDataValue FindBoundAfter(IReadOnlyList<DataValue> view, DateTime timestamp)
        {
            for (int ii = 0; ii < view.Count; ii++)
            {
                if ((DateTime)view[ii].SourceTimestamp > timestamp)
                {
                    return new HistoricalDataValue(view[ii], true);
                }
            }

            return CreateMissingBound(timestamp);
        }

        /// <summary>
        /// Creates the placeholder for a requested bound the archive cannot supply.
        /// </summary>
        private static HistoricalDataValue CreateMissingBound(DateTime timestamp)
        {
            return new HistoricalDataValue(
                new DataValue(Variant.Null, StatusCodes.BadBoundNotFound, timestamp, timestamp),
                true);
        }

        /// <summary>
        /// Encodes the timestamp a raw page ended at into the resume token the
        /// framework hands back for the next page.
        /// </summary>
        private static HistorianResumeToken EncodeTimestamp(DateTime timestamp)
        {
            byte[] state = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(state, timestamp.Ticks);
            return new HistorianResumeToken(state);
        }

        /// <summary>
        /// Decodes the timestamp the previous raw page ended at; MinValue on the
        /// first page.
        /// </summary>
        private static DateTime DecodeTimestamp(HistorianResumeToken token)
        {
            if (token.IsEmpty || token.State.Length != 8)
            {
                return DateTime.MinValue;
            }

            return new DateTime(BinaryPrimitives.ReadInt64BigEndian(token.State.Span), DateTimeKind.Utc);
        }
        #endregion

        #region Private Fields
        private const uint kDefaultPageSize = 1000;

        private readonly TestDataSystem m_system;
        private readonly ILogger m_logger;
        #endregion
    }
}
