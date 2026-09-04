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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Creates the OPC 10000-12 §7.10.16 <c>ManagedApplications</c> node manager of the
    /// stack over the <see cref="IConfigurationDataStore"/> registered with the container.
    /// </summary>
    /// <remarks>
    /// The store keeps its files next to the GDS database, whose location comes from the
    /// configuration. The configuration is not loaded when the container creates the
    /// factory - the hosted server of the stack collects its node manager factories
    /// before it reads the configuration - so the store is resolved when the server asks
    /// for the node manager, which happens with the configuration loaded.
    /// </remarks>
    public sealed class ManagedApplicationsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IServiceProvider m_provider;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="provider">The container the configuration data store comes from.</param>
        public ManagedApplicationsNodeManagerFactory(IServiceProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The managed applications live below the standard <c>Directory</c> object, in
        /// the namespace of the stack.
        /// </remarks>
        public ArrayOf<string> NamespacesUris => new ArrayOf<string>(new string[] { Namespaces.OpcUa });

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: node manager ownership is transferred to the master node manager.
            return new ValueTask<IAsyncNodeManager>(
                new DefaultManagedApplicationsNodeManager(
                    server,
                    configuration,
                    m_provider.GetRequiredService<IConfigurationDataStore>()));
#pragma warning restore CA2000
        }
    }

    /// <summary>
    /// Creates the OPC 10000-21 onboarding registrar node manager of the sample over the
    /// <see cref="ITicketStore"/> registered with the container.
    /// </summary>
    public sealed class DeviceRegistrarNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly ITicketStore m_ticketStore;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="ticketStore">The tickets the registrar accepts.</param>
        public DeviceRegistrarNodeManagerFactory(ITicketStore ticketStore)
        {
            m_ticketStore = ticketStore ?? throw new ArgumentNullException(nameof(ticketStore));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => new ArrayOf<string>(new string[] { DeviceRegistrarNodeManager.NamespaceUri });

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: node manager ownership is transferred to the master node manager.
            return new ValueTask<IAsyncNodeManager>(
                new DeviceRegistrarNodeManager(server, configuration, m_ticketStore));
#pragma warning restore CA2000
        }
    }
}
