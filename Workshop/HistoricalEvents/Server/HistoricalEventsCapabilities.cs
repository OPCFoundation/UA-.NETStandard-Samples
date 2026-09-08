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
    /// Gives the <c>Server</c> object the <c>HistoryRead</c> flag of its EventNotifier,
    /// so that a client which asks whether the server keeps a history of its events
    /// before it subscribes gets the right answer.
    /// </summary>
    /// <remarks>
    /// The <c>HistoryServerCapabilities</c> flags themselves are not set here any more:
    /// the diagnostics node manager rolls them up from what every registered historian
    /// provider claims in <c>GetCapabilitiesAsync</c>, and
    /// <see cref="WellReportHistorianProvider"/> claims the five event operations it
    /// serves. What is left is the EventNotifier of the Server object, which is derived
    /// from the same roll-up but only when something asks for it. This runs once the
    /// server has started, with the complete address space - the seam of the stack for
    /// what used to be an override of <c>OnNodeManagerStarted</c> in a server class of
    /// the sample.
    /// </remarks>
    public sealed class HistoricalEventsCapabilities : IServerStartupTask
    {
        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            // the capabilities live in the diagnostics node manager, which the ambient
            // server context deliberately does not hand out; the live server does.
            IDiagnosticsNodeManager diagnostics = ((IServerInternal)server).DiagnosticsNodeManager;

            return diagnostics.UpdateServerEventNotifierAsync(cancellationToken);
        }
    }
}
