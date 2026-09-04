/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua;
using Opc.Ua.Server;
using Quickstarts.RoleManagement.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the RoleManagement server sample: its configuration
    /// file, the node manager the server is made of, the Roles of its demonstration
    /// accounts and how those accounts log in.
    /// </summary>
    /// <remarks>
    /// Everything is registered with the server builder of the stack: the role
    /// mappings configure the role manager the stack installs on the server, the
    /// authenticator joins its identity registry once the server has started, and the
    /// startup task finishes the one part of the role configuration which cannot be
    /// written down before the server knows its own endpoints. The sample has no server
    /// class of its own. The entry point of the sample and the tests which host it share
    /// this one registration.
    /// </remarks>
    public static class RoleManagementServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.RoleManagementServer.Config.xml";

        /// <summary>
        /// Registers the RoleManagement server as the hosted OPC UA server of the
        /// stack, together with the node manager it serves and the accounts it knows.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddRoleManagementServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            return services.AddSampleServer(
                configurationFile ?? ConfigurationFile,
                server => server
                    .AddNodeManager<RoleManagementNodeManagerFactory>()
                    .ConfigureRoles(SampleUsers.ConfigureRoles)
                    .AddStartupTask<WorkstationEndpoints>()
                    .AddIdentityAuthenticator(
                        (_, _) => new UserNamePasswordAuthenticator(SampleUsers.AuthenticateAsync)),
                configure);
        }
    }
}
