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
