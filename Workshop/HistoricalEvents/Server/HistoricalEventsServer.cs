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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.HistoricalEvents.Server
{
    /// <summary>
    /// Implements a basic Quickstart Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    ///
    /// This sub-class specifies non-configurable metadata such as Product Name and registers
    /// the HistoricalEventsNodeManager which provides access to the data exposed by the Server.
    /// </remarks>
    public partial class HistoricalEventsServer : StandardServer
    {
        public HistoricalEventsServer(ITelemetryContext telemetry) : base(telemetry)
        {
        }

        #region Overridden Methods
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
            properties.ProductName = "Quickstart HistoricalEvents Server";
            properties.ProductUri = "http://opcfoundation.org/Quickstart/HistoricalEventsServer/v1.0";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            return properties;
        }

        /// <summary>
        /// Advertises the event history this server offers.
        /// </summary>
        /// <remarks>
        /// The diagnostics node manager rolls the capabilities of every registered
        /// historian provider up into the HistoryServerCapabilities node on startup,
        /// but that roll-up only covers the data half of that node - the flags which
        /// describe reading and writing values. The event flags are set here, once
        /// the whole address space exists, and the event notifier of the server
        /// object is then derived again so that it advertises the event history the
        /// way the flags now describe it.
        /// </remarks>
        protected override async ValueTask OnNodeManagerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            await base.OnNodeManagerStartedAsync(server, cancellationToken).ConfigureAwait(false);

            HistoryServerCapabilitiesState capabilities = await server.DiagnosticsNodeManager
                .GetDefaultHistoryCapabilitiesAsync(cancellationToken)
                .ConfigureAwait(false);

            capabilities.AccessHistoryEventsCapability.Value = true;
            capabilities.InsertEventCapability.Value = true;
            capabilities.ReplaceEventCapability.Value = true;
            capabilities.UpdateEventCapability.Value = true;
            capabilities.DeleteEventCapability.Value = true;

            await server.DiagnosticsNodeManager
                .UpdateServerEventNotifierAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        #endregion
    }
}
