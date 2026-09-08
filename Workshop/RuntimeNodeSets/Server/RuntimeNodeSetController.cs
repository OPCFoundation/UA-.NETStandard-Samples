/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Quickstarts.RuntimeNodeSets.Server
{
    /// <summary>
    /// Which of the four ways of publishing a generation a caller of <c>Reload</c>
    /// asked for. Mirrors the <c>ReloadMode</c> enumeration of the control NodeSet.
    /// </summary>
    public enum ReloadMode
    {
        /// <summary>Drain the requests in flight, then swap.</summary>
        Reload = 0,

        /// <summary>Swap for new requests, let the old generation keep its monitored items.</summary>
        ShadowReload = 1,

        /// <summary>Swap at once and invalidate the monitored items of the old generation.</summary>
        ImmediateReload = 2,
    }

    /// <summary>
    /// The control model of the sample and the <see cref="INodeManagerLifecycle"/>
    /// operations behind its Methods.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample uses both ways of publishing a NodeSet2 document, one for each half of
    /// its address space. The control model - the half which never moves - is registered
    /// in the composition root with <c>AddRuntimeNodeSet</c>. The vendor model - the half
    /// the Methods below replace - is published from
    /// <see cref="IServerStartupTask.OnServerStartedAsync"/> with
    /// <see cref="RuntimeNodeSetLifecycleExtensions.AddRuntimeNodeSetAsync"/>, because
    /// only a registration the lifecycle made can be handed back to it.
    /// </para>
    /// <para>
    /// A lifecycle operation waits for the requests in flight to drain. A Method call is
    /// a request, so the operations below would wait for themselves if the Method and the
    /// model being replaced were served by the same node manager. They are not: the
    /// Methods are on the control model, which is never reloaded, and the vendor options
    /// set <see cref="RuntimeNodeSetOptions.AllowLifecycleFromRequestCallback"/> so that
    /// the lifecycle excludes the initiating request from the drain.
    /// </para>
    /// </remarks>
    public sealed class RuntimeNodeSetController : IServerStartupTask
    {
        private readonly RuntimeNodeSetLibrary m_library;
        private readonly object m_lock = new object();
        private bool m_busy;

        private INodeManagerLifecycle m_lifecycle;
        private ILogger m_logger;
        private NodeManagerRegistration m_vendor;
        private string m_revision;

        /// <summary>
        /// Creates the controller.
        /// </summary>
        /// <remarks>
        /// The composition root creates the controller itself rather than letting the
        /// container do it, because the control model is registered with the fluent
        /// <see cref="RuntimeNodeSetOptions.Configure"/> callback of this instance and
        /// that registration is made before there is a container to resolve anything
        /// from. What the controller needs from the container it is handed in
        /// <see cref="UseServices"/>.
        /// </remarks>
        /// <param name="library">The NodeSet2 documents the sample ships.</param>
        public RuntimeNodeSetController(RuntimeNodeSetLibrary library)
        {
            m_library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>
        /// The revision of the vendor model which is published, or null while none is.
        /// </summary>
        public string LoadedRevision => m_revision;

        /// <summary>
        /// The generation of the live registration of the vendor model, or 0 while it is
        /// not published. Every reload increments it.
        /// </summary>
        public long Generation => m_vendor?.Generation ?? 0;

        /// <summary>
        /// The options which publish the control model at start up. The composition root
        /// hands them to <c>AddRuntimeNodeSet</c>.
        /// </summary>
        /// <remarks>
        /// The control model is the half of this sample which never moves, and it is
        /// registered the way a NodeSet2 document is normally registered: one call in the
        /// composition root, before the server exists. The vendor model is the half which
        /// does move, and it takes the other route - see
        /// <see cref="IServerStartupTask.OnServerStartedAsync"/>.
        /// </remarks>
        public RuntimeNodeSetOptions ControlModelOptions()
        {
            return new RuntimeNodeSetOptions {
                Sources = [RuntimeNodeSetSource.FromFile(m_library.ControlFilePath())],
                DefaultNamespaceUri = RuntimeNodeSetLibrary.ControlNamespaceUri,
                Configure = WireControlModel,
            };
        }

        /// <summary>
        /// Hands the controller what only the container can supply, and returns it as the
        /// startup task the hosted server runs once the server is up.
        /// </summary>
        /// <param name="lifecycle">The lifecycle of the running server, which adds,
        /// reloads and removes node managers.</param>
        /// <param name="logger">The logger of the sample.</param>
        public IServerStartupTask UseServices(INodeManagerLifecycle lifecycle, ILogger logger)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));

            return this;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The vendor model is published here rather than in the composition root so
        /// that the registration it produces is in hand: Reload and Remove take a
        /// <see cref="NodeManagerRegistration"/>, and this is where the sample gets one
        /// to keep. A node manager composed before start up is adopted into
        /// <see cref="INodeManagerLifecycle.Registrations"/> once the server is up, so
        /// it could be found there instead; a model which only has to be served - the
        /// control model of this sample - is still better off in the composition root.
        /// </remarks>
        async ValueTask IServerStartupTask.OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            m_vendor = await m_lifecycle.AddRuntimeNodeSetAsync(
                m_library.VendorOptions(RuntimeNodeSetLibrary.InitialRevision),
                callerContext: null,
                cancellationToken).ConfigureAwait(false);

            m_revision = RuntimeNodeSetLibrary.InitialRevision;
        }

        /// <summary>
        /// Wires the Methods and the variables of the control model, which the NodeSet2
        /// document declares and this class gives behaviour to.
        /// </summary>
        /// <remarks>
        /// This is the <see cref="RuntimeNodeSetOptions.Configure"/> hook: it runs after
        /// the nodes of the document have been added and before the node manager is
        /// published, and the browse paths it resolves are rooted at those nodes. A path
        /// which does not resolve throws here, at start up, rather than at the first
        /// call.
        /// </remarks>
        private void WireControlModel(INodeManagerBuilder builder)
        {
            Wire("ModelControl/Load", OnLoadAsync);
            Wire("ModelControl/Reload", OnReloadAsync);
            Wire("ModelControl/Remove", OnRemoveAsync);

            builder.Variable<string>("ModelControl/LoadedRevision")
                .OnRead(() => m_revision ?? string.Empty);
            builder.Variable<long>("ModelControl/Generation")
                .OnRead(() => Generation);
            // the revisions the sample ships never change while it runs, so this one is
            // a value rather than a getter
            builder.Node<BaseDataVariableState>("ModelControl/AvailableRevisions")
                .Node.WrappedValue = new Variant(RuntimeNodeSetLibrary.Revisions.ToArray());

            void Wire(string browsePath, GenericMethodCalledEventHandler2Async handler)
            {
                builder.Node<MethodState>(browsePath).OnCall(handler);
            }
        }

        /// <summary>
        /// Claims the controller for one lifecycle operation, so a second Client cannot
        /// start a reload while one is still running.
        /// </summary>
        private bool TryBeginOperation()
        {
            lock (m_lock)
            {
                if (m_busy)
                {
                    return false;
                }

                m_busy = true;
                return true;
            }
        }

        /// <summary>
        /// Releases the claim <see cref="TryBeginOperation"/> took.
        /// </summary>
        private void EndOperation()
        {
            lock (m_lock)
            {
                m_busy = false;
            }
        }

        /// <summary>
        /// Publishes a revision of the vendor model on the running server.
        /// </summary>
        private async ValueTask<ServiceResult> OnLoadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            if (!TryGetRevision(inputArguments, 0, out string revision, out ServiceResult error))
            {
                return error;
            }

            if (!TryBeginOperation())
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "Another lifecycle operation is still running.");
            }

            try
            {
                if (m_vendor != null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "Revision {0} is already loaded. Reload or Remove it first.",
                        m_revision);
                }

                m_vendor = await m_lifecycle.AddRuntimeNodeSetAsync(
                    m_library.VendorOptions(revision),
                    context.GetOperationContext(),
                    cancellationToken).ConfigureAwait(false);

                m_revision = revision;
                long generation = m_vendor.Generation;

                #pragma warning disable CA1873 // Justification: the logged values are locals read in a sample.
                m_logger.LogInformation(
                    "Loaded the vendor NodeSet revision {Revision} as generation {Generation}.",
                    revision, generation);
                #pragma warning restore CA1873

                return ServiceResult.Good;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Replaces the published generation of the vendor model with another revision.
        /// </summary>
        private async ValueTask<ServiceResult> OnReloadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            if (!TryGetRevision(inputArguments, 0, out string revision, out ServiceResult error) ||
                !TryGetMode(inputArguments, 1, out ReloadMode mode, out error))
            {
                return error;
            }

            if (!TryBeginOperation())
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "Another lifecycle operation is still running.");
            }

            try
            {
                if (m_vendor == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "No revision of the vendor NodeSet is loaded. Call Load first.");
                }

                RuntimeNodeSetOptions replacement = m_library.VendorOptions(revision);
                IOperationContext caller = context.GetOperationContext();

                // the three modes differ only in what happens to the MonitoredItems of
                // the generation being replaced, and the two which keep serving them do
                // not take a caller context: they never wait for the requests in flight.
                m_vendor = mode switch {
                    ReloadMode.ShadowReload => await m_lifecycle
                        .ShadowReloadRuntimeNodeSetAsync(m_vendor, replacement, cancellationToken)
                        .ConfigureAwait(false),
                    ReloadMode.ImmediateReload => await m_lifecycle
                        .ImmediateReloadRuntimeNodeSetAsync(m_vendor, replacement, cancellationToken)
                        .ConfigureAwait(false),
                    _ => await m_lifecycle
                        .ReloadRuntimeNodeSetAsync(m_vendor, replacement, caller, cancellationToken)
                        .ConfigureAwait(false),
                };

                m_revision = revision;
                long generation = m_vendor.Generation;

                #pragma warning disable CA1873 // Justification: the logged values are locals read in a sample.
                m_logger.LogInformation(
                    "Reloaded the vendor NodeSet as revision {Revision} ({Mode}), generation {Generation}.",
                    revision, mode, generation);
                #pragma warning restore CA1873

                return ServiceResult.Good;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Takes the vendor model off the running server.
        /// </summary>
        private async ValueTask<ServiceResult> OnRemoveAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            if (!TryBeginOperation())
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "Another lifecycle operation is still running.");
            }

            try
            {
                if (m_vendor == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "No revision of the vendor NodeSet is loaded.");
                }

                await m_lifecycle.RemoveAsync(
                    m_vendor,
                    context.GetOperationContext(),
                    cancellationToken).ConfigureAwait(false);

                string removed = m_revision;

                m_vendor = null;
                m_revision = null;

                #pragma warning disable CA1873 // Justification: the logged values are locals read in a sample.
                m_logger.LogInformation("Removed the vendor NodeSet revision {Revision}.", removed);
                #pragma warning restore CA1873

                return ServiceResult.Good;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Reads a revision argument and checks it against the revisions the sample
        /// ships, so an unknown one is a status code rather than an exception out of
        /// the library.
        /// </summary>
        private static bool TryGetRevision(
            ArrayOf<Variant> inputArguments,
            int index,
            out string revision,
            out ServiceResult error)
        {
            revision = null;

            if (inputArguments.Count <= index)
            {
                error = StatusCodes.BadArgumentsMissing;
                return false;
            }

            if (!inputArguments[index].TryGetValue(out string value))
            {
                error = StatusCodes.BadTypeMismatch;
                return false;
            }

            if (!RuntimeNodeSetLibrary.Revisions.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Unknown revision '{0}'. Known revisions: {1}.",
                    value, string.Join(", ", RuntimeNodeSetLibrary.Revisions));
                return false;
            }

            revision = value;
            error = ServiceResult.Good;
            return true;
        }

        /// <summary>
        /// Reads the reload mode, which reaches the server as the Int32 of the
        /// <c>ReloadMode</c> enumeration the control NodeSet declares.
        /// </summary>
        private static bool TryGetMode(
            ArrayOf<Variant> inputArguments,
            int index,
            out ReloadMode mode,
            out ServiceResult error)
        {
            mode = ReloadMode.Reload;

            if (inputArguments.Count <= index)
            {
                error = StatusCodes.BadArgumentsMissing;
                return false;
            }

            // an enumeration reaches the server as the Int32 of its value
            if (!inputArguments[index].TryGetValue(out int value))
            {
                error = StatusCodes.BadTypeMismatch;
                return false;
            }

            if (!Enum.IsDefined(typeof(ReloadMode), value))
            {
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Unknown reload mode {0}.", value);
                return false;
            }

            mode = (ReloadMode)value;
            error = ServiceResult.Good;
            return true;
        }
    }
}
