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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the test data node manager does: it serves one variable per data type, in a
    /// static and a simulated flavour, and archives one of them.
    /// </summary>
    /// <remarks>
    /// The node manager loads a large type model as typed nodes, which is the part a
    /// migration is most likely to change. The simulated variables are driven by a system
    /// object which only samples while somebody is monitoring them, so a subscription is
    /// the only way to see that working.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class TestDataNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Sample";

        /// <summary>
        /// The data folder holds the static and the simulated variables.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DataFolderHoldsStaticAndDynamicVariables(CancellationToken ct)
        {
            NodeId data = await PathAsync(ct, "Data").ConfigureAwait(false);

            IReadOnlyList<string> folders = await BrowseNamesAsync(data, ct).ConfigureAwait(false);

            await ReportAsync("Data", folders).ConfigureAwait(false);

            Assert.That(
                folders,
                Does.Contain("Static").And.Contain("Dynamic"),
                "The sample serves a static and a simulated copy of its variables.");

            NodeId scalars = await PathAsync(ct, "Data", "Static", "Scalar").ConfigureAwait(false);

            IReadOnlyList<string> names = await BrowseNamesAsync(scalars, ct).ConfigureAwait(false);

            await ReportAsync("Data/Static/Scalar", names).ConfigureAwait(false);

            Assert.That(
                names,
                Does.Contain("Int32Value").And.Contain("DoubleValue").And.Contain("StringValue"),
                "The scalar folder holds one variable per data type.");
        }

        /// <summary>
        /// A static variable keeps what a client writes into it.
        /// </summary>
        /// <remarks>
        /// Writing goes through the node manager into the simulated system and back out
        /// again on the next read, which is the round trip this checks.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StaticVariableKeepsWhatIsWrittenToIt(CancellationToken ct)
        {
            NodeId value = await PathAsync(ct, "Data", "Static", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            DataValue before = await SessionOps.ReadValueAsync(Session, value, ct).ConfigureAwait(false);

            int written = (before.WrappedValue.TryGetValue(out int previous) ? previous : 0) + 4711;

            StatusCode result = await SessionOps
                .WriteValueAsync(Session, value, Variant.From(written), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result),
                Is.True,
                $"Writing a static variable failed: {result}");

            DataValue after = await SessionOps.ReadValueAsync(Session, value, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Int32Value was {before.WrappedValue}, wrote {written}, read {after.WrappedValue}")
                .ConfigureAwait(false);

            Assert.That(
                after.WrappedValue.TryGetValue(out int current) ? current : 0,
                Is.EqualTo(written),
                "A static variable has to keep the value it was written.");
        }

        /// <summary>
        /// The simulated variables change while a client is monitoring them.
        /// </summary>
        /// <remarks>
        /// The node manager tells the simulated system to start sampling a value when the
        /// first monitored item for it is created, and to stop when the last one goes away.
        /// A client can only see the starting half of that, which is what this checks.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DynamicVariableChangesWhileItIsMonitored(CancellationToken ct)
        {
            NodeId value = await PathAsync(ct, "Data", "Dynamic", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, value, ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> reported = await capture
                .CollectDistinctAsync(3, TimeSpan.FromSeconds(25), ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Dynamic Int32Value",
                reported.Select(item => string.Format(CultureInfo.InvariantCulture, "{0}", item.WrappedValue)))
                .ConfigureAwait(false);

            Assert.That(
                reported,
                Has.Count.EqualTo(3),
                "The simulated variable has to keep changing while it is monitored.");
        }

        /// <summary>
        /// Exactly one variable is marked as archived, and only that one.
        /// </summary>
        /// <remarks>
        /// The node manager turns archiving on for the simulated integer while it builds
        /// the address space, and leaves every other variable alone. Which variable it
        /// picks is arbitrary, but that it picks one and only one is the behaviour.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OnlyTheSimulatedIntegerIsArchived(CancellationToken ct)
        {
            NodeId archived = await PathAsync(ct, "Data", "Dynamic", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            NodeId notArchived = await PathAsync(ct, "Data", "Dynamic", "Scalar", "DoubleValue")
                .ConfigureAwait(false);

            NodeId staticOne = await PathAsync(ct, "Data", "Static", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            DataValue onArchived = await SessionOps
                .ReadAttributeAsync(Session, archived, Attributes.Historizing, ct)
                .ConfigureAwait(false);

            DataValue onOther = await SessionOps
                .ReadAttributeAsync(Session, notArchived, Attributes.Historizing, ct)
                .ConfigureAwait(false);

            DataValue onStatic = await SessionOps
                .ReadAttributeAsync(Session, staticOne, Attributes.Historizing, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Historizing: dynamic Int32 {onArchived.WrappedValue}, " +
                    $"dynamic Double {onOther.WrappedValue}, static Int32 {onStatic.WrappedValue}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    onArchived.WrappedValue.TryGetValue(out bool archivedFlag) && archivedFlag,
                    Is.True,
                    "The node manager turns archiving on for the simulated integer.");

                Assert.That(
                    onOther.WrappedValue.TryGetValue(out bool otherFlag) ? otherFlag : (bool?)null,
                    Is.False,
                    "No other simulated variable is archived.");

                Assert.That(
                    onStatic.WrappedValue.TryGetValue(out bool staticFlag) ? staticFlag : (bool?)null,
                    Is.False,
                    "The static variables are not archived.");
            });

            DataValue accessLevel = await SessionOps
                .ReadAttributeAsync(Session, archived, Attributes.AccessLevel, ct)
                .ConfigureAwait(false);

            Assert.That(
                (accessLevel.WrappedValue.TryGetValue(out byte flags) ? flags : (byte)0)
                    & AccessLevels.HistoryRead,
                Is.Not.Zero,
                "The archived variable has to say that its history can be read.");
        }

        /// <summary>
        /// The archive of the simulated integer can be read back.
        /// </summary>
        /// <remarks>
        /// The archive is seeded with a sample every ten seconds reaching back hours, so
        /// a read over the last hour has plenty to return without waiting for anything.
        /// The read runs on its own authenticated session because that is how a client
        /// entitled to the archive would connect; the sample accepts any user name with a
        /// non-empty password.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ArchivedIntegerServesItsHistory(CancellationToken ct)
        {
            NodeId archived = await PathAsync(ct, "Data", "Dynamic", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            await using TestClient reader = await TestClient
                .ConnectAsync(
                    EndpointUrl,
                    "history reader",
                    new UserIdentity("history reader", Encoding.UTF8.GetBytes("history")),
                    ct)
                .ConfigureAwait(false);

            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            IReadOnlyList<DataValue> values = await HistoryOps
                .ReadAllRawAsync(reader.Session, archived, startTime, endTime, 100, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The archive returned {values.Count} samples for the last hour.")
                .ConfigureAwait(false);

            Assert.That(values, Is.Not.Empty, "The archive holds samples for the last hour.");

            Assert.Multiple(() => {
                Assert.That(
                    values.Select(value => value.StatusCode),
                    Is.All.Matches<StatusCode>(StatusCode.IsGood),
                    "The archived samples are good values.");

                Assert.That(
                    values.Select(value => (DateTime)value.SourceTimestamp),
                    Is.Ordered.Ascending,
                    "A forward read returns the samples in the order they were recorded.");

                Assert.That(
                    values.Select(value => (DateTime)value.SourceTimestamp),
                    Is.All.InRange(startTime, endTime),
                    "Every sample lies inside the requested window.");
            });
        }

        /// <summary>
        /// A read at a time between two samples is interpolated by the framework.
        /// </summary>
        /// <remarks>
        /// The provider of this sample implements raw reads only, so a read at a
        /// point in time runs on the fallback of the stack: it reads the raw
        /// samples around the requested time and interpolates between them. This
        /// is the one place in the repository where that fallback is what is under
        /// test - the historian samples implement at-time reads natively.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadAtTimeIsInterpolatedByTheFramework(CancellationToken ct)
        {
            NodeId archived = await PathAsync(ct, "Data", "Dynamic", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            await using TestClient reader = await TestClient
                .ConnectAsync(
                    EndpointUrl,
                    "at-time reader",
                    new UserIdentity("history reader", Encoding.UTF8.GetBytes("history")),
                    ct)
                .ConfigureAwait(false);

            DateTime endTime = DateTime.UtcNow;

            IReadOnlyList<DataValue> samples = await HistoryOps
                .ReadAllRawAsync(reader.Session, archived, endTime.AddMinutes(-10), endTime, 100, ct)
                .ConfigureAwait(false);

            Assert.That(samples, Has.Count.GreaterThan(2), "The archive holds several samples of the last ten minutes.");

            // halfway between two recorded samples, where nothing was recorded.
            DateTime before = (DateTime)samples[1].SourceTimestamp;
            DateTime after = (DateTime)samples[2].SourceTimestamp;
            DateTime between = before.AddTicks((after - before).Ticks / 2);

            HistoryReadOutcome outcome = await HistoryOps
                .ReadAtTimeAsync(reader.Session, archived, [between], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Between {samples[1].WrappedValue} at {before:O} and {samples[2].WrappedValue} at {after:O} " +
                    $"the read at {between:O} answers {outcome.Values[0].WrappedValue} ({outcome.Values[0].StatusCode})")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(outcome.StatusCode), Is.True, $"Reading at a time failed: {outcome.StatusCode}");
                Assert.That(outcome.Values, Has.Count.EqualTo(1), "One time asked for, one value expected.");
                Assert.That((DateTime)outcome.Values[0].SourceTimestamp, Is.EqualTo(between), "The value carries the time asked for.");
                Assert.That(StatusCode.IsNotBad(outcome.Values[0].StatusCode), Is.True, "Between two good samples an interpolated value is not bad.");
                Assert.That(outcome.Values[0].WrappedValue.IsNull, Is.False, "An interpolated value carries a value.");
            });
        }

        /// <summary>
        /// An aggregate over the archive is computed by the framework.
        /// </summary>
        /// <remarks>
        /// The provider implements no processed read, so the average runs on the
        /// streaming fallback of the stack: the raw samples are read with their
        /// bounds and fed through the aggregate calculator of the server, one
        /// interval at a time. The samples are ten seconds apart, so a minute holds
        /// six of them and an average per minute summarises rather than repeats.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AverageIsComputedByTheFramework(CancellationToken ct)
        {
            NodeId archived = await PathAsync(ct, "Data", "Dynamic", "Scalar", "Int32Value")
                .ConfigureAwait(false);

            await using TestClient reader = await TestClient
                .ConnectAsync(
                    EndpointUrl,
                    "aggregate reader",
                    new UserIdentity("history reader", Encoding.UTF8.GetBytes("history")),
                    ct)
                .ConfigureAwait(false);

            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddMinutes(-10);

            IReadOnlyList<DataValue> samples = await HistoryOps
                .ReadAllRawAsync(reader.Session, archived, startTime, endTime, 100, ct)
                .ConfigureAwait(false);

            HistoryReadOutcome outcome = await HistoryOps.ReadProcessedAsync(
                reader.Session,
                archived,
                startTime,
                endTime,
                60_000,
                ObjectIds.AggregateFunction_Average,
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Average per minute over ten minutes: {outcome.StatusCode}, " +
                    $"{outcome.Values.Count} values from {samples.Count} samples")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(outcome.StatusCode), Is.True, $"Reading an average failed: {outcome.StatusCode}");
                Assert.That(outcome.Values, Has.Count.EqualTo(10), "Ten minutes in intervals of one minute are ten averages.");
                Assert.That(outcome.Values, Has.Count.LessThan(samples.Count), "An average summarises the raw samples rather than repeating them.");

                Assert.That(
                    outcome.Values.Select(value => (DateTime)value.SourceTimestamp),
                    Is.Ordered.Ascending,
                    "The averages are stamped with the start of their interval, in order.");
            });
        }
    }
}
