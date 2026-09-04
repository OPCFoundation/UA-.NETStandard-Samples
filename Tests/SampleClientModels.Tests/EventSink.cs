/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Collects the events of a client model so a test can wait for one.
    /// </summary>
    /// <remarks>
    /// A model constructed without a <see cref="SynchronizationContext"/> raises its events
    /// inline, on the publish worker of the subscription engine or on whichever thread
    /// completed the operation, so the sink is thread safe and the waits poll it. It also
    /// counts how many handlers are inside it at once, which is how a test proves that a
    /// model delivers serially.
    /// </remarks>
    /// <typeparam name="TArgs">The event arguments.</typeparam>
    public sealed class EventSink<TArgs>
    {
        private readonly ConcurrentQueue<TArgs> m_events = new ConcurrentQueue<TArgs>();
        private int m_inside;
        private int m_maxInside;

        /// <summary>
        /// The handler to subscribe to the event.
        /// </summary>
        public void Handle(object sender, TArgs args)
        {
            int inside = Interlocked.Increment(ref m_inside);

            try
            {
                int seen;

                do
                {
                    seen = m_maxInside;
                }
                while (inside > seen && Interlocked.CompareExchange(ref m_maxInside, inside, seen) != seen);

                m_events.Enqueue(args);
            }
            finally
            {
                Interlocked.Decrement(ref m_inside);
            }
        }

        /// <summary>
        /// Everything received so far, in order.
        /// </summary>
        public IReadOnlyList<TArgs> Events => m_events.ToArray();

        /// <summary>
        /// How many events were received so far.
        /// </summary>
        public int Count => m_events.Count;

        /// <summary>
        /// The most handlers that were ever inside the sink at the same time.
        /// </summary>
        public int MaxConcurrency => m_maxInside;

        /// <summary>
        /// Waits for the first event which satisfies the predicate.
        /// </summary>
        /// <returns>That event, or a failure which says how many events were seen instead.</returns>
        public async Task<TArgs> WaitForAsync(
            Func<TArgs, bool> predicate,
            string because,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            return await Poll.UntilAsync(
                _ => Task.FromResult(m_events.FirstOrDefault(predicate)),
                found => found != null,
                $"{because} (events seen: {Count})",
                timeout,
                ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Waits for the sink to hold at least the given number of events.
        /// </summary>
        public async Task WaitForCountAsync(
            int count,
            string because,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            await Poll.UntilAsync(
                _ => Task.FromResult(Count),
                seen => seen >= count,
                because,
                timeout,
                ct: ct).ConfigureAwait(false);
        }
    }
}
