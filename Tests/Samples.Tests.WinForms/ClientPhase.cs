/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Diagnostics;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The step a sample client is in, so that a harness timeout says where it hung.
    /// </summary>
    /// <remarks>
    /// Written on the message loop thread and read by the test thread once the clock has
    /// run out. Neither the reference nor the reading of the stopwatch tears, and a
    /// message which names the step before last would still be enough to go on, so the
    /// two are left unsynchronized rather than locked on every step.
    /// </remarks>
    public sealed class ClientPhase
    {
        private readonly Stopwatch m_since = Stopwatch.StartNew();
        private volatile string m_current = "starting up";

        /// <summary>
        /// What the client is doing, phrased to follow "It was ".
        /// </summary>
        public string Current => m_current;

        /// <summary>
        /// How long it has been in that step.
        /// </summary>
        public TimeSpan Elapsed => m_since.Elapsed;

        /// <summary>
        /// Records that the client moved on to the next step.
        /// </summary>
        public void Enter(string what)
        {
            m_current = what;
            m_since.Restart();
        }

        /// <summary>
        /// Turns a harness timeout into one which names the step the client hung in.
        /// </summary>
        public TimeoutException Explain(TimeoutException expired)
        {
            ArgumentNullException.ThrowIfNull(expired);

            // the bare message says only that the clock ran out, which is the same for
            // every way a sample can hang. The step it was in is what tells them apart.
            return new TimeoutException(
                $"{expired.Message} It was {Current}, " +
                $"and had been for {Elapsed.TotalSeconds:F0} of those seconds.",
                expired);
        }
    }
}
