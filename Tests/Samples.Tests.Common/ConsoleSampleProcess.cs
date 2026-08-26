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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Runs a console sample as the process it really is, and waits until it says it is up.
    /// </summary>
    /// <remarks>
    /// The WinForms samples are started in process, because their servers are reachable
    /// without their user interface. The console samples are not: they build their host in
    /// Main and block there. Running the built executable also covers what an in process test
    /// skips - the entry point, the command line and the configuration lookup through the
    /// application configuration file.
    /// </remarks>
    public sealed class ConsoleSampleProcess : IAsyncDisposable
    {
        private readonly Process m_process;
        private readonly StringBuilder m_output = new();
        private readonly Lock m_lock = new();
        private readonly string m_quitCommand;
        private bool m_disposed;

        private ConsoleSampleProcess(Process process, string quitCommand)
        {
            m_process = process;
            m_quitCommand = quitCommand;
        }

        /// <summary>
        /// Everything the sample has written to stdout and stderr so far.
        /// </summary>
        public string Output
        {
            get
            {
                lock (m_lock)
                {
                    return m_output.ToString();
                }
            }
        }

        /// <summary>
        /// Starts the sample and waits until it prints the marker which means it is running.
        /// </summary>
        /// <param name="projectPath">The sample project, relative to the repository root.</param>
        /// <param name="assemblyName">The name of the executable the project produces.</param>
        /// <param name="readyMarker">A fragment of the line the sample prints once it is up.</param>
        /// <param name="timeout">How long the sample gets to print it.</param>
        /// <param name="quitCommand">Written to stdin to stop the sample, or null to kill it.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<ConsoleSampleProcess> StartAsync(
            string projectPath,
            string assemblyName,
            string readyMarker,
            TimeSpan timeout,
            string quitCommand = null,
            CancellationToken ct = default)
        {
            string executable = FindExecutable(projectPath, assemblyName);

            var startInfo = new ProcessStartInfo {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var sample = new ConsoleSampleProcess(process, quitCommand);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnOutput(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null)
                {
                    return;
                }

                sample.Append(e.Data);

                if (e.Data.Contains(readyMarker, StringComparison.OrdinalIgnoreCase))
                {
                    ready.TrySetResult();
                }
            }

            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;
            process.Exited += (sender, e) => ready.TrySetException(
                new InvalidOperationException(
                    $"{assemblyName} exited with code {process.ExitCode} before it was ready.{Environment.NewLine}{sample.Output}"));

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                Task finished = await Task.WhenAny(ready.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);

                if (finished != ready.Task)
                {
                    throw new TimeoutException(
                        $"{assemblyName} did not print '{readyMarker}' within {timeout.TotalSeconds:F0} seconds." +
                        $"{Environment.NewLine}{sample.Output}");
                }

                await ready.Task.ConfigureAwait(false);

                return sample;
            }
            catch
            {
                await sample.DisposeAsync().ConfigureAwait(false);
                throw;
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

            try
            {
                if (!m_process.HasExited)
                {
                    if (m_quitCommand != null)
                    {
                        await m_process.StandardInput.WriteLineAsync(m_quitCommand).ConfigureAwait(false);
                        await m_process.StandardInput.FlushAsync().ConfigureAwait(false);

                        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        try
                        {
                            await m_process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // the sample ignored the command, fall through and kill it
                        }
                    }

                    if (!m_process.HasExited)
                    {
                        m_process.Kill(true);

                        await m_process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // the process was already gone
            }
            catch (IOException)
            {
                // stdin was already closed
            }

            m_process.Dispose();
        }

        private void Append(string line)
        {
            lock (m_lock)
            {
                m_output.AppendLine(line);
            }
        }

        /// <summary>
        /// Finds the executable a sample project produced.
        /// </summary>
        /// <remarks>
        /// The test project builds the sample through a project reference, so the newest
        /// match is the one that was just built for the configuration under test.
        /// </remarks>
        private static string FindExecutable(string projectPath, string assemblyName)
        {
            string projectDirectory = Path.GetDirectoryName(RepositoryLayout.PathOf(projectPath));
            string binDirectory = Path.Combine(projectDirectory!, "bin");

            if (!Directory.Exists(binDirectory))
            {
                throw new FileNotFoundException(
                    $"'{binDirectory}' does not exist, so {assemblyName} was never built. " +
                    "The test project has to reference the sample project.");
            }

            List<string> candidates = Directory
                .EnumerateFiles(binDirectory, assemblyName + ".exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (candidates.Count == 0)
            {
                throw new FileNotFoundException($"No {assemblyName}.exe was found below '{binDirectory}'.");
            }

            return candidates[0];
        }
    }
}
