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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Hands the hosted server of the stack the server instance of the sample from
    /// the container, instead of a private instance of its own. The main form of the
    /// sample resolves the same instance, so it shows the server which is running.
    /// </summary>
    /// <typeparam name="TServer">The server class of the sample.</typeparam>
    internal sealed class SampleServerFactory<TServer> : IOpcUaServerFactory
        where TServer : StandardServer
    {
        private readonly IServiceProvider m_provider;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="provider">The container holding the server of the sample.</param>
        public SampleServerFactory(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            m_provider = provider;
        }

        /// <inheritdoc/>
        public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            // telemetry and time provider are not handed through: the server of the
            // sample takes both from the container it is created by.
            TServer server = m_provider.GetRequiredService<TServer>();

            // the node managers which could only be described with the configuration
            // loaded - which it is now, right before the hosted server starts the
            // server. The registered factory types are added by the hosted server.
            foreach (ConfiguredNodeManagerFactories configured
                in m_provider.GetServices<ConfiguredNodeManagerFactories>())
            {
                foreach (IAsyncNodeManagerFactory factory in configured.Create(m_provider))
                {
                    server.AddNodeManager(factory);
                }
            }

            return server;
        }
    }

    /// <summary>
    /// Node manager factories which are created once the configuration of the sample
    /// has been loaded, see <c>AddNodeManagers</c>.
    /// </summary>
    internal sealed class ConfiguredNodeManagerFactories
    {
        /// <summary>
        /// Creates the registration.
        /// </summary>
        /// <param name="create">Creates the factories.</param>
        public ConfiguredNodeManagerFactories(Func<IServiceProvider, IEnumerable<IAsyncNodeManagerFactory>> create)
        {
            ArgumentNullException.ThrowIfNull(create);

            Create = create;
        }

        /// <summary>
        /// Creates the factories, from the container with the configuration loaded.
        /// </summary>
        public Func<IServiceProvider, IEnumerable<IAsyncNodeManagerFactory>> Create { get; }
    }
}
