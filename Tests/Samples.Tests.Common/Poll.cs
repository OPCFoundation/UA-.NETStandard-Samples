/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Waits for a server to reach a state, without sleeping for a guessed duration.
    /// </summary>
    /// <remarks>
    /// Most of the sample node managers are driven by a timer, so a test which asserts
    /// their behaviour has to wait for something to happen. Sleeping for the length of
    /// the timer makes the suite slow when it works and flaky when the machine is busy,
    /// so every wait in these tests is a bounded poll instead: it returns the moment the
    /// condition holds and reports what it last saw when it does not.
    /// </remarks>
    public static class Poll
    {
        /// <summary>
        /// The time a condition gets before it counts as never happening.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The pause between two attempts.
        /// </summary>
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Repeats the probe until it returns a value the condition accepts.
        /// </summary>
        /// <param name="probe">Asks the server for the current value.</param>
        /// <param name="accept">Decides whether the value is the one the test waits for.</param>
        /// <param name="because">What the caller is waiting for, used in the failure message.</param>
        /// <param name="timeout">How long to keep trying. Defaults to <see cref="DefaultTimeout"/>.</param>
        /// <param name="interval">The pause between attempts. Defaults to <see cref="DefaultInterval"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The first accepted value.</returns>
        /// <exception cref="TimeoutException">
        /// The condition did not hold within the timeout. The message carries the last value
        /// seen, which is the piece of information a failing regression test has to report.
        /// </exception>
        public static async Task<T> UntilAsync<T>(
            Func<CancellationToken, Task<T>> probe,
            Func<T, bool> accept,
            string because,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(probe);
            ArgumentNullException.ThrowIfNull(accept);

            TimeSpan limit = timeout ?? DefaultTimeout;
            TimeSpan pause = interval ?? DefaultInterval;

            long started = Environment.TickCount64;
            T last = default;
            int attempts = 0;

            while (true)
            {
                last = await probe(ct).ConfigureAwait(false);
                attempts++;

                if (accept(last))
                {
                    return last;
                }

                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

                if (elapsed >= limit)
                {
                    throw new TimeoutException(string.Format(
                        CultureInfo.InvariantCulture,
                        "Timed out after {0:0.#} s and {1} attempts waiting for {2}. The last value was: {3}",
                        limit.TotalSeconds,
                        attempts,
                        because,
                        Describe(last)));
                }

                await Task.Delay(pause, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Repeats the probe until it stops throwing and the condition accepts its value.
        /// </summary>
        /// <remarks>
        /// A node which the server creates only once a simulation has run does not exist
        /// yet when the fixture starts, and asking for it fails rather than returning
        /// something the condition can reject. This overload treats such a failure as a
        /// value which is not there yet, and reports the last exception if it never is.
        /// </remarks>
        public static async Task<T> UntilNoThrowAsync<T>(
            Func<CancellationToken, Task<T>> probe,
            Func<T, bool> accept,
            string because,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(probe);
            ArgumentNullException.ThrowIfNull(accept);

            TimeSpan limit = timeout ?? DefaultTimeout;
            TimeSpan pause = interval ?? DefaultInterval;

            long started = Environment.TickCount64;
            Exception lastFailure = null;
            T last = default;
            int attempts = 0;

            while (true)
            {
                attempts++;

                try
                {
                    last = await probe(ct).ConfigureAwait(false);
                    lastFailure = null;

                    if (accept(last))
                    {
                        return last;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    lastFailure = exception;
                }

                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

                if (elapsed >= limit)
                {
                    string state = lastFailure != null
                        ? $"the last attempt failed with: {lastFailure.Message}"
                        : $"the last value was: {Describe(last)}";

                    throw new TimeoutException(string.Format(
                        CultureInfo.InvariantCulture,
                        "Timed out after {0:0.#} s and {1} attempts waiting for {2}. And {3}",
                        limit.TotalSeconds,
                        attempts,
                        because,
                        state));
                }

                await Task.Delay(pause, ct).ConfigureAwait(false);
            }
        }

        private static string Describe<T>(T value)
        {
            if (value == null)
            {
                return "null";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}", value);
        }
    }
}
