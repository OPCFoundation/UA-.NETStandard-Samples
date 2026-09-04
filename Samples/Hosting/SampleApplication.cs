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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// The <see cref="ApplicationInstance"/> of a sample, together with the
    /// configuration it was loaded with.
    /// </summary>
    /// <remarks>
    /// The instance is resolved from dependency injection, so it gets the
    /// <see cref="ITelemetryContext"/> of the host and everything it creates logs
    /// through the host. The configuration is read once the host starts, which is
    /// what <see cref="SampleApplicationHostedService"/> is for; a server of the
    /// sample registered with <c>AddSampleServer(configureServer)</c> runs on the
    /// same instance and configuration, which the hosted server of the stack takes
    /// through <see cref="IOpcUaApplicationConfigurationProvider"/>.
    /// </remarks>
    public sealed class SampleApplication : IOpcUaApplicationConfigurationProvider
    {
        private readonly SampleApplicationOptions m_options;
        private readonly ILogger m_logger;
        private readonly object m_lock = new();
        private Task<ApplicationConfiguration> m_load;

        /// <summary>
        /// Creates the application instance of the sample.
        /// </summary>
        /// <param name="options">The description of the sample.</param>
        /// <param name="telemetry">The telemetry context of the host.</param>
        public SampleApplication(
            IOptions<SampleApplicationOptions> options,
            ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(telemetry);

            m_options = options.Value;
            m_logger = telemetry.CreateLogger<SampleApplication>();

            Instance = new ApplicationInstance(telemetry) {
                ApplicationType = m_options.ApplicationType
            };

            if (!string.IsNullOrEmpty(m_options.ApplicationName))
            {
                Instance.ApplicationName = m_options.ApplicationName;
            }
        }

        /// <summary>
        /// The application instance of the sample.
        /// </summary>
        public ApplicationInstance Instance { get; }

        /// <inheritdoc/>
        IApplicationInstance IOpcUaApplicationConfigurationProvider.Application => Instance;

        /// <summary>
        /// The configuration the sample was started with.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// When the host has not started yet, and the configuration has therefore
        /// not been read.
        /// </exception>
        public ApplicationConfiguration Configuration
        {
            get
            {
                Task<ApplicationConfiguration> load = Volatile.Read(ref m_load);

                return load is { IsCompletedSuccessfully: true }
                    ? load.Result
                    : throw new InvalidOperationException(
                        "The application configuration is only available once the host has started.");
            }
        }

        /// <summary>
        /// Reads the configuration, points the logging at the file it names and makes
        /// sure the application instance certificate is usable. Once: every caller
        /// after the first gets the same load.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public Task<ApplicationConfiguration> GetAsync(CancellationToken ct = default)
        {
            Task<ApplicationConfiguration> load;

            lock (m_lock)
            {
                m_load ??= LoadAsync();
                load = m_load;
            }

            return load.WaitAsync(ct);
        }

        /// <inheritdoc/>
        ValueTask IAsyncDisposable.DisposeAsync()
        {
            // the container disposes the application instance: it is registered with
            // it as a service of its own, which the forms of the samples take.
            return ValueTask.CompletedTask;
        }

        private async Task<ApplicationConfiguration> LoadAsync()
        {
            ApplicationConfiguration configuration = await Instance
                .LoadApplicationConfigurationAsync(
                    SampleConfigurationFile.Resolve(m_options.ConfigurationFile),
                    silent: false)
                .ConfigureAwait(false);

            // what the configuration file cannot express, for example a certificate
            // validation callback.
            m_options.ConfigureConfiguration?.Invoke(configuration);

            // the file to log to is part of the configuration, so it can only be
            // attached now. Loggers handed out before this point write to the file as
            // well, see SampleLogging.
            string logFile = SampleLogging.UseTraceConfiguration(configuration);
            string applicationName = configuration.ApplicationName;
            if (logFile != null && m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation("{Application} logs to {LogFile}.", applicationName, logFile);
            }

            bool certificateOk = await Instance
                .CheckApplicationInstanceCertificatesAsync(silent: false)
                .ConfigureAwait(false);

            if (!certificateOk)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The application instance certificate of '{0}' is invalid.",
                        applicationName));
            }

            return configuration;
        }
    }
}
