/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Quickstarts.HistoricalEvents.Server
{
    /// <summary>
    /// Advertises the event history the server serves: the
    /// <c>HistoryServerCapabilities</c> object says that events can be read, inserted,
    /// replaced, updated and deleted, and the <c>Server</c> object gets the
    /// <c>HistoryRead</c> flag of its EventNotifier. A client which asks the server
    /// what it can do before it subscribes gets the right answer.
    /// </summary>
    /// <remarks>
    /// The capabilities node is owned by the diagnostics node manager of the stack and
    /// the flags are not rolled up from the node managers which serve history, so
    /// something has to set them. This runs once the server has started, with the
    /// complete address space - the seam of the stack for what used to be an override
    /// of <c>OnNodeManagerStarted</c> in a server class of the sample.
    /// </remarks>
    public sealed class HistoricalEventsCapabilities : IServerStartupTask
    {
        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            // the capabilities live in the diagnostics node manager, which the ambient
            // server context deliberately does not hand out; the live server does.
            IDiagnosticsNodeManager diagnostics = ((IServerInternal)server).DiagnosticsNodeManager;

            HistoryServerCapabilitiesState capabilities = await diagnostics
                .GetDefaultHistoryCapabilitiesAsync(cancellationToken)
                .ConfigureAwait(false);

            capabilities.AccessHistoryEventsCapability.Value = true;
            capabilities.InsertEventCapability.Value = true;
            capabilities.ReplaceEventCapability.Value = true;
            capabilities.UpdateEventCapability.Value = true;
            capabilities.DeleteEventCapability.Value = true;

            await diagnostics
                .UpdateServerEventNotifierAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
