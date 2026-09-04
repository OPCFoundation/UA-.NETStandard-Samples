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
using NUnit.Framework;
using Quickstarts.HistoricalAccess.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The model of the HistoricalAccess client, driven the way its window drives it.
    /// </summary>
    /// <remarks>
    /// The namespace constant is the one of the server assembly: the client of this sample
    /// has no generated model types, so there is no second definition to collide with.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class HistoricalAccessClientModelTests : ClientModelFixtureBase<HistoricalAccessClientModel>
    {
        /// <summary>
        /// A window which covers the whole archive, whenever it was recorded.
        /// </summary>
        private static readonly DateTime kArchiveStart = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime kArchiveEnd = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        protected override string SampleName => "HistoricalAccess";

        protected override HistoricalAccessClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new HistoricalAccessClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheArchiveItemIsHistorizedAndALiveVariableIsNot(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct).ConfigureAwait(false);

            bool archive = await Model.IsHistorizedAsync(item, ct).ConfigureAwait(false);
            bool live = await Model.IsHistorizedAsync(VariableIds.Server_ServerStatus_CurrentTime, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(archive, Is.True, "The Double item of the sample archive allows reading its history.");
                Assert.That(live, Is.False, "The server time has no history; a client which reads it has to subscribe.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task RawReadReturnsTheRecordedValuesInOrder(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await Model
                .ReadRawAsync(item, kArchiveStart, kArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The model read {values.Count} values of {item}.")
                .ConfigureAwait(false);

            Assert.That(values, Is.Not.Empty, "The sample archive has values in it.");

            DateTime[] timestamps = values.Select(At).ToArray();

            Assert.Multiple(() => {
                Assert.That(timestamps, Is.Ordered, "A raw read returns the values in the order they were recorded.");

                Assert.That(
                    values.Select(value => value.StatusCode),
                    Has.None.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData),
                    "BadWaitingForInitialData is what a monitored item reports before its first " +
                    "data change, so it means the values came from a subscription rather than the archive.");
            });

            // the same window read with the raw service, page by page
            IReadOnlyList<DataValue> expected = await HistoryOps
                .ReadAllRawAsync(Session, item, kArchiveStart, kArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            Assert.That(
                timestamps,
                Is.EqualTo(expected.Select(At)),
                "The history client of the SDK has to return exactly what the raw service returns.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task RawReadStopsAtTheRequestedCount(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> first = await Model
                .ReadRawAsync(item, kArchiveStart, kArchiveEnd, 3, ct)
                .ConfigureAwait(false);

            Assert.That(first, Has.Count.EqualTo(3), "A bounded read returns no more than it was asked for.");

            // and leaving the enumeration early released the continuation point, so the
            // session can read the whole archive again right away
            IReadOnlyList<DataValue> all = await Model
                .ReadRawAsync(item, kArchiveStart, kArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            Assert.That(
                all.Take(3).Select(At),
                Is.EqualTo(first.Select(At)),
                "The bounded read has to return the first values of the window.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheSelectionFollowsTheSessionLifetime(CancellationToken ct)
        {
            Assert.That(Model.SelectedNodeId.IsNull, Is.True, "A new model already has a selection.");

            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct).ConfigureAwait(false);

            Model.SelectNode(item);

            Assert.That(Model.SelectedNodeId, Is.EqualTo(item));

            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.SelectedNodeId.IsNull, Is.True, "A detached model still holds a selection.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public void ReadingBeforeTheAttachIsRefused()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                () => Model.ReadRawAsync(VariableIds.Server_ServerStatus_CurrentTime, kArchiveStart, kArchiveEnd),
                "A detached model has no session to read on.");
        }

        /// <summary>
        /// The Double item of the fixed archive, the one the aggregates are meaningful for.
        /// </summary>
        private async Task<NodeId> ArchiveItemAsync(CancellationToken ct)
        {
            var sampleFolder = new NodeId(
                "Sample",
                NamespaceIndex(Quickstarts.HistoricalAccessServer.Namespaces.HistoricalAccess));

            IReadOnlyList<ReferenceDescription> items = await SessionOps
                .BrowseAsync(Session, sampleFolder, ct)
                .ConfigureAwait(false);

            ReferenceDescription item = items.FirstOrDefault(child =>
                child.NodeClass == NodeClass.Variable
                && child.BrowseName.Name.Contains("Double", StringComparison.Ordinal));

            Assert.That(
                item,
                Is.Not.Null,
                "The Sample folder of the archive holds no Double item. It holds: " +
                string.Join(", ", items.Select(child => child.BrowseName.Name)));

            return ExpandedNodeId.ToNodeId(item.NodeId, Session.NamespaceUris);
        }

        private static DateTime At(DataValue value)
        {
            return (DateTime)value.SourceTimestamp;
        }
    }
}
