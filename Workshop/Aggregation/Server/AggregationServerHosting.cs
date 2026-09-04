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
using AggregationServer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the Aggregation server sample: the server class, its
    /// configuration file and the node managers the server is made of.
    /// </summary>
    /// <remarks>
    /// The server aggregates one downstream server per endpoint of its configuration,
    /// with one <see cref="AggregationNodeManagerFactory"/> each. How many there are is
    /// only known once the configuration is loaded, which is why the factories are not
    /// registered as types with the server builder of the stack but added by the
    /// server factory of the container, which runs with the configuration loaded and
    /// right before the hosted server starts the server. The entry points of the
    /// WinForms and the console variant of the sample and the tests which host it
    /// share this one registration.
    /// </remarks>
    public static class AggregationServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.AggregationServer.Config.xml";

        /// <summary>
        /// Registers the Aggregation server as the hosted OPC UA server of the stack,
        /// together with one aggregation node manager per configured endpoint and the
        /// reverse connect manager the node managers connect through.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddAggregationServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<AggregationReverseConnect>();

            services.AddSampleServer(
                configurationFile ?? ConfigurationFile,
                CreateServer,
                configure);

            // registered after the hosted server: the reverse connect manager is created
            // for the loaded configuration when the server is, and started once the
            // server listens.
            services.AddHostedService(provider => provider.GetRequiredService<AggregationReverseConnect>());

            return services;
        }

        /// <summary>
        /// Creates the server with one aggregation node manager per configured endpoint.
        /// Only the first one publishes the aggregation type model.
        /// </summary>
        private static AggregationServer.AggregationServer CreateServer(IServiceProvider provider)
        {
            ApplicationConfiguration configuration = provider.GetRequiredService<ApplicationConfiguration>();
            ReverseConnectManager reverseConnectManager = provider
                .GetRequiredService<AggregationReverseConnect>()
                .GetOrCreate(configuration);

            var server = new AggregationServer.AggregationServer(provider.GetRequiredService<ITelemetryContext>());

            ConfiguredEndpointCollection endpoints = configuration.ParseExtension<ConfiguredEndpointCollection>();

            bool ownsTypeModel = true;
            foreach (ConfiguredEndpoint endpoint in endpoints.Endpoints)
            {
                server.AddNodeManager(new AggregationNodeManagerFactory(endpoint, reverseConnectManager, ownsTypeModel));
                ownsTypeModel = false;
            }

            return server;
        }
    }

    /// <summary>
    /// Owns the reverse connect manager the aggregation node managers connect to their
    /// downstream servers through, when the configuration asks for reverse connections:
    /// created with the server, started with the host, disposed with it.
    /// </summary>
    internal sealed class AggregationReverseConnect : IHostedService, IAsyncDisposable
    {
        private readonly ITelemetryContext m_telemetry;
        private ReverseConnectManager m_manager;
        private ApplicationConfiguration m_configuration;

        public AggregationReverseConnect(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            m_telemetry = telemetry;
        }

        /// <summary>
        /// The reverse connect manager for the configuration, or <c>null</c> when the
        /// configuration has no reverse connect section. The manager is started when
        /// the host starts this service, so that the app configuration can change during
        /// operation and the manager object is there for the node managers regardless.
        /// </summary>
        public ReverseConnectManager GetOrCreate(ApplicationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (m_manager != null || configuration.ClientConfiguration?.ReverseConnect == null)
            {
                return m_manager;
            }

            m_configuration = configuration;
            m_manager = new ReverseConnectManager(m_telemetry);

            foreach (ReverseConnectClientEndpoint endpoint in configuration.ClientConfiguration.ReverseConnect.ClientEndpoints)
            {
                m_manager.AddEndpoint(new Uri(endpoint.EndpointUrl));
            }

            return m_manager;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (m_manager == null)
            {
                return Task.CompletedTask;
            }

            return m_manager.StartServiceAsync(m_configuration, cancellationToken);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The manager is torn down here rather than in a <c>Dispose</c>: its
        /// synchronous <c>Dispose</c> is obsolete because closing the listening hosts
        /// is asynchronous, and the host awaits this method, which a disposer cannot.
        /// </remarks>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return DisposeAsync().AsTask();
        }

        /// <summary>
        /// Closes the listening hosts of the manager. Idempotent, so that the container
        /// disposing this service after the host already stopped it does nothing.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            ReverseConnectManager manager = m_manager;
            m_manager = null;

            if (manager != null)
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
