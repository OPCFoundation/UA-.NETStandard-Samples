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

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// Runs the enumerations of a streaming subscription for as long as a model is
    /// attached, and stops them when it detaches.
    /// </summary>
    /// <remarks>
    /// A streaming enumeration (<c>await foreach</c> over a subscription of the V2 engine)
    /// runs until its token is cancelled. The pump owns that token and the tasks of the
    /// enumerations, so that <see cref="StopAsync"/> can wait for them: nothing is raised
    /// after a model has detached, and nothing is left running on a session the window
    /// is about to close.
    /// </remarks>
    public sealed class SubscriptionPump : IAsyncDisposable
    {
        private static readonly TimeSpan kDefaultStopTimeout = TimeSpan.FromSeconds(5);

        private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
        private readonly List<Task> m_tasks = new List<Task>();
        private bool m_disposed;

        /// <summary>
        /// The token the enumerations run on.
        /// </summary>
        public CancellationToken Token => m_cancellation.Token;

        /// <summary>
        /// True until the pump was stopped.
        /// </summary>
        public bool IsRunning => !m_cancellation.IsCancellationRequested;

        /// <summary>
        /// Starts one enumeration.
        /// </summary>
        /// <param name="pump">The enumeration, which runs until the token is cancelled.</param>
        public void Run(Func<CancellationToken, Task> pump)
        {
            ArgumentNullException.ThrowIfNull(pump);
            ObjectDisposedException.ThrowIf(m_disposed, this);

            Task task;

            try
            {
                task = pump(m_cancellation.Token);
            }
            catch (Exception e)
            {
                task = Task.FromException(e);
            }

            lock (m_tasks)
            {
                m_tasks.Add(task);
            }
        }

        /// <summary>
        /// Cancels the enumerations and waits for them to end.
        /// </summary>
        /// <remarks>
        /// The wait is bounded: an enumeration which does not answer its cancellation in
        /// time is left to end on its own, which it does once the subscription is gone.
        /// Failures of the enumerations are not reported here - each pump logs its own.
        /// </remarks>
        /// <param name="timeout">How long to wait. Five seconds by default.</param>
        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (!m_cancellation.IsCancellationRequested)
            {
                await m_cancellation.CancelAsync().ConfigureAwait(false);
            }

            Task[] tasks;

            lock (m_tasks)
            {
                tasks = m_tasks.ToArray();
            }

            if (tasks.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(timeout ?? kDefaultStopTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // the enumerations ended the way they were asked to
            }
            catch (TimeoutException)
            {
                // an enumeration did not answer the cancellation in time
            }
            catch (Exception)
            {
                // an enumeration failed earlier and has logged that itself
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
    }
}
