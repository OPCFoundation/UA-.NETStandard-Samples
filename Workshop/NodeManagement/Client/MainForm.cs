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
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Quickstarts.NodeManagement.Client
{
    // The source generator emits a Quickstarts.NodeManagement.BrowseNames and ObjectIds for
    // the model of the server. This namespace is a child of that one, so those would win over
    // the standard sets of the same name: both are named apart here.
    using ModelNames = Quickstarts.NodeManagement.BrowseNames;
    using BrowseNames = Opc.Ua.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// The main form of the OPC UA NodeManagement Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server this client talks to serves four nodes of its own and nothing else.
    /// Everything else in the upper list was put there by a client, over the four services of
    /// OPC 10000-4 5.8: AddNodes creates a node, DeleteNodes removes one, and AddReferences
    /// and DeleteReferences only make an existing node reachable from somewhere else, which
    /// is what the lower list shows.
    /// </para>
    /// <para>
    /// Every button reports the status code the server answered into the status bar rather
    /// than into a dialog, because the refusals are half of what there is to see: a browse
    /// name a sibling already uses, a node id which is taken, a parent the server does not
    /// open to its clients, and a node whose node manager never opted in at all.
    /// </para>
    /// <para>
    /// The client also subscribes to GeneralModelChangeEvents, so a second copy of it sees
    /// the address space change under it while it is looking at it. That is the situation
    /// this service set creates and the reason Part 5 9.32 exists.
    /// </para>
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the application.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62575/Quickstarts/NodeManagementServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The value a new variable starts with, so that there is something to read.
        /// </summary>
        private const double kInitialValue = 0.0;

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private ISession m_session;

        /// <summary>
        /// The index the server gave the namespace of the model.
        /// </summary>
        /// <remarks>
        /// Every browse name this client sends has to carry it. A QualifiedName built from a
        /// bare string is in namespace zero, and AddNodes routes an item whose NodeId the
        /// server assigns by the namespace of its <b>browse name</b> - so a bare string is
        /// not a cosmetic mistake here, it sends the request to the wrong node manager.
        /// </remarks>
        private ushort m_namespaceIndex;

        private NodeId m_devicesId;
        private NodeId m_commissionedId;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed asynchronously by DeleteSubscriptionAsync.")]
        private StreamingSubscription m_streaming;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed by DeleteSubscriptionAsync.")]
        private CancellationTokenSource m_cts;
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // the subscription goes first: closing a session which still carries one
                // waits for the publish pipeline to drain. Awaited rather than the
                // synchronous Disconnect, which blocks the UI thread on work that needs the
                // same message loop.
                await DeleteSubscriptionAsync().ConfigureAwait(true);

                await ConnectServerCTRL.DisconnectAsync(default).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    await DeleteSubscriptionAsync().ConfigureAwait(true);

                    m_devicesId = NodeId.Null;
                    m_commissionedId = NodeId.Null;
                    NodesLV.Items.Clear();
                    GroupLV.Items.Clear();
                    SetButtonsEnabled(false);
                    return;
                }

                // this client has built-in knowledge of the information model of the server
                var wellKnownNamespaceUris = new NamespaceTable();
                wellKnownNamespaceUris.Append(Namespaces.NodeManagement);

                List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                    m_session,
                    ObjectIds.ObjectsFolder,
                    wellKnownNamespaceUris,
                    default,
                    $"1:{ModelNames.Plant}/1:{ModelNames.Devices}",
                    $"1:{ModelNames.Plant}/1:{ModelNames.Commissioned}");

                m_devicesId = nodes.Count > 0 ? nodes[0] : NodeId.Null;
                m_commissionedId = nodes.Count > 1 ? nodes[1] : NodeId.Null;

                int index = m_session.NamespaceUris.GetIndex(Namespaces.NodeManagement);

                if (index < 0)
                {
                    // no point in offering the buttons: every browse name they send would go
                    // out in namespace zero and be routed to the standard address space
                    Report($"Looking for the namespace {Namespaces.NodeManagement}", StatusCodes.BadNodeIdUnknown);
                    return;
                }

                m_namespaceIndex = (ushort)index;

                SetButtonsEnabled(true);

                await CreateSubscriptionAsync().ConfigureAwait(true);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                SetButtonsEnabled(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                SetButtonsEnabled(m_session != null);

                if (m_session != null)
                {
                    await RefreshAsync().ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // the subscription goes first: closing a session which still carries one waits
            // for the publish pipeline to drain
            ClientUtils.WaitForTeardown(DeleteSubscriptionAsync);

            ConnectServerCTRL.Disconnect();
        }

        /// <summary>
        /// Reads the address space again.
        /// </summary>
        private async void RefreshBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Creates an object below the selected node.
        /// </summary>
        private async void AddObjectBTN_ClickAsync(object sender, EventArgs e)
        {
            await AddAsync(NodeClass.Object).ConfigureAwait(true);
        }

        /// <summary>
        /// Creates a variable below the selected node.
        /// </summary>
        private async void AddVariableBTN_ClickAsync(object sender, EventArgs e)
        {
            await AddAsync(NodeClass.Variable).ConfigureAwait(true);
        }

        /// <summary>
        /// Deletes the selected node.
        /// </summary>
        private async void DeleteBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeRow selected = Selected(NodesLV);

                if (m_session == null || selected == null)
                {
                    Report("Deleting a node", StatusCodes.BadNothingToDo);
                    return;
                }

                StatusCode status = await DeleteNodeAsync(selected.NodeId).ConfigureAwait(true);

                Report($"Deleting {selected.Name}", status);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Makes the selected node reachable from the commissioned group as well.
        /// </summary>
        /// <remarks>
        /// The node is not copied and not moved: one Organizes reference is added to the
        /// group, and afterwards the very same node is browsable under both folders.
        /// </remarks>
        private async void AddReferenceBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeRow selected = Selected(NodesLV);

                if (m_session == null || selected == null || m_commissionedId.IsNull)
                {
                    Report("Adding a reference", StatusCodes.BadNothingToDo);
                    return;
                }

                var item = new AddReferencesItem {
                    SourceNodeId = m_commissionedId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsForward = true,

                    // an empty server uri and a node id without a server index or namespace
                    // uri is what tells the server the target is one of its own nodes
                    TargetServerUri = string.Empty,
                    TargetNodeId = selected.NodeId,
                    TargetNodeClass = selected.NodeClass,
                };

                AddReferencesResponse response = await m_session
                    .AddReferencesAsync(null, new List<AddReferencesItem> { item }, default)
                    .ConfigureAwait(true);

                Report($"Referencing {selected.Name} from the group", response.Results.ToArray()[0]);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Drops the reference which puts the selected node in the group.
        /// </summary>
        /// <remarks>
        /// The node itself survives, which is the whole difference between DeleteReferences
        /// and DeleteNodes: it disappears from the lower list and stays in the upper one.
        /// </remarks>
        private async void DeleteReferenceBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeRow selected = Selected(GroupLV);

                if (m_session == null || selected == null || m_commissionedId.IsNull)
                {
                    Report("Deleting a reference", StatusCodes.BadNothingToDo);
                    return;
                }

                var item = new DeleteReferencesItem {
                    SourceNodeId = m_commissionedId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsForward = true,
                    TargetNodeId = selected.NodeId,

                    // the inverse edge was never added, because the source and the target are
                    // owned by the same node manager and the server mirrors an edge only
                    // across node managers
                    DeleteBidirectional = false,
                };

                DeleteReferencesResponse response = await m_session
                    .DeleteReferencesAsync(null, new List<DeleteReferencesItem> { item }, default)
                    .ConfigureAwait(true);

                Report($"Dropping the reference to {selected.Name}", response.Results.ToArray()[0]);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
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
        private async void RefusedBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                StatusCode status = await DeleteNodeAsync(ObjectIds.Server_ServerCapabilities)
                    .ConfigureAwait(true);

                Report($"Deleting the standard node {BrowseNames.ServerCapabilities}", status);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds a node of the given class below the selected node.
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
        private async Task AddAsync(NodeClass nodeClass)
        {
            try
            {
                string name = NewNameTB.Text?.Trim();

                if (m_session == null || string.IsNullOrEmpty(name))
                {
                    Report("Adding a node", StatusCodes.BadBrowseNameInvalid);
                    return;
                }

                NodeId parentId = ParentForNewNodes();

                if (parentId.IsNull)
                {
                    Report("Adding a node", StatusCodes.BadParentNodeIdInvalid);
                    return;
                }

                var item = new AddNodesItem {
                    ParentNodeId = parentId,
                    NodeClass = nodeClass,

                    // the browse name carries the namespace of the model, which is both what
                    // makes the node part of that model and what routes the request to the
                    // node manager which owns it
                    BrowseName = new QualifiedName(name, m_namespaceIndex),

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

                AddNodesResponse response = await m_session
                    .AddNodesAsync(null, new List<AddNodesItem> { item }, default)
                    .ConfigureAwait(true);

                AddNodesResult result = response.Results.ToArray()[0];

                Report(
                    StatusCode.IsGood(result.StatusCode)
                        ? $"Adding {nodeClass} '{name}' as {result.AddedNodeId}"
                        : $"Adding {nodeClass} '{name}'",
                    result.StatusCode);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

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
        /// The node a new node is attached to.
        /// </summary>
        /// <remarks>
        /// The selected object, so that a client can give a device it just created a variable
        /// of its own, and the Devices folder when nothing useful is selected. The server
        /// refuses any other parent, which the status bar then says.
        /// </remarks>
        private NodeId ParentForNewNodes()
        {
            NodeRow selected = Selected(NodesLV);

            return selected != null && selected.NodeClass == NodeClass.Object
                ? selected.NodeId
                : m_devicesId;
        }

        /// <summary>
        /// Deletes one node, and the references which point at it.
        /// </summary>
        /// <remarks>
        /// DeleteTargetReferences is what removes the reference the parent holds. Without it
        /// the node is gone and the parent still points at it, so a browse reports a child
        /// which cannot be read.
        /// </remarks>
        private async Task<StatusCode> DeleteNodeAsync(NodeId nodeId)
        {
            var item = new DeleteNodesItem {
                NodeId = nodeId,
                DeleteTargetReferences = true,
            };

            DeleteNodesResponse response = await m_session
                .DeleteNodesAsync(null, new List<DeleteNodesItem> { item }, default)
                .ConfigureAwait(true);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Reads both lists again.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (m_session == null)
            {
                return;
            }

            await FillAsync(NodesLV, m_devicesId, recursive: true).ConfigureAwait(true);
            await FillAsync(GroupLV, m_commissionedId, recursive: false).ConfigureAwait(true);
        }

        /// <summary>
        /// Fills a list with the children of a node.
        /// </summary>
        /// <remarks>
        /// Built from a browse rather than from anything the client remembers: what a client
        /// added is not what the address space holds, because another client is free to add
        /// and delete at the same time.
        /// </remarks>
        private async Task FillAsync(ListView list, NodeId rootId, bool recursive)
        {
            NodeId selected = Selected(list)?.NodeId ?? NodeId.Null;

            list.BeginUpdate();

            try
            {
                list.Items.Clear();

                if (rootId.IsNull)
                {
                    return;
                }

                await AppendAsync(list, rootId, 0, recursive, new HashSet<NodeId> { rootId })
                    .ConfigureAwait(true);

                // keep the selection across the refresh, so that adding a variable to a
                // device does not lose the device
                foreach (ListViewItem item in list.Items)
                {
                    if (((NodeRow)item.Tag).NodeId == selected)
                    {
                        item.Selected = true;
                        break;
                    }
                }
            }
            finally
            {
                list.EndUpdate();
            }
        }

        /// <summary>
        /// Appends the children of a node to a list, indented by their depth.
        /// </summary>
        /// <remarks>
        /// The visited set is not paranoia. In a server whose references are added by its
        /// clients, a hierarchy is only a tree for as long as every client keeps it one, and
        /// a client which walks one has to survive the round trip somebody else creates.
        /// </remarks>
        private async Task AppendAsync(
            ListView list,
            NodeId parentId,
            int depth,
            bool recursive,
            HashSet<NodeId> visited)
        {
            var browse = new BrowseDescription {
                NodeId = parentId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await m_session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, default)
                .ConfigureAwait(true);

            BrowseResult result = response.Results.ToArray()[0];

            if (StatusCode.IsBad(result.StatusCode))
            {
                return;
            }

            foreach (ReferenceDescription reference in result.References.ToArray())
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);

                var row = new NodeRow {
                    Name = reference.BrowseName.Name,
                    NodeId = nodeId,
                    NodeClass = reference.NodeClass,
                };

                var item = new ListViewItem(new string(' ', depth * 4) + row.Name) { Tag = row };

                item.SubItems.Add(row.NodeClass.ToString());
                item.SubItems.Add(nodeId.ToString());
                item.SubItems.Add(await ValueOfAsync(row).ConfigureAwait(true));

                list.Items.Add(item);

                if (recursive && row.NodeClass == NodeClass.Object && visited.Add(nodeId))
                {
                    await AppendAsync(list, nodeId, depth + 1, recursive, visited).ConfigureAwait(true);
                }
            }
        }

        /// <summary>
        /// The value of a variable, or why it could not be read.
        /// </summary>
        private async Task<string> ValueOfAsync(NodeRow row)
        {
            if (row.NodeClass != NodeClass.Variable)
            {
                return string.Empty;
            }

            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = row.NodeId, AttributeId = Attributes.Value },
            };

            ReadResponse response = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, default)
                .ConfigureAwait(true);

            DataValue value = response.Results.ToArray()[0];

            return StatusCode.IsGood(value.StatusCode)
                ? value.WrappedValue.ToString()
                : value.StatusCode.ToString();
        }

        /// <summary>
        /// Subscribes to the model change events of the server.
        /// </summary>
        /// <remarks>
        /// A client of a server whose address space is built by its clients cannot assume
        /// that what it read is still true. Part 5 9.32 answers that with the model change
        /// events, and this client uses them the simple way: any GeneralModelChangeEvent
        /// means "browse again". The event says which nodes changed, which a client with a
        /// large address space would use to refresh only the part which did.
        /// </remarks>
        private async Task CreateSubscriptionAsync()
        {
            await DeleteSubscriptionAsync().ConfigureAwait(true);

            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, ClientUtils.DefaultSubscriptionOptions);
            m_cts = new CancellationTokenSource();

            // nothing is awaited here on purpose: the enumeration runs for as long as the
            // client is connected
            _ = PumpModelChangesAsync(m_cts.Token);
        }

        /// <summary>
        /// Refreshes the lists whenever the server reports that its model changed.
        /// </summary>
        private async Task PumpModelChangesAsync(CancellationToken ct)
        {
            IStreamingSubscription streaming = m_streaming;

            var options = new MonitoredItemOptions {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(ObjectIds.Server, ModelChangeFilter(), options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    // without a window there is nothing to update, and the enumeration keeps
                    // running rather than ending for good
                    if (!IsHandleCreated)
                    {
                        continue;
                    }

                    // the enumeration runs on a publish worker, so the display is updated on
                    // the UI thread
                    BeginInvoke(new Action(ModelChangedAsync));
                }
            }
            catch (OperationCanceledException)
            {
                // the client disconnected.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to read the model change events.");
            }
        }

        /// <summary>
        /// A filter which accepts nothing but GeneralModelChangeEvents.
        /// </summary>
        /// <remarks>
        /// One select clause is enough for a client which only wants to know that something
        /// changed. The Changes field of the event would name the nodes, and is worth reading
        /// for an address space too large to browse again.
        /// </remarks>
        private static EventFilter ModelChangeFilter()
        {
            var filter = new EventFilter {
                SelectClauses = new[] {
                    new SimpleAttributeOperand {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        AttributeId = Attributes.Value,
                        BrowsePath = new[] { new QualifiedName(BrowseNames.EventType) }.ToArrayOf(),
                    },
                }.ToArrayOf(),
            };

            filter.WhereClause = new ContentFilter();
            filter.WhereClause.Push(
                FilterOperator.OfType,
                Variant.From(ObjectTypeIds.GeneralModelChangeEventType));

            return filter;
        }

        /// <summary>
        /// Reads the address space again after the server reported a change.
        /// </summary>
        private async void ModelChangedAsync()
        {
            try
            {
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to refresh after a model change.");
            }
        }

        /// <summary>
        /// Stops the stream and deletes the subscription on the server.
        /// </summary>
        private async Task DeleteSubscriptionAsync()
        {
            StreamingSubscription streaming = m_streaming;
            CancellationTokenSource cts = m_cts;

            m_streaming = null;
            m_cts = null;

            if (cts != null)
            {
                await cts.CancelAsync().ConfigureAwait(true);
                cts.Dispose();
            }

            if (streaming == null)
            {
                return;
            }

            try
            {
                await streaming.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                // this also runs when the session has already gone away, and then the
                // subscription cannot be deleted on the server any more
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to delete the subscription.");
            }
        }

        /// <summary>
        /// The row selected in a list, or null.
        /// </summary>
        private static NodeRow Selected(ListView list)
        {
            return list.SelectedItems.Count > 0 ? (NodeRow)list.SelectedItems[0].Tag : null;
        }

        /// <summary>
        /// Reports what the server answered to an operation the user asked for.
        /// </summary>
        /// <remarks>
        /// The status bar rather than a message box: most of what this sample has to show is
        /// which requests are refused and with which status code, and a modal dialog between
        /// every click makes trying them tedious. It also keeps the buttons drivable from a
        /// test, which a modal dialog does not.
        /// </remarks>
        private void Report(string what, StatusCode status)
        {
            ActionStatusLB.Text = $"{what} answered {status}";
            ActionStatusLB.ForeColor = StatusCode.IsGood(status) ? Color.Empty : Color.Red;
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            AddObjectBTN.Enabled = enabled;
            AddVariableBTN.Enabled = enabled;
            DeleteBTN.Enabled = enabled;
            AddReferenceBTN.Enabled = enabled;
            DeleteReferenceBTN.Enabled = enabled;
            RefusedBTN.Enabled = enabled;
        }
        #endregion

        #region Private Types
        /// <summary>
        /// One row of either list.
        /// </summary>
        private sealed class NodeRow
        {
            public string Name { get; init; }
            public NodeId NodeId { get; init; }
            public NodeClass NodeClass { get; init; }
        }
        #endregion
    }
}
