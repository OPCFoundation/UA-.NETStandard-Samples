/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Quickstarts.PerfTestClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The model of the PerfTest client, driven the way its window drives it: the test
    /// starts with the attach, the window reads the counters on a timer, and Stop ends it.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class PerfTestClientModelTests : ClientModelFixtureBase<PerfTestClientModel>
    {
        private static readonly TimeSpan kUpdateTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The publishing interval the window offers by default.
        /// </summary>
        private const int kSamplingRate = 100;

        /// <summary>
        /// Two publishing intervals, with room for a publish which is already in flight.
        /// </summary>
        private static readonly TimeSpan kQuietPeriod = TimeSpan.FromMilliseconds(kSamplingRate * 4);

        protected override string SampleName => "PerfTest";

        protected override PerfTestClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new PerfTestClientModel(telemetry) {
                SamplingRate = kSamplingRate,
                ItemCount = 100,
            };
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachStartsTheTestAndCountsTheUpdates(CancellationToken ct)
        {
            Assert.Multiple(() => {
                Assert.That(Model.IsRunning, Is.False, "A detached model already runs a test.");
                Assert.That(Model.ReadStatistics(), Is.EqualTo(PerfTestStatistics.Empty), "A model which is not running counted something.");
                Assert.That(Model.TakeMessages(), Is.Empty, "A model which is not running logged something.");
            });

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(Model.IsRunning, Is.True, "The test starts as soon as the session is attached.");

            // the server changes its registers continuously, so with a hundred items on a
            // 100 ms publishing interval the counters leave zero within a few intervals
            PerfTestStatistics counted = await Poll.UntilAsync(
                _ => Task.FromResult(Model.ReadStatistics()),
                statistics => statistics.TotalItemUpdateCount > 0,
                "no item update arrived",
                kUpdateTimeout,
                ct: ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The model counted {counted.MessageCount} messages and {counted.TotalItemUpdateCount} item updates.")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(counted.MessageCount, Is.GreaterThan(0), "Item updates arrived but no publish was counted, which cannot be.");
                Assert.That(counted.MaxItemUpdateCount, Is.GreaterThanOrEqualTo(counted.MinItemUpdateCount));
                Assert.That(counted.MaxItemUpdateCount, Is.GreaterThan(0), "No single item received an update.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadingTheStatisticsStartsANewInterval(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            await Poll.UntilAsync(
                _ => Task.FromResult(Model.ReadStatistics()),
                statistics => statistics.TotalItemUpdateCount > 0,
                "no item update arrived",
                kUpdateTimeout,
                ct: ct).ConfigureAwait(false);

            // the window reads on a timer and derives rates from each read, so a read has to
            // hand out only what arrived since the previous one - and the message count is
            // the one counter which keeps growing over the whole test
            PerfTestStatistics first = await Poll.UntilAsync(
                _ => Task.FromResult(Model.ReadStatistics()),
                statistics => statistics.TotalItemUpdateCount > 0 && statistics.Elapsed > TimeSpan.Zero,
                "no interval with two messages was seen",
                kUpdateTimeout,
                ct: ct).ConfigureAwait(false);

            PerfTestStatistics next = Model.ReadStatistics();

            Assert.Multiple(() => {
                Assert.That(next.MessageCount, Is.GreaterThanOrEqualTo(first.MessageCount), "The message count was reset by the read.");
                Assert.That(
                    next.FirstMessageTime,
                    Is.EqualTo(first.LastMessageTime),
                    "The last message of one interval is the first of the next, so the rates cover contiguous intervals.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheStartupMessagesAreHandedOutOnce(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            string[] messages = Model.TakeMessages();

            await TestContext.Out
                .WriteLineAsync("The tester logged: " + string.Join(" / ", messages))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    messages,
                    Has.Some.Contains("Time to add 100 items"),
                    "The tester reports how long the engine took to create the items on the server.");
                Assert.That(
                    messages,
                    Has.Some.Contains("publishing"),
                    "The tester reports how long enabling publishing took.");
            });

            Assert.That(Model.TakeMessages(), Is.Empty, "Taking the messages has to clear them, or the window shows them twice.");
        }

        /// <summary>
        /// The unbounded monitored item mode: one logical subscription holds more items
        /// than a single server side subscription is allowed to, by spreading them over
        /// partition subscriptions the caller never has to know about.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ABoundPerPartitionSpreadsTheItemsOverSeveralPartitions(CancellationToken ct)
        {
            // a bound far below the real limit of the server forces the split which a
            // server with a low MaxMonitoredItemsPerSubscription would force by itself
            Model.MaxMonitoredItemsPerPartition = 25;

            await AttachAsync(ct).ConfigureAwait(false);

            PerfTestPartitionStatistics partitions = Model.ReadPartitions();

            await TestContext.Out
                .WriteLineAsync($"100 items landed in {partitions.PartitionCount} partition(s).")
                .ConfigureAwait(false);

            Assert.That(
                partitions.PartitionCount,
                Is.GreaterThan(1),
                "A bound of 25 items per partition did not split the 100 items of the test.");

            // and the items really are monitored: the updates arrive, and from more than
            // one server side subscription
            PerfTestPartitionStatistics counted = await Poll.UntilAsync(
                _ => {
                    Model.ReadStatistics();
                    return Task.FromResult(Model.ReadPartitions());
                },
                statistics => statistics.UpdatesPerPartition.Count > 1,
                "updates arrived from at most one partition",
                kUpdateTimeout,
                ct: ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    counted.UpdatesPerPartition.Select(partition => partition.Value),
                    Has.All.GreaterThan(0),
                    "A partition was reported which delivered no update at all.");
                Assert.That(
                    counted.UpdatesPerPartition.Select(partition => partition.Key).Distinct().Count(),
                    Is.EqualTo(counted.UpdatesPerPartition.Count),
                    "The same server side subscription id was reported twice.");
            });
        }

        /// <summary>
        /// The common case stays the fast path: a block of items which fits into one
        /// server side subscription is not partitioned.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnItemCountBelowTheBoundStaysInOnePartition(CancellationToken ct)
        {
            Model.MaxMonitoredItemsPerPartition = 1000;

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(
                Model.ReadPartitions().PartitionCount,
                Is.EqualTo(1),
                "100 items were split even though a thousand fit into one partition.");
        }

        /// <summary>
        /// Affinity is the promise items which take part in a triggering relationship
        /// depend on: <c>SetTriggering</c> is scoped to one server side subscription, so a
        /// group which shares a tag must not be split across partitions.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnAffinityGroupIsKeptWithinOnePartition(CancellationToken ct)
        {
            // 100 items in groups of 10, into partitions of 20: two whole groups fill a
            // partition exactly, so the next group starts the next one and nothing is left
            // over. A bound which is a multiple of the group size is what makes that work.
            Model.MaxMonitoredItemsPerPartition = 20;
            Model.AffinityGroupSize = 10;

            await AttachAsync(ct).ConfigureAwait(false);

            PerfTestPartitionStatistics partitions = Model.ReadPartitions();
            string[] messages = Model.TakeMessages();

            await TestContext.Out
                .WriteLineAsync($"Ten affinity groups landed in {partitions.PartitionCount} partition(s).")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(partitions.PartitionCount, Is.GreaterThan(1), "The bound per partition did not split the items.");
                Assert.That(
                    messages,
                    Has.None.Contains("refused"),
                    "Groups of ten did not fill partitions of twenty cleanly: " + string.Join(" / ", messages));
            });

            await Poll.UntilAsync(
                _ => Task.FromResult(Model.ReadStatistics()),
                statistics => statistics.TotalItemUpdateCount > 0,
                "no item update arrived",
                kUpdateTimeout,
                ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The strict half of the contract: a group is pinned to the partition its first
        /// item landed in, and the engine refuses the rest of it rather than split it.
        /// </summary>
        /// <remarks>
        /// This is the trap a caller has to plan around. A group is not placed as a unit -
        /// the items arrive one at a time and the first one of a group decides its
        /// partition - so a bound which is not a multiple of the group size lets a group
        /// start near the end of a partition and lose its tail. 100 items in groups of ten
        /// into partitions of 25 refuses fifteen of them.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AGroupWhichDoesNotFitTheRemainingSpaceIsRefused(CancellationToken ct)
        {
            Model.MaxMonitoredItemsPerPartition = 25;
            Model.AffinityGroupSize = 10;

            await AttachAsync(ct).ConfigureAwait(false);

            string[] messages = Model.TakeMessages();

            await TestContext.Out
                .WriteLineAsync("The tester logged: " + string.Join(" / ", messages))
                .ConfigureAwait(false);

            Assert.That(
                messages,
                Has.Some.Contains("refused"),
                "A group was split across partitions instead of being refused, so the affinity contract was not kept.");
        }

        /// <summary>
        /// A group larger than a whole partition can never be placed.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnAffinityGroupLargerThanAPartitionIsRefused(CancellationToken ct)
        {
            Model.MaxMonitoredItemsPerPartition = 10;
            Model.AffinityGroupSize = 25;

            await AttachAsync(ct).ConfigureAwait(false);

            string[] messages = Model.TakeMessages();

            await TestContext.Out
                .WriteLineAsync("The tester logged: " + string.Join(" / ", messages))
                .ConfigureAwait(false);

            Assert.That(
                messages,
                Has.Some.Contains("refused"),
                "A group of twenty five was placed into partitions of ten, so the affinity contract was not kept.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ADetachedModelReportsNoPartitions(CancellationToken ct)
        {
            Assert.That(Model.ReadPartitions(), Is.EqualTo(PerfTestPartitionStatistics.Empty));

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.StopAsync(ct).ConfigureAwait(false);

            Assert.That(Model.ReadPartitions(), Is.EqualTo(PerfTestPartitionStatistics.Empty), "A stopped test still reports partitions.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task StopEndsTheTestButKeepsTheSession(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            await Poll.UntilAsync(
                _ => Task.FromResult(Model.ReadStatistics()),
                statistics => statistics.TotalItemUpdateCount > 0,
                "no item update arrived",
                kUpdateTimeout,
                ct: ct).ConfigureAwait(false);

            await Model.StopAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.IsRunning, Is.False, "Stop did not end the test.");
                Assert.That(Model.IsConnected, Is.True, "Stop must not detach the session; only Disconnect does.");
                Assert.That(Model.ReadStatistics(), Is.EqualTo(PerfTestStatistics.Empty), "A stopped test still reports counters.");
                Assert.That(SubscriptionCount(), Is.Zero, "Stop has to delete the subscription of the tester.");
            });

            // and nothing arrives any more: the subscription is gone, not just ignored
            await Task.Delay(kQuietPeriod, ct).ConfigureAwait(false);

            Assert.That(Model.ReadStatistics(), Is.EqualTo(PerfTestStatistics.Empty), "Updates kept arriving after Stop.");

            // stopping twice is harmless, the window's Stop button stays clickable
            await Model.StopAsync(ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachDeletesTheSubscription(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(SubscriptionCount(), Is.EqualTo(1), "The attach creates exactly one subscription for the test.");

            await Model.DetachAsync().ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.IsRunning, Is.False);
                Assert.That(Model.IsConnected, Is.False);
                Assert.That(
                    SubscriptionCount(),
                    Is.Zero,
                    "The detach has to delete the subscription before the window closes the session.");
            });
        }

        /// <summary>
        /// How many subscriptions the V2 engine of the session holds.
        /// </summary>
        private int SubscriptionCount()
        {
            Assert.That(
                Session.TryGetSubscriptionManager(out ISubscriptionManager manager),
                Is.True,
                "The session does not run the V2 subscription engine.");

            return manager.Items.Count();
        }
    }
}
