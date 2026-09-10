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

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// Advertises filtered retain on the <c>ConditionType</c> node, which is where OPC UA
    /// Part 9 puts the answer to "does this server support it".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>SupportsFilteredRetain</c> Property is declared on <c>ConditionType</c> with
    /// no modelling rule, so it exists once, on the type node, and never on a condition
    /// instance: a client asks the type whether the trailing event of Part 9 B.1.4 is
    /// something it can rely on. Whether a given condition takes part is the other half,
    /// and the stack models that as a server side switch on <c>ConditionState</c> which
    /// <c>SourceState.CreateAlarm</c> sets on every alarm of this server. This task makes
    /// the two agree: the standard address space ships the type node with the Property set
    /// to <c>false</c>, so a server which does support filtered retain has to say so.
    /// </para>
    /// <para>
    /// The type node belongs to the standard address space rather than to any node manager
    /// of the sample. A startup task is the seam which hands a sample the running server,
    /// and the diagnostics node manager is what resolves a node of that address space by
    /// its NodeId.
    /// </para>
    /// </remarks>
    public sealed class FilteredRetainCapability : IServerStartupTask
    {
        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            // the node managers live on the live server; the ambient server context
            // deliberately does not hand them out.
            var live = (IServerInternal)server;

            PropertyState<bool> supportsFilteredRetain = live.DiagnosticsNodeManager
                .FindPredefinedNode<PropertyState<bool>>(VariableIds.ConditionType_SupportsFilteredRetain);

            if (supportsFilteredRetain == null)
            {
                return;
            }

            supportsFilteredRetain.Value = true;

            await supportsFilteredRetain
                .ClearChangeMasksAsync(new ServerSystemContext(live), false, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
