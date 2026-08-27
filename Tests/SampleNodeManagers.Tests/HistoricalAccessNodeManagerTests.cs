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
                Convert.ToByte(accessLevel.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture)
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
                    onCollecting.WrappedValue.AsBoxedObject(),
                    Is.EqualTo(true),
                    "An item the simulation appends to is being collected.");

                Assert.That(
                    onRecorded.WrappedValue.AsBoxedObject(),
                    Is.EqualTo(false),
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
                    value.WrappedValue.AsBoxedObject(),
                    Is.EqualTo(true),
                    $"The sample turns {name} on while it builds its address space.");
            }

            await ReportAsync("History capabilities", announced).ConfigureAwait(false);
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
        /// A read at a recorded time ought to return that value, and today it returns none.
        /// </summary>
        /// <remarks>
        /// The request itself succeeds and the server answers with one value per requested
        /// time, stamped correctly, which ReadAtTimeReturnsTheRecordedValue pins down. The
        /// value that comes back is bad, though, even for a point in time the archive
        /// demonstrably holds a value for - the raw read the test takes the timestamp from
        /// returns it.
        ///
        /// The archive is searched with a binary search over a view sorted by source
        /// timestamp, so this is a good place to look first: a read at a time is the one
        /// operation which depends on that search finding an exact match.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task ReadAtTimeCarriesTheValue(CancellationToken ct)
        {
            return KnownIssueAsync(
                async () => {
                    NodeId item = await ResolveSampleItemAsync(ct).ConfigureAwait(false);

                    IReadOnlyList<DataValue> values = await HistoryOps
                        .ReadAllRawAsync(Session, item, ArchiveStart, ArchiveEnd, 0, ct)
                        .ConfigureAwait(false);

                    DataValue expected = values[values.Count / 2];

                    HistoryReadOutcome outcome = await HistoryOps
                        .ReadAtTimeAsync(Session, item, [At(expected)], ct)
                        .ConfigureAwait(false);

                    Assert.That(
                        StatusCode.IsNotBad(outcome.Values[0].StatusCode),
                        Is.True,
                        "A read at a time the archive has a value for has to succeed.");

                    Assert.That(
                        outcome.Values[0].WrappedValue.ToString(),
                        Is.EqualTo(expected.WrappedValue.ToString()),
                        "The value at a recorded time is the value which was recorded there.");
                },
                "a read at a point in time the archive holds a value for returns a bad value, " +
                "although the same point in time comes back from a raw read.");
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
                Convert.ToDouble(inserted.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(42.0),
                "The value which comes back has to be the value which was written.");
        }

        /// <summary>
        /// Deleting a point in time which holds nothing is refused.
        /// </summary>
        /// <remarks>
        /// This is the error path of the delete, and it is the half of it which works: the
        /// server reports per value that there was no entry to delete.
        ///
        /// Deleting a point in time which does hold a recorded value is not tested, because
        /// today it answers BadUnexpectedError - which is what a server answers when the
        /// handler threw - and leaves the archive item in a state where every later read of
        /// it is refused as well. A test which did that would take the rest of the fixture
        /// down with it, so the behaviour is recorded here in prose instead.
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
        /// What a dynamic item reports ought to end up in its history, and today it does not.
        /// </summary>
        /// <remarks>
        /// The simulation appends to the dynamic archive while the item is monitored, which
        /// DynamicItemChangesWhileItIsMonitored shows happening. Reading the history of the
        /// span the subscription covered returns nothing, so the values were reported to the
        /// subscriber but never archived.
        ///
        /// This is no longer the archive being thrown away between operations - that was
        /// real, and is fixed, which is why an inserted value survives now. What is left is
        /// either the simulation not writing what it reports into the archive at all, or a
        /// bounded read failing to find it: a raw read whose range starts before the first
        /// archived value returns nothing rather than the values inside the range.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task DynamicItemRecordsWhatItReported(CancellationToken ct)
        {
            return KnownIssueAsync(
                async () => {
                    NodeId item = await ResolveDynamicItemAsync(ct).ConfigureAwait(false);

                    DateTime start = DateTime.UtcNow;

                    await using DataChangeCapture capture = await DataChangeCapture
                        .CreateAsync(Session, item, ct)
                        .ConfigureAwait(false);

                    await capture.CollectDistinctAsync(3, TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);

                    IReadOnlyList<DataValue> recorded = await Poll.UntilNoThrowAsync(
                        token => HistoryOps.ReadAllRawAsync(Session, item, start, DateTime.UtcNow, 0, token),
                        values => values.Count >= 2,
                        "the dynamic item to record what the subscription reported",
                        timeout: TimeSpan.FromSeconds(15),
                        ct: ct).ConfigureAwait(false);

                    Assert.That(
                        recorded.Select(At),
                        Is.All.GreaterThanOrEqualTo(start),
                        "Everything recorded during the test has to be stamped during the test.");
                },
                "the values a dynamic item reports to a subscriber do not turn up in its " +
                "history, although the simulation which produces them is what is meant to " +
                "archive them.");
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

        /// <summary>
        /// The point in time a recorded value carries.
        /// </summary>
        private static DateTime At(DataValue value)
        {
            return (DateTime)value.SourceTimestamp;
        }

        private static DataValue SampleValue(DateTime when, double value)
        {
            return new DataValue(Variant.From(value), StatusCodes.Good, when, when);
        }

        private NodeId SampleFolder => new("Sample", NamespaceIndex(HistoryNamespace));

        private NodeId DynamicFolder => new("Dynamic", NamespaceIndex(HistoryNamespace));

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
        /// The item the tests which write history use.
        /// </summary>
        /// <remarks>
        /// Writing goes to a different item from the one the reading tests use, because an
        /// insert leaves the item it was written to in a state where every later read of it
        /// is refused - see InsertedValueIsReadBack. Keeping the two apart means one broken
        /// path does not take the tests for the other paths down with it.
        /// </remarks>
        private Task<NodeId> ResolveWritableItemAsync(CancellationToken ct)
        {
            return ItemOfAsync(SampleFolder, "Sample", "Float", ct);
        }

        private Task<NodeId> ResolveDynamicItemAsync(CancellationToken ct)
        {
            return ItemOfAsync(DynamicFolder, "Dynamic", "Double", ct);
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
