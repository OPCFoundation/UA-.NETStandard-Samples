/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// Processes the notifications of a callback subscription one at a time, in the order
    /// they arrived, on a single consumer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The V2 engine delivers a notification batch on a publish worker and expects the
    /// callback to return quickly. A model whose processing has to await something per
    /// notification - a type lookup in the node cache, a browse of the supertypes - cannot
    /// do that inside the callback, and doing it in a fire and forget continuation lets a
    /// second batch overtake the first at every await. That is precisely what corrupted the
    /// condition list of the alarm client (the reentrancy <c>docs/TESTING.md</c> records).
    /// </para>
    /// <para>
    /// So the callback only <see cref="Post"/>s, and one consumer does the awaiting. The
    /// handler sees every notification in arrival order and is never entered twice.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The notification type.</typeparam>
    public sealed class SerialNotificationPump<T> : IAsyncDisposable
    {
        private static readonly TimeSpan kStopTimeout = TimeSpan.FromSeconds(5);

        private readonly Channel<T> m_channel = Channel.CreateUnbounded<T>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly Func<T, CancellationToken, Task> m_handler;
        private readonly Action<T, Exception> m_onError;
        private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
        private Task m_consumer;
        private bool m_disposed;

        /// <summary>
        /// Creates the pump.
        /// </summary>
        /// <param name="handler">Processes one notification. May await.</param>
        /// <param name="onError">Called when the handler throws; the pump keeps going.</param>
        public SerialNotificationPump(
            Func<T, CancellationToken, Task> handler,
            Action<T, Exception> onError)
        {
            m_handler = handler ?? throw new ArgumentNullException(nameof(handler));
            m_onError = onError ?? throw new ArgumentNullException(nameof(onError));
        }

        /// <summary>
        /// Starts the consumer.
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            m_consumer ??= ConsumeAsync();
        }

        /// <summary>
        /// Queues a notification. Returns false once the pump was stopped.
        /// </summary>
        public bool Post(T notification)
        {
            return m_channel.Writer.TryWrite(notification);
        }

        /// <summary>
        /// Stops accepting notifications, cancels the handler and waits for the consumer.
        /// </summary>
        public async Task StopAsync()
        {
            m_channel.Writer.TryComplete();

            if (!m_cancellation.IsCancellationRequested)
            {
                await m_cancellation.CancelAsync().ConfigureAwait(false);
            }

            if (m_consumer == null)
            {
                return;
            }

            try
            {
                await m_consumer.WaitAsync(kStopTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // the consumer ended the way it was asked to
            }
            catch (TimeoutException)
            {
                // the handler did not answer the cancellation in time
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            await StopAsync().ConfigureAwait(false);
            m_disposed = true;
            m_cancellation.Dispose();
        }

        private async Task ConsumeAsync()
        {
            CancellationToken ct = m_cancellation.Token;

            try
            {
                await foreach (T notification in m_channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        await m_handler(notification, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        m_onError(notification, e);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
        }
    }
}
