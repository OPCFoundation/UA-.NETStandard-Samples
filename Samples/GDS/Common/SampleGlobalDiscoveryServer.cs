/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// The <see cref="GlobalDiscoverySampleServer"/> both sample GDS hosts - the Windows
    /// <c>GlobalDiscoveryServer</c> and the cross-platform
    /// <c>NetCoreGlobalDiscoveryServer</c> - are built on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It adds four things to the stock GDS server, each of them optional and each switched
    /// on by handing the corresponding object to the constructor:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// the GDS master AliasNames list (issue #274 / OPC 10000-17): the supplied
    /// <see cref="GlobalDiscoveryServerAliasMerger"/>'s master <c>InMemoryAliasNameStore</c>
    /// is registered with the server-wide alias-name store registry once the server has
    /// started, so the standard well-known <c>Aliases</c> / <c>TagVariables</c> /
    /// <c>Topics</c> nodes on the GDS dispatch <c>FindAlias</c> through the merged list;
    /// </item>
    /// <item>
    /// the OPC 10000-12 §7.10.14 - §7.10.16 <c>ManagedApplications</c> folder, populated
    /// through an <see cref="IConfigurationDataStore"/>;
    /// </item>
    /// <item>
    /// the Optional §7.10.3 / §7.10.13 / §7.10.20 <c>ServerConfiguration</c> surface -
    /// <c>HasSecureElement</c>, <c>InApplicationSetup</c>, <c>ResetToServerDefaults</c> and
    /// <c>ConfigurationFile</c> - which <c>ConfigurationNodeManager</c> suppresses unless it
    /// is given a <see cref="ServerConfigurationOptions"/>;
    /// </item>
    /// <item>
    /// the OPC 10000-21 onboarding registrar administration Object, when a ticket store is
    /// supplied (see <see cref="DeviceRegistrarNodeManager"/>).
    /// </item>
    /// </list>
    /// <para>
    /// The rest of the OPC 10000-12 v1.05.07 surface - the PushManagement transaction model,
    /// the pull-model Methods, the certificate and TrustList alarms - needs nothing from the
    /// host: the stack exposes it on every <c>StandardServer</c>.
    /// </para>
    /// </remarks>
    public class SampleGlobalDiscoveryServer : GlobalDiscoverySampleServer
    {
        private readonly GlobalDiscoveryServerAliasMerger m_merger;
        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_request;
        private readonly ICertificateGroup m_certificateGroup;
        private readonly bool m_autoApprove;
        private readonly ServerConfigurationOptions m_serverConfigurationOptions;

        /// <summary>
        /// Creates the sample GDS server.
        /// </summary>
        /// <param name="database">The applications database.</param>
        /// <param name="request">The certificate request store.</param>
        /// <param name="certificateGroup">The certificate group / CA implementation.</param>
        /// <param name="userDatabase">
        /// The user database. Pass a <see cref="IGdsUserDatabase"/> - for example a
        /// <see cref="GdsApplicationAdminUserDatabase"/> - to support the OPC 10000-12 §7.2
        /// <c>ApplicationAdmin</c> privilege.
        /// </param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="merger">The AliasNames merger whose master list the GDS serves.</param>
        /// <param name="autoApprove">Whether certificate requests are approved automatically.</param>
        /// <param name="serverConfigurationOptions">
        /// Configures the Optional §7.10.3 <c>ServerConfiguration</c> members. When
        /// <c>null</c> only the always-known identity Properties are exposed.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="merger"/> is <c>null</c>.</exception>
        public SampleGlobalDiscoveryServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            ITelemetryContext telemetry,
            GlobalDiscoveryServerAliasMerger merger,
            bool autoApprove = true,
            ServerConfigurationOptions serverConfigurationOptions = null)
            : base(database, request, certificateGroup, userDatabase, telemetry, autoApprove)
        {
            m_merger = merger ?? throw new ArgumentNullException(nameof(merger));
            m_database = database;
            m_request = request;
            m_certificateGroup = certificateGroup;
            m_autoApprove = autoApprove;
            m_serverConfigurationOptions = serverConfigurationOptions;
        }

        /// <inheritdoc/>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
            m_merger.RegisterWithServer(server);
        }

        /// <summary>
        /// Creates the GDS node manager and, alongside it, the node managers of the
        /// factories registered with the server: the <c>ManagedApplications</c> and the
        /// onboarding-registrar node managers the host registers.
        /// </summary>
        /// <remarks>
        /// The base class builds the <c>MasterNodeManager</c> with the GDS
        /// <c>ApplicationsNodeManager</c> alone and ignores the node manager factories
        /// registered with the server, so the list is rebuilt here: the GDS node manager
        /// first, then one node manager per registered factory, the way
        /// <see cref="StandardServer"/> creates them.
        /// </remarks>
        protected override async ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var nodeManagers = new List<IAsyncNodeManager> {
                new ApplicationsNodeManager(
                    server,
                    configuration,
                    m_database,
                    m_request,
                    m_certificateGroup,
                    m_autoApprove)
            };

            foreach (INodeManagerFactory factory in NodeManagerFactories)
            {
                nodeManagers.Add(factory.Create(server, configuration).ToAsyncNodeManager());
            }

            foreach (IAsyncNodeManagerFactory factory in AsyncNodeManagerFactories)
            {
                nodeManagers.Add(
                    await factory.CreateAsync(server, configuration, cancellationToken).ConfigureAwait(false));
            }

            #pragma warning disable CA2000 // Justification: ownership of the MasterNodeManager transfers to the caller.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
            #pragma warning restore CA2000
        }

        /// <summary>
        /// Supplies the Optional OPC 10000-12 §7.10.3 <c>ServerConfiguration</c> surface to
        /// the <c>ConfigurationNodeManager</c>.
        /// </summary>
        /// <remarks>
        /// A host built through the dependency-injection API resolves these options from the
        /// container. The sample GDS hosts construct their server themselves, so the options
        /// are threaded through the <c>MainNodeManagerFactory</c> instead. Returning the base
        /// factory when nothing is configured keeps the default behaviour - identity
        /// Properties only - intact.
        /// </remarks>
        protected override IMainNodeManagerFactory CreateMainNodeManagerFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (m_serverConfigurationOptions == null)
            {
                return base.CreateMainNodeManagerFactory(server, configuration);
            }

            return new MainNodeManagerFactory(
                configuration,
                server,
                coordinator: null,
                pendingKeyStore: null,
                keyGenerator: null,
                trustListEffectHandler: null,
                serverConfigurationOptions: m_serverConfigurationOptions);
        }
    }
}
