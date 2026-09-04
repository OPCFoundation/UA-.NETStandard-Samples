/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Quickstarts.RuntimeNodeSets.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the RuntimeNodeSets server sample: its configuration
    /// file, the vendor NodeSet2 document it hosts and the controller which reloads and
    /// removes that document while the server runs.
    /// </summary>
    /// <remarks>
    /// This sample has no node manager class, no model design and no generated code. The
    /// address space is two NodeSet2 documents; <c>AddRuntimeNodeSet</c> is the whole of
    /// the registration for one of them, and the other is published by
    /// <see cref="RuntimeNodeSetController"/> once the server is up.
    /// </remarks>
    public static class RuntimeNodeSetsServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.RuntimeNodeSetsServer.Config.xml";

        /// <summary>
        /// Registers the RuntimeNodeSets server as the hosted OPC UA server of the
        /// stack, together with the NodeSet2 documents it serves.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddRuntimeNodeSetsServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            var library = new RuntimeNodeSetLibrary();
            var controller = new RuntimeNodeSetController(library);

            return services.AddSampleServer(
                configurationFile ?? ConfigurationFile,
                server => {
                    server.Services.TryAddSingleton(library);
                    server.Services.TryAddSingleton(controller);

                    // the control model, hosted the way a NodeSet2 document which only
                    // has to be served is normally hosted: one call, no code. The factory
                    // reads the Models metadata of the file right here, so the namespace
                    // it claims is in the namespace table before the server starts.
                    server.AddRuntimeNodeSet(controller.ControlModelOptions());

                    // the vendor model is published by the controller instead, because it
                    // is the one which gets reloaded and removed and only a registration
                    // the lifecycle made can be handed back to it.
                    server.Services.AddSingleton(provider => controller.UseServices(
                        provider.GetRequiredService<INodeManagerLifecycle>(),
                        provider.GetRequiredService<ILogger<RuntimeNodeSetController>>()));
                },
                configure);
        }
    }
}
