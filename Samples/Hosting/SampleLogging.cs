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
using System.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// The logging of the samples: a Serilog file logger driven by the
    /// TraceConfiguration of the application configuration.
    /// </summary>
    /// <remarks>
    /// The samples are Windows Forms applications for the most part, and a console
    /// logger is of no use to them - there is no console to write to. They therefore
    /// log to the file the configuration names in
    /// <see cref="TraceConfiguration.OutputFilePath"/>, which is what the pre 2.0
    /// samples did through <c>SerilogTraceLogger</c>.
    /// <para>
    /// The file to log to is only known once the configuration has been read, while
    /// the loggers of the stack are created before that and keep the Serilog logger
    /// they were created from. The static <see cref="Log.Logger"/> is therefore built
    /// once, with a placeholder sink the file is attached to later by
    /// <see cref="UseTraceConfiguration"/>, so loggers handed out in between write to
    /// the file as well.
    /// </para>
    /// </remarks>
    public static class SampleLogging
    {
        /// <summary>
        /// The layout of a line in the log file.
        /// </summary>
        private const string kOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        /// <summary>
        /// The trace masks which the stack maps to <see cref="LogEventLevel.Information"/>
        /// and above. Any mask beyond these asks for verbose output.
        /// </summary>
        private const int kInformationTraceMasks =
            Utils.TraceMasks.Information |
            Utils.TraceMasks.Error |
            Utils.TraceMasks.Security |
            Utils.TraceMasks.StartStop |
            Utils.TraceMasks.StackTrace;

        private static readonly LoggingLevelSwitch s_levelSwitch = new(LogEventLevel.Information);

        private static readonly DeferredSink s_fileSink = new();

        private static int s_created;

        /// <summary>
        /// Installs the logger of the sample. The file sink is added by
        /// <see cref="UseTraceConfiguration"/>, as soon as the configuration names one.
        /// </summary>
        public static void CreateBootstrapLogger()
        {
            if (Interlocked.Exchange(ref s_created, 1) != 0)
            {
                return;
            }

            LoggerConfiguration configuration = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(s_levelSwitch)
                .Enrich.FromLogContext()
                .WriteTo.Sink(s_fileSink);

#if DEBUG
            configuration.WriteTo.Debug(
                restrictedToMinimumLevel: LogEventLevel.Verbose,
                formatProvider: CultureInfo.InvariantCulture);
#endif

            Log.Logger = configuration.CreateLogger();
        }

        /// <summary>
        /// Starts writing to the file named by the trace configuration of
        /// <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">The configuration of the sample application.</param>
        /// <returns>The path of the log file, or null when the configuration names none.</returns>
        public static string UseTraceConfiguration(ApplicationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            TraceConfiguration trace = configuration.TraceConfiguration;

            // the stack asks for verbose output through any trace mask beyond the ones
            // which map to Information and above.
            LogEventLevel fileLevel = trace != null && (trace.TraceMasks & ~kInformationTraceMasks) != 0
                ? LogEventLevel.Verbose
                : LogEventLevel.Information;

            return UseLogFile(
                trace?.OutputFilePath,
                trace != null && trace.DeleteOnLoad,
                fileLevel == LogEventLevel.Verbose);
        }

        /// <summary>
        /// Starts writing to <paramref name="outputFilePath"/>, for the samples which
        /// have no application configuration to take the path from.
        /// </summary>
        /// <param name="outputFilePath">
        /// The file to write to. Special folder names are expanded, and the directory
        /// is created when it does not exist yet.
        /// </param>
        /// <param name="deleteOnLoad">Whether an existing file is replaced.</param>
        /// <param name="verbose">Whether everything the samples log is written.</param>
        /// <returns>The path of the log file, or null when there is none to write to.</returns>
        public static string UseLogFile(
            string outputFilePath,
            bool deleteOnLoad = true,
            bool verbose = false)
        {
            LogEventLevel fileLevel = verbose ? LogEventLevel.Verbose : LogEventLevel.Information;

            s_levelSwitch.MinimumLevel = fileLevel;

            outputFilePath = PrepareOutputFile(outputFilePath, deleteOnLoad);
            if (outputFilePath == null)
            {
                return null;
            }

            // a logger is a sink as well, which keeps the file and its formatting out
            // of the logger the rest of the sample already writes to.
            Logger fileLogger = new LoggerConfiguration()
                .MinimumLevel.Is(fileLevel)
                .WriteTo.File(
                    outputFilePath,
                    restrictedToMinimumLevel: fileLevel,
                    outputTemplate: kOutputTemplate,
                    formatProvider: CultureInfo.InvariantCulture,
                    rollOnFileSizeLimit: true)
                .CreateLogger();

            s_fileSink.Attach(fileLogger);

            return outputFilePath;
        }

        /// <summary>
        /// Flushes and closes the logger. To be called once, when the sample exits.
        /// </summary>
        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }

        /// <summary>
        /// Expands the configured output file path and honours DeleteOnLoad.
        /// </summary>
        private static string PrepareOutputFile(string configuredPath, bool deleteOnLoad)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

            string outputFilePath = Utils.ReplaceSpecialFolderNames(configuredPath);
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return null;
            }

            // the configuration files of the samples were written on Windows and
            // separate with a backslash, which is an ordinary character in a file name
            // everywhere else. Without this, Path.GetDirectoryName below sees no
            // directory at all and the log lands next to the executable under a name
            // with backslashes in it.
            outputFilePath = outputFilePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            try
            {
                string directory = Path.GetDirectoryName(outputFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (deleteOnLoad && File.Exists(outputFilePath))
                {
                    File.Delete(outputFilePath);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // a log file which cannot be prepared must not keep the sample from starting.
                return null;
            }

            return outputFilePath;
        }

        /// <summary>
        /// A sink which drops everything until the sink to write to is known.
        /// </summary>
        private sealed class DeferredSink : ILogEventSink, IDisposable
        {
            private ILogEventSink m_sink;

            /// <summary>
            /// Starts forwarding to <paramref name="sink"/>.
            /// </summary>
            public void Attach(ILogEventSink sink)
            {
                ILogEventSink previous = Interlocked.Exchange(ref m_sink, sink);
                (previous as IDisposable)?.Dispose();
            }

            /// <inheritdoc/>
            public void Emit(LogEvent logEvent)
            {
                Volatile.Read(ref m_sink)?.Emit(logEvent);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                (Interlocked.Exchange(ref m_sink, null) as IDisposable)?.Dispose();
            }
        }
    }
}
