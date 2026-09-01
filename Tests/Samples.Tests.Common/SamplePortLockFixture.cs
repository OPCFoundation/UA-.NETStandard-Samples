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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Makes a test assembly wait for the machine wide sample port lock before its first
    /// test runs, and releases the lock after its last.
    /// </summary>
    /// <remarks>
    /// The sample servers bind the fixed ports they ship with, so two test runs on one
    /// machine fight over the ports no matter which repository worktrees they come from -
    /// <c>[NonParallelizable]</c> only serializes fixtures inside one process. Deriving from
    /// this class in a <c>[SetUpFixture]</c> makes the whole assembly queue behind every
    /// other port using run instead. The configuration tier uses no ports and must not take
    /// the lock.
    ///
    /// The lock is a file opened without sharing under the temp directory rather than a
    /// named mutex: a mutex must be released on the thread that acquired it, and NUnit does
    /// not promise to run one-time setup and teardown on the same thread. The file handle
    /// has no such affinity, and the operating system closes it when a test host dies,
    /// however it dies, so a crashed run cannot leave the machine locked.
    /// </remarks>
    public abstract class SamplePortLockFixture
    {
        /// <summary>
        /// How long a run waits for the machine before giving up. Long enough for a queue
        /// of every port using tier to drain in front of it on a slow build agent.
        /// </summary>
        private static readonly TimeSpan kAcquireTimeout = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan kPollDelay = TimeSpan.FromSeconds(1);

        private FileStream m_lock;

        private static string LockPath =>
            Path.Combine(Path.GetTempPath(), "opcua-samples-tests.lock");

        /// <summary>
        /// Takes the machine wide lock, waiting for whatever sample test run holds it.
        /// </summary>
        [OneTimeSetUp]
        public async Task AcquireSamplePortsAsync()
        {
            string path = LockPath;
            DateTime deadline = DateTime.UtcNow + kAcquireTimeout;
            bool announced = false;

            while (true)
            {
                try
                {
                    var stream = new FileStream(
                        path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);

                    // held from here on; the content is only read by a human who finds the
                    // file after a run died holding it
                    m_lock = stream;

                    string owner = string.Create(
                        CultureInfo.InvariantCulture,
                        $"pid {Environment.ProcessId} took the sample port lock at {DateTime.UtcNow:O}");

                    stream.SetLength(0);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(owner)).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);

                    return;
                }
                catch (IOException) when (m_lock == null)
                {
                    // another run holds the file
                }
                catch (UnauthorizedAccessException) when (m_lock == null)
                {
                    // transient on Windows while the previous holder's delete is in flight
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"Another sample test run has held {path} for over " +
                        $"{kAcquireTimeout.TotalMinutes:F0} minutes. Wait for it to finish, or " +
                        "find and stop the test host which owns the file.");
                }

                if (!announced)
                {
                    announced = true;
                    await TestContext.Progress.WriteLineAsync(
                        $"Waiting for another sample test run to release {path} - " +
                        "the sample servers bind fixed ports, so runs take turns.").ConfigureAwait(false);
                }

                await Task.Delay(kPollDelay).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases the lock so the next queued run can start.
        /// </summary>
        [OneTimeTearDown]
        public void ReleaseSamplePorts()
        {
            FileStream stream = m_lock;
            m_lock = null;

            if (stream == null)
            {
                return;
            }

            stream.Dispose();

            try
            {
                File.Delete(LockPath);
            }
            catch (IOException)
            {
                // the next queued run opened the file between the dispose and the delete;
                // it owns the lock now and the file existing is harmless
            }
            catch (UnauthorizedAccessException)
            {
                // same race, surfaced differently on Windows
            }
        }
    }
}
