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
using Opc.Ua.Client.Historian;
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

        /// <summary>
        /// Points in time well before the archive starts, for the tests which write;
        /// one per test, because inserting where another test inserted is refused.
        /// </summary>
        private static readonly DateTime kModifiedAt = new(1991, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime kAuditedAt = new(1991, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        protected override string SampleName => "HistoricalAccess";

        /// <summary>
        /// The sessions are opened on an encrypted endpoint: a server delivers audit
        /// events only to those, and watching them is one of the things the model does.
        /// </summary>
        protected override bool UseSecurity => true;

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

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ModifiedHistoryCarriesWhatWasDoneToEachValue(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct, "Float").ConfigureAwait(false);

            // a value of our own, inserted and deleted again, so the modified history
            // of the item holds two records at its timestamp
            (StatusCode inserted, IReadOnlyList<StatusCode> perInserted) = await HistoryOps
                .UpdateDataAsync(Session, item, PerformUpdateType.Insert, [new DataValue(Variant.From(1.0f), StatusCodes.Good, kModifiedAt, kModifiedAt)], ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(inserted) && perInserted.All(StatusCode.IsGood), Is.True, $"Inserting failed: {inserted}");

            await HistoryOps.DeleteAtTimeAsync(Session, item, [kModifiedAt], ct).ConfigureAwait(false);

            IReadOnlyList<ModifiedHistoryValue> modified = await Model
                .ReadModifiedAsync(item, kArchiveStart, kArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            List<ModifiedHistoryValue> ofOurs = modified.Where(entry => At(entry.Value) == kModifiedAt).ToList();

            await TestContext.Out
                .WriteLineAsync(
                    "Modified history at our timestamp: " +
                    string.Join(" | ", ofOurs.Select(entry => $"{entry.Info.UpdateType} at {entry.Info.ModificationTime:O} by {entry.Info.UserName}")))
                .ConfigureAwait(false);

            Assert.That(ofOurs, Has.Count.EqualTo(2), "The insert and the delete both left a record.");

            Assert.Multiple(() => {
                Assert.That(
                    ofOurs.Select(entry => entry.Info.UpdateType),
                    Is.EquivalentTo(new[] { HistoryUpdateType.Insert, HistoryUpdateType.Delete }),
                    "The history client keeps the modification info beside each value.");

                Assert.That(
                    ofOurs.Select(entry => (DateTime)entry.Info.ModificationTime),
                    Is.All.GreaterThan(DateTime.UtcNow.AddMinutes(-5)),
                    "The modification time is when the records were written.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheConfigurationOfAnArchiveItemIsRead(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct).ConfigureAwait(false);

            HistoricalDataConfigurationInfo configuration = await Model.ReadConfigurationAsync(item, ct).ConfigureAwait(false);
            HistoricalDataConfigurationInfo none = await Model.ReadConfigurationAsync(VariableIds.Server_ServerStatus_CurrentTime, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Configuration of the Double item: stepped {configuration.Stepped}, " +
                    $"min interval {configuration.MinTimeInterval}, start of archive {configuration.StartOfArchive:O}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(configuration.HasConfiguration, Is.True, "An archive item carries the companion object the SDK installed for it.");
                Assert.That(configuration.Stepped, Is.Not.Null, "The companion object says whether the item is stepped.");
                Assert.That(configuration.MinTimeInterval, Is.GreaterThan(0), "The companion object says how often the item was sampled.");
                Assert.That(configuration.StartOfArchive, Is.Not.Null, "The companion object says where the archive starts.");
                Assert.That(none.HasConfiguration, Is.False, "A variable without history has no companion object, and that is not an error.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheServerSaysWhatItCanDoWithHistory(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            HistoryServerCapabilitiesInfo capabilities = await Model.ReadCapabilitiesAsync(ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Capabilities: {capabilities}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(capabilities.AccessHistoryData, Is.True, "The server serves data history.");
                Assert.That(capabilities.InsertData, Is.True, "The archive accepts inserts.");
                Assert.That(capabilities.DeleteAtTime, Is.True, "The archive accepts deletes at a time.");
                Assert.That(capabilities.InsertAnnotation, Is.True, "The archive accepts annotations.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AuditEventsArriveWhileWatching(CancellationToken ct)
        {
            var audits = new EventSink<AuditEventReceivedEventArgs>();
            Model.AuditEventReceived += audits.Handle;

            // the choice is made before there is a session, the way the menu of the
            // window can be checked before connecting, and applied on attach
            await Model.SetWatchingAuditEventsAsync(true).ConfigureAwait(false);

            Assert.That(Model.IsWatchingAuditEvents, Is.True);

            await AttachAsync(ct).ConfigureAwait(false);

            NodeId item = await ArchiveItemAsync(ct, "Float").ConfigureAwait(false);

            (StatusCode inserted, IReadOnlyList<StatusCode> perInserted) = await HistoryOps
                .UpdateDataAsync(Session, item, PerformUpdateType.Insert, [new DataValue(Variant.From(3.0f), StatusCodes.Good, kAuditedAt, kAuditedAt)], ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(inserted) && perInserted.All(StatusCode.IsGood), Is.True, $"Inserting failed: {inserted}");

            AuditEventReceivedEventArgs audited = await audits
                .WaitForAsync(
                    candidate => candidate.Record.UpdatedNode == item && candidate.Record.PerformInsertReplace == PerformUpdateType.Insert,
                    "no audit event of the insert arrived",
                    TimeSpan.FromSeconds(20),
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Audited: {audited.Record}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(audited.Record.EventType, Is.EqualTo(ObjectTypeIds.AuditHistoryValueUpdateEventType), "A value update is audited as such.");
                Assert.That(audited.Record.Succeeded, Is.True, "The server accepted the insert.");
                Assert.That(audited.Record.NewValueCount, Is.EqualTo(1), "The audit event carries the value which was written.");
                Assert.That(audited.Record.OldValueCount, Is.EqualTo(0), "An insert displaces nothing.");
            });

            await Model.SetWatchingAuditEventsAsync(false).ConfigureAwait(false);

            Assert.That(Model.IsWatchingAuditEvents, Is.False);
        }

        /// <summary>
        /// An item of the fixed archive: the Double one by default, the one the
        /// aggregates are meaningful for; the Float one for the tests which write,
        /// so the recorded archive the reading tests measure stays as it is.
        /// </summary>
        private async Task<NodeId> ArchiveItemAsync(CancellationToken ct, string typeName = "Double")
        {
            var sampleFolder = new NodeId(
                "Sample",
                NamespaceIndex(Quickstarts.HistoricalAccessServer.Namespaces.HistoricalAccess));

            IReadOnlyList<ReferenceDescription> items = await SessionOps
                .BrowseAsync(Session, sampleFolder, ct)
                .ConfigureAwait(false);

            ReferenceDescription item = items.FirstOrDefault(child =>
                child.NodeClass == NodeClass.Variable
                && child.BrowseName.Name.Contains(typeName, StringComparison.Ordinal));

            Assert.That(
                item,
                Is.Not.Null,
                $"The Sample folder of the archive holds no {typeName} item. It holds: " +
                string.Join(", ", items.Select(child => child.BrowseName.Name)));

            return ExpandedNodeId.ToNodeId(item.NodeId, Session.NamespaceUris);
        }

        private static DateTime At(DataValue value)
        {
            return (DateTime)value.SourceTimestamp;
        }
    }
}
