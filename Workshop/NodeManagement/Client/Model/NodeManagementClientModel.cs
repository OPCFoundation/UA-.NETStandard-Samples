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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ModelChange;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Samples.Client;

namespace Quickstarts.NodeManagement.Client.Model
{
    // The source generator emits a Quickstarts.NodeManagement.BrowseNames and ObjectIds for
    // the model of the server. This namespace is a child of that one, so those would win over
    // the standard sets of the same name: both are named apart here.
    using ModelNames = Quickstarts.NodeManagement.BrowseNames;
    using BrowseNames = Opc.Ua.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// One node of the address space the client shows.
    /// </summary>
    /// <param name="Name">The browse name of the node.</param>
    /// <param name="NodeId">The node.</param>
    /// <param name="NodeClass">Object or Variable.</param>
    /// <param name="Depth">How far below the folder the node sits; a child of a device is 1.</param>
    /// <param name="Value">The value of a variable, the status when it could not be read, or empty for an object.</param>
    public sealed record NodeEntry(
        string Name,
        NodeId NodeId,
        NodeClass NodeClass,
        int Depth,
        string Value);

    /// <summary>
    /// What the two folders of the server hold.
    /// </summary>
    /// <param name="Devices">The Devices folder and everything below it, in browse order.</param>
    /// <param name="Commissioned">The nodes the Commissioned group references.</param>
    public sealed record AddressSpace(
        IReadOnlyList<NodeEntry> Devices,
        IReadOnlyList<NodeEntry> Commissioned)
    {
        /// <summary>
        /// Nothing at all, which is what a detached model reports.
        /// </summary>
        public static AddressSpace Empty { get; } = new AddressSpace(Array.Empty<NodeEntry>(), Array.Empty<NodeEntry>());
    }

    /// <summary>
    /// The client model of the NodeManagement client: builds an address space over the
    /// four services of OPC 10000-4 5.8 and watches it change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server this client talks to serves four nodes of its own and nothing else.
    /// Everything else below the Devices folder was put there by a client: AddNodes creates
    /// a node, DeleteNodes removes one, and AddReferences and DeleteReferences only make an
    /// existing node reachable from somewhere else, which is what the Commissioned group
    /// shows.
    /// </para>
    /// <para>
    /// Every operation answers with an <see cref="OperationResult"/> rather than throwing,
    /// because the refusals are half of what there is to see: a browse name a sibling
    /// already uses, a node id which is taken, a parent the server does not open to its
    /// clients, and a node whose node manager never opted in at all.
    /// </para>
    /// <para>
    /// The model also subscribes to GeneralModelChangeEvents and raises
    /// <see cref="ModelChanged"/> for each, so a second copy of the client sees the address
    /// space change under it while it is looking at it. That is the situation this service
    /// set creates and the reason Part 5 9.32 exists.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The handles below are taken, cleared and released by OnDetachingAsync, which the detach of the base class runs - on a detach as well as on a dispose. The analyzer does not follow an asynchronous release through a virtual hook.")]
    public sealed class NodeManagementClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the model of the server, for a caller which cannot name the
        /// generated constant (it exists in the server assembly as well).
        /// </summary>
        public const string NodeManagementNamespaceUri = Namespaces.NodeManagement;

        /// <summary>
        /// The value a new variable starts with, so that there is something to read.
        /// </summary>
        private const double kInitialValue = 0.0;

        private StreamingSubscription m_streaming;
        private ModelChangeTracker m_modelChanges;
        private readonly EventHandler<ModelChangedEventArgs> m_onModelChanged;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public NodeManagementClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_onModelChanged = OnModelChanged;
        }

        /// <summary>
        /// The index the server gave the namespace of the model.
        /// </summary>
        /// <remarks>
        /// Every browse name this client sends has to carry it. A QualifiedName built from a
        /// bare string is in namespace zero, and AddNodes routes an item whose NodeId the
        /// server assigns by the namespace of its <b>browse name</b> - so a bare string is
        /// not a cosmetic mistake here, it sends the request to the wrong node manager.
        /// </remarks>
        public ushort NamespaceIndex { get; private set; }

        /// <summary>
        /// The Devices folder, the one parent the server opens to its clients.
        /// </summary>
        public NodeId DevicesId { get; private set; } = NodeId.Null;

        /// <summary>
        /// The Commissioned group, which references nodes rather than owning them.
        /// </summary>
        public NodeId CommissionedId { get; private set; } = NodeId.Null;

        /// <summary>
        /// Whether the server serves the model of this sample at all.
        /// </summary>
        /// <remarks>
        /// False when the namespace is missing: there is then no point in offering the
        /// operations, because every browse name they send would go out in namespace zero
        /// and be routed to the standard address space.
        /// </remarks>
        public bool IsModelAvailable { get; private set; }

        /// <summary>
        /// Raised whenever the server reports that its address space changed.
        /// </summary>
        /// <remarks>
        /// Any GeneralModelChangeEvent means "browse again". The event says which nodes
        /// changed, which a client with a large address space would use to refresh only
        /// the part which did.
        /// </remarks>
        public event EventHandler<EventArgs> ModelChanged;

        /// <summary>
        /// Reads what the two folders hold.
        /// </summary>
        /// <remarks>
        /// Built from a browse rather than from anything the client remembers: what a client
        /// added is not what the address space holds, because another client is free to add
        /// and delete at the same time.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<AddressSpace> ReadAddressSpaceAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var devices = new List<NodeEntry>();
            var commissioned = new List<NodeEntry>();

            if (!DevicesId.IsNull)
            {
                await AppendAsync(session, devices, DevicesId, 0, true, new HashSet<NodeId> { DevicesId }, ct)
                    .ConfigureAwait(false);
            }

            if (!CommissionedId.IsNull)
            {
                await AppendAsync(session, commissioned, CommissionedId, 0, false, new HashSet<NodeId> { CommissionedId }, ct)
                    .ConfigureAwait(false);
            }

            return new AddressSpace(devices, commissioned);
        }

        /// <summary>
        /// Adds a node of the given class below a parent.
        /// </summary>
        /// <remarks>
        /// <para>
        /// RequestedNewNodeId is left empty on purpose, so that the node manager of the
        /// server assigns the identifier through INodeIdFactory.New - which this sample's
        /// server overrides to build a readable string identifier from the browse name. A
        /// client which wants a particular identifier sets the field instead, and gets
        /// BadNodeIdExists when it is taken.
        /// </para>
        /// <para>
        /// A variable is sent with VariableAttributes, because a variable added without them
        /// is a null BaseDataType with no value: the attributes are how a client says what
        /// the node it is creating actually is.
        /// </para>
        /// </remarks>
        /// <param name="nodeClass">Object or Variable.</param>
        /// <param name="name">The browse name, in the namespace of the model.</param>
        /// <param name="parentId">The parent; the selected object, or the Devices folder.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> AddNodeAsync(
            NodeClass nodeClass,
            string name,
            NodeId parentId,
            CancellationToken ct = default)
        {
            ISession session = RequireSession();

            name = name?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                return new OperationResult("Adding a node", StatusCodes.BadBrowseNameInvalid);
            }

            if (parentId.IsNull)
            {
                return new OperationResult("Adding a node", StatusCodes.BadParentNodeIdInvalid);
            }

            var item = new AddNodesItem {
                ParentNodeId = parentId,
                NodeClass = nodeClass,

                // the browse name carries the namespace of the model, which is both what
                // makes the node part of that model and what routes the request to the
                // node manager which owns it
                BrowseName = new QualifiedName(name, NamespaceIndex),

                ReferenceTypeId = nodeClass == NodeClass.Object
                    ? ReferenceTypeIds.Organizes
                    : ReferenceTypeIds.HasComponent,

                TypeDefinition = nodeClass == NodeClass.Object
                    ? ObjectTypeIds.BaseObjectType
                    : VariableTypeIds.BaseDataVariableType,

                NodeAttributes = nodeClass == NodeClass.Variable
                    ? new ExtensionObject(NewVariableAttributes(name))
                    : new ExtensionObject(NewObjectAttributes(name)),
            };

            AddNodesResponse response = await session
                .AddNodesAsync(null, new List<AddNodesItem> { item }, ct)
                .ConfigureAwait(false);

            AddNodesResult result = response.Results.ToArray()[0];

            return new OperationResult(
                StatusCode.IsGood(result.StatusCode)
                    ? $"Adding {nodeClass} '{name}' as {result.AddedNodeId}"
                    : $"Adding {nodeClass} '{name}'",
                result.StatusCode);
        }

        /// <summary>
        /// Deletes a node, and the references which point at it.
        /// </summary>
        /// <param name="entry">The node to delete.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> DeleteNodeAsync(NodeEntry entry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            StatusCode status = await DeleteNodeAsync(RequireSession(), entry.NodeId, ct).ConfigureAwait(false);

            return new OperationResult($"Deleting {entry.Name}", status);
        }

        /// <summary>
        /// Makes a node reachable from the Commissioned group as well.
        /// </summary>
        /// <remarks>
        /// The node is not copied and not moved: one Organizes reference is added to the
        /// group, and afterwards the very same node is browsable under both folders.
        /// </remarks>
        /// <param name="entry">The node to reference.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> AddReferenceAsync(NodeEntry entry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            ISession session = RequireSession();

            if (CommissionedId.IsNull)
            {
                return new OperationResult("Adding a reference", StatusCodes.BadNothingToDo);
            }

            var item = new AddReferencesItem {
                SourceNodeId = CommissionedId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,

                // an empty server uri and a node id without a server index or namespace
                // uri is what tells the server the target is one of its own nodes
                TargetServerUri = string.Empty,
                TargetNodeId = entry.NodeId,
                TargetNodeClass = entry.NodeClass,
            };

            AddReferencesResponse response = await session
                .AddReferencesAsync(null, new List<AddReferencesItem> { item }, ct)
                .ConfigureAwait(false);

            return new OperationResult($"Referencing {entry.Name} from the group", response.Results.ToArray()[0]);
        }

        /// <summary>
        /// Drops the reference which puts a node in the Commissioned group.
        /// </summary>
        /// <remarks>
        /// The node itself survives, which is the whole difference between DeleteReferences
        /// and DeleteNodes: it disappears from the group and stays below Devices.
        /// </remarks>
        /// <param name="entry">The node to drop from the group.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> DeleteReferenceAsync(NodeEntry entry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            ISession session = RequireSession();

            if (CommissionedId.IsNull)
            {
                return new OperationResult("Deleting a reference", StatusCodes.BadNothingToDo);
            }

            var item = new DeleteReferencesItem {
                SourceNodeId = CommissionedId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = entry.NodeId,

                // the inverse edge was never added, because the source and the target are
                // owned by the same node manager and the server mirrors an edge only
                // across node managers
                DeleteBidirectional = false,
            };

            DeleteReferencesResponse response = await session
                .DeleteReferencesAsync(null, new List<DeleteReferencesItem> { item }, ct)
                .ConfigureAwait(false);

            return new OperationResult($"Dropping the reference to {entry.Name}", response.Results.ToArray()[0]);
        }

        /// <summary>
        /// Tries the same delete on a node of the standard address space.
        /// </summary>
        /// <remarks>
        /// The opt-in is per node manager rather than per server, so the answer depends on
        /// who owns the node. ServerCapabilities belongs to the core node manager, which has
        /// not opted in, and the answer is BadUserAccessDenied - the status the service set
        /// defines for "this server does not allow this operation here".
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> DeleteStandardNodeAsync(CancellationToken ct = default)
        {
            StatusCode status = await DeleteNodeAsync(RequireSession(), ObjectIds.Server_ServerCapabilities, ct)
                .ConfigureAwait(false);

            return new OperationResult($"Deleting the standard node {BrowseNames.ServerCapabilities}", status);
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // this client has built-in knowledge of the information model of the server
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.NodeManagement);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                $"1:{ModelNames.Plant}/1:{ModelNames.Devices}",
                $"1:{ModelNames.Plant}/1:{ModelNames.Commissioned}").ConfigureAwait(false);

            DevicesId = nodes.Count > 0 ? nodes[0] : NodeId.Null;
            CommissionedId = nodes.Count > 1 ? nodes[1] : NodeId.Null;

            int index = session.NamespaceUris.GetIndex(Namespaces.NodeManagement);

            if (index < 0)
            {
                IsModelAvailable = false;
                return;
            }

            NamespaceIndex = (ushort)index;
            IsModelAvailable = true;

            await CreateSubscriptionAsync(session).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            await DeleteSubscriptionAsync().ConfigureAwait(false);

            DevicesId = NodeId.Null;
            CommissionedId = NodeId.Null;
            NamespaceIndex = 0;
            IsModelAvailable = false;
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so the reconnect hooks of the base
        // class are not overridden. What the address space holds by then is for the caller
        // to read again.

        /// <summary>
        /// The attributes of a new object.
        /// </summary>
        private static ObjectAttributes NewObjectAttributes(string name)
        {
            return new ObjectAttributes {
                SpecifiedAttributes = (uint)(
                    NodeAttributesMask.DisplayName |
                    NodeAttributesMask.Description),

                DisplayName = new LocalizedText(name),
                Description = new LocalizedText("Added at runtime by the NodeManagement Quickstart client."),
            };
        }

        /// <summary>
        /// The attributes of a new variable.
        /// </summary>
        /// <remarks>
        /// Only the attributes named in SpecifiedAttributes are applied, so a mask which
        /// forgets a field silently leaves the default in place.
        /// </remarks>
        private static VariableAttributes NewVariableAttributes(string name)
        {
            return new VariableAttributes {
                SpecifiedAttributes = (uint)(
                    NodeAttributesMask.DisplayName |
                    NodeAttributesMask.Description |
                    NodeAttributesMask.DataType |
                    NodeAttributesMask.ValueRank |
                    NodeAttributesMask.AccessLevel |
                    NodeAttributesMask.UserAccessLevel |
                    NodeAttributesMask.Value),

                DisplayName = new LocalizedText(name),
                Description = new LocalizedText("Added at runtime by the NodeManagement Quickstart client."),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From(kInitialValue),
            };
        }

        /// <summary>
        /// Deletes one node, and the references which point at it.
        /// </summary>
        /// <remarks>
        /// DeleteTargetReferences is what removes the reference the parent holds. Without it
        /// the node is gone and the parent still points at it, so a browse reports a child
        /// which cannot be read.
        /// </remarks>
        private static async Task<StatusCode> DeleteNodeAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            var item = new DeleteNodesItem {
                NodeId = nodeId,
                DeleteTargetReferences = true,
            };

            DeleteNodesResponse response = await session
                .DeleteNodesAsync(null, new List<DeleteNodesItem> { item }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Appends the children of a node to a list, with their depth.
        /// </summary>
        /// <remarks>
        /// The visited set is not paranoia. In a server whose references are added by its
        /// clients, a hierarchy is only a tree for as long as every client keeps it one, and
        /// a client which walks one has to survive the round trip somebody else creates.
        /// </remarks>
        private static async Task AppendAsync(
            ISession session,
            List<NodeEntry> entries,
            NodeId parentId,
            int depth,
            bool recursive,
            HashSet<NodeId> visited,
            CancellationToken ct)
        {
            var nodeToBrowse = new BrowseDescription {
                NodeId = parentId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
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

                entries.Add(new NodeEntry(
                    reference.BrowseName.Name,
                    nodeId,
                    reference.NodeClass,
                    depth,
                    await ValueOfAsync(session, nodeId, reference.NodeClass, ct).ConfigureAwait(false)));

                if (recursive && reference.NodeClass == NodeClass.Object && visited.Add(nodeId))
                {
                    await AppendAsync(session, entries, nodeId, depth + 1, recursive, visited, ct).ConfigureAwait(false);
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

            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, ct)
                .ConfigureAwait(false);

            DataValue value = response.Results.ToArray()[0];

            return StatusCode.IsGood(value.StatusCode)
                ? value.WrappedValue.ToString()
                : value.StatusCode.ToString();
        }

        /// <summary>
        /// Starts tracking the model changes of the server.
        /// </summary>
        /// <remarks>
        /// A client of a server whose address space is built by its clients cannot assume
        /// that what it read is still true. Part 5 9.32 answers that with the model change
        /// events, and the stack answers it with <see cref="ModelChangeTracker"/>: it owns
        /// the event filter, the subscription on the Server object and the decoding of the
        /// Changes field, reports the changes as a structured payload, refreshes the
        /// namespace table when one of them needs it, and - the part a hand written pump
        /// tends to forget - evicts the changed nodes from the <c>INodeCache</c>, so that
        /// the next read does not answer out of a stale cache.
        /// </remarks>
        private async Task CreateSubscriptionAsync(ISession session)
        {
            await DeleteSubscriptionAsync().ConfigureAwait(false);

            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, SampleSession.DefaultSubscriptionOptions);

            // the node cache and the namespace table of the session are handed to the
            // tracker so that it can keep both consistent with what the server reports.
            m_modelChanges = new ModelChangeTracker(
                m_streaming,
                session.NodeCache,
                Logger,
                session as INamespaceTableRefresher);

            m_modelChanges.ModelChanged += m_onModelChanged;

            await m_modelChanges.StartTrackingAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Raises <see cref="ModelChanged"/> when the tracker reports a change.
        /// </summary>
        /// <remarks>
        /// This model reports "something changed" and lets the window read everything
        /// again, because everything it shows fits on one form. A client with a large
        /// address space would hand on <see cref="ModelChangedEventArgs.Changes"/> and
        /// refresh only what they name, and only rebuild the whole of it when
        /// <see cref="ModelChangedEventArgs.RequiresFullCacheInvalidation"/> says the server
        /// could not say what changed. The tracker raises this on a publish worker; the
        /// base class posts it to the thread the model was created on.
        /// </remarks>
        private void OnModelChanged(object sender, ModelChangedEventArgs e)
        {
            Raise(ModelChanged, EventArgs.Empty);
        }

        /// <summary>
        /// Stops tracking and deletes the subscription on the server.
        /// </summary>
        /// <remarks>
        /// The tracker ends first, so that nothing is reported after the detach returns.
        /// Done before the session is closed: closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async Task DeleteSubscriptionAsync()
        {
            ModelChangeTracker modelChanges = m_modelChanges;
            StreamingSubscription streaming = m_streaming;

            m_modelChanges = null;
            m_streaming = null;

            if (modelChanges != null)
            {
                modelChanges.ModelChanged -= m_onModelChanged;

                await modelChanges.DisposeAsync().ConfigureAwait(false);
            }

            if (streaming != null)
            {
                await streaming.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
