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
        private readonly ApplicationInstance m_application;
        private readonly TemporaryPki m_pki;
        private readonly StandardServer m_server;
        private bool m_stopped;

        private SampleServerHost(
            ApplicationInstance application,
            StandardServer server,
            TemporaryPki pki,
            string endpointUrl)
        {
            m_application = application;
            m_server = server;
            m_pki = pki;
            EndpointUrl = endpointUrl;
        }

        /// <summary>
        /// The opc.tcp endpoint the server listens on.
        /// </summary>
        public string EndpointUrl { get; }

        /// <summary>
        /// The configuration the server was started with.
        /// </summary>
        public ApplicationConfiguration Configuration => m_application.ApplicationConfiguration;

        /// <summary>
        /// The running server instance.
        /// </summary>
        public StandardServer Server => m_server;

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
            StandardServer server = null;

            try
            {
                ApplicationConfiguration configuration =
                    await SampleConfigurationLoader.LoadAsync(configPath, pki, ct).ConfigureAwait(false);

                string endpointUrl = KeepOpcTcpEndpointsOnly(configuration);

                configure?.Invoke(configuration);

                var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

                bool certificateOk = await application
                    .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                    .ConfigureAwait(false);

                if (!certificateOk)
                {
                    throw new InvalidOperationException(
                        $"{name}: the application instance certificate could not be created.");
                }

                server = serverFactory(NullTelemetry.Instance);

                await application.StartAsync(server, ct).ConfigureAwait(false);

                return new SampleServerHost(application, server, pki, endpointUrl);
            }
            catch
            {
                server?.Dispose();
                pki.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (!m_stopped)
            {
                m_stopped = true;

                try
                {
                    await m_application.StopAsync().ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // a server which is already down must not fail the test
                }
            }

            m_server?.Dispose();

            await m_application.DisposeAsync().ConfigureAwait(false);

            m_pki.Dispose();
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
