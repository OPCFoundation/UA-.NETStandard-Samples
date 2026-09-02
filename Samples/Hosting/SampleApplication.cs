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
    /// through the host. The configuration is only read once the host starts, which
    /// is what <see cref="SampleApplicationHostedService"/> is for.
    /// </remarks>
    public sealed class SampleApplication
    {
        private readonly SampleApplicationOptions m_options;
        private readonly ILogger m_logger;
        private ApplicationConfiguration m_configuration;

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

        /// <summary>
        /// The configuration the sample was started with.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// When the host has not started yet, and the configuration has therefore
        /// not been read.
        /// </exception>
        public ApplicationConfiguration Configuration
            => m_configuration ?? throw new InvalidOperationException(
                "The application configuration is only available once the host has started.");

        /// <summary>
        /// Reads the configuration, points the logging at the file it names and makes
        /// sure the application instance certificate is usable.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        internal async Task InitializeAsync(CancellationToken ct)
        {
            m_configuration = await Instance
                .LoadApplicationConfigurationAsync(
                    SampleConfigurationFile.Resolve(m_options.ConfigurationFile),
                    m_options.Silent,
                    ct)
                .ConfigureAwait(false);

            // what the configuration file cannot express, for example a certificate
            // validation callback.
            m_options.ConfigureConfiguration?.Invoke(m_configuration);

            // the file to log to is part of the configuration, so it can only be
            // attached now. Loggers handed out before this point write to the file as
            // well, see SampleLogging.
            string logFile = SampleLogging.UseTraceConfiguration(m_configuration);
            string applicationName = m_configuration.ApplicationName;
            if (logFile != null && m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation("{Application} logs to {LogFile}.", applicationName, logFile);
            }

            bool certificateOk = await Instance
                .CheckApplicationInstanceCertificatesAsync(m_options.Silent, ct: ct)
                .ConfigureAwait(false);

            if (!certificateOk)
            {
                if (m_options.RequireApplicationCertificate)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "The application instance certificate of '{0}' is invalid.",
                            applicationName));
                }

                m_logger.LogWarning(
                    "The application instance certificate of '{Application}' is invalid.",
                    applicationName);
            }
        }
    }
}
