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
using Quickstarts.UserAuthenticationServer;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the UserAuthentication server sample: the server class, its
    /// configuration file and the node managers the server is made of.
    /// </summary>
    /// <remarks>
    /// The node managers are registered with the server builder of the stack and
    /// created by the container. The hosted server hands them to the server before it
    /// starts, so the server class registers nothing itself. The entry point of the
    /// sample and the tests which host it share this one registration.
    /// </remarks>
    public static class UserAuthenticationServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.UserAuthenticationServer.Config.xml";

        /// <summary>
        /// Registers the UserAuthentication server as the hosted OPC UA server of the stack,
        /// together with the node manager it serves.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddUserAuthenticationServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            return services.AddSampleServer<UserAuthenticationServer>(
                configurationFile ?? ConfigurationFile,
                server => server.AddNodeManager<UserAuthenticationNodeManagerFactory>(),
                configure);
        }
    }
}