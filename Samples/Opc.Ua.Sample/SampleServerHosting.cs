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
using Opc.Ua.Sample;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the UA sample server: the node managers the
    /// <see cref="SampleServer"/> is made of, and the server as a hosted server of the
    /// stack.
    /// </summary>
    /// <remarks>
    /// The sample server is hosted two ways: the sample server application and the
    /// sample client, which is a client and a server at once, start it through the
    /// application instance of the sample application host, and the tests host it as
    /// the hosted server of the stack. Both take the node managers from the container,
    /// so the registration is made once, here, and the server class registers nothing
    /// itself.
    /// </remarks>
    public static class UaSampleServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample server.
        /// </summary>
        public const string ConfigurationFile = "Opc.Ua.SampleServer.Config.xml";

        /// <summary>
        /// Registers the node managers of the sample server with the container: the test
        /// data, the memory buffer and the boiler.
        /// </summary>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddUaSampleServerNodeManagers(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            return services
                .AddSampleNodeManager<global::TestData.TestDataNodeManagerFactory>()
                .AddSampleNodeManager<global::MemoryBuffer.MemoryBufferNodeManagerFactory>()
                .AddSampleNodeManager<global::Boiler.BoilerNodeManagerFactory>();
        }

        /// <summary>
        /// Registers the sample server as the hosted OPC UA server of the stack,
        /// together with its node managers.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample server when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddUaSampleServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            return services
                .AddSampleServer<SampleServer>(configurationFile ?? ConfigurationFile, configure)
                .AddUaSampleServerNodeManagers();
        }
    }
}
