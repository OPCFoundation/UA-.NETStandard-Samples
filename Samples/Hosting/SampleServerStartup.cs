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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Follows the startup of the hosted server of a sample: it captures the
    /// configuration the server was started with, when the load happens inside the
    /// hosted server, and it reports the moment the server is up.
    /// </summary>
    /// <remarks>
    /// The hosted server of the stack starts in the background, while the samples
    /// were written against a host whose start returns with the server listening:
    /// they resolve the running server and its configuration right after
    /// <c>StartAsync</c>. <see cref="SampleServerReadyHostedService"/> waits on this
    /// class to restore that guarantee.
    /// </remarks>
    public sealed class SampleServerStartup : IServerStartupTask
    {
        private readonly TaskCompletionSource m_started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ApplicationConfiguration m_configuration;

        /// <summary>
        /// The configuration the hosted server loaded from the configuration file of
        /// the sample.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// When the host has not started yet, and the configuration has therefore not
        /// been read.
        /// </exception>
        public ApplicationConfiguration Configuration
            => m_configuration ?? throw new InvalidOperationException(
                "The application configuration is only available once the host has started.");

        /// <summary>
        /// Completes when the server of the sample has started.
        /// </summary>
        internal Task Started => m_started.Task;

        /// <summary>
        /// The configuration when it has been loaded, or null while it has not - for
        /// reporting, which must not fail on a startup that failed before the load.
        /// </summary>
        internal ApplicationConfiguration ConfigurationOrNull => m_configuration;

        /// <summary>
        /// Called by the hosted server once the configuration file has been read,
        /// before certificates are checked and the server starts.
        /// </summary>
        /// <param name="configuration">The loaded configuration.</param>
        internal void OnConfigurationLoaded(ApplicationConfiguration configuration)
        {
            m_configuration = configuration;

            // the file to log to is part of the configuration, so it can only be
            // attached now. Loggers handed out before this point write to the file as
            // well, see SampleLogging.
            SampleLogging.UseTraceConfiguration(configuration);
        }

        /// <inheritdoc/>
        ValueTask IServerStartupTask.OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            m_started.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
