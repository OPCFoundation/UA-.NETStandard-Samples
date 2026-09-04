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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Samples.Client;

namespace Quickstarts.PerfTestClient.Model
{
    /// <summary>
    /// What the tester counted since the last time it was asked.
    /// </summary>
    /// <param name="MessageCount">The publish responses which arrived since the test started.</param>
    /// <param name="TotalItemUpdateCount">The item updates which arrived since the last read.</param>
    /// <param name="FirstMessageTime">When the interval the counts cover began, in UTC.</param>
    /// <param name="LastMessageTime">When the last message of the interval arrived, in UTC.</param>
    /// <param name="MinItemUpdateCount">The fewest updates any one item received in the interval.</param>
    /// <param name="MaxItemUpdateCount">The most updates any one item received in the interval.</param>
    public sealed record PerfTestStatistics(
        int MessageCount,
        int TotalItemUpdateCount,
        DateTime FirstMessageTime,
        DateTime LastMessageTime,
        int MinItemUpdateCount,
        int MaxItemUpdateCount)
    {
        /// <summary>
        /// Nothing counted, which is what a model which is not running reports.
        /// </summary>
        public static PerfTestStatistics Empty { get; } = new PerfTestStatistics(0, 0, DateTime.MinValue, DateTime.MinValue, 0, 0);

        /// <summary>
        /// How long the interval the counts cover lasted; zero when fewer than two messages arrived.
        /// </summary>
        public TimeSpan Elapsed => LastMessageTime > FirstMessageTime ? LastMessageTime - FirstMessageTime : TimeSpan.Zero;
    }

    /// <summary>
    /// How the monitored items of a running test are spread over the server side
    /// subscriptions the V2 engine created for them.
    /// </summary>
    /// <param name="PartitionCount">How many server side subscriptions carry the items;
    /// one while the whole block fits into a single subscription.</param>
    /// <param name="UpdatesPerPartition">The item updates each partition delivered since
    /// the test started, by the server side subscription id.</param>
    public sealed record PerfTestPartitionStatistics(
        int PartitionCount,
        IReadOnlyList<KeyValuePair<uint, int>> UpdatesPerPartition)
    {
        /// <summary>
        /// What a model which is not running reports.
        /// </summary>
        public static PerfTestPartitionStatistics Empty { get; } =
            new PerfTestPartitionStatistics(0, Array.Empty<KeyValuePair<uint, int>>());
    }

    /// <summary>
    /// The client model of the PerfTest client: subscribes to a block of register items as
    /// soon as it is attached and counts the updates which arrive.
    /// </summary>
    /// <remarks>
    /// The measuring itself is the <see cref="Tester"/>, which is the notification handler
    /// of the subscription it creates and keeps its counters under a lock, because the
    /// engine calls it on a publish worker. The model owns the tester's lifetime: the test
    /// starts when a session is attached and stops when the caller asks or the session is
    /// detached, and what it counted is read out on whatever schedule the caller likes.
    /// </remarks>
    public sealed class PerfTestClientModel : SampleClientModel
    {
        private Tester m_tester;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public PerfTestClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The publishing interval of the subscription in milliseconds. Set before attaching.
        /// </summary>
        public int SamplingRate { get; set; } = 100;

        /// <summary>
        /// How many register items the subscription monitors. Set before attaching.
        /// </summary>
        public int ItemCount { get; set; } = 100;

        /// <summary>
        /// The upper bound of monitored items per server side partition subscription;
        /// zero lets the engine discover the effective limit of the server. Set before
        /// attaching.
        /// </summary>
        public int MaxMonitoredItemsPerPartition { get; set; }

        /// <summary>
        /// Whether the subscription stays bound to one server side subscription, so that
        /// items beyond the server cap fail instead of moving to a new partition. Set
        /// before attaching.
        /// </summary>
        public bool DisableUnboundedItemMode { get; set; }

        /// <summary>
        /// How many consecutive items share an affinity tag and are therefore guaranteed
        /// to land in the same partition; zero for no affinity. Set before attaching.
        /// </summary>
        /// <remarks>
        /// This is what items which take part in a triggering relationship need, because
        /// <c>SetTriggering</c> is scoped to one server side subscription.
        /// </remarks>
        public int AffinityGroupSize { get; set; }

        /// <summary>
        /// Whether a test is running right now.
        /// </summary>
        public bool IsRunning => m_tester != null;

        /// <summary>
        /// Ends the test the attach started, without detaching the session.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task StopAsync(CancellationToken ct = default)
        {
            Tester tester = m_tester;
            m_tester = null;

            if (tester != null)
            {
                // disposing the subscription deletes it on the server and drops it from the
                // subscription manager, which also stops the notifications
                await tester.StopAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads what the tester counted since the last read, and starts a new interval.
        /// </summary>
        /// <remarks>
        /// Reading is what closes an interval: the per item counts and the update total
        /// start again from zero, and the last message of this interval becomes the first
        /// of the next, so that the rates a caller derives cover contiguous intervals. The
        /// message count is not reset.
        /// </remarks>
        public PerfTestStatistics ReadStatistics()
        {
            Tester tester = m_tester;

            if (tester == null)
            {
                return PerfTestStatistics.Empty;
            }

            tester.GetStatistics(
                out int messageCount,
                out int totalItemUpdateCount,
                out DateTime firstMessageTime,
                out DateTime lastMessageTime,
                out int minItemUpdateCount,
                out int maxItemUpdateCount);

            return new PerfTestStatistics(
                messageCount,
                totalItemUpdateCount,
                firstMessageTime,
                lastMessageTime,
                minItemUpdateCount,
                maxItemUpdateCount);
        }

        /// <summary>
        /// Takes the messages the tester logged since the last call; empty when nothing is running.
        /// </summary>
        public string[] TakeMessages()
        {
            return m_tester?.GetMessages() ?? Array.Empty<string>();
        }

        /// <summary>
        /// How the items of the running test are spread over server side subscriptions.
        /// </summary>
        public PerfTestPartitionStatistics ReadPartitions()
        {
            Tester tester = m_tester;

            if (tester == null)
            {
                return PerfTestPartitionStatistics.Empty;
            }

            tester.GetPartitions(out int partitionCount, out IReadOnlyList<KeyValuePair<uint, int>> updatesPerPartition);

            return new PerfTestPartitionStatistics(partitionCount, updatesPerPartition);
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            await StopAsync(ct).ConfigureAwait(false);

            var tester = new Tester {
                SamplingRate = SamplingRate,
                ItemCount = ItemCount,
                MaxMonitoredItemsPerPartition = MaxMonitoredItemsPerPartition,
                DisableUnboundedItemMode = DisableUnboundedItemMode,
                AffinityGroupSize = AffinityGroupSize,
            };

            await tester.StartAsync(RequireSession(), Telemetry).ConfigureAwait(false);

            m_tester = tester;
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            // the subscription goes before the session is closed: closing a session which
            // still carries one waits for the publish pipeline to drain
            return StopAsync();
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so the reconnect hooks of the base
        // class are not overridden: the tester keeps counting.
    }
}
