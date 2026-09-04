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
using Quickstarts.HistoricalEvents.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the HistoricalEvents server sample: its configuration
    /// file, the node manager the server is made of and the history capabilities it
    /// advertises.
    /// </summary>
    /// <remarks>
    /// Everything is registered with the server builder of the stack and created by
    /// the container; the hosted server hands it to the shared sample server, so the
    /// sample has no server class of its own. The entry point of the sample and the
    /// tests which host it share this one registration.
    /// </remarks>
    public static class HistoricalEventsServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "HistoricalEventsServer.Config.xml";

        /// <summary>
        /// Registers the HistoricalEvents server as the hosted OPC UA server of the
        /// stack, together with the node manager it serves and the event history
        /// capabilities it advertises once it has started.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddHistoricalEventsServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            return services.AddSampleServer(
                configurationFile ?? ConfigurationFile,
                server => server
                    .AddNodeManager<HistoricalEventsNodeManagerFactory>()
                    .AddStartupTask<HistoricalEventsCapabilities>(),
                configure);
        }
    }
}
