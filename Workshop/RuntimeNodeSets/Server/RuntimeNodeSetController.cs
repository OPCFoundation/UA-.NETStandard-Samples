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
    /// What a caller of <c>Reload</c> asked to happen to the MonitoredItems of the
    /// generation being replaced. Mirrors the <c>ReloadMode</c> enumeration of the control
    /// NodeSet, and maps onto <see cref="NodeManagerMonitoredItemTransitionMode"/>.
    /// </summary>
    public enum ReloadMode
    {
        /// <summary>Move the monitored items whose node still exists to the replacement, invalidate the others.</summary>
        Migrate = 0,

        /// <summary>Invalidate every monitored item of the old generation.</summary>
        Invalidate = 1,
    }

    /// <summary>
    /// The control model of the sample and the <see cref="INodeManagerLifecycle"/>
    /// transactions behind its Methods.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample uses both ways of publishing a NodeSet2 document, one for each half of
    /// its address space. The control model - the half which never moves - is registered
    /// in the composition root with <c>AddRuntimeNodeSet</c>. The vendor model - the half
    /// the Methods below replace - is added through the lifecycle from
    /// <see cref="IServerStartupTask.OnServerStartedAsync"/>, so that the controller holds
    /// the <see cref="NodeManagerRegistration"/> every later change is made against.
    /// </para>
    /// <para>
    /// Every change is one transaction in three steps, see <see cref="ApplyAsync"/>:
    /// <see cref="INodeManagerLifecycle.PrepareAsync"/> builds the candidate privately,
    /// a decision callback is the last point at which it can still be refused, and
    /// <see cref="IPreparedNodeManagerBatch.CommitAsync(Func{CancellationToken, ValueTask}, CancellationToken)"/>
    /// publishes it in one routing switch.
    /// </para>
    /// <para>
    /// A commit waits for reconciliation and readiness, but not for the requests in
    /// flight to drain: the generation it replaces is retired asynchronously. A Method
    /// call can therefore run a transaction without waiting for itself, and needs no
    /// caller context. The Methods still live on the control model, which is never
    /// reloaded, so that a reload never replaces the node manager serving the call that
    /// asked for it.
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
        /// that the registration it produces is in hand: a replacement or a removal is
        /// made against the exact <see cref="NodeManagerRegistration"/> it changes, and
        /// this is where the sample gets one to keep. A node manager composed before
        /// start up is listed in <see cref="INodeManagerLifecycle.Registrations"/> too,
        /// so it could be found there instead; publishing it here is what lets
        /// <c>Load</c> after a <c>Remove</c> take the same route as the first load.
        /// </remarks>
        async ValueTask IServerStartupTask.OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            string revision = RuntimeNodeSetLibrary.InitialRevision;

            m_vendor = await ApplyAsync(
                NodeManagerBatchChange.Add(VendorFactory(revision)),
                revision,
                cancellationToken).ConfigureAwait(false);

            m_revision = revision;
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
        /// The factory which builds one generation of the vendor model from a revision.
        /// </summary>
        /// <remarks>
        /// <see cref="RuntimeNodeSetNodeManagerFactory"/> is the factory
        /// <c>AddRuntimeNodeSet</c> registers in the composition root; handed to the
        /// lifecycle it builds the same node manager on the running server.
        /// </remarks>
        private RuntimeNodeSetNodeManagerFactory VendorFactory(string revision)
        {
            return new RuntimeNodeSetNodeManagerFactory(m_library.VendorOptions(revision));
        }

        /// <summary>
        /// Runs one lifecycle transaction: prepare, decide, commit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="INodeManagerLifecycle.PrepareAsync"/> takes a batch: every change in
        /// it is published by one routing switch or none is. The Methods of this sample
        /// each make one change, so each batch holds one. A server which replaced the
        /// vendor model and retired a second model it depends on would put both changes
        /// in the same array, and no request would ever see one without the other.
        /// </para>
        /// <para>
        /// Preparing is where the work happens - the document is parsed, the
        /// <see cref="RuntimeNodeSetOptions.Configure"/> hook runs and the candidate
        /// address space is built - but nothing is published yet, and a client sees
        /// the old generation throughout. A batch which is not committed is disposed,
        /// which releases its candidates; <c>await using</c> covers every way out of
        /// this method, and disposing a committed batch does nothing.
        /// </para>
        /// </remarks>
        /// <param name="change">The addition, replacement or removal to publish.</param>
        /// <param name="revision">The revision the change publishes, or null for a
        /// removal.</param>
        /// <param name="cancellationToken">Cancels the transaction before its decision.
        /// Once the routing switch is published it cannot be undone.</param>
        /// <returns>The registration of the published generation, or null for a
        /// removal.</returns>
        private async ValueTask<NodeManagerRegistration> ApplyAsync(
            NodeManagerBatchChange change,
            string revision,
            CancellationToken cancellationToken)
        {
            await using IPreparedNodeManagerBatch prepared = await m_lifecycle
                .PrepareAsync([change], cancellationToken)
                .ConfigureAwait(false);

            NodeManagerBatchResult result = await prepared
                .CommitAsync(ct => DecideAsync(prepared, revision, ct), cancellationToken)
                .ConfigureAwait(false);

            // the change is live from here on; a failure while retiring the generation
            // it replaced is reported beside the result rather than thrown, because
            // nothing can make the commit undone any more
            if (result.CleanupFailure != null)
            {
                m_logger.LogWarning(
                    result.CleanupFailure,
                    "The vendor NodeSet change was published, but retiring the old generation failed.");
            }

            return result.Registrations.Count > 0 ? result.Registrations[0] : null;
        }

        /// <summary>
        /// The decision step of a transaction: the last moment at which the prepared
        /// candidates can still be refused.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="IPreparedNodeManagerBatch.CommitAsync(Func{CancellationToken, ValueTask}, CancellationToken)"/>
        /// calls it exactly once, after rechecking that the routing has not changed since
        /// the batch was prepared and before it publishes anything. Throwing here refuses
        /// the batch: nothing is published and the caller disposes it.
        /// </para>
        /// <para>
        /// This is where a server records a durable decision - which revision is
        /// published, so that a restart publishes the same one - because a record written
        /// before this point may describe a change which is never made, and one written
        /// after the commit may be lost to a crash in between. This sample persists
        /// nothing, so its decision checks what it is about to publish: the prepared
        /// registrations must serve the vendor namespace and nothing else. A callback
        /// here must not start another lifecycle operation; the SDK rejects one.
        /// </para>
        /// </remarks>
        private ValueTask DecideAsync(
            IPreparedNodeManagerBatch prepared,
            string revision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (NodeManagerRegistration candidate in prepared.Registrations)
            {
                if (candidate.NamespaceUris.Count != 1 ||
                    candidate.NamespaceUris[0] != RuntimeNodeSetLibrary.VendorNamespaceUri)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Revision {0} does not serve the vendor namespace alone.",
                        revision);
                }
            }

            #pragma warning disable CA1873 // Justification: the logged values are locals read in a sample.
            m_logger.LogDebug(
                "Committing {Count} prepared vendor NodeSet generation(s) for revision {Revision}.",
                prepared.Registrations.Count, revision ?? "(removal)");
            #pragma warning restore CA1873

            return ValueTask.CompletedTask;
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

                m_vendor = await ApplyAsync(
                    NodeManagerBatchChange.Add(VendorFactory(revision)),
                    revision,
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

                // the two transitions differ only in what happens to the MonitoredItems
                // of the generation being replaced. Neither waits for the requests in
                // flight, so a Method call may run one without deadlocking.
                NodeManagerMonitoredItemTransitionMode transition = mode == ReloadMode.Invalidate
                    ? NodeManagerMonitoredItemTransitionMode.Invalidate
                    : NodeManagerMonitoredItemTransitionMode.Migrate;

                m_vendor = await ApplyAsync(
                    NodeManagerBatchChange.Replace(m_vendor, VendorFactory(revision), transition),
                    revision,
                    cancellationToken).ConfigureAwait(false);

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

                await ApplyAsync(
                    NodeManagerBatchChange.Remove(m_vendor),
                    revision: null,
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
            mode = ReloadMode.Migrate;

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
