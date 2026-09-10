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

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the historical access sample does: it serves an archive of recorded values and
    /// implements the history services over it.
    /// </summary>
    /// <remarks>
    /// This is the largest of the sample node managers, and almost none of what it does can
    /// be reached with an ordinary read: raw reads, aggregates, reads at a point in time,
    /// inserting and deleting, and the continuation points which hold a long read together
    /// are all separate service calls. Each of them is a branch a migration can break on
    /// its own, so each has a test.
    ///
    /// The Sample folder is a fixed archive which is loaded from resources, so tests may
    /// read it as often as they like. The tests which change history work on their own
    /// timestamps, far away from where the archive has data, so they do not disturb it.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class HistoricalAccessNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "HistoricalAccess";

        private const string HistoryNamespace = Quickstarts.HistoricalAccessServer.Namespaces.HistoricalAccess;

        /// <summary>
        /// The archive is browsable and its items say that they are historized.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ArchiveIsBrowsableAndItemsAreHistorized(CancellationToken ct)
        {
            IReadOnlyList<string> folders = await BrowseNamesAsync(SampleFolder, ct).ConfigureAwait(false);

            await ReportAsync("Sample folder", folders).ConfigureAwait(false);

            Assert.That(folders, Is.Not.Empty, "The sample archive folder is empty.");

            Assert.That(
                folders,
                Does.Contain("Double").And.Contain("Int32").And.Contain("String"),
                "The archive holds one item per data type.");

            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            DataValue accessLevel = await SessionOps
                .ReadAttributeAsync(Session, item, Attributes.AccessLevel, ct)
                .ConfigureAwait(false);

            Assert.That(
                (accessLevel.WrappedValue.TryGetValue(out byte flags) ? flags : (byte)0)
                    & AccessLevels.HistoryRead,
                Is.Not.Zero,
                "An archive item has to allow reading its history.");
        }

        /// <summary>
        /// Only the items which are still being collected report that they are historized.
        /// </summary>
        /// <remarks>
        /// Historizing says whether the server is currently collecting history for a node,
        /// not whether history exists for it - that is what the history bit of the access
        /// level is for. The two folders of this archive make the distinction visible: the
        /// dynamic items are being appended to and say so, the sample items are a recording
        /// which nothing is adding to any more, and both can be read.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OnlyTheItemsStillBeingCollectedAreHistorizing(CancellationToken ct)
        {
            NodeId recorded = await ResolveSampleItemAsync(ct).ConfigureAwait(false);
            NodeId collecting = await ResolveDynamicItemAsync(ct).ConfigureAwait(false);

            DataValue onRecorded = await SessionOps
                .ReadAttributeAsync(Session, recorded, Attributes.Historizing, ct)
                .ConfigureAwait(false);

            DataValue onCollecting = await SessionOps
                .ReadAttributeAsync(Session, collecting, Attributes.Historizing, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Historizing: sample archive {onRecorded.WrappedValue}, " +
                    $"dynamic archive {onCollecting.WrappedValue}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    onCollecting.WrappedValue.TryGetValue(out bool collecting) && collecting,
                    Is.True,
                    "An item the simulation appends to is being collected.");

                Assert.That(
                    onRecorded.WrappedValue.TryGetValue(out bool recorded) ? recorded : (bool?)null,
                    Is.False,
                    "A finished recording is not being collected, even though it can be read.");
            });
        }

        /// <summary>
        /// The server reports which parts of the history profile it supports.
        /// </summary>
        /// <remarks>
        /// The node manager turns these on itself while it builds the address space, and a
        /// client uses them to decide what it may ask for. Claiming a capability the
        /// sample does not have would be as much of a regression as losing one.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task HistoryCapabilitiesAreAnnounced(CancellationToken ct)
        {
            (string Name, NodeId NodeId)[] capabilities = [
                ("AccessHistoryDataCapability", VariableIds.HistoryServerCapabilities_AccessHistoryDataCapability),
                ("InsertDataCapability", VariableIds.HistoryServerCapabilities_InsertDataCapability),
                ("ReplaceDataCapability", VariableIds.HistoryServerCapabilities_ReplaceDataCapability),
                ("UpdateDataCapability", VariableIds.HistoryServerCapabilities_UpdateDataCapability),
                ("DeleteRawCapability", VariableIds.HistoryServerCapabilities_DeleteRawCapability),
                ("DeleteAtTimeCapability", VariableIds.HistoryServerCapabilities_DeleteAtTimeCapability),
                ("InsertAnnotationCapability", VariableIds.HistoryServerCapabilities_InsertAnnotationCapability),
            ];

            var announced = new List<string>();

            foreach ((string name, NodeId nodeId) in capabilities)
            {
                DataValue value = await SessionOps
                    .ReadValueAsync(Session, nodeId, ct)
                    .ConfigureAwait(false);

                announced.Add($"{name}={value.WrappedValue}");

                Assert.That(
                    value.WrappedValue.TryGetValue(out bool announcedValue) && announcedValue,
                    Is.True,
                    $"The sample turns {name} on while it builds its address space.");
            }

            await ReportAsync("History capabilities", announced).ConfigureAwait(false);
        }

        /// <summary>
        /// An archive item carries the companion object which describes how its
        /// history was recorded.
        /// </summary>
        /// <remarks>
        /// Part 11 §5.2.3 hangs a HistoricalDataConfigurationType object off the
        /// variable with a HasHistoricalConfiguration reference, and clients decide
        /// what to offer from it: the shared history control of this repository reads
        /// Stepped through exactly this browse path and falls back to a live
        /// subscription for a node which does not answer, so an item whose companion
        /// object is unreachable silently loses every history feature in the UI.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ItemsCarryTheirHistoricalConfiguration(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            NodeId stepped = await ResolveFromAsync(
                item,
                ct,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                new QualifiedName(Opc.Ua.BrowseNames.Stepped)).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"HA Configuration/Stepped of the double item: {stepped}")
                .ConfigureAwait(false);

            Assert.That(
                stepped,
                Is.Not.EqualTo(NodeId.Null),
                "An archive item has to expose HA Configuration/Stepped, or a client cannot " +
                "tell that its history is worth reading.");

            DataValue value = await SessionOps.ReadValueAsync(Session, stepped, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Stepped = {value.WrappedValue} ({value.StatusCode})")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading Stepped of an archive item failed: {value.StatusCode}");
        }

        /// <summary>
        /// A raw read returns the recorded values in order.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RawReadReturnsRecordedValuesInOrder(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The archive holds {values.Count} values for {item}.")
                .ConfigureAwait(false);

            Assert.That(values, Is.Not.Empty, "The sample archive has no values in it.");

            DateTime[] timestamps = values.Select(At).ToArray();

            Assert.That(
                timestamps,
                Is.Ordered,
                "A raw read has to return the values in the order they were recorded.");
        }

        /// <summary>
        /// A read which asks for fewer values than there are is continued.
        /// </summary>
        /// <remarks>
        /// The continuation point is the part of the history services which is easiest to
        /// get subtly wrong, so this walks the whole archive in steps of three and checks
        /// that the result is the same as reading it in one go, with nothing repeated and
        /// nothing lost.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadIsContinuedInStepsWithoutLosingValues(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> inOneGo = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            Assert.That(inOneGo.Count, Is.GreaterThan(3), "The archive is too small for this test.");

            HistoryReadOutcome first = await HistoryOps
                .ReadRawAsync(Session, item, ArchiveStart, ArchiveEnd, 3, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(first.Values, Has.Count.EqualTo(3), "The server has to honour the requested count.");
                Assert.That(first.HasMore, Is.True, "The server has more values, so it has to continue.");
            });

            IReadOnlyList<DataValue> inSteps = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 3, ct)
                .ConfigureAwait(false);

            Assert.That(
                inSteps.Select(At),
                Is.EqualTo(inOneGo.Select(At)),
                "Reading in steps has to return exactly what reading in one go returns.");
        }

        /// <summary>
        /// A continuation point which was released cannot be used again.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReleasedContinuationPointIsRejected(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            HistoryReadOutcome first = await HistoryOps
                .ReadRawAsync(Session, item, ArchiveStart, ArchiveEnd, 2, ct)
                .ConfigureAwait(false);

            Assert.That(first.HasMore, Is.True, "The archive is too small for this test.");

            StatusCode released = await HistoryOps
                .ReleaseAsync(Session, item, first.ContinuationPoint, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(released),
                Is.True,
                $"Releasing a continuation point failed: {released}");

            HistoryReadOutcome reused = await HistoryOps
                .ReadRawAsync(Session, item, ArchiveStart, ArchiveEnd, 2, ct,
                    continuationPoint: first.ContinuationPoint)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Reusing a released continuation point: {reused.StatusCode}")
                .ConfigureAwait(false);

            Assert.That(
                reused.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadContinuationPointInvalid),
                "A continuation point which was released must not be usable again.");
        }

        /// <summary>
        /// Reading at a recorded point in time returns the value recorded there.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadAtTimeReturnsTheRecordedValue(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            DataValue expected = values[values.Count / 2];

            HistoryReadOutcome outcome = await HistoryOps
                .ReadAtTimeAsync(Session, item, [At(expected)], ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(outcome.StatusCode),
                Is.True,
                $"Reading at a point in time failed: {outcome.StatusCode}");

            Assert.That(outcome.Values, Has.Count.EqualTo(1), "One time asked for, one value expected.");

            await TestContext.Out
                .WriteLineAsync(
                    $"At {At(expected):O} the archive holds {expected.WrappedValue}, " +
                    $"a read at that time returns {outcome.Values[0].WrappedValue} " +
                    $"({outcome.Values[0].StatusCode})")
                .ConfigureAwait(false);

            Assert.That(
                At(outcome.Values[0]),
                Is.EqualTo(At(expected)),
                "The value has to carry the timestamp which was asked for.");
        }

        /// <summary>
        /// A read at a recorded time returns the value which was recorded there.
        /// </summary>
        /// <remarks>
        /// ReadAtTimeReturnsTheRecordedValue pins the shape of the answer down - one
        /// value per requested time, stamped correctly - and this pins its payload
        /// down. The value asked for is one the archive recorded as good, because the
        /// archive also holds deliberately bad values and a read at their time
        /// faithfully returns the recorded bad status.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadAtTimeCarriesTheValue(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            DataValue expected = values.First(value => StatusCode.IsGood(value.StatusCode));

            HistoryReadOutcome outcome = await HistoryOps
                .ReadAtTimeAsync(Session, item, [At(expected)], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"At {At(expected):O} the archive holds {expected.WrappedValue}, " +
                    $"a read at that time returns {outcome.Values[0].WrappedValue} " +
                    $"({outcome.Values[0].StatusCode})")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsNotBad(outcome.Values[0].StatusCode),
                Is.True,
                "A read at a time the archive has a good value for has to succeed.");

            Assert.That(
                outcome.Values[0].WrappedValue.ToString(),
                Is.EqualTo(expected.WrappedValue.ToString()),
                "The value at a recorded time is the value which was recorded there.");
        }

        /// <summary>
        /// A read at the time of a recorded bad value returns that bad value.
        /// </summary>
        /// <remarks>
        /// The sample archive deliberately records failures, and asking to skip bad
        /// values only changes which neighbours an interpolation may use - a value
        /// recorded at the requested time itself always answers, whatever its
        /// status. Interpolating over it would fabricate a measurement where the
        /// archive says there was none.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadAtTimeReturnsTheRecordedBadValue(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            Assert.That(
                values.Any(value => StatusCode.IsBad(value.StatusCode)),
                Is.True,
                "The sample archive records deliberately bad values.");

            DataValue recorded = values.First(value => StatusCode.IsBad(value.StatusCode));

            HistoryReadOutcome outcome = await HistoryOps
                .ReadAtTimeAsync(Session, item, [At(recorded)], ct, useSimpleBounds: false)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"At {At(recorded):O} the archive recorded {recorded.StatusCode}, " +
                    $"a read at that time returns {outcome.Values[0].StatusCode}")
                .ConfigureAwait(false);

            Assert.That(
                outcome.Values[0].StatusCode,
                Is.EqualTo(recorded.StatusCode),
                "The value recorded at the requested time answers, whatever its status.");
        }

        /// <summary>
        /// An average over the archive is computed per interval.
        /// </summary>
        /// <remarks>
        /// The aggregate itself is the server's, but the sample has to hand it the raw
        /// values and revise the request, and a client can only tell that happened by the
        /// shape of what comes back.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AverageIsComputedPerInterval(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            DateTime start = At(values[0]);
            DateTime end = At(values[^1]);
            double span = (end - start).TotalMilliseconds;

            Assert.That(span, Is.GreaterThan(0), "The archive covers no time at all.");

            HistoryReadOutcome outcome = await HistoryOps.ReadProcessedAsync(
                Session,
                item,
                start,
                end,
                span / 4,
                ObjectIds.AggregateFunction_Average,
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Average over {span:0} ms in steps of {span / 4:0} ms: " +
                    $"{outcome.StatusCode}, {outcome.Values.Count} values")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(outcome.StatusCode),
                Is.True,
                $"Reading an average failed: {outcome.StatusCode}");

            Assert.That(
                outcome.Values,
                Is.Not.Empty,
                "An average over an archive which has values has to return something.");

            // an aggregate is computed per interval, so asking for four of them across the
            // archive has to give fewer values back than the archive holds
            Assert.That(
                outcome.Values.Count,
                Is.LessThan(values.Count),
                "An average has to summarise the raw values rather than repeat them.");
        }

        /// <summary>
        /// The server accepts an insert into the history of an archive item.
        /// </summary>
        /// <remarks>
        /// The timestamp is far in the past, well before anything the archive holds, so
        /// this cannot disturb the tests which read the recorded values. Whether the value
        /// can be read back afterwards is a separate question - see InsertedValueIsReadBack.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task InsertIntoHistoryIsAccepted(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            (StatusCode result, IReadOnlyList<StatusCode> perValue) = await HistoryOps
                .UpdateDataAsync(Session, item, PerformUpdateType.Insert, [SampleValue(InsertedAt, 42.0)], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Insert: {result}, per value: {string.Join(", ", perValue)}")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result) && perValue.All(StatusCode.IsGood),
                Is.True,
                $"Inserting a value failed: {result} / {string.Join(", ", perValue)}");
        }

        /// <summary>
        /// An inserted value is in the history afterwards.
        /// </summary>
        /// <remarks>
        /// The archive is kept for as long as the server runs, so a write to it is still
        /// there when the next read comes along. That sounds obvious and was not: every
        /// operation used to be given its own freshly loaded copy of the archive, so a
        /// write went into a copy which was thrown away and the next read loaded the file
        /// again. Both halves reported success and nothing was stored.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task InsertedValueIsReadBack(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            (StatusCode result, IReadOnlyList<StatusCode> perValue) = await HistoryOps
                .UpdateDataAsync(Session, item, PerformUpdateType.Insert, [SampleValue(ReadBackAt, 42.0)], ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result) && perValue.All(StatusCode.IsGood),
                Is.True,
                $"Inserting a value failed: {result} / {string.Join(", ", perValue)}");

            IReadOnlyList<DataValue> archive = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"After inserting at {ReadBackAt:O} the archive holds {archive.Count} values.")
                .ConfigureAwait(false);

            Assert.That(
                archive.Select(At),
                Does.Contain(ReadBackAt),
                "A value which was inserted has to be in the history afterwards.");

            DataValue inserted = archive.First(value => At(value) == ReadBackAt);

            Assert.That(
                inserted.WrappedValue.ConvertTo(BuiltInType.Double).TryGetValue(out double insertedValue) ? insertedValue : double.NaN,
                Is.EqualTo(42.0),
                "The value which comes back has to be the value which was written.");
        }

        /// <summary>
        /// Deleting a point in time which holds nothing is refused.
        /// </summary>
        /// <remarks>
        /// This is the error path of the delete: the server reports per value that
        /// there was no entry to delete. The path which deletes a value that is
        /// there has its own test - DeletingARecordedValueRemovesItFromTheHistory.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DeletingAValueWhichIsNotThereIsRefused(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            var never = new DateTime(1985, 6, 15, 12, 0, 0, DateTimeKind.Utc);

            (_, IReadOnlyList<StatusCode> perValue) = await HistoryOps
                .DeleteAtTimeAsync(Session, item, [never], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Deleting nothing, per value: {string.Join(", ", perValue)}")
                .ConfigureAwait(false);

            Assert.That(
                perValue,
                Is.Not.Empty.And.All.Matches<StatusCode>(StatusCode.IsBad),
                "Deleting a value which is not there has to be refused per value.");

            Assert.That(
                perValue[0],
                Is.EqualTo((StatusCode)StatusCodes.BadNoEntryExists),
                "The refusal has to say that there was no entry.");
        }

        /// <summary>
        /// A recorded value can be deleted, and the modified history remembers it.
        /// </summary>
        /// <remarks>
        /// The value is inserted first, at a timestamp far away from anything the
        /// archive recorded, so the test owns what it deletes and the reading tests
        /// are not disturbed. The timestamp deliberately carries milliseconds,
        /// because timestamps are matched with full precision and truncating them
        /// to the second is exactly the kind of bug this path had.
        ///
        /// The delete has to remove the value from the raw history and leave a
        /// deletion record in the modified history next to the insertion record.
        /// Both records carry the same source timestamp, so reading them back one
        /// value at a time also pins down that a continuation point inside such a
        /// group loses nothing.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DeletingARecordedValueRemovesItFromTheHistory(CancellationToken ct)
        {
            NodeId item = await ResolveWritableItemAsync(ct).ConfigureAwait(false);

            (StatusCode inserted, IReadOnlyList<StatusCode> perInserted) = await HistoryOps
                .UpdateDataAsync(
                    Session,
                    item,
                    PerformUpdateType.Insert,
                    [new DataValue(Variant.From(42.0f), StatusCodes.Good, DeletedAt, DeletedAt)],
                    ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(inserted) && perInserted.All(StatusCode.IsGood),
                Is.True,
                $"Inserting the value to delete failed: {inserted} / {string.Join(", ", perInserted)}");

            (_, IReadOnlyList<StatusCode> perDeleted) = await HistoryOps
                .DeleteAtTimeAsync(Session, item, [DeletedAt], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Deleting at {DeletedAt:O}, per value: {string.Join(", ", perDeleted)}")
                .ConfigureAwait(false);

            Assert.That(
                perDeleted,
                Is.Not.Empty.And.All.Matches<StatusCode>(StatusCode.IsGood),
                "Deleting a value which is in the archive has to succeed.");

            IReadOnlyList<DataValue> archive = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            Assert.That(
                archive.Select(At),
                Does.Not.Contain(DeletedAt),
                "A deleted value must not be in the raw history any more.");

            // read the modified history one value at a time: the insertion and the
            // deletion record share the timestamp and both have to come back.
            var modified = new List<(DataValue Value, ModificationInfo Info)>();
            ByteString continuationPoint = default;
            DateTime started = DateTime.UtcNow;

            do
            {
                HistoryReadOutcome page = await HistoryOps
                    .ReadModifiedAsync(Session, item, ArchiveStart, ArchiveEnd, 1, ct, continuationPoint)
                    .ConfigureAwait(false);

                Assert.That(
                    page.ModificationInfos,
                    Has.Count.EqualTo(page.Values.Count),
                    "A modified read answers with one modification info beside every value.");

                modified.AddRange(page.Values.Zip(page.ModificationInfos));
                continuationPoint = page.ContinuationPoint;
            }
            while (!continuationPoint.IsNull && continuationPoint.Length > 0);

            await ReportAsync(
                "Modified history",
                modified.Select(entry =>
                    $"{entry.Value.WrappedValue} at {At(entry.Value):O}: " +
                    $"{entry.Info.UpdateType} at {entry.Info.ModificationTime:O} by {entry.Info.UserName}"))
                .ConfigureAwait(false);

            List<ModificationInfo> ofDeleted = modified
                .Where(entry => At(entry.Value) == DeletedAt)
                .Select(entry => entry.Info)
                .ToList();

            Assert.That(
                ofDeleted,
                Has.Count.EqualTo(2),
                "The modified history has to remember both the insertion and the deletion of the value.");

            // the modification info is what makes a modified read worth anything: it
            // says what was done, when and by whom, and a server which returned it
            // empty would look correct to a test which only counts the entries.
            Assert.Multiple(() => {
                Assert.That(
                    ofDeleted.Select(info => info.UpdateType),
                    Is.EquivalentTo(new[] { HistoryUpdateType.Insert, HistoryUpdateType.Delete }),
                    "One record says the value was inserted, the other that it was deleted.");

                Assert.That(
                    ofDeleted.Select(info => (DateTime)info.ModificationTime),
                    Is.All.InRange(started.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1)),
                    "The modification time is when this test wrote, not the timestamp of the value.");

                Assert.That(
                    ofDeleted.Select(info => info.UserName),
                    Is.All.Not.Null.And.All.Not.Empty,
                    "The modification info names the user of the session which wrote.");
            });
        }

        /// <summary>
        /// A history update is audited, and the audit event says what it displaced.
        /// </summary>
        /// <remarks>
        /// Part 11 asks a server to audit every HistoryUpdate, and the dispatcher of
        /// the stack reports one audit event per update - a value update, an
        /// annotation update, a raw delete, an at-time delete - with the values the
        /// provider says it displaced. The sample switches auditing on in its
        /// configuration and its provider reports what a replace and a delete took
        /// out, so this replaces and deletes a value of its own and reads both
        /// audit events. A server only delivers audit events to a session on an
        /// encrypted channel, so the writing session is opened on one.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task HistoryUpdatesAreAuditedWithTheValuesTheyDisplaced(CancellationToken ct)
        {
            NodeId item = await ResolveWritableItemAsync(ct).ConfigureAwait(false);

            await using TestClient writer = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "history auditor", null, ct)
                .ConfigureAwait(false);

            await writer.Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            await using EventCapture audit = await EventCapture
                .CreateAsync(
                    writer.Session,
                    ObjectIds.Server,
                    ct,
                    ObjectTypeIds.AuditHistoryUpdateEventType,
                    [new QualifiedName(Opc.Ua.BrowseNames.Status)],
                    [new QualifiedName(Opc.Ua.BrowseNames.UpdatedNode)],
                    [new QualifiedName(Opc.Ua.BrowseNames.PerformInsertReplace)],
                    [new QualifiedName(Opc.Ua.BrowseNames.NewValues)],
                    [new QualifiedName(Opc.Ua.BrowseNames.OldValues)])
                .ConfigureAwait(false);

            DataValue auditing = await SessionOps
                .ReadValueAsync(writer.Session, VariableIds.Server_Auditing, ct)
                .ConfigureAwait(false);

            Assert.That(
                auditing.WrappedValue.TryGetValue(out bool enabled) && enabled,
                Is.True,
                $"The sample configures AuditingEnabled, so Server.Auditing has to read true: {auditing.WrappedValue}");

            var first = new DataValue(Variant.From(1.5f), StatusCodes.Good, AuditedAt, AuditedAt);
            var second = new DataValue(Variant.From(2.5f), StatusCodes.Good, AuditedAt, AuditedAt);

            (StatusCode inserted, IReadOnlyList<StatusCode> perInserted) = await HistoryOps
                .UpdateDataAsync(writer.Session, item, PerformUpdateType.Insert, [first], ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(inserted) && perInserted.All(StatusCode.IsGood),
                Is.True,
                $"Inserting the value to replace failed: {inserted} / {string.Join(", ", perInserted)}");

            (StatusCode replaced, IReadOnlyList<StatusCode> perReplaced) = await HistoryOps
                .UpdateDataAsync(writer.Session, item, PerformUpdateType.Replace, [second], ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(replaced) && perReplaced.All(StatusCode.IsGood),
                Is.True,
                $"Replacing the value failed: {replaced} / {string.Join(", ", perReplaced)}");

            CapturedEvent ofReplace = await audit
                .WaitAsync(
                    candidate => candidate.EventType == ObjectTypeIds.AuditHistoryValueUpdateEventType
                        && candidate.Field(Opc.Ua.BrowseNames.UpdatedNode).TryGetValue(out NodeId updated)
                        && updated == item
                        && candidate.Field(Opc.Ua.BrowseNames.PerformInsertReplace).TryGetValue(out int perform)
                        && perform == (int)PerformUpdateType.Replace,
                    TimeSpan.FromSeconds(20),
                    "the audit event of the replace",
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The server audited the replace: {ofReplace}").ConfigureAwait(false);

            (_, IReadOnlyList<StatusCode> perDeleted) = await HistoryOps
                .DeleteAtTimeAsync(writer.Session, item, [AuditedAt], ct)
                .ConfigureAwait(false);

            Assert.That(perDeleted, Is.Not.Empty.And.All.Matches<StatusCode>(StatusCode.IsGood), "Deleting the replaced value failed.");

            CapturedEvent ofDelete = await audit
                .WaitAsync(
                    candidate => candidate.EventType == ObjectTypeIds.AuditHistoryAtTimeDeleteEventType
                        && candidate.Field(Opc.Ua.BrowseNames.UpdatedNode).TryGetValue(out NodeId updated)
                        && updated == item,
                    TimeSpan.FromSeconds(20),
                    "the audit event of the delete",
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The server audited the delete: {ofDelete}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    ofReplace.Field(Opc.Ua.BrowseNames.Status).TryGetValue(out bool succeeded) && succeeded,
                    Is.True,
                    "The audit event of an update the server accepted reports Status true.");

                Assert.That(
                    ofReplace.Field(Opc.Ua.BrowseNames.NewValues).TryGetValue(out ArrayOf<DataValue> newValues) ? newValues.Count : -1,
                    Is.EqualTo(1),
                    "The audit event carries the value the replace wrote.");

                Assert.That(
                    ofReplace.Field(Opc.Ua.BrowseNames.OldValues).TryGetValue(out ArrayOf<DataValue> oldValues) ? oldValues.Count : -1,
                    Is.EqualTo(1),
                    "The audit event carries the value the replace displaced, which the provider reported.");

                Assert.That(
                    ofReplace.Field(Opc.Ua.BrowseNames.OldValues).TryGetValue(out ArrayOf<DataValue> displaced)
                        && displaced[0].WrappedValue.TryGetValue(out float value)
                        ? value
                        : float.NaN,
                    Is.EqualTo(1.5f),
                    "The old value of the replace is the value which stood there before it.");

                Assert.That(
                    ofDelete.Field(Opc.Ua.BrowseNames.OldValues).TryGetValue(out ArrayOf<DataValue> deletedValues) ? deletedValues.Count : -1,
                    Is.EqualTo(1),
                    "The audit event of the delete carries the value it removed.");
            });
        }

        /// <summary>
        /// A batch which cannot be applied as a whole leaves the archive as it was.
        /// </summary>
        /// <remarks>
        /// The dispatcher prefers the atomic path of a provider which offers one,
        /// so every insert a client sends to this archive is applied as a whole:
        /// one value which cannot be written rolls the others back, they answer
        /// that nothing became of them, and the update as a whole answers that
        /// the transaction failed.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnAtomicBatchIsRolledBackAsAWhole(CancellationToken ct)
        {
            NodeId item = await ResolveWritableItemAsync(ct).ConfigureAwait(false);

            (StatusCode inserted, IReadOnlyList<StatusCode> perInserted) = await HistoryOps
                .UpdateDataAsync(Session, item, PerformUpdateType.Insert, [new DataValue(Variant.From(1.0f), StatusCodes.Good, AtomicAt, AtomicAt)], ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(inserted) && perInserted.All(StatusCode.IsGood),
                Is.True,
                $"Inserting the value which makes the batch collide failed: {inserted} / {string.Join(", ", perInserted)}");

            DateTime alsoAt = AtomicAt.AddSeconds(1);

            (StatusCode result, IReadOnlyList<StatusCode> perValue) = await HistoryOps
                .UpdateDataAsync(
                    Session,
                    item,
                    PerformUpdateType.Insert,
                    [
                        new DataValue(Variant.From(2.0f), StatusCodes.Good, alsoAt, alsoAt),
                        new DataValue(Variant.From(3.0f), StatusCodes.Good, AtomicAt, AtomicAt),
                    ],
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Inserting a batch with a collision: {result}, per value: {string.Join(", ", perValue)}")
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> archive = await HistoryOps
                .ReadAllRawAsync(Session, item, AtomicAt, alsoAt.AddSeconds(1), 0, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    result,
                    Is.EqualTo((StatusCode)StatusCodes.BadTransactionFailed),
                    "An atomic batch which could not be applied fails as a transaction.");

                Assert.That(
                    perValue[1],
                    Is.EqualTo((StatusCode)StatusCodes.BadEntryExists),
                    "The value which collided says why.");

                Assert.That(
                    perValue[0],
                    Is.EqualTo((StatusCode)StatusCodes.BadHistoryOperationUnsupported),
                    "The value which could have been written says nothing became of it.");

                Assert.That(
                    archive.Select(At),
                    Does.Not.Contain(alsoAt),
                    "The value which could have been written was rolled back with the batch.");
            });
        }

        /// <summary>
        /// An aggregate the server has no calculator for is refused before any
        /// provider is reached.
        /// </summary>
        /// <remarks>
        /// The dispatcher checks the aggregate against the aggregate manager of the
        /// server first and answers BadAggregateNotSupported itself; the node
        /// manager used to do that in an override, and no longer has to.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnUnsupportedAggregateIsRefusedBeforeTheProvider(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

            HistoryReadOutcome outcome = await HistoryOps.ReadProcessedAsync(
                Session,
                item,
                ArchiveStart,
                ArchiveEnd,
                60_000,
                new NodeId(4711, 0),
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Asking for an aggregate the server does not have: {outcome.StatusCode}")
                .ConfigureAwait(false);

            Assert.That(
                outcome.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadAggregateNotSupported),
                "An aggregate the server has no calculator for is refused as such.");
        }

        /// <summary>
        /// A live variable historizes what it publishes, without anybody subscribing.
        /// </summary>
        /// <remarks>
        /// The live variables have no store of their own: the in-memory engine of the
        /// SDK keeps their history, and the capture pipeline fills it from the values
        /// the simulation publishes. The variable shares the namespace of the file
        /// archive, so its history reaching a reader at all shows that the binding
        /// by node id wins over the binding by namespace.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task LiveVariableHistorizesWhatItPublishes(CancellationToken ct)
        {
            NodeId temperature = await ResolveLiveItemAsync("Temperature", ct).ConfigureAwait(false);

            DataValue historizing = await SessionOps
                .ReadAttributeAsync(Session, temperature, Attributes.Historizing, ct)
                .ConfigureAwait(false);

            Assert.That(
                historizing.WrappedValue.TryGetValue(out bool collecting) && collecting,
                Is.True,
                "A live variable is being collected, and says so.");

            DateTime now = DateTime.UtcNow;

            IReadOnlyList<DataValue> before = await HistoryOps
                .ReadAllRawAsync(Session, temperature, now.AddHours(-1), now.AddHours(1), 0, ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> grown = await Poll.UntilAsync(
                token => HistoryOps.ReadAllRawAsync(Session, temperature, now.AddHours(-1), now.AddHours(1), 0, token),
                values => values.Count > before.Count + 1,
                $"the captured history of the live variable to grow beyond the {before.Count} values it held",
                timeout: TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The captured history grew from {before.Count} to {grown.Count} values without a subscription.")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    grown.Select(value => value.StatusCode),
                    Is.All.Matches<StatusCode>(StatusCode.IsGood),
                    "The captured samples are the good values the simulation published.");

                Assert.That(
                    grown.Select(At),
                    Is.Ordered.Ascending,
                    "The captured samples are stored in the order they were published.");

                Assert.That(
                    grown.Select(value => value.WrappedValue.TryGetValue(out double reading) ? reading : double.NaN),
                    Is.All.InRange(15.0, 25.0),
                    "The captured samples are the readings of the simulation.");
            });
        }

        /// <summary>
        /// A value a client writes into a live variable is captured into its history.
        /// </summary>
        /// <remarks>
        /// The capture pipeline listens to the state change of the variable, and a
        /// Write through the service is a state change like any other: the value
        /// goes into the archive without the client asking for a HistoryUpdate.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task WriteToALiveVariableIsCaptured(CancellationToken ct)
        {
            NodeId setpoint = await ResolveLiveItemAsync("Setpoint", ct).ConfigureAwait(false);

            double written = 12.5 + (DateTime.UtcNow.Second / 100.0);
            DateTime before = DateTime.UtcNow.AddMinutes(-1);

            StatusCode result = await SessionOps
                .WriteValueAsync(Session, setpoint, Variant.From(written), ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result), Is.True, $"Writing the setpoint failed: {result}");

            IReadOnlyList<DataValue> captured = await Poll.UntilAsync(
                token => HistoryOps.ReadAllRawAsync(Session, setpoint, before, DateTime.UtcNow.AddMinutes(1), 0, token),
                values => values.Any(value => value.WrappedValue.TryGetValue(out double reading) && reading == written),
                "the written setpoint to show up in the captured history",
                timeout: TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            await ReportAsync(
                "Captured setpoints",
                captured.Select(value => $"{value.WrappedValue} at {At(value):O}"))
                .ConfigureAwait(false);

            Assert.That(
                captured.Select(value => value.WrappedValue.TryGetValue(out double reading) ? reading : double.NaN),
                Does.Contain(written),
                "A value written through the Write service is in the history of the variable.");
        }

        /// <summary>
        /// The companion object the SDK installs beside a live variable says how it
        /// is recorded.
        /// </summary>
        /// <remarks>
        /// The archive items build nothing by hand any more either: HistorizeAsync
        /// installs the HistoricalDataConfiguration object from what the provider
        /// reports for the node, and a client reads the sampling interval, the
        /// stepped flag and the start of the archive from it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task LiveVariablesCarryTheirInstalledConfiguration(CancellationToken ct)
        {
            NodeId temperature = await ResolveLiveItemAsync("Temperature", ct).ConfigureAwait(false);

            NodeId minInterval = await ResolveFromAsync(
                temperature,
                ct,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                new QualifiedName(Opc.Ua.BrowseNames.MinTimeInterval)).ConfigureAwait(false);

            NodeId stepped = await ResolveFromAsync(
                temperature,
                ct,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                new QualifiedName(Opc.Ua.BrowseNames.Stepped)).ConfigureAwait(false);

            NodeId startOfOnlineArchive = await ResolveFromAsync(
                temperature,
                ct,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                new QualifiedName(Opc.Ua.BrowseNames.StartOfOnlineArchive)).ConfigureAwait(false);

            DataValue interval = await SessionOps.ReadValueAsync(Session, minInterval, ct).ConfigureAwait(false);
            DataValue isStepped = await SessionOps.ReadValueAsync(Session, stepped, ct).ConfigureAwait(false);
            DataValue start = await SessionOps.ReadValueAsync(Session, startOfOnlineArchive, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"HA Configuration of the live temperature: MinTimeInterval {interval.WrappedValue}, " +
                    $"Stepped {isStepped.WrappedValue}, StartOfOnlineArchive {start.WrappedValue}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    interval.WrappedValue.TryGetValue(out double milliseconds) ? milliseconds : double.NaN,
                    Is.EqualTo(1000.0),
                    "The simulation publishes once a second, and the companion object says so.");

                Assert.That(
                    isStepped.WrappedValue.TryGetValue(out bool flag) ? flag : (bool?)null,
                    Is.False,
                    "A reading which changes continuously is not stepped.");

                Assert.That(
                    start.WrappedValue.TryGetValue(out DateTimeUtc since) ? (DateTime)since : DateTime.MinValue,
                    Is.GreaterThan(DateTime.UtcNow.AddDays(-1)).And.LessThan(DateTime.UtcNow.AddMinutes(1)),
                    "The online archive of a live variable starts when the server did.");
            });
        }

        /// <summary>
        /// A structured variable holds several entries at one instant, told apart by
        /// the key inside the value.
        /// </summary>
        /// <remarks>
        /// StructuredHistoryData is written through UpdateStructureData, which keys
        /// an entry by its source timestamp and - for a KeyValuePair - the key of
        /// the pair, so three readings of one sample share the instant it was taken
        /// at. They are read back through the ordinary raw read, replaced by key,
        /// and removed by key; the simulation writes its own three readings every
        /// few seconds through the capture pipeline, and they show up the same way.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StructuredHistoryHoldsSeveralEntriesPerInstant(CancellationToken ct)
        {
            NodeId labSample = await ResolveLiveItemAsync("LabSample", ct).ConfigureAwait(false);

            // half an hour ago, on a whole second: the in-memory engine keeps an hour of
            // history behind the newest sample of a node, and the simulation stamps its
            // samples with the ticks of the moment, so an instant of our own on a whole
            // second inside that window is safe from both the eviction and a collision.
            DateTime structuredAt = new DateTime(
                DateTime.UtcNow.AddMinutes(-30).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond,
                DateTimeKind.Utc);

            (StatusCode inserted, IReadOnlyList<StatusCode> perInserted) = await HistoryOps
                .UpdateStructureDataAsync(
                    Session,
                    labSample,
                    PerformUpdateType.Insert,
                    [Reading("pH", 7.1, structuredAt), Reading("Temperature", 21.0, structuredAt), Reading("Conductivity", 512.0, structuredAt)],
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Inserting three readings at one instant: {inserted} / {string.Join(", ", perInserted)}")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(inserted) && perInserted.All(StatusCode.IsGood),
                Is.True,
                $"Inserting structured entries failed: {inserted} / {string.Join(", ", perInserted)}");

            IReadOnlyList<DataValue> readBack = await HistoryOps
                .ReadAllRawAsync(Session, labSample, structuredAt, structuredAt.AddSeconds(1), 0, ct)
                .ConfigureAwait(false);

            await ReportAsync("Entries at the instant", readBack.Select(Describe)).ConfigureAwait(false);

            Assert.That(
                readBack.Select(KeyOf),
                Is.EquivalentTo(new[] { "pH", "Temperature", "Conductivity" }),
                "The three readings share the instant and are told apart by their key.");

            (_, IReadOnlyList<StatusCode> perReplaced) = await HistoryOps
                .UpdateStructureDataAsync(Session, labSample, PerformUpdateType.Replace, [Reading("pH", 7.4, structuredAt)], ct)
                .ConfigureAwait(false);

            Assert.That(
                perReplaced[0],
                Is.EqualTo((StatusCode)StatusCodes.GoodEntryReplaced),
                "A replace addresses one entry of the instant by its key.");

            (_, IReadOnlyList<StatusCode> perRemoved) = await HistoryOps
                .UpdateStructureDataAsync(Session, labSample, PerformUpdateType.Remove, [Reading("Conductivity", 0.0, structuredAt)], ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(perRemoved[0]),
                Is.True,
                $"A remove addresses one entry of the instant by its key: {perRemoved[0]}");

            IReadOnlyList<DataValue> afterwards = await HistoryOps
                .ReadAllRawAsync(Session, labSample, structuredAt, structuredAt.AddSeconds(1), 0, ct)
                .ConfigureAwait(false);

            await ReportAsync("Entries after the replace and the remove", afterwards.Select(Describe)).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    afterwards.Select(KeyOf),
                    Is.EquivalentTo(new[] { "pH", "Temperature" }),
                    "The removed reading is gone, the other two stay.");

                Assert.That(
                    afterwards.Where(value => KeyOf(value) == "pH").Select(ReadingOf),
                    Is.EqualTo(new[] { 7.4 }),
                    "The replaced reading carries its new value.");
            });

            // the simulation records its own sample every few seconds: three readings
            // at one instant, captured through the pipeline rather than written by a
            // client.
            DateTime now = DateTime.UtcNow;

            IReadOnlyList<DataValue> captured = await Poll.UntilAsync(
                token => HistoryOps.ReadAllRawAsync(Session, labSample, now.AddMinutes(-2), now.AddMinutes(1), 0, token),
                values => values.GroupBy(At).Any(group => group.Count() == 3),
                "the simulation to capture a lab sample of three readings",
                timeout: TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            IGrouping<DateTime, DataValue> sample = captured.GroupBy(At).First(group => group.Count() == 3);

            await ReportAsync($"Captured sample at {sample.Key:O}", sample.Select(Describe)).ConfigureAwait(false);

            Assert.That(
                sample.Select(KeyOf),
                Is.EquivalentTo(new[] { "pH", "Temperature", "Conductivity" }),
                "A captured sample is three readings at one instant, one per quantity.");
        }

        /// <summary>
        /// The annotations recorded in the archive are served, and a client can add
        /// its own.
        /// </summary>
        /// <remarks>
        /// Annotations are the structured half of the history profile: they live on
        /// the Annotations property of an item and travel through HistoryRead and
        /// HistoryUpdate like values do, just as extension objects. The sample
        /// archive ships with annotations, so the read has fixed recorded content to
        /// check; the written annotation goes to the item the deleting test uses, at
        /// its own timestamp, so the recorded archive stays as it is.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnnotationsAreServedAndAccepted(CancellationToken ct)
        {
            NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);
            NodeId annotations = await ResolveAnnotationsPropertyAsync(item, ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> recorded = await HistoryOps
                .ReadAllRawAsync(Session, annotations, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Recorded annotations",
                recorded.Select(value => $"{value.WrappedValue} at {At(value):O}"))
                .ConfigureAwait(false);

            Assert.That(recorded, Is.Not.Empty, "The sample archive ships with annotations.");

            Assert.That(
                recorded.Select(Decode),
                Is.All.Not.Null,
                "Every value on the Annotations property has to decode as an Annotation.");

            // add an annotation of our own on the item the deleting test writes to.
            NodeId writable = await ResolveWritableItemAsync(ct).ConfigureAwait(false);
            NodeId writableAnnotations = await ResolveAnnotationsPropertyAsync(writable, ct).ConfigureAwait(false);

            var annotation = new Annotation {
                AnnotationTime = AnnotatedAt,
                UserName = "Tester",
                Message = "Written by the node manager tests.",
            };

            (StatusCode result, IReadOnlyList<StatusCode> perValue) = await HistoryOps
                .UpdateStructureDataAsync(
                    Session,
                    writableAnnotations,
                    PerformUpdateType.Insert,
                    [new DataValue(new ExtensionObject(annotation), StatusCodes.Good, AnnotatedAt, AnnotatedAt)],
                    ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result) && perValue.All(StatusCode.IsGood),
                Is.True,
                $"Inserting an annotation failed: {result} / {string.Join(", ", perValue)}");

            IReadOnlyList<DataValue> readBack = await HistoryOps
                .ReadAllRawAsync(Session, writableAnnotations, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            Assert.That(
                readBack.Select(At),
                Does.Contain(AnnotatedAt),
                "An inserted annotation has to be served from the Annotations property afterwards.");
        }

        /// <summary>
        /// The dynamic items record new values while a client is subscribed to them.
        /// </summary>
        /// <remarks>
        /// The simulation which appends to the dynamic archive only runs while somebody is
        /// monitoring the item, so this subscribes first and then asks the history what was
        /// recorded during that time.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DynamicItemChangesWhileItIsMonitored(CancellationToken ct)
        {
            NodeId item = await ResolveDynamicItemAsync(ct).ConfigureAwait(false);

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, item, ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await capture
                .CollectDistinctAsync(3, TimeSpan.FromSeconds(25), ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Dynamic item reported",
                values.Select(value => $"{value.WrappedValue} ({value.StatusCode})"))
                .ConfigureAwait(false);

            Assert.That(
                values,
                Has.Count.EqualTo(3),
                "The simulation of a dynamic item has to keep changing its value.");
        }

        /// <summary>
        /// What a dynamic item reports is added to its history.
        /// </summary>
        /// <remarks>
        /// The simulation appends to the dynamic archive while the item is monitored, which
        /// DynamicItemChangesWhileItIsMonitored shows from the subscriber's side. This is
        /// the other side of it: the archive has to grow while that is happening.
        ///
        /// Growth is what is measured, over the whole archive, rather than reading back the
        /// span the subscription covered: the simulation stamps its samples on its own
        /// clock, so a range pinned to the subscription would answer differently depending
        /// on where the generated data happens to end relative to the test's clock.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DynamicItemRecordsWhatItReported(CancellationToken ct)
        {
            NodeId item = await ResolveDynamicItemAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DataValue> before = await HistoryOps
                .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                .ConfigureAwait(false);

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, item, ct)
                .ConfigureAwait(false);

            await capture.CollectDistinctAsync(3, TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);

            int grown = await Poll.UntilAsync(
                async token => {
                    IReadOnlyList<DataValue> now = await HistoryOps
                        .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, token)
                        .ConfigureAwait(false);

                    return now.Count;
                },
                count => count > before.Count,
                $"the history of the dynamic item to grow beyond the {before.Count} values it held",
                timeout: TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The dynamic archive grew from {before.Count} to {grown} values while monitored.")
                .ConfigureAwait(false);

            Assert.That(
                grown,
                Is.GreaterThan(before.Count),
                "A dynamic item has to archive what it reports while it is being monitored.");
        }

        /// <summary>
        /// Points in time well before the archive starts, for the tests which write.
        /// </summary>
        /// <remarks>
        /// Each writing test gets its own, because the tests share one server and inserting
        /// where a previous test already inserted is refused rather than repeated.
        /// </remarks>
        /// <summary>
        /// A range which is wide enough to cover the whole archive.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than given as DateTime.MinValue and DateTime.MaxValue: those
        /// carry no time zone, and a server refuses a history read whose bounds it cannot
        /// place, which is what BadHistoryOperationInvalid means here.
        /// </remarks>
        private static readonly DateTime ArchiveStart = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime ArchiveEnd = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime InsertedAt = new(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime ReadBackAt = new(1990, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime DeletedAt = new(1990, 4, 1, 0, 0, 0, 500, DateTimeKind.Utc);

        private static readonly DateTime AnnotatedAt = new(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc);


        private static readonly DateTime AuditedAt = new(1990, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime AtomicAt = new(1990, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The point in time a recorded value carries.
        /// </summary>
        private static DateTime At(DataValue value)
        {
            return (DateTime)value.SourceTimestamp;
        }

        /// <summary>
        /// One reading of a lab sample, as the structured variable stores it: a
        /// KeyValuePair whose key names the quantity.
        /// </summary>
        private static DataValue Reading(string quantity, double reading, DateTime at)
        {
            var pair = new Opc.Ua.KeyValuePair {
                Key = new QualifiedName(quantity),
                Value = Variant.From(reading),
            };

            return new DataValue(Variant.From(new ExtensionObject(pair)), StatusCodes.Good, at, at);
        }

        /// <summary>
        /// The quantity a stored reading is keyed by.
        /// </summary>
        private static string KeyOf(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out ExtensionObject extension)
                && extension.TryGetValue(out Opc.Ua.KeyValuePair pair)
                ? pair.Key.Name
                : null;
        }

        /// <summary>
        /// The reading a stored entry carries.
        /// </summary>
        private static double ReadingOf(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out ExtensionObject extension)
                && extension.TryGetValue(out Opc.Ua.KeyValuePair pair)
                && pair.Value.TryGetValue(out double reading)
                ? reading
                : double.NaN;
        }

        /// <summary>
        /// A stored entry, for the report.
        /// </summary>
        private static string Describe(DataValue value)
        {
            return $"{KeyOf(value)} = {ReadingOf(value)} at {At(value):O} ({value.StatusCode})";
        }

        /// <summary>
        /// The annotation a value read from an Annotations property carries.
        /// </summary>
        private static Annotation Decode(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out ExtensionObject extension)
                ? ExtensionObject.ToEncodeable(extension) as Annotation
                : null;
        }

        private static DataValue SampleValue(DateTime when, double value)
        {
            return new DataValue(Variant.From(value), StatusCodes.Good, when, when);
        }

        private NodeId SampleFolder => new("Sample", NamespaceIndex(HistoryNamespace));

        private NodeId DynamicFolder => new("Dynamic", NamespaceIndex(HistoryNamespace));

        private NodeId LiveFolder => new("Live", NamespaceIndex(HistoryNamespace));

        /// <summary>
        /// The double item of the fixed archive.
        /// </summary>
        /// <remarks>
        /// The archive holds one item per data type, and the tests use the double one
        /// throughout: it is the type the aggregates are meaningful for, and the type an
        /// inserted value can be written as. The string item, which is what an unfiltered
        /// browse happens to return first, supports none of that.
        /// </remarks>
        private Task<NodeId> ResolveSampleItemAsync(CancellationToken ct)
        {
            return ItemOfAsync(SampleFolder, "Sample", "Double", ct);
        }

        /// <summary>
        /// The item the test which deletes history uses.
        /// </summary>
        /// <remarks>
        /// Deleting goes to a different item from the one the reading tests use, so
        /// the recorded archive those tests measure is never disturbed, whichever
        /// order the fixture runs in.
        /// </remarks>
        private Task<NodeId> ResolveWritableItemAsync(CancellationToken ct)
        {
            return ItemOfAsync(SampleFolder, "Sample", "Float", ct);
        }

        private Task<NodeId> ResolveDynamicItemAsync(CancellationToken ct)
        {
            return ItemOfAsync(DynamicFolder, "Dynamic", "Double", ct);
        }

        /// <summary>
        /// One of the live variables, whose history the in-memory engine of the SDK
        /// keeps.
        /// </summary>
        private Task<NodeId> ResolveLiveItemAsync(string name, CancellationToken ct)
        {
            return ItemOfAsync(LiveFolder, "Live", name, ct);
        }

        /// <summary>
        /// The Annotations property of an archive item.
        /// </summary>
        private async Task<NodeId> ResolveAnnotationsPropertyAsync(NodeId item, CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, item, ct)
                .ConfigureAwait(false);

            ReferenceDescription annotations = children.FirstOrDefault(child =>
                child.BrowseName.Name == Opc.Ua.BrowseNames.Annotations);

            Assert.That(annotations, Is.Not.Null, "An archive item exposes its Annotations property.");

            return ExpandedNodeId.ToNodeId(annotations.NodeId, Session.NamespaceUris);
        }

        private async Task<NodeId> ItemOfAsync(
            NodeId folder,
            string what,
            string typeName,
            CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, folder, ct)
                .ConfigureAwait(false);

            string[] names = children.Select(child => child.BrowseName.Name).ToArray();

            await ReportAsync($"{what} archive", names).ConfigureAwait(false);

            ReferenceDescription item = children.FirstOrDefault(child =>
                child.NodeClass == NodeClass.Variable
                && child.BrowseName.Name.Contains(typeName, StringComparison.Ordinal));

            Assert.That(
                item,
                Is.Not.Null,
                $"The {what} folder of the archive holds no {typeName} item. " +
                $"It holds: {string.Join(", ", names)}");

            return ExpandedNodeId.ToNodeId(item.NodeId, Session.NamespaceUris);
        }
    }
}
