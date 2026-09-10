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
    /// The payload of <see cref="HistoricalAccessClientModel.AuditEventReceived"/>.
    /// </summary>
    public sealed class AuditEventReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public AuditEventReceivedEventArgs(HistoryAuditRecord record)
        {
            Record = record;
        }

        /// <summary>
        /// What the server reported about the update.
        /// </summary>
        public HistoryAuditRecord Record { get; }
    }

    /// <summary>
    /// The client model of the HistoricalAccess client: the half of the sample which talks
    /// OPC UA without a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window of this sample is thin by design: the session goes straight into the
    /// shared history control, which owns its historian calls. What the model adds is the
    /// selection the window makes and the questions the control answers for itself
    /// before it reads - whether a node has history at all, how its history is configured,
    /// what the server can do with history, and what a raw or a modified read of it
    /// returns - through the same <see cref="HistoryClient"/> the control uses, so that
    /// the logic can be exercised without the control.
    /// </para>
    /// <para>
    /// The history client of the SDK follows the continuation points a read leaves behind
    /// on its own, so a caller enumerates one stream per time window; leaving the
    /// enumeration early is what releases the continuation point still open.
    /// </para>
    /// <para>
    /// The model can also watch the audit trail of the server: every history update
    /// the server accepts or refuses is reported as an audit event, with the values it
    /// displaced, and <see cref="AuditEventReceived"/> hands each one on.
    /// </para>
    /// </remarks>
    public sealed class HistoricalAccessClientModel : SampleClientModel
    {
        private AuditEventStream m_auditStream;

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
        /// Whether the audit events of the server are streamed while a session is attached.
        /// </summary>
        public bool IsWatchingAuditEvents { get; private set; }

        /// <summary>
        /// Raised for every history update the server audited, live or refused.
        /// </summary>
        public event EventHandler<AuditEventReceivedEventArgs> AuditEventReceived;

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
        /// Reads what the server says it can do with history.
        /// </summary>
        /// <remarks>
        /// The HistoryServerCapabilities object is the roll-up of every historian
        /// provider the server registered; a client asks once and shapes what it
        /// offers around the answer.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<HistoryServerCapabilitiesInfo> ReadCapabilitiesAsync(CancellationToken ct = default)
        {
            return await RequireSession().Historian().GetServerCapabilitiesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads how the history of a variable is recorded.
        /// </summary>
        /// <remarks>
        /// The HistoricalDataConfiguration object beside a variable says whether its
        /// values are stepped, how often they are sampled and where the archive starts.
        /// Part 11 leaves the object optional, so a variable without one answers with
        /// <c>HasConfiguration</c> false rather than an error.
        /// </remarks>
        /// <param name="nodeId">The variable.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<HistoricalDataConfigurationInfo> ReadConfigurationAsync(NodeId nodeId, CancellationToken ct = default)
        {
            return await RequireSession().Historian().GetConfigurationAsync(nodeId, ct).ConfigureAwait(false);
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
                cancellationToken: ct).ConfigureAwait(false))
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

        /// <summary>
        /// Reads the modified history of a variable in a time window: the values a
        /// replace or a delete displaced, each with what was done to it, when and by
        /// whom.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="startTime">The start of the window, inclusive; MinValue for an open start.</param>
        /// <param name="endTime">The end of the window, exclusive; MinValue for an open end.</param>
        /// <param name="maxValues">How many values to return at most; zero for the whole window.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The modified values with their modification info, in the order the server returns them.</returns>
        public async Task<IReadOnlyList<ModifiedHistoryValue>> ReadModifiedAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValues = 0,
            CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var values = new List<ModifiedHistoryValue>();

            // the history client keeps the modification info beside each value, which
            // is the whole point of reading modified history
            await foreach (ModifiedHistoryValue value in session.Historian().ReadModifiedAsync(
                nodeId,
                startTime,
                endTime,
                maxValues,
                TimestampsToReturn.Both,
                cancellationToken: ct).ConfigureAwait(false))
            {
                values.Add(value);

                if (maxValues > 0 && values.Count >= maxValues)
                {
                    break;
                }
            }

            return values;
        }

        /// <summary>
        /// Starts or stops watching the audit events the server raises for history
        /// updates.
        /// </summary>
        /// <remarks>
        /// The choice is remembered while detached and applied when a session is attached.
        /// </remarks>
        /// <param name="watch">Whether to stream the audit events.</param>
        public async Task SetWatchingAuditEventsAsync(bool watch)
        {
            if (IsWatchingAuditEvents == watch)
            {
                return;
            }

            IsWatchingAuditEvents = watch;

            if (!IsConnected)
            {
                return;
            }

            if (watch)
            {
                await StartAuditStreamAsync().ConfigureAwait(false);
            }
            else
            {
                await StopAuditStreamAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            if (IsWatchingAuditEvents)
            {
                await StartAuditStreamAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            SelectedNodeId = NodeId.Null;

            // done before the session is closed: the stream is ended and its subscription
            // deleted while the session can still do that.
            await StopAuditStreamAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Starts streaming the audit events, creating the stream on first use.
        /// </summary>
        private async Task StartAuditStreamAsync()
        {
            m_auditStream ??= new AuditEventStream(RequireSession(), OnAuditEventAsync, ReportError);

            await m_auditStream.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Ends the stream and deletes its subscription.
        /// </summary>
        private async Task StopAuditStreamAsync()
        {
            AuditEventStream stream = m_auditStream;

            m_auditStream = null;

            if (stream != null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports an audit event. Runs on the pump, one event at a time.
        /// </summary>
        private Task OnAuditEventAsync(HistoryAuditRecord record, CancellationToken ct)
        {
            Raise(AuditEventReceived, new AuditEventReceivedEventArgs(record));

            return Task.CompletedTask;
        }
    }
}
