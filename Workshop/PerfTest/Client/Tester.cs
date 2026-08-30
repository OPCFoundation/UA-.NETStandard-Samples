/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

namespace Quickstarts.PerfTestClient
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Subscription lifetime is managed by StartAsync and StopAsync in this sample.")]
    internal sealed class Tester : ISubscriptionNotificationHandler
    {
        // gets or sets the update rate.
        public int SamplingRate
        {
            get { return m_samplingRate; }
            set { m_samplingRate = value; }
        }

        // gets or sets the item count.
        public int ItemCount
        {
            get { return m_itemCount; }
            set { m_itemCount = value; }
        }

        // returns the number of callbacks that have arrived.
        public int MessageCount
        {
            get { return m_messageCount; }
        }

        // returns the total number of item updates that have arrived.
        public int TotalItemUpdateCount
        {
            get { return m_totalItemUpdateCount; }
        }

        // returns the time of the first callback.
        public DateTime FirstMessageTime
        {
            get { return m_firstMessageTime; }
        }

        // returns the time of the last callback.
        public DateTime LastMessageTime
        {
            get { return m_lastMessageTime; }
        }

        /// <summary>
        /// Gets the last sequence number.
        /// </summary>
        /// <value>The last sequence number.</value>
        public string[] GetMessages()
        {
            lock (m_lock)
            {
                string[] strings = m_logMessages.ToArray();
                m_logMessages.Clear();
                return strings;
            }
        }

        /// <summary>
        /// Gets the statistics.
        /// </summary>
        /// <param name="messageCount">The message count.</param>
        /// <param name="totalItemUpdateCount">The total item update count.</param>
        /// <param name="firstMessageTime">The first message time.</param>
        /// <param name="lastMessageTime">The last message time.</param>
        /// <param name="minItemUpdateCount">The min item update count.</param>
        /// <param name="maxItemUpdateCount">The max item update count.</param>
        public void GetStatistics(
            out int messageCount,
            out int totalItemUpdateCount,
            out DateTime firstMessageTime,
            out DateTime lastMessageTime,
            out int minItemUpdateCount,
            out int maxItemUpdateCount)
        {
            lock (m_lock)
            {
                messageCount = m_messageCount;
                totalItemUpdateCount = m_totalItemUpdateCount;
                firstMessageTime = m_firstMessageTime;
                lastMessageTime = m_lastMessageTime;
                minItemUpdateCount = Int32.MaxValue;
                maxItemUpdateCount = 0;

                if (m_itemUpdateCounts != null)
                {
                    for (int ii = 0; ii < m_itemUpdateCounts.Length; ii++)
                    {
                        if (minItemUpdateCount > m_itemUpdateCounts[ii])
                        {
                            minItemUpdateCount = m_itemUpdateCounts[ii];
                        }

                        if (maxItemUpdateCount < m_itemUpdateCounts[ii])
                        {
                            maxItemUpdateCount = m_itemUpdateCounts[ii];
                        }
                    }
                }

                m_totalItemUpdateCount = 0;
                m_firstMessageTime = m_lastMessageTime;
                m_lastMessageTime = DateTime.MinValue;
                m_itemUpdateCounts = new int[m_itemCount];
            }
        }

        /// <summary>
        /// Starts the specified session.
        /// </summary>
        /// <param name="session">The session.</param>
        public async Task StartAsync(ISession session, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            // the test measures how fast the notifications arrive, so it takes them straight
            // off the engine: the tester itself is the notification handler.
            m_options = new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions {
                PublishingInterval = TimeSpan.FromMilliseconds(m_samplingRate),
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 50000,
                PublishingEnabled = false,
                Priority = 1,
            });

            ISubscription subscription = m_subscription = manager.Add(this, m_options);

            DateTime start = DateTime.UtcNow;

            var indexes = new Dictionary<uint, int>();

            for (int ii = 0; ii < m_itemCount; ii++)
            {
                var options = new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                    StartNodeId = new NodeId((uint)((1 << 24) + ii), 2),
                    AttributeId = Attributes.Value,
                    TimestampsToReturn = TimestampsToReturn.Neither,
                    SamplingInterval = TimeSpan.FromMilliseconds(-1),
                    QueueSize = 0,
                    DiscardOldest = true,
                    MonitoringMode = MonitoringMode.Reporting,
                });

                if (subscription.MonitoredItems.TryAdd(
                    Utils.Format("Item{0}", ii),
                    options,
                    out IMonitoredItem monitoredItem))
                {
                    // the engine assigns the client handle, so the test keeps the mapping it
                    // used to get by constructing the item with the index as its handle.
                    indexes[monitoredItem.ClientHandle] = ii;
                }
            }

            // the engine applies the added items on its own worker, so the time it takes for
            // them to exist on the server is the time until nothing is pending any more.
            await WaitForPendingChangesAsync(subscription);
            DateTime end = DateTime.UtcNow;

            lock (m_lock)
            {
                m_itemIndexes = indexes;
            }

            ReportMessage("Time to add {1} items {0}ms.", (end - start).TotalMilliseconds, m_itemCount);

            // reconfiguring the options monitor is what modifies the subscription; there is no
            // SetPublishingMode call in the V2 engine.
            start = DateTime.UtcNow;
            m_options.Configure(options => options with { PublishingEnabled = true });
            end = DateTime.UtcNow;

            ReportMessage("Time to emable publishing {0}ms.", (end - start).TotalMilliseconds);
        }

        /// <summary>
        /// Waits until the engine has applied the pending monitored item changes.
        /// </summary>
        private static async Task WaitForPendingChangesAsync(ISubscription subscription, CancellationToken ct = default)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                bool pending = false;

                foreach (IMonitoredItem monitoredItem in subscription.MonitoredItems.Items)
                {
                    if (monitoredItem is Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItemApplyState state &&
                        state.HasPendingChanges)
                    {
                        pending = true;
                        break;
                    }
                }

                if (!pending)
                {
                    return;
                }

                await Task.Delay(10, ct);
            }
        }

        /// <summary>
        /// Stops the test.
        /// </summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            ISubscription subscription = null;

            lock (m_lock)
            {
                subscription = m_subscription;
                m_subscription = null;
                m_itemIndexes = null;
            }

            if (subscription != null)
            {
                // disposing the subscription deletes it on the server and drops it from the
                // subscription manager, which also stops the notifications.
                await subscription.DisposeAsync();
            }
        }

        void ReportMessage(string message, params object[] args)
        {
            lock (m_lock)
            {
                if (m_logMessages == null)
                {
                    m_logMessages = new List<string>();
                }

                if (args != null && args.Length > 0)
                {
                    m_logMessages.Add(Utils.Format(message, args));
                }
                else
                {
                    m_logMessages.Add(message);
                }
            }
        }

        /// <summary>
        /// Counts one publish response worth of data changes.
        /// </summary>
        /// <remarks>
        /// The engine hands the decoded values of the whole notification over, which is what
        /// the test used to decode out of the raw notification message itself. The buffer
        /// belongs to the engine, so nothing is kept beyond this call.
        /// </remarks>
        ValueTask ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            lock (m_lock)
            {
                if (m_messageCount == 0)
                {
                    m_firstMessageTime = DateTime.UtcNow;
                    m_totalItemUpdateCount = 0;
                    m_itemUpdateCounts = new int[m_itemCount];
                }

                m_messageCount++;
                m_lastMessageTime = DateTime.UtcNow;

                ReadOnlySpan<DataValueChange> changes = notification.Span;

                for (int ii = 0; ii < changes.Length; ii++)
                {
                    m_totalItemUpdateCount++;

                    if (changes[ii].MonitoredItem == null || m_itemIndexes == null)
                    {
                        continue;
                    }

                    if (m_itemIndexes.TryGetValue(changes[ii].MonitoredItem.ClientHandle, out int index) &&
                        index >= 0 && index < m_itemUpdateCounts.Length)
                    {
                        m_itemUpdateCounts[index]++;
                    }
                }
            }

            return default;
        }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnEventDataNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            return default;
        }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnKeepAliveNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask)
        {
            return default;
        }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnSubscriptionStateChangedAsync(
            ISubscription subscription,
            Opc.Ua.Client.Subscriptions.SubscriptionState state,
            PublishState publishStateMask,
            CancellationToken ct)
        {
            return default;
        }

        private object m_lock = new object();
        private List<string> m_logMessages;
        private int m_samplingRate;
        private int m_itemCount;
        private int m_messageCount;
        private int m_totalItemUpdateCount;
        private DateTime m_firstMessageTime;
        private DateTime m_lastMessageTime;
        private int[] m_itemUpdateCounts;
        private ISubscription m_subscription;
        private OptionsMonitor<SubscriptionOptions> m_options;

        /// <summary>
        /// The index of each item by the client handle the engine assigned to it.
        /// </summary>
        private Dictionary<uint, int> m_itemIndexes;
    }
}
