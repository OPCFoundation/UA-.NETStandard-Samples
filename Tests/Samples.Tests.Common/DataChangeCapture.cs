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
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Subscribes to the value of a node and collects what the server sends.
    /// </summary>
    /// <remarks>
    /// Several sample node managers only do their work while somebody is listening: the
    /// historical access server runs its simulation while an item is monitored, the data
    /// access server starts a block when it is first subscribed to. A test for those has
    /// to subscribe the way a client does and then wait for notifications, which is what
    /// this collects.
    /// </remarks>
    public sealed class DataChangeCapture : IAsyncDisposable
    {
        private readonly ISession m_session;
        private readonly Subscription m_subscription;
        private readonly MonitoredItem m_item;
        private readonly Channel<DataValue> m_values;
        private readonly MonitoredItemNotificationEventHandler m_handler;
        private bool m_disposed;

        private DataChangeCapture(ISession session, Subscription subscription, MonitoredItem item)
        {
            m_session = session;
            m_subscription = subscription;
            m_item = item;
            m_values = Channel.CreateUnbounded<DataValue>();
            m_handler = OnNotification;
            m_item.Notification += m_handler;
        }

        /// <summary>
        /// The status the server gave the monitored item when it was created.
        /// </summary>
        public ServiceResult ItemError => m_item.Status?.Error;

        /// <summary>
        /// The filter the server revised the requested one into.
        /// </summary>
        public MonitoringFilter RevisedFilter => m_item.Status?.Filter;

        /// <summary>
        /// The sampling interval the server revised the requested one into.
        /// </summary>
        public double RevisedSamplingInterval => m_item.Status?.SamplingInterval ?? 0;

        /// <summary>
        /// Subscribes to the value of a node.
        /// </summary>
        /// <param name="session">The session to subscribe on.</param>
        /// <param name="nodeId">The node to watch.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <param name="samplingInterval">The sampling interval to ask for.</param>
        /// <param name="publishingInterval">The publishing interval to ask for.</param>
        /// <param name="filter">An optional monitoring filter.</param>
        /// <param name="indexRange">An optional index range.</param>
        /// <param name="dataEncoding">An optional data encoding.</param>
        /// <param name="throwOnItemError">
        /// False to return the capture even when the server refused the item, so that the
        /// caller can assert the status code the sample answered with.
        /// </param>
        public static async Task<DataChangeCapture> CreateAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct,
            int samplingInterval = 100,
            int publishingInterval = 250,
            MonitoringFilter filter = null,
            string indexRange = null,
            QualifiedName dataEncoding = default,
            bool throwOnItemError = true)
        {
            ArgumentNullException.ThrowIfNull(session);

            var subscription = new Subscription(NullTelemetry.Instance) {
                DisplayName = null,
                PublishingInterval = publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                PublishingEnabled = true,
                TimestampsToReturn = TimestampsToReturn.Both,
            };

            DataChangeCapture capture = null;

            try
            {
                session.AddSubscription(subscription);
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                var item = new MonitoredItem(NullTelemetry.Instance) {
                    StartNodeId = nodeId,
                    AttributeId = Attributes.Value,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = samplingInterval,
                    QueueSize = 1000,
                    DiscardOldest = true,
                    Filter = filter,
                    IndexRange = indexRange,
                    Encoding = dataEncoding,
                };

                capture = new DataChangeCapture(session, subscription, item);

                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

                if (throwOnItemError && item.Status?.Error != null && ServiceResult.IsBad(item.Status.Error))
                {
                    throw new ServiceResultException(item.Status.Error);
                }

                return capture;
            }
            catch
            {
                if (capture != null)
                {
                    await capture.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await SafeRemoveAsync(session, subscription).ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>
        /// Waits for the next notification.
        /// </summary>
        public async Task<DataValue> NextAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            using var timer = new CancellationTokenSource(timeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

            try
            {
                return await m_values.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested)
            {
                throw new TimeoutException(string.Format(
                    CultureInfo.InvariantCulture,
                    "No data change for {0} arrived within {1:0.#} s.",
                    m_item.StartNodeId,
                    timeout.TotalSeconds));
            }
        }

        /// <summary>
        /// Collects notifications until the requested number of distinct values was seen.
        /// </summary>
        /// <remarks>
        /// Distinct values rather than notifications, because a test which asserts that a
        /// simulation runs wants to see the value move. A server which republishes the same
        /// value is not proof of that.
        /// </remarks>
        public async Task<IReadOnlyList<DataValue>> CollectDistinctAsync(
            int count,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            var distinct = new List<DataValue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            long started = Environment.TickCount64;

            while (distinct.Count < count)
            {
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
                TimeSpan left = timeout - elapsed;

                if (left <= TimeSpan.Zero)
                {
                    throw new TimeoutException(string.Format(
                        CultureInfo.InvariantCulture,
                        "Only {0} of {1} distinct values for {2} arrived within {3:0.#} s.",
                        distinct.Count,
                        count,
                        m_item.StartNodeId,
                        timeout.TotalSeconds));
                }

                DataValue value = await NextAsync(left, ct).ConfigureAwait(false);

                string key = string.Format(CultureInfo.InvariantCulture, "{0}", value.WrappedValue);

                if (seen.Add(key))
                {
                    distinct.Add(value);
                }
            }

            return distinct;
        }

        /// <summary>
        /// Collects distinct values until one of them satisfies the condition.
        /// </summary>
        /// <remarks>
        /// A subscription reports the current value before anything else happens, so a test
        /// which watches for the effect of an action it triggers has to be able to say
        /// "collect until you see this", rather than counting notifications from a starting
        /// point it does not control.
        /// </remarks>
        public async Task<IReadOnlyList<DataValue>> CollectDistinctUntilAsync(
            Func<DataValue, bool> until,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(until);

            var distinct = new List<DataValue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            long started = Environment.TickCount64;

            while (true)
            {
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
                TimeSpan left = timeout - elapsed;

                if (left <= TimeSpan.Zero)
                {
                    throw new TimeoutException(string.Format(
                        CultureInfo.InvariantCulture,
                        "The awaited value for {0} did not arrive within {1:0.#} s. Seen so far: {2}",
                        m_item.StartNodeId,
                        timeout.TotalSeconds,
                        string.Join(", ", distinct.Select(value => value.WrappedValue.ToString()))));
                }

                DataValue value = await NextAsync(left, ct).ConfigureAwait(false);

                string key = string.Format(CultureInfo.InvariantCulture, "{0}", value.WrappedValue);

                if (seen.Add(key))
                {
                    distinct.Add(value);
                }

                if (until(value))
                {
                    return distinct;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_item.Notification -= m_handler;
            m_values.Writer.TryComplete();

            await SafeRemoveAsync(m_session, m_subscription).ConfigureAwait(false);
        }

        private void OnNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (e.NotificationValue is MonitoredItemNotification notification)
            {
                m_values.Writer.TryWrite(notification.Value);
            }
        }

        private static async Task SafeRemoveAsync(ISession session, Subscription subscription)
        {
            try
            {
                if (subscription.Created)
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }

                await session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // tearing down a subscription on a server which is already gone, or which
                // dropped it itself, must not fail the test that was using it
            }

            subscription.Dispose();
        }
    }
}
