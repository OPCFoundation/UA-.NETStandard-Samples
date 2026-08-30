/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Starts a sample server the way its Program.Main does, minus the user interface.
    /// </summary>
    /// <remarks>
    /// Every sample server loads its configuration, checks its certificate and hands a
    /// server instance to the ApplicationInstance before it opens a window. Everything up
    /// to that point is what this host repeats, so the test covers the real startup path
    /// of the sample and not a rewrite of it.
    /// </remarks>
    public sealed class SampleServerHost : IAsyncDisposable
    {
        private readonly string m_name;
        private readonly string m_configPath;
        private readonly Func<ITelemetryContext, StandardServer> m_serverFactory;
        private readonly Action<ApplicationConfiguration> m_configure;
        private readonly TemporaryPki m_pki;
        private ApplicationInstance m_application;
        private StandardServer m_server;

        private SampleServerHost(
            string name,
            string configPath,
            Func<ITelemetryContext, StandardServer> serverFactory,
            Action<ApplicationConfiguration> configure,
            TemporaryPki pki)
        {
            m_name = name;
            m_configPath = configPath;
            m_serverFactory = serverFactory;
            m_configure = configure;
            m_pki = pki;
        }

        /// <summary>
        /// The opc.tcp endpoint the server listens on.
        /// </summary>
        public string EndpointUrl { get; private set; }

        /// <summary>
        /// The configuration the server was started with.
        /// </summary>
        public ApplicationConfiguration Configuration => m_application?.ApplicationConfiguration;

        /// <summary>
        /// The running server instance, or null while the host is stopped.
        /// </summary>
        public StandardServer Server => m_server;

        /// <summary>
        /// Whether the server is currently listening.
        /// </summary>
        public bool IsRunning => m_application != null;

        /// <summary>
        /// Loads the configuration of a sample, creates its server and starts it.
        /// </summary>
        /// <param name="name">The name of the sample, used for the temporary PKI.</param>
        /// <param name="configPath">The sample configuration, relative to the repository root.</param>
        /// <param name="serverFactory">Creates the server instance of the sample.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <param name="configure">
        /// Changes the configuration after it was loaded and before the server is started.
        /// Used by the aggregation sample, whose shipped configuration names a downstream
        /// server which is not the one a test starts.
        /// </param>
        public static async Task<SampleServerHost> StartAsync(
            string name,
            string configPath,
            Func<ITelemetryContext, StandardServer> serverFactory,
            CancellationToken ct = default,
            Action<ApplicationConfiguration> configure = null)
        {
            if (serverFactory == null)
            {
                throw new ArgumentNullException(nameof(serverFactory));
            }

            var pki = new TemporaryPki(name);
            var host = new SampleServerHost(name, configPath, serverFactory, configure, pki);

            try
            {
                await host.StartServerAsync(ct).ConfigureAwait(false);

                return host;
            }
            catch
            {
                pki.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Stops the server and frees its endpoint, leaving the host able to start again.
        /// </summary>
        public async Task StopAsync()
        {
            ApplicationInstance application = m_application;
            StandardServer server = m_server;

            m_application = null;
            m_server = null;

            if (application == null)
            {
                return;
            }

            try
            {
                await application.StopAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // a server which is already down must not fail the test
            }

            server?.Dispose();

            await application.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a new instance of the server on the same endpoint after <see cref="StopAsync"/>.
        /// </summary>
        /// <remarks>
        /// The temporary PKI is kept, so the server comes back with the certificate a client
        /// already knows. What the client sees is the server it was talking to going away and
        /// returning, which is what a reconnect has to survive - not a different server which
        /// happens to answer on the same port.
        /// </remarks>
        public Task StartAgainAsync(CancellationToken ct = default)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException(
                    $"{m_name}: the server is still running. Stop it before starting it again.");
            }

            return StartServerAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);

            m_pki.Dispose();
        }

        /// <summary>
        /// Loads the configuration and starts one instance of the server from it.
        /// </summary>
        private async Task StartServerAsync(CancellationToken ct)
        {
            ApplicationInstance application = null;
            StandardServer server = null;

            try
            {
                ApplicationConfiguration configuration =
                    await SampleConfigurationLoader.LoadAsync(m_configPath, m_pki, ct).ConfigureAwait(false);

                EndpointUrl = KeepOpcTcpEndpointsOnly(configuration);

                m_configure?.Invoke(configuration);

                application = new ApplicationInstance(configuration, NullTelemetry.Instance);

                bool certificateOk = await application
                    .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                    .ConfigureAwait(false);

                if (!certificateOk)
                {
                    throw new InvalidOperationException(
                        $"{m_name}: the application instance certificate could not be created.");
                }

                server = m_serverFactory(NullTelemetry.Instance);

                await application.StartAsync(server, ct).ConfigureAwait(false);

                m_application = application;
                m_server = server;
            }
            catch
            {
                // a half started server holds its listener, and a second attempt on the same
                // port would fail for a reason which has nothing to do with the first failure
                server?.Dispose();

                if (application != null)
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>
        /// Drops every base address which is not opc.tcp and returns the remaining one.
        /// </summary>
        /// <remarks>
        /// Tier 1 is about "does the sample serve its address space", not about transports.
        /// The https endpoints of the samples need their own bindings and a TLS certificate,
        /// and they would double the number of ports a test run occupies.
        /// </remarks>
        private static string KeepOpcTcpEndpointsOnly(ApplicationConfiguration configuration)
        {
            ServerBaseConfiguration server = configuration.ServerConfiguration;

            if (server == null)
            {
                throw new InvalidOperationException("The configuration does not describe a server.");
            }

            string[] opcTcp = server.BaseAddresses
                .Filter(address => address.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (opcTcp.Length == 0)
            {
                throw new InvalidOperationException("The server does not offer an opc.tcp endpoint.");
            }

            server.BaseAddresses = new ArrayOf<string>(opcTcp);
            server.AlternateBaseAddresses = ArrayOf<string>.Empty;

            return opcTcp[0];
        }
    }
}
