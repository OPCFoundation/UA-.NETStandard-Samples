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
using Quickstarts.ReferenceServer;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the durable subscription server sample: the reference
    /// server of the quickstart library, hosted with a configuration file which turns
    /// durable subscriptions on.
    /// </summary>
    /// <remarks>
    /// The server is the same <see cref="ReferenceServer"/> the reference server sample
    /// runs. What makes this sample a durable subscription server is its configuration:
    /// <c>DurableSubscriptionsEnabled</c> is set, so the reference server backs its
    /// monitored item queues with the <see cref="Quickstarts.Servers.DurableMonitoredItemQueueFactory"/>
    /// and persists its subscriptions with the <see cref="Quickstarts.Servers.SubscriptionStore"/>,
    /// both of which the quickstart library ships and wires up through the
    /// <c>CreateMonitoredItemQueueFactory</c> and <c>CreateSubscriptionStore</c> hooks of
    /// <see cref="Opc.Ua.Server.StandardServer"/>.
    ///
    /// A durable subscription and its queued notifications survive a disconnect, a close
    /// with <c>DeleteSubscriptionsOnClose = false</c>, and a restart of the server; a
    /// client transfers it back with <c>TransferSubscriptions</c> and recovers the values
    /// it missed. The durable subscription client sample is the counterpart which does so.
    /// </remarks>
    public static class DurableSubscriptionServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.DurableSubscriptionServer.Config.xml";

        /// <summary>
        /// Registers the reference server as the hosted OPC UA server of the stack, with
        /// the durable subscription configuration of this sample and the node managers of
        /// the quickstart library.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of the
        /// sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has been
        /// read, for the settings the file cannot express.</param>
        public static IServiceCollection AddDurableSubscriptionServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // the configuration file lists an opc.https base address next to the opc.tcp
            // one. Every transport other than opc.tcp has to be registered or the stack
            // skips the base address whose scheme has no listener factory.
            services.AddOpcUa().AddHttpsTransport();

            return services.AddSampleServer<ReferenceServer>(
                configurationFile ?? ConfigurationFile,
                server => {
                    foreach (INodeManagerFactory factory in Quickstarts.Servers.Utils.NodeManagerFactories)
                    {
                        server.AddNodeManager(factory);
                    }

                    foreach (IAsyncNodeManagerFactory factory in Quickstarts.Servers.Utils.AsyncNodeManagerFactories)
                    {
                        server.AddNodeManager(factory);
                    }
                },
                configure);
        }
    }
}
