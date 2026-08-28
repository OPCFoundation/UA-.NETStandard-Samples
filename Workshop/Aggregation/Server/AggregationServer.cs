/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Server;


namespace AggregationServer
{
    /// <summary>
    /// Implements a basic Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    ///
    /// This sub-class specifies non-configurable metadata such as Product Name and registers
    /// one AggregationNodeManager per configured downstream endpoint, each of which provides
    /// access to the data exposed by one aggregated server.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample public API name matches the workshop namespace.")]
    public partial class AggregationServer : ReverseConnectServer
    {
        public AggregationServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        #region Overridden Methods
        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// The endpoints of the servers to aggregate come from the configuration, which is
        /// not available before startup, so the node manager factories are registered here
        /// rather than in the constructor. The base implementation then creates one node
        /// manager per registered <see cref="IAsyncNodeManagerFactory"/>; only the first
        /// one publishes the aggregation type model.
        /// </remarks>
        protected override async ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            m_logger.LogInformation("Creating the Node Managers.");

            ConfiguredEndpointCollection endpoints = configuration.ParseExtension<ConfiguredEndpointCollection>();

            // start the reverse connect host
            ReverseConnectManager reverseConnectManager = null;
            if (configuration.ClientConfiguration?.ReverseConnect != null)
            {
                var reverseConnect = configuration.ClientConfiguration.ReverseConnect;
                // start the reverse connection manager
#pragma warning disable CA2000 // Justification: ReverseConnectManager ownership is transferred to node managers.
                reverseConnectManager = new Opc.Ua.Client.ReverseConnectManager(server.Telemetry);
#pragma warning restore CA2000
                foreach (var endpoint in reverseConnect.ClientEndpoints)
                {
                    reverseConnectManager.AddEndpoint(new Uri(endpoint.EndpointUrl));
                }
                // start the server even if no endpoint is configured, because
                // app config can change during operation  and the manager object
                // is needed
                await reverseConnectManager.StartServiceAsync(configuration, cancellationToken).ConfigureAwait(false);
            }

            // a restarted server registers a fresh factory set for the current configuration.
            foreach (AggregationNodeManagerFactory factory in m_aggregationFactories)
            {
                RemoveNodeManager(factory);
            }
            m_aggregationFactories.Clear();

            bool ownsTypeModel = true;
            foreach (ConfiguredEndpoint endpoint in endpoints.Endpoints)
            {
                var factory = new AggregationNodeManagerFactory(endpoint, reverseConnectManager, ownsTypeModel);
                m_aggregationFactories.Add(factory);
                AddNodeManager(factory);
                ownsTypeModel = false;
            }

            // create master node manager from the registered factories.
            return await base.CreateMasterNodeManagerAsync(server, configuration, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName = "Aggregation Server";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.ProductUri = "http://opcfoundation.org/AggregationServer/v1.4";
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            return properties;
        }
        #endregion

        #region Private Fields
        private readonly List<AggregationNodeManagerFactory> m_aggregationFactories = new List<AggregationNodeManagerFactory>();
        #endregion
    }
}
