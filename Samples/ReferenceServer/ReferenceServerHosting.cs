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
using Opc.Ua.Server.Hosting;
using Quickstarts.ReferenceServer;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the reference server sample: the server class, its
    /// configuration file and the node managers of the quickstart library the server
    /// is made of.
    /// </summary>
    /// <remarks>
    /// The quickstart library hands out its node manager factories as instances, so
    /// they are registered with the container the way the server builder of the stack
    /// registers a factory type: as node manager registrations the hosted server hands
    /// to the server before it starts. The entry point of the sample and the tests which
    /// host it share this one registration.
    /// </remarks>
    public static class ReferenceServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.ReferenceServer.Config.xml";

        /// <summary>
        /// Registers the reference server as the hosted OPC UA server of the stack,
        /// together with the node managers of the quickstart library.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddReferenceServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            foreach (INodeManagerFactory factory in Quickstarts.Servers.Utils.NodeManagerFactories)
            {
                services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
            }

            foreach (IAsyncNodeManagerFactory factory in Quickstarts.Servers.Utils.AsyncNodeManagerFactories)
            {
                services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
            }

            // the configuration file lists an opc.https base address next to the opc.tcp
            // one, and the server advertises the https-uabinary profile. Every transport
            // other than opc.tcp has to be registered: the stack skips a base address
            // whose scheme has no listener factory, so without this the reference server
            // would advertise an endpoint nobody answers.
            services.AddOpcUa().AddHttpsTransport();

            return services.AddSampleServer(
                configurationFile ?? ConfigurationFile,
                provider => new ReferenceServer(provider.GetRequiredService<ITelemetryContext>()),
                configure);
        }
    }
}
