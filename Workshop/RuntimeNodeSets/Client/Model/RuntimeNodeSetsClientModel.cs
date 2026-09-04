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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Quickstarts.RuntimeNodeSets.Client.Model
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// Which reload the server was asked for. Mirrors the <c>ReloadMode</c> enumeration
    /// of the control model, which no code on either side compiles.
    /// </summary>
    public enum ReloadMode
    {
        /// <summary>Drain the requests in flight, then swap.</summary>
        Reload = 0,

        /// <summary>Swap, and let the old generation keep serving the MonitoredItems it has.</summary>
        ShadowReload = 1,

        /// <summary>Swap, and invalidate the MonitoredItems of the old generation.</summary>
        ImmediateReload = 2,
    }

    /// <summary>
    /// What the control model of the server reports about the model it hosts.
    /// </summary>
    /// <param name="LoadedRevision">The revision that is published, or an empty string.</param>
    /// <param name="Generation">The generation of the live registration; every reload
    /// increments it.</param>
    public sealed record ModelState(string LoadedRevision, long Generation)
    {
        /// <summary>
        /// What a detached model reports.
        /// </summary>
        public static ModelState None { get; } = new ModelState(string.Empty, 0);
    }

    /// <summary>
    /// One node of the vendor model, as the client found it by browsing.
    /// </summary>
    /// <param name="Name">The browse name.</param>
    /// <param name="NodeId">The node.</param>
    /// <param name="Depth">How far below the top folder the node sits.</param>
    /// <param name="Value">The value of a variable, or the status when it could not be
    /// read; empty for an object.</param>
    public sealed record VendorNode(string Name, NodeId NodeId, int Depth, string Value);

    /// <summary>
    /// The payload of <see cref="RuntimeNodeSetsClientModel.WatchedValueChanged"/>.
    /// </summary>
    public sealed class WatchedValueEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public WatchedValueEventArgs(DataValue value)
        {
            Value = value;
        }

        /// <summary>
        /// What the server reported for the watched conveyor speed - the value while the
        /// MonitoredItem is being served, the status code once it is not.
        /// </summary>
        public DataValue Value { get; }
    }

    /// <summary>
    /// The client model of the RuntimeNodeSets client: it drives the control model of the
    /// server, browses the model that is published, and watches one of its variables
    /// across a reload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is generated. The server's address space is a pair of NodeSet2
    /// documents it reads at run time, and this client finds everything it needs by
    /// browse path in the two namespaces of those documents. That is the situation the
    /// sample is about: a model the client was never compiled against.
    /// </para>
    /// <para>
    /// The MonitoredItem on <c>Conveyor1/Speed</c> is the interesting half. The three
    /// reload modes differ only in what happens to the items of the generation being
    /// replaced, and a client sees that difference and nothing else: a plain reload
    /// deletes them, a shadow reload keeps them alive on the retired generation until
    /// they drain, and an immediate reload invalidates them with
    /// <see cref="StatusCodes.BadNodeIdUnknown"/>.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The handles below are taken, cleared and released by OnDetachingAsync, which the detach of the base class runs - on a detach as well as on a dispose. The analyzer does not follow an asynchronous release through a virtual hook.")]
    public sealed class RuntimeNodeSetsClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the vendor model, in both of its revisions.
        /// </summary>
        public const string VendorNamespaceUri =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Line/";

        /// <summary>
        /// The namespace of the control model, which the server never replaces.
        /// </summary>
        public const string ControlNamespaceUri =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Control/";

        private const string kWatchedItem = "Conveyor1/Speed";

        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        private ISubscription m_subscription;
        private NodeId m_control = NodeId.Null;
        private NodeId m_load = NodeId.Null;
        private NodeId m_reload = NodeId.Null;
        private NodeId m_remove = NodeId.Null;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public RuntimeNodeSetsClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_callbacks.DataChangeCallback = OnDataChanges;
        }

        /// <summary>
        /// The revisions the server has NodeSet2 documents for, read from the control
        /// model when the session was attached.
        /// </summary>
        public IReadOnlyList<string> AvailableRevisions { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Whether the server serves the control model at all. False against any other
        /// server, and then none of the operations below can be offered.
        /// </summary>
        public bool IsControlModelAvailable => !m_control.IsNull;

        /// <summary>
        /// Raised for every notification of the watched conveyor speed, including the one
        /// which reports that the MonitoredItem no longer resolves.
        /// </summary>
        public event EventHandler<WatchedValueEventArgs> WatchedValueChanged;

        /// <summary>
        /// Reads what the control model reports about the model that is published.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task<ModelState> ReadStateAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (m_control.IsNull)
            {
                return ModelState.None;
            }

            List<NodeId> nodes = await ResolveControlChildrenAsync(
                session, ct, "LoadedRevision", "Generation").ConfigureAwait(false);

            var valuesToRead = new List<ReadValueId>();

            foreach (NodeId nodeId in nodes)
            {
                valuesToRead.Add(new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value });
            }

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, valuesToRead, ct)
                .ConfigureAwait(false);

            DataValue[] results = response.Results.ToArray();

            string revision = results.Length > 0 && results[0].WrappedValue.TryGetValue(out string text)
                ? text
                : string.Empty;

            long generation = results.Length > 1 && results[1].WrappedValue.TryGetValue(out long value)
                ? value
                : 0;

            return new ModelState(revision, generation);
        }

        /// <summary>
        /// Browses the vendor model, or reports that nothing is published.
        /// </summary>
        /// <remarks>
        /// Read again after every operation rather than remembered: the whole point of the
        /// sample is that the address space below this folder is replaced while the
        /// session stays open.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<VendorNode>> BrowseVendorModelAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            NodeId lineId = await ResolveVendorRootAsync(session, ct).ConfigureAwait(false);

            var nodes = new List<VendorNode>();

            if (lineId.IsNull)
            {
                return nodes;
            }

            await AppendAsync(session, nodes, lineId, 0, new HashSet<NodeId> { lineId }, ct)
                .ConfigureAwait(false);

            return nodes;
        }

        /// <summary>
        /// Publishes a revision of the vendor model on the running server.
        /// </summary>
        /// <param name="revision">One of <see cref="AvailableRevisions"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> LoadAsync(string revision, CancellationToken ct = default)
        {
            return CallAsync($"Loading {revision}", m_load, ct, Variant.From(revision));
        }

        /// <summary>
        /// Replaces the published generation with another revision.
        /// </summary>
        /// <param name="revision">One of <see cref="AvailableRevisions"/>.</param>
        /// <param name="mode">Which of the three reloads to ask for.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> ReloadAsync(
            string revision,
            ReloadMode mode,
            CancellationToken ct = default)
        {
            return CallAsync(
                $"{mode} to {revision}",
                m_reload,
                ct,
                Variant.From(revision),

                // the argument is declared as the ReloadMode enumeration of the control
                // model. A client which has no compiled copy of that model sends the Int32
                // its value is, which is what an enumeration is on the wire.
                Variant.From((int)mode));
        }

        /// <summary>
        /// Takes the vendor model off the running server.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> RemoveAsync(CancellationToken ct = default)
        {
            return CallAsync("Removing the model", m_remove, ct);
        }

        /// <summary>
        /// Watches <c>Conveyor1/Speed</c> of the published model, replacing whatever was
        /// being watched before.
        /// </summary>
        /// <remarks>
        /// The item is created against the node id the vendor document declares. Both
        /// revisions give that node the same id, which is what lets a shadow reload keep
        /// the item alive across the swap.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> WatchSpeedAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            await DeleteSubscriptionAsync().ConfigureAwait(false);

            NodeId lineId = await ResolveVendorRootAsync(session, ct).ConfigureAwait(false);

            if (lineId.IsNull)
            {
                return new OperationResult("Watching the conveyor speed", StatusCodes.BadNodeIdUnknown);
            }

            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(VendorNamespaceUri);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                lineId,
                wellKnownNamespaceUris,
                ct,
                "1:Conveyor1/1:Speed").ConfigureAwait(false);

            if (nodes.Count == 0 || nodes[0].IsNull)
            {
                return new OperationResult("Watching the conveyor speed", StatusCodes.BadNodeIdUnknown);
            }

            var options = new OptionsMonitor<SubscriptionOptions>(
                SampleSession.DefaultSubscriptionOptions);

            ISubscription subscription = SampleSession.AddSubscription(session, m_callbacks, options);
            m_subscription = subscription;

            subscription.MonitoredItems.TryAdd(
                kWatchedItem,
                new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                    StartNodeId = nodes[0],
                    AttributeId = Attributes.Value,
                    SamplingInterval = TimeSpan.FromMilliseconds(500),
                }),
                out IMonitoredItem _);

            return new OperationResult($"Watching {nodes[0]}", StatusCodes.Good);
        }

        /// <summary>
        /// Stops watching the conveyor speed.
        /// </summary>
        public async Task StopWatchingAsync()
        {
            await DeleteSubscriptionAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(ControlNamespaceUri);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                Opc.Ua.ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                "1:ModelControl").ConfigureAwait(false);

            m_control = nodes.Count > 0 ? nodes[0] : NodeId.Null;

            if (m_control.IsNull)
            {
                return;
            }

            List<NodeId> methods = await ResolveControlChildrenAsync(
                session, ct, "Load", "Reload", "Remove").ConfigureAwait(false);

            m_load = methods[0];
            m_reload = methods[1];
            m_remove = methods[2];

            AvailableRevisions = await ReadRevisionsAsync(session, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            // done before the session is closed: closing a session which still carries a
            // subscription waits for the publish pipeline to drain.
            await DeleteSubscriptionAsync().ConfigureAwait(false);

            m_control = NodeId.Null;
            m_load = NodeId.Null;
            m_reload = NodeId.Null;
            m_remove = NodeId.Null;
            AvailableRevisions = Array.Empty<string>();
        }

        /// <summary>
        /// Calls one Method of the control model and reports what the server answered.
        /// </summary>
        /// <remarks>
        /// The refusals are half of what there is to see - a Load over a model which is
        /// already published, a revision the server has no document for - so the status
        /// code is reported rather than thrown.
        /// </remarks>
        private async Task<OperationResult> CallAsync(
            string what,
            NodeId methodId,
            CancellationToken ct,
            params Variant[] arguments)
        {
            ISession session = RequireSession();

            if (m_control.IsNull || methodId.IsNull)
            {
                return new OperationResult(what, StatusCodes.BadNotSupported);
            }

            var request = new CallMethodRequest {
                ObjectId = m_control,
                MethodId = methodId,
                InputArguments = arguments,
            };

            CallResponse response = await session
                .CallAsync(null, new List<CallMethodRequest> { request }, ct)
                .ConfigureAwait(false);

            CallMethodResult[] results = response.Results.ToArray();

            return new OperationResult(
                what,
                results.Length > 0 ? results[0].StatusCode : StatusCodes.BadUnexpectedError);
        }

        /// <summary>
        /// Resolves children of the control object by browse name.
        /// </summary>
        private Task<List<NodeId>> ResolveControlChildrenAsync(
            ISession session,
            CancellationToken ct,
            params string[] names)
        {
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(ControlNamespaceUri);

            var paths = new string[names.Length];

            for (int ii = 0; ii < names.Length; ii++)
            {
                paths[ii] = $"1:{names[ii]}";
            }

            return SampleSession.TranslateBrowsePathsAsync(
                session, m_control, wellKnownNamespaceUris, ct, paths);
        }

        /// <summary>
        /// The top folder of the vendor model, or a null node id while none is published.
        /// </summary>
        private static async Task<NodeId> ResolveVendorRootAsync(ISession session, CancellationToken ct)
        {
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(VendorNamespaceUri);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                Opc.Ua.ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                "1:ConveyorLine").ConfigureAwait(false);

            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        /// <summary>
        /// Reads the revisions the control model advertises.
        /// </summary>
        private async Task<IReadOnlyList<string>> ReadRevisionsAsync(ISession session, CancellationToken ct)
        {
            List<NodeId> nodes = await ResolveControlChildrenAsync(session, ct, "AvailableRevisions")
                .ConfigureAwait(false);

            if (nodes.Count == 0 || nodes[0].IsNull)
            {
                return Array.Empty<string>();
            }

            DataValue value = await session.ReadValueAsync(nodes[0], ct).ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out ArrayOf<string> revisions)
                ? revisions.ToArray()
                : Array.Empty<string>();
        }

        /// <summary>
        /// Appends the children of a node to a list, with their depth and their value.
        /// </summary>
        private static async Task AppendAsync(
            ISession session,
            List<VendorNode> nodes,
            NodeId parentId,
            int depth,
            HashSet<NodeId> visited,
            CancellationToken ct)
        {
            var nodeToBrowse = new BrowseDescription {
                NodeId = parentId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, nodeToBrowse, false, ct)
                .ConfigureAwait(false);

            if (references == null)
            {
                return;
            }

            foreach (ReferenceDescription reference in references)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);

                nodes.Add(new VendorNode(
                    reference.BrowseName.Name,
                    nodeId,
                    depth,
                    await ValueOfAsync(session, nodeId, reference.NodeClass, ct).ConfigureAwait(false)));

                if (reference.NodeClass == NodeClass.Object && visited.Add(nodeId))
                {
                    await AppendAsync(session, nodes, nodeId, depth + 1, visited, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// The value of a variable, or why it could not be read.
        /// </summary>
        private static async Task<string> ValueOfAsync(
            ISession session,
            NodeId nodeId,
            NodeClass nodeClass,
            CancellationToken ct)
        {
            if (nodeClass != NodeClass.Variable)
            {
                return string.Empty;
            }

            DataValue value = await session.ReadValueAsync(nodeId, ct).ConfigureAwait(false);

            return StatusCode.IsGood(value.StatusCode)
                ? value.WrappedValue.ToString()
                : value.StatusCode.ToString();
        }

        /// <summary>
        /// Reports what the server sent for the watched conveyor speed.
        /// </summary>
        /// <remarks>
        /// The engine calls this on a publish worker; the base class posts the event to
        /// the thread the model was created on.
        /// </remarks>
        private void OnDataChanges(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] notifications,
            PublishState publishState)
        {
            foreach (DataValueChange change in notifications)
            {
                if (change.MonitoredItem?.Name == kWatchedItem)
                {
                    Raise(WatchedValueChanged, new WatchedValueEventArgs(change.Value));
                }
            }
        }

        /// <summary>
        /// Deletes the subscription on the server and drops it from the subscription
        /// manager.
        /// </summary>
        private async Task DeleteSubscriptionAsync()
        {
            ISubscription subscription = m_subscription;

            m_subscription = null;

            if (subscription != null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
