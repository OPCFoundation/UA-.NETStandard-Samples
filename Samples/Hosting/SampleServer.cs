/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// The server every sample runs: the server of the stack which takes its parts
    /// from the container, so a sample describes its server on the server builder of
    /// the stack and needs no server class of its own.
    /// </summary>
    /// <remarks>
    /// The one thing the stack still takes from an override alone is the product
    /// description of the server. It is derived from the configuration here, so no
    /// sample has to repeat it.
    /// </remarks>
    public class SampleServer : DependencyInjectionStandardServer
    {
        /// <summary>
        /// Creates the server.
        /// </summary>
        /// <param name="services">The container the parts of the server come from.</param>
        /// <param name="telemetry">The telemetry context of the host.</param>
        /// <param name="timeProvider">The time provider of the host.</param>
        public SampleServer(
            IServiceProvider services,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
            : base(services, telemetry, timeProvider)
        {
        }

        /// <summary>
        /// The product description of the server, as the configuration of the sample
        /// names it: the application name is the product, the product URI is the one
        /// of the configuration, and the build is the one of the sample assembly.
        /// </summary>
        protected override ServerProperties LoadServerProperties()
        {
            ApplicationConfiguration configuration = Configuration;

            return new ServerProperties {
                ManufacturerName = "OPC Foundation",
                ProductName = configuration.ApplicationName,
                ProductUri = configuration.ProductUri,
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp(),
            };
        }
    }
}
