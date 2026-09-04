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
using Opc.Ua.Client.Historian;
using Opc.Ua.Samples.Client;

namespace Quickstarts.HistoricalAccess.Client.Model
{
    /// <summary>
    /// The client model of the HistoricalAccess client: the half of the sample which talks
    /// OPC UA without a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window of this sample is thin by design: the session goes straight into the
    /// shared history control, which owns its historian calls. What the model adds is the
    /// selection the window makes and the two questions the control answers for itself
    /// before it reads - whether a node has history at all, and what a raw read of it
    /// returns - through the same <see cref="HistoryClient"/> the control uses, so that the
    /// logic can be exercised without the control.
    /// </para>
    /// <para>
    /// The history client of the SDK follows the continuation points a read leaves behind
    /// on its own, so a caller enumerates one stream per time window; leaving the
    /// enumeration early is what releases the continuation point still open.
    /// </para>
    /// </remarks>
    public sealed class HistoricalAccessClientModel : SampleClientModel
    {
        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public HistoricalAccessClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The variable the user picked to read the history of, or null.
        /// </summary>
        public NodeId SelectedNodeId { get; private set; } = NodeId.Null;

        /// <summary>
        /// Remembers the variable the user picked.
        /// </summary>
        /// <param name="nodeId">The variable, or <see cref="NodeId.Null"/> to clear the selection.</param>
        public void SelectNode(NodeId nodeId)
        {
            SelectedNodeId = nodeId;
        }

        /// <summary>
        /// Whether the history of a variable can be read.
        /// </summary>
        /// <remarks>
        /// Decided from the HistoryRead bit of the AccessLevel attribute, the way the history
        /// control decides whether to read history or to fall back to a subscription. The
        /// Historizing attribute is deliberately not consulted: it says whether the server
        /// is still collecting, and a finished recording is readable without it.
        /// </remarks>
        /// <param name="nodeId">The variable.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<bool> IsHistorizedAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.AccessLevel },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, ct)
                .ConfigureAwait(false);

            DataValue accessLevel = response.Results.ToList()[0];

            return StatusCode.IsGood(accessLevel.StatusCode) &&
                accessLevel.WrappedValue.TryGetValue(out byte flags) &&
                (flags & AccessLevels.HistoryRead) != 0;
        }

        /// <summary>
        /// Reads the raw history of a variable in a time window.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="startTime">The start of the window, inclusive; MinValue for an open start.</param>
        /// <param name="endTime">The end of the window, exclusive; MinValue for an open end.</param>
        /// <param name="maxValues">How many values to return at most; zero for the whole window.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The recorded values, in the order the server returns them.</returns>
        public async Task<IReadOnlyList<DataValue>> ReadRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValues = 0,
            CancellationToken ct = default)
        {
            ISession session = RequireSession();

            // the history services of the session, as one object: it builds the read
            // details and follows the continuation points a read leaves behind
            HistoryClient historian = session.Historian();

            var values = new List<DataValue>();

            await foreach (DataValue value in historian.ReadRawAsync(
                nodeId,
                startTime,
                endTime,
                maxValues,
                returnBounds: false,
                TimestampsToReturn.Both,
                ct).ConfigureAwait(false))
            {
                values.Add(value);

                // leaving the loop is what releases the continuation point the server
                // opened for the rest of the window
                if (maxValues > 0 && values.Count >= maxValues)
                {
                    break;
                }
            }

            return values;
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            SelectedNodeId = NodeId.Null;

            return Task.CompletedTask;
        }
    }
}
