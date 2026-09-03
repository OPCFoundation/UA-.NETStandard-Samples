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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AggregationModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace AggregationServer
{
    // The V2 client subscription types collide by name with the server side ones this
    // node manager implements - RemoteSubscription and IMonitoredItem exist in both - so the
    // downstream ones are aliased rather than imported wholesale. The aliases have to sit
    // inside the namespace body: at the top of the file the enclosing namespace wins and
    // they silently resolve to the server types.
    using DataValueChange = Opc.Ua.Client.Subscriptions.DataValueChange;
    using EventNotification = Opc.Ua.Client.Subscriptions.EventNotification;
    using IMonitoredItemApplyState = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItemApplyState;
    using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;
    using ISubscriptionNotificationHandler = Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using PublishState = Opc.Ua.Client.Subscriptions.PublishState;
    using RemoteMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using RemoteSubscription = Opc.Ua.Client.Subscriptions.ISubscription;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// One monitored item of this server, and the item forwarded for it to the
    /// aggregated server.
    /// </summary>
    /// <remarks>
    /// The V2 engine assigns the client handle itself and offers no <c>Handle</c> to
    /// hang the local item off, so the pairing is kept here instead. The options
    /// monitor has to be kept too: reconfiguring it <em>is</em> the modify request,
    /// there is no ApplyChanges to call.
    /// </remarks>
    internal sealed class ForwardedMonitoredItem
    {
        public ForwardedMonitoredItem(
            Opc.Ua.Server.MonitoredItem local,
            RemoteMonitoredItem remote,
            OptionsMonitor<MonitoredItemOptions> options)
        {
            Local = local;
            Remote = remote;
            Options = options;
        }

        public Opc.Ua.Server.MonitoredItem Local { get; }
        public RemoteMonitoredItem Remote { get; }
        public OptionsMonitor<MonitoredItemOptions> Options { get; }
    }

    /// <summary>
    /// The information about a client session.
    /// </summary>
    public class AggregationClientSession
    {
        public AggregationClientSession(NodeId sessionId, bool isMetaDataSession)
        {
            ClientSessionId = sessionId;
            IsMetaDataSession = isMetaDataSession;
            SessionSessionId = NodeId.Null;
            LastUsed = DateTime.MinValue;
        }

        public NodeId ClientSessionId { get; }
        public NodeId SessionSessionId { get; private set; }
        public bool IsMetaDataSession { get; }
        public Opc.Ua.Client.ISession Session
        {
            get => m_session;
            set
            {
                m_session = value;
                SessionSessionId = value != null ? value.SessionId : NodeId.Null;
            }
        }
        public DateTime LastUsed { get; set; }

        /// <summary>
        /// The subscription this session forwards to the aggregated server, and the
        /// options monitor which configures it.
        /// </summary>
        internal RemoteSubscription Subscription { get; set; }
        internal OptionsMonitor<SubscriptionOptions> SubscriptionOptions { get; set; }

        /// <summary>
        /// The forwarded items, by the id of the local monitored item and by the client
        /// handle the engine assigned to the remote one.
        /// </summary>
        /// <remarks>
        /// Notifications arrive on a publish worker while the subscription services run
        /// under <see cref="SubscriptionLock"/>, so these are concurrent rather than
        /// covered by that lock.
        /// </remarks>
        internal ConcurrentDictionary<uint, ForwardedMonitoredItem> ItemsByLocalId { get; }
            = new ConcurrentDictionary<uint, ForwardedMonitoredItem>();
        internal ConcurrentDictionary<uint, ForwardedMonitoredItem> ItemsByClientHandle { get; }
            = new ConcurrentDictionary<uint, ForwardedMonitoredItem>();

        /// <summary>
        /// Serializes changes to the subscription forwarded to the aggregated
        /// server. The old node manager used <c>lock (session)</c> for this,
        /// which cannot span the awaits of the async subscription services.
        /// </summary>
        public SemaphoreSlim SubscriptionLock { get; } = new SemaphoreSlim(1, 1);

        private Opc.Ua.Client.ISession m_session;
    }

    /// <summary>
    /// A node manager which mirrors the address space of another, aggregated server.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>FluentNodeManagerBase</c>, loads the predefined nodes generated from
    /// <c>Model/ModelDesign.xml</c>, calls <see cref="Configure"/> once the
    /// address space is in place and then publishes the references Configure
    /// made to nodes of other node managers. The factory stays hand written
    /// (<c>GenerateFactory = false</c>) because the server creates one manager
    /// per aggregated endpoint, with arguments a generated factory cannot know.
    ///
    /// The fluent builder covers the part of the address space this manager
    /// owns outright: the root folder and the status object below it.
    /// Everything else is a proxy over an address space which is materialised
    /// lazily, one node per operation, from the aggregated server - and the
    /// builder wires behaviour to nodes which exist when Configure runs. The
    /// service overrides below therefore stay: browsing
    /// (<see cref="OnCreateBrowser"/>), handle resolution and validation, the
    /// batched Read, Write and Call forwarding, the monitored item hooks and
    /// the event subscription forwarding. The SDK has no fluent counterpart
    /// for a node handle resolver, for manager level batch fallbacks or for an
    /// asynchronous monitored item completion callback; the issues
    /// OPCFoundation/UA-.NETStandard#4397, #4398 and #4399 track those hooks.
    /// </remarks>
    [NodeManager(GenerateFactory = false)]
    public partial class AggregationNodeManager
    {
        const uint DefaultSessionTimeout = 60000;
        const int DefaultMetadataRefresh = 300000;
        const int DefaultMetadataInitDelay = 5000;

        /// <summary>
        /// How long the node manager waits for a downstream session to close before it
        /// stops waiting and disposes the session instead.
        /// </summary>
        static readonly TimeSpan kCloseTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long a subscription service waits for the V2 engine to apply the monitored
        /// item changes it made before it reports the results it has.
        /// </summary>
        static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(15);

        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        /// <remarks>
        /// The first namespace is the instance namespace all dynamically created
        /// nodes live in; the second is the namespace of the generated aggregation
        /// type model. The generated constructor only knows the model namespace,
        /// so this one chains to the base class itself.
        /// </remarks>
        public AggregationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Opc.Ua.Client.ReverseConnectManager reverseConnectManager,
            bool ownsTypeModel)
        :
            base(server, configuration, server.Telemetry.CreateLogger<AggregationNodeManager>(),
                Namespaces.Aggregation, AggregationModel.Namespaces.Aggregation)
        {
            SystemContext.NodeIdFactory = this;

            m_configuration = configuration;
            m_endpoint = endpoint;
            if (endpoint.ReverseConnect != null &&
                endpoint.ReverseConnect.Enabled)
            {
                // reverse connect manager endpoint is required
                if (reverseConnectManager == null) throw new ArgumentNullException(nameof(reverseConnectManager));
                m_reverseConnectManager = reverseConnectManager;
            }
            m_ownsTypeModel = ownsTypeModel;
            m_clients = new Dictionary<NodeId, AggregationClientSession>();
            m_clientsLock = new object();
            m_mapper = new NamespaceMapper();
            m_sessionTimeout = DefaultSessionTimeout;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_metadataUpdateCancellation?.Cancel();
                m_metadataUpdateCancellation?.Dispose();
                m_metadataUpdateCancellation = null;
                m_root = null;
                m_status = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            // generate a numeric node id if the node has a parent and no node id assigned.
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                return GenerateNodeId();
            }

            return node.NodeId;
        }
        #endregion

        #region Address Space Creation
        /// <summary>
        /// Loads the predefined nodes generated from the model design.
        /// </summary>
        /// <remarks>
        /// The server creates one node manager per aggregated endpoint but the
        /// aggregation type model must only be published once, by the first
        /// instance.
        /// </remarks>
        protected override async ValueTask LoadPredefinedNodesAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (m_ownsTypeModel)
            {
                await base.LoadPredefinedNodesAsync(context, externalReferences, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds the dynamic part of the address space and wires the behaviour
        /// of the manager once the predefined nodes exist.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            // the root folder which mirrors the aggregated server. it is named
            // after the remote server once the first connection succeeds.
            string rootName = "Root";

            if (m_endpoint.Description != null && m_endpoint.Description.Server != null && !m_endpoint.Description.Server.ApplicationName.IsNull)
            {
                rootName = m_endpoint.Description.Server.ApplicationName.Text;
            }

            FolderState root = m_root = new FolderState(null);
            root.NodeId = GenerateNodeId();
            root.BrowseName = new QualifiedName(rootName, NamespaceIndex);
            root.DisplayName = new LocalizedText(root.BrowseName.Name);
            root.TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType;
            root.EventNotifier = EventNotifiers.SubscribeToEvents;
            root.OnCreateBrowser = OnCreateBrowser;

            AddPredefinedNodeSynchronously(root);

            // place the root below the objects folder and make it a notifier of the
            // server object. both are nodes of other node managers, so only the inverse
            // references are added here: the generated partial publishes the forward
            // references to their owners once Configure returns, and registers the
            // root as a root notifier on the way.
            builder.Node(root.NodeId)
                .UnderObjectsFolder()
                .AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);

            // create the status object which reports the connection to the aggregated
            // server. the generated factory builds the properties the type declares and
            // assigns their node ids through New; the builder hangs the object below the
            // root and registers it. the property values are updated through the bound
            // updaters, which report the change to monitored items themselves.
            var statusName = new QualifiedName("Status", NamespaceIndex);

            m_status = builder.Node(root.NodeId)
                .CreateInstance(statusName, parent => SystemContext.CreateInstanceOfAggregatedServerStatusType(parent, statusName))
                .Configure(status => {
                    // the builder attaches the object as a component; a folder organizes.
                    status.Node.ReferenceTypeId = ReferenceTypeIds.Organizes;

                    status.Properties().EndpointUrl().Bind(out m_endpointUrl);
                    status.Properties().Status().Bind(out m_connectionStatus);
                    status.Properties().ConnectTime().Bind(out m_connectTime);
                })
                .Node;

            ReportConnectionState(StatusCodes.BadNotConnected, DateTimeUtc.MinValue);

            // periodically connect to the aggregated server and refresh its
            // metadata. the loop replaces the timer of the synchronous manager
            // and is cancelled when the manager is disposed.
            m_metadataUpdateCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = m_metadataUpdateCancellation.Token;
            _ = Task.Run(() => MetadataUpdateLoopAsync(cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Used to receive notifications when a node browser is created.
        /// </summary>
        /// <remarks>
        /// The browser connects to the aggregated server on first use, so an
        /// internal-only browse does not open a session at all.
        /// </remarks>
        public NodeBrowser OnCreateBrowser(
            ISystemContext context,
            NodeState node,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            ServerSystemContext systemContext = context as ServerSystemContext;

            Browser browser = new Browser(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly,
                async ct => (await GetClientSessionAsync(systemContext, ct).ConfigureAwait(false)).Session,
                m_mapper,
                Object.ReferenceEquals(node, m_root) ? null : node,
                m_root.NodeId);

            return browser;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // quickly exclude nodes that are not in the namespace.
            if (!IsNodeIdInNamespace(nodeId))
            {
                return new ValueTask<NodeHandle>((NodeHandle)null);
            }

            NodeState node = null;

            // check cache (the cache is used because the same node id can appear many times in a single request).
            if (cache != null)
            {
                if (cache.TryGetValue(nodeId, out node))
                {
                    return new ValueTask<NodeHandle>(new NodeHandle(nodeId, node));
                }
            }

            // look up predefined node.
            if (PredefinedNodes.TryGetValue(nodeId, out node))
            {
                NodeHandle handle = new NodeHandle(nodeId, node);

                if (cache != null)
                {
                    cache[nodeId] = node;
                }

                return new ValueTask<NodeHandle>(handle);
            }

            // nodes in the instance namespace which are not predefined do not exist.
            if (nodeId.NamespaceIndex == NamespaceIndex)
            {
                return new ValueTask<NodeHandle>((NodeHandle)null);
            }

            // a possible node of the aggregated server.
            return new ValueTask<NodeHandle>(new NodeHandle() { NodeId = nodeId, Validated = false });
        }

        /// <summary>
        /// Verifies that the specified node exists, fetching it from the
        /// aggregated server when it is not a local node.
        /// </summary>
        protected override async ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            // lookup in operation cache.
            NodeState target = await FindNodeInCacheAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                // get remote node.
                NodeId targetId = m_mapper.ToRemoteId(handle.NodeId);
                ILocalNode node = await Opc.Ua.Client.SessionClientExtensions.ReadNodeAsync(
                    clientSession.Session, targetId, cancellationToken).ConfigureAwait(false);

                if (node == null)
                {
                    return null;
                }

                // map remote node to local object.
                switch (node.NodeClass)
                {
                    case NodeClass.ObjectType:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        BaseObjectTypeState value = new BaseObjectTypeState();
#pragma warning restore CA2000
                        value.IsAbstract = ((IObjectType)node).IsAbstract;
                        target = value;
                        break;
                    }

                    case NodeClass.VariableType:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        BaseVariableTypeState value = new BaseDataVariableTypeState();
#pragma warning restore CA2000
                        value.IsAbstract = ((IVariableType)node).IsAbstract;
                        value.Value = m_mapper.ToLocalVariant(((IVariableType)node).Value);
                        value.DataType = m_mapper.ToLocalId(((IVariableType)node).DataType);
                        value.ValueRank = ((IVariableType)node).ValueRank;
                        value.ArrayDimensions = ((IVariableType)node).ArrayDimensions;
                        target = value;
                        break;
                    }

                    case NodeClass.DataType:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        DataTypeState value = new DataTypeState();
#pragma warning restore CA2000
                        value.IsAbstract = ((IDataType)node).IsAbstract;
                        target = value;
                        break;
                    }

                    case NodeClass.ReferenceType:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        ReferenceTypeState value = new ReferenceTypeState();
#pragma warning restore CA2000
                        value.IsAbstract = ((IReferenceType)node).IsAbstract;
                        value.InverseName = ((IReferenceType)node).InverseName;
                        value.Symmetric = ((IReferenceType)node).Symmetric;
                        target = value;
                        break;
                    }

                    case NodeClass.Object:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        BaseObjectState value = new BaseObjectState(null);
#pragma warning restore CA2000
                        value.EventNotifier = ((IObject)node).EventNotifier;
                        target = value;
                        break;
                    }

                    case NodeClass.Variable:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        BaseDataVariableState value = new BaseDataVariableState(null);
#pragma warning restore CA2000
                        value.Value = m_mapper.ToLocalVariant(((IVariable)node).Value);
                        value.DataType = m_mapper.ToLocalId(((IVariable)node).DataType);
                        value.ValueRank = ((IVariable)node).ValueRank;
                        value.ArrayDimensions = ((IVariable)node).ArrayDimensions;
                        value.AccessLevel = ((IVariable)node).AccessLevel;
                        value.UserAccessLevel = ((IVariable)node).UserAccessLevel;
                        value.Historizing = ((IVariable)node).Historizing;
                        value.MinimumSamplingInterval = ((IVariable)node).MinimumSamplingInterval;
                        target = value;
                        break;
                    }

                    case NodeClass.Method:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        MethodState value = new MethodState(null);
#pragma warning restore CA2000
                        value.Executable = ((IMethod)node).Executable;
                        value.UserExecutable = ((IMethod)node).UserExecutable;
                        target = value;
                        break;
                    }

                    case NodeClass.View:
                    {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node cache/handle.
                        ViewState value = new ViewState();
#pragma warning restore CA2000
                        value.ContainsNoLoops = ((IView)node).ContainsNoLoops;
                        target = value;
                        break;
                    }
                }

                target.NodeId = handle.NodeId;
                target.BrowseName = m_mapper.ToLocalName(node.BrowseName);
                target.DisplayName = node.DisplayName;
                target.Description = node.Description;
                target.WriteMask = node.WriteMask;
                target.UserWriteMask = node.UserWriteMask;
                target.Handle = node;
                target.OnCreateBrowser = OnCreateBrowser;
            }

            // ignore errors.
            catch
            {
                return null;
            }

            // put root into operation cache.
            if (cache != null)
            {
                cache[handle.NodeId] = target;
            }

            handle.Node = target;
            handle.Validated = true;
            return handle.Node;
        }

        /// <summary>
        /// Handles a read operation that fetches data from the aggregated server.
        /// </summary>
        protected override async ValueTask ReadAsync(
            ServerSystemContext context,
            ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            List<ReadValueId> requests = new List<ReadValueId>();
            List<int> indexes = new List<int>();

            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                // validate node.
                NodeState source = await ValidateNodeAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);

                if (source == null)
                {
                    continue;
                }

                // only nodes of the aggregated server get this far: the handles of
                // the local nodes carry their node, so the base class reads those
                // itself and never asks for them to be validated.
                ReadValueId nodeToRead = nodesToRead[handle.Index];

                ReadValueId request = (ReadValueId)nodeToRead.MemberwiseClone();
                request.NodeId = m_mapper.ToRemoteId(nodeToRead.NodeId);
                request.DataEncoding = m_mapper.ToRemoteName(nodeToRead.DataEncoding);
                requests.Add(request);
                indexes.Add(handle.Index);
            }

            if (requests.Count == 0)
            {
                return;
            }

            // send request to external system.
            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                ReadResponse response = await clientSession.Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    requests,
                    cancellationToken).ConfigureAwait(false);

                ResponseHeader responseHeader = response.ResponseHeader;
                ArrayOf<DataValue> results = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                // these do sanity checks on the result - make sure response matched the request.
                ClientBase.ValidateResponse<ReadValueId, DataValue>((IReadOnlyList<DataValue>)results.ToArray(), (IReadOnlyList<ReadValueId>)requests.ToArray());
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos.ToArray(), requests);

                // set results.
                for (int ii = 0; ii < requests.Count; ii++)
                {
                    DataValue result = results[ii];
                    values[indexes[ii]] = new DataValue(
                        m_mapper.ToLocalVariant(result.WrappedValue),
                        result.StatusCode,
                        result.SourceTimestamp,
                        result.ServerTimestamp,
                        result.SourcePicoseconds,
                        result.ServerPicoseconds);

                    errors[indexes[ii]] = ServiceResult.Good;

                    if (result.StatusCode != StatusCodes.Good)
                    {
                        errors[indexes[ii]] = new ServiceResult(result.StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    }
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    errors[indexes[ii]] = error;
                }
            }
        }

        /// <summary>
        /// Handles a write operation, forwarding writes to nodes of the
        /// aggregated server.
        /// </summary>
        protected override async ValueTask WriteAsync(
            ServerSystemContext context,
            ArrayOf<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            List<WriteValue> requests = new List<WriteValue>();
            List<int> indexes = new List<int>();

            // validates the nodes and constructs requests for external nodes.
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                // validate node.
                NodeState source = await ValidateNodeAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);

                if (source == null)
                {
                    continue;
                }

                // only nodes of the aggregated server get this far, for the same
                // reason as in ReadAsync.
                WriteValue nodeToWrite = nodesToWrite[handle.Index];

                WriteValue request = (WriteValue)nodeToWrite.MemberwiseClone();
                request.NodeId = m_mapper.ToRemoteId(nodeToWrite.NodeId);
                request.Value = new DataValue(
                    m_mapper.ToRemoteVariant(nodeToWrite.Value.WrappedValue),
                    nodeToWrite.Value.StatusCode,
                    nodeToWrite.Value.SourceTimestamp,
                    nodeToWrite.Value.ServerTimestamp,
                    nodeToWrite.Value.SourcePicoseconds,
                    nodeToWrite.Value.ServerPicoseconds);
                requests.Add(request);
                indexes.Add(handle.Index);
            }

            if (requests.Count == 0)
            {
                return;
            }

            // send request to external system.
            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                WriteResponse response = await clientSession.Session.WriteAsync(
                    null,
                    requests,
                    cancellationToken).ConfigureAwait(false);

                ResponseHeader responseHeader = response.ResponseHeader;
                ArrayOf<StatusCode> results = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                // these do sanity checks on the result - make sure response matched the request.
                ClientBase.ValidateResponse<WriteValue, StatusCode>((IReadOnlyList<StatusCode>)results.ToArray(), (IReadOnlyList<WriteValue>)requests.ToArray());
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos.ToArray(), requests);

                // set results.
                for (int ii = 0; ii < requests.Count; ii++)
                {
                    errors[indexes[ii]] = ServiceResult.Good;

                    if (results[ii] != StatusCodes.Good)
                    {
                        errors[indexes[ii]] = new ServiceResult(results[ii], ii, diagnosticInfos, responseHeader.StringTable);
                    }
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    errors[indexes[ii]] = error;
                }
            }
        }

        /// <summary>
        /// Handles a call operation, forwarding calls on methods of the
        /// aggregated server.
        /// </summary>
        public override async ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();

            List<CallMethodRequest> requests = new List<CallMethodRequest>();
            List<int> indexes = new List<int>();

            // validates the nodes and constructs requests for external nodes.
            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                CallMethodRequest methodToCall = methodsToCall[ii];

                // skip items that have already been processed.
                if (methodToCall.Processed)
                {
                    continue;
                }

                // check for valid handle.
                NodeHandle handle = await GetManagerHandleAsync(systemContext, methodToCall.ObjectId, operationCache, cancellationToken).ConfigureAwait(false);

                if (handle == null)
                {
                    continue;
                }

                // owned by this node manager.
                methodToCall.Processed = true;

                // validate the source node.
                NodeState source = await ValidateNodeAsync(systemContext, handle, operationCache, cancellationToken).ConfigureAwait(false);

                if (source == null)
                {
                    errors[ii] = StatusCodes.BadNodeIdUnknown;
                    continue;
                }

                MethodState method = null;

                // determine if a local node.
                if (PredefinedNodes.ContainsKey(handle.NodeId))
                {
                    // find the method.
                    method = source.FindMethod(systemContext, methodToCall.MethodId);

                    if (method == null)
                    {
                        // check for loose coupling.
                        if (source.ReferenceExists(ReferenceTypeIds.HasComponent, false, methodToCall.MethodId))
                        {
                            method = FindPredefinedNode<MethodState>(methodToCall.MethodId);
                        }

                        if (method == null)
                        {
                            errors[ii] = StatusCodes.BadMethodInvalid;
                            continue;
                        }
                    }
                }

                if (method != null)
                {
                    // call the local method.
                    CallMethodResult result = results[ii] = new CallMethodResult();

                    errors[ii] = await CallAsync(
                        systemContext,
                        methodToCall,
                        method,
                        result,
                        cancellationToken).ConfigureAwait(false);

                    continue;
                }

                CallMethodRequest request = (CallMethodRequest)methodToCall.MemberwiseClone();
                request.ObjectId = m_mapper.ToRemoteId(methodToCall.ObjectId);
                request.MethodId = m_mapper.ToRemoteId(methodToCall.MethodId);

                List<Variant> inputArguments = new List<Variant>();
                for (int jj = 0; jj < request.InputArguments.Count; jj++)
                {
                    inputArguments.Add(m_mapper.ToRemoteVariant(methodToCall.InputArguments[jj]));
                }
                request.InputArguments = inputArguments.ToArrayOf();

                requests.Add(request);
                indexes.Add(ii);
            }

            if (requests.Count == 0)
            {
                return;
            }

            // send request to external system.
            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(systemContext, cancellationToken).ConfigureAwait(false);

                CallResponse response = await clientSession.Session.CallAsync(
                    null,
                    requests,
                    cancellationToken).ConfigureAwait(false);

                ResponseHeader responseHeader = response.ResponseHeader;
                ArrayOf<CallMethodResult> remoteResults = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                // these do sanity checks on the result - make sure response matched the request.
                ClientBase.ValidateResponse<CallMethodRequest, CallMethodResult>((IReadOnlyList<CallMethodResult>)remoteResults.ToArray(), (IReadOnlyList<CallMethodRequest>)requests.ToArray());
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos.ToArray(), requests);

                // set results.
                for (int ii = 0; ii < requests.Count; ii++)
                {
                    CallMethodResult remoteResult = remoteResults[ii];
                    results[indexes[ii]] = remoteResult;
                    errors[indexes[ii]] = ServiceResult.Good;

                    if (remoteResult.StatusCode != StatusCodes.Good)
                    {
                        errors[indexes[ii]] = new ServiceResult(remoteResult.StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    }
                    else
                    {
                        List<Variant> outputArguments = new List<Variant>();
                        for (int jj = 0; jj < remoteResult.OutputArguments.Count; jj++)
                        {
                            outputArguments.Add(m_mapper.ToLocalVariant(remoteResult.OutputArguments[jj]));
                        }
                        remoteResult.OutputArguments = outputArguments.ToArrayOf();
                    }
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    errors[indexes[ii]] = error;
                }
            }
        }

        /// <summary>
        /// Creates the monitored items and forwards the ones which target nodes
        /// of the aggregated server to a subscription on the downstream session.
        /// </summary>
        /// <remarks>
        /// The base class has no asynchronous counterpart of the
        /// <c>OnCreateMonitoredItemsComplete</c> callback the synchronous manager
        /// used, so the forwarding happens after the base implementation created
        /// the items - which is the same point in the operation.
        /// </remarks>
        public override async ValueTask CreateMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory,
            CancellationToken cancellationToken = default)
        {
            await base.CreateMonitoredItemsAsync(
                context,
                subscriptionId,
                publishingInterval,
                timestampsToReturn,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                createDurable,
                monitoredItemIdFactory,
                cancellationToken).ConfigureAwait(false);

            ServerSystemContext systemContext = SystemContext.Copy(context);

            List<MonitoredItem> toForward = new List<MonitoredItem>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                // the list spans the whole service call, so it also carries the items
                // other node managers created. Only forward the ones this instance
                // created, which the base class tracks by their id.
                if (monitoredItem == null || !MonitoredItems.ContainsKey(monitoredItem.Id))
                {
                    continue;
                }

                // local nodes report their own changes.
                if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                {
                    continue;
                }

                toForward.Add(monitoredItem);
            }

            if (toForward.Count == 0)
            {
                return;
            }

            // send request to external system.
            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(systemContext, cancellationToken).ConfigureAwait(false);

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RemoteSubscription target = GetOrCreateSubscription(clientSession);

                    var forwarded = new List<ForwardedMonitoredItem>();

                    foreach (MonitoredItem monitoredItem in toForward)
                    {
                        var options = new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                            StartNodeId = m_mapper.ToRemoteId(monitoredItem.NodeId),
                            AttributeId = Attributes.Value,
                            MonitoringMode = monitoredItem.MonitoringMode,
                            SamplingInterval = TimeSpan.FromMilliseconds(monitoredItem.SamplingInterval / 2),
                            TimestampsToReturn = TimestampsToReturn.Both,
                        });

                        if (target.MonitoredItems.TryAdd(ForwardedItemName(monitoredItem.Id), options, out RemoteMonitoredItem remote))
                        {
                            var pair = new ForwardedMonitoredItem(monitoredItem, remote, options);
                            clientSession.ItemsByLocalId[monitoredItem.Id] = pair;
                            clientSession.ItemsByClientHandle[remote.ClientHandle] = pair;
                            forwarded.Add(pair);
                        }
                    }

                    // the engine creates the items on its own worker, so the operation
                    // results only exist once it has caught up.
                    await WaitForPendingChangesAsync(target, cancellationToken).ConfigureAwait(false);

                    // check status.
                    foreach (ForwardedMonitoredItem pair in forwarded)
                    {
                        if (ServiceResult.IsBad(pair.Remote.Error))
                        {
                            pair.Local.QueueValue(DataValue.Null, pair.Remote.Error);
                        }
                    }
                }
                finally
                {
                    clientSession.SubscriptionLock.Release();
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                foreach (MonitoredItem monitoredItem in toForward)
                {
                    monitoredItem.QueueValue(DataValue.Null, error);
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has been modified.
        /// </summary>
        protected override async ValueTask OnModifyMonitoredItemsCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            List<ForwardedMonitoredItem> remoteItems = new List<ForwardedMonitoredItem>();

            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RemoteSubscription target = clientSession.Subscription;

                    if (target == null)
                    {
                        return;
                    }

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        // the base class only passes the items this node manager
                        // processed, so no ownership check is needed here.
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (monitoredItem == null)
                        {
                            continue;
                        }

                        // determine if a local node.
                        if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                        {
                            continue;
                        }

                        // find matching item.
                        if (!clientSession.ItemsByLocalId.TryGetValue(monitoredItem.Id, out ForwardedMonitoredItem forwarded))
                        {
                            continue;
                        }

                        // update item: reconfiguring the monitor is the modify request.
                        MonitoringMode mode = monitoredItem.MonitoringMode;
                        TimeSpan samplingInterval = TimeSpan.FromMilliseconds(monitoredItem.SamplingInterval / 2);
                        forwarded.Options.Configure(o => o with {
                            MonitoringMode = mode,
                            SamplingInterval = samplingInterval,
                        });

                        remoteItems.Add(forwarded);
                    }

                    if (remoteItems.Count == 0)
                    {
                        return;
                    }

                    await WaitForPendingChangesAsync(target, cancellationToken).ConfigureAwait(false);

                    // check status.
                    foreach (ForwardedMonitoredItem forwarded in remoteItems)
                    {
                        if (ServiceResult.IsBad(forwarded.Remote.Error))
                        {
                            forwarded.Local.QueueValue(DataValue.Null, forwarded.Remote.Error);
                        }
                    }
                }
                finally
                {
                    clientSession.SubscriptionLock.Release();
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                foreach (ForwardedMonitoredItem forwarded in remoteItems)
                {
                    forwarded.Local.QueueValue(DataValue.Null, error);
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has been deleted.
        /// </summary>
        protected override async ValueTask OnDeleteMonitoredItemsCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (clientSession.Subscription == null)
                    {
                        return;
                    }

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        // the base class only passes the items this node manager
                        // processed, so no ownership check is needed here.
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (monitoredItem == null)
                        {
                            continue;
                        }

                        // determine if a local node.
                        if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                        {
                            continue;
                        }

                        // find matching item.
                        if (!clientSession.ItemsByLocalId.TryGetValue(monitoredItem.Id, out ForwardedMonitoredItem forwarded))
                        {
                            continue;
                        }

                        await RemoveForwardedItemAsync(clientSession, forwarded, cancellationToken).ConfigureAwait(false);

                        if (clientSession.Subscription == null)
                        {
                            // the last item is gone and the subscription with it.
                            return;
                        }
                    }
                }
                finally
                {
                    clientSession.SubscriptionLock.Release();
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not access external system.");
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has their monitoring mode changed.
        /// </summary>
        protected override async ValueTask OnSetMonitoringModeCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            List<ForwardedMonitoredItem> remoteItems = new List<ForwardedMonitoredItem>();

            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RemoteSubscription target = clientSession.Subscription;

                    if (target == null)
                    {
                        return;
                    }

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        // the base class only passes the items this node manager
                        // processed, so no ownership check is needed here.
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (monitoredItem == null)
                        {
                            continue;
                        }

                        // determine if a local node.
                        if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                        {
                            continue;
                        }

                        // find matching item.
                        if (!clientSession.ItemsByLocalId.TryGetValue(monitoredItem.Id, out ForwardedMonitoredItem forwarded))
                        {
                            continue;
                        }

                        MonitoringMode mode = monitoredItem.MonitoringMode;
                        forwarded.Options.Configure(o => o with { MonitoringMode = mode });

                        remoteItems.Add(forwarded);
                    }

                    if (remoteItems.Count == 0)
                    {
                        return;
                    }

                    await WaitForPendingChangesAsync(target, cancellationToken).ConfigureAwait(false);

                    // check status.
                    foreach (ForwardedMonitoredItem forwarded in remoteItems)
                    {
                        if (ServiceResult.IsBad(forwarded.Remote.Error))
                        {
                            forwarded.Local.QueueValue(DataValue.Null, forwarded.Remote.Error);
                        }
                    }
                }
                finally
                {
                    clientSession.SubscriptionLock.Release();
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                foreach (ForwardedMonitoredItem forwarded in remoteItems)
                {
                    forwarded.Local.QueueValue(DataValue.Null, error);
                }
            }
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced by all event sources.
        /// </summary>
        public override ValueTask<ServiceResult> SubscribeToAllEventsAsync(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            return SubscribeToEventsAsync(context, null, subscriptionId, monitoredItem, unsubscribe, cancellationToken);
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced by an event source,
        /// forwarding the subscription to the aggregated server.
        /// </summary>
        public override async ValueTask<ServiceResult> SubscribeToEventsAsync(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            // send request to external system.
            try
            {
                MonitoredItem localItem = monitoredItem as MonitoredItem;

                if (localItem == null)
                {
                    return ServiceResult.Good;
                }

                AggregationClientSession clientSession = await GetClientSessionAsync(systemContext, cancellationToken).ConfigureAwait(false);

                if (unsubscribe)
                {
                    await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (clientSession.Subscription == null)
                        {
                            return ServiceResult.Good;
                        }

                        // find matching item.
                        if (!clientSession.ItemsByLocalId.TryGetValue(localItem.Id, out ForwardedMonitoredItem forwarded))
                        {
                            return ServiceResult.Good;
                        }

                        await RemoveForwardedItemAsync(clientSession, forwarded, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        clientSession.SubscriptionLock.Release();
                    }

                    return ServiceResult.Good;
                }

                // create a request. An event item needs its filter in the options it is
                // created with; the V2 engine does not take one pushed in afterwards.
                NodeId startNodeId = (localItem.NodeId == ObjectIds.Server || localItem.NodeId == m_root.NodeId)
                    ? ObjectIds.Server
                    : m_mapper.ToRemoteId(localItem.NodeId);

                var options = new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                    StartNodeId = startNodeId,
                    AttributeId = Attributes.EventNotifier,
                    MonitoringMode = localItem.MonitoringMode,
                    SamplingInterval = TimeSpan.FromMilliseconds(localItem.SamplingInterval),
                    QueueSize = localItem.QueueSize,
                    DiscardOldest = true,
                    Filter = localItem.Filter,
                });

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RemoteSubscription target = GetOrCreateSubscription(clientSession);

                    if (target.MonitoredItems.TryAdd(ForwardedItemName(localItem.Id), options, out RemoteMonitoredItem remote))
                    {
                        var pair = new ForwardedMonitoredItem(localItem, remote, options);
                        clientSession.ItemsByLocalId[localItem.Id] = pair;
                        clientSession.ItemsByClientHandle[remote.ClientHandle] = pair;

                        await WaitForPendingChangesAsync(target, cancellationToken).ConfigureAwait(false);

                        if (ServiceResult.IsBad(remote.Error))
                        {
                            m_logger.LogError("Could not create event item. {Error}", remote.Error.ToLongString());
                        }
                    }
                }
                finally
                {
                    clientSession.SubscriptionLock.Release();
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not access external system.");
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Notification Callbacks
        /// <summary>
        /// Receives the notifications of one downstream subscription and queues them on
        /// the local monitored items they were forwarded for.
        /// </summary>
        /// <remarks>
        /// The V2 engine takes its handler when the subscription is created, so there is
        /// one of these per client session, holding the pairing maps that replace the
        /// <c>Handle</c> the classic client monitored item carried.
        /// </remarks>
        private sealed class ForwardingHandler : ISubscriptionNotificationHandler
        {
            public ForwardingHandler(AggregationNodeManager nodeManager, AggregationClientSession clientSession)
            {
                m_nodeManager = nodeManager;
                m_clientSession = clientSession;
            }

            public ValueTask OnDataChangeNotificationAsync(
                RemoteSubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notifications,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<DataValueChange> changes = notifications.Span;

                for (int ii = 0; ii < changes.Length; ii++)
                {
                    DataValueChange change = changes[ii];

                    if (change.MonitoredItem == null ||
                        !m_clientSession.ItemsByClientHandle.TryGetValue(change.MonitoredItem.ClientHandle, out ForwardedMonitoredItem forwarded))
                    {
                        continue;
                    }

                    ServiceResult error = null;

                    if (change.Value.StatusCode != StatusCodes.Good)
                    {
                        error = new ServiceResult(change.Value.StatusCode, change.DiagnosticInfo, stringTable.ToArrayOf());
                    }

                    var value = new DataValue(
                        m_nodeManager.m_mapper.ToLocalVariant(change.Value.WrappedValue),
                        change.Value.StatusCode,
                        change.Value.SourceTimestamp,
                        DateTime.UtcNow,
                        change.Value.SourcePicoseconds,
                        change.Value.ServerPicoseconds);

                    forwarded.Local.QueueValue(value, error);
                }

                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                RemoteSubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notifications,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<EventNotification> events = notifications.Span;

                for (int ii = 0; ii < events.Length; ii++)
                {
                    EventNotification e = events[ii];

                    if (e.MonitoredItem == null ||
                        !m_clientSession.ItemsByClientHandle.TryGetValue(e.MonitoredItem.ClientHandle, out ForwardedMonitoredItem forwarded))
                    {
                        continue;
                    }

                    // the engine reports the fields alone, so they are wrapped back into the
                    // EventFieldList the server side queues - and copied out while doing so,
                    // because the engine may recycle the notification once this returns.
                    var eventFields = new List<Variant>();
                    foreach (Variant field in e.Fields)
                    {
                        eventFields.Add(m_nodeManager.m_mapper.ToLocalVariant(field));
                    }

                    forwarded.Local.QueueEvent(new EventFieldList {
                        ClientHandle = forwarded.Local.ClientHandle,
                        EventFields = eventFields.ToArrayOf(),
                    });
                }

                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                RemoteSubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                RemoteSubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct)
            {
                return default;
            }

            private readonly AggregationNodeManager m_nodeManager;
            private readonly AggregationClientSession m_clientSession;
        }
        #endregion

        #region Downstream Session Management
        /// <summary>
        /// Get a cached client session or create a new one per server connection.
        /// </summary>
        private async Task<AggregationClientSession> GetClientSessionAsync(ServerSystemContext context, CancellationToken cancellationToken)
        {
            NodeId sessionId;
            string sessionName;
            IUserIdentity userIdentity = null;
            ArrayOf<string> preferredLocales = ArrayOf<string>.Empty;
            AggregationClientSession clientSession = null;

            if (context != null)
            {
                sessionId = context.SessionId ?? NodeId.Null;
                sessionName = context.OperationContext.Session.ReadDiagnostics(d => d.SessionName);
                userIdentity = context.UserIdentity;
                preferredLocales = context.PreferredLocales;
            }
            else
            {
                lock (m_clientsLock)
                {
                    clientSession = m_clients.Where(c => c.Value?.IsMetaDataSession ?? false).FirstOrDefault().Value;
                }
                if (clientSession != null)
                {
                    sessionId = clientSession.ClientSessionId;
                }
                else
                {
                    sessionId = new NodeId(Guid.NewGuid());
                }
                sessionName = $"Aggregation Server({Utils.GetHostName()})";

                // The internal metadata session has no incoming user identity, so
                // derive one from the endpoint configuration. Defaulting to an
                // anonymous token fails whenever the remote server only accepts
                // e.g. a certificate user token policy (see issue #658).
                userIdentity = await GetMetadataUserIdentityAsync(cancellationToken).ConfigureAwait(false);
            }

            Opc.Ua.Client.ISession staleSession = null;

            lock (m_clientsLock)
            {
                // do not allow client session until metadata handler is connected
                if (context != null &&
                    (!m_typeCacheInitialized || m_status.Status.Value != StatusCodes.Good))
                {
                    throw new ServiceResultException(StatusCodes.BadNotConnected, "Server not connected or not finished to load the type cache.");
                }

                if (clientSession != null ||
                    m_clients.TryGetValue(sessionId, out clientSession))
                {
                    clientSession.LastUsed = DateTime.UtcNow;
                    var session = clientSession.Session;
                    if (session != null)
                    {
                        if (session.Connected)
                        {
                            return clientSession;
                        }
                        if (session.Reconnecting)
                        {
                            // the managed session is busy reconnecting and keeps its identity
                            // while it does, so the caller has to come back rather than start
                            // a second downstream session.
                            throw new ServiceResultException(StatusCodes.BadNotConnected, "Reconnecting server!");
                        }

                        // the client session is stale and not reconnecting
                        m_clients.Remove(sessionId);
                        staleSession = session;
                        clientSession = null;
                    }
                    else
                    {
                        // race condition, waiting on another connection
                        throw new ServiceResultException(StatusCodes.BadNotConnected, "Waiting for server connection.");
                    }
                }

                if (clientSession == null)
                {
                    clientSession = new AggregationClientSession(sessionId, context == null) {
                        LastUsed = DateTime.UtcNow
                    };
                    m_clients.Add(sessionId, clientSession);
                }
            }

            // close the stale session outside the dictionary lock.
            if (staleSession != null)
            {
                if (staleSession is Opc.Ua.Client.ManagedSession staleManagedSession)
                {
                    staleManagedSession.ConnectionStateChanged -= Client_ConnectionStateChanged;
                }

                try
                {
                    // bounded for the same reason as the teardown in CloseDownstreamSessionAsync.
                    using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    closeTimeout.CancelAfter(kCloseTimeout);

                    await staleSession.CloseAsync(closeTimeout.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    m_logger.LogDebug(e, "Error closing the stale downstream session.");
                }
                finally
                {
                    staleSession.Dispose();
                }
            }

            try
            {
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("Create Connect Session: {Endpoint} for {SessionName}", m_endpoint, sessionName);
                }
                // the managed session brings its own connection state machine and reconnect
                // policy, so no SessionReconnectHandler is wired up here.
                Opc.Ua.Client.ISession session = await new Opc.Ua.Client.ManagedSessionFactory(Server.Telemetry).CreateAsync(
                    m_configuration,
                    m_reverseConnectManager,
                    m_endpoint,
                    m_endpoint.NeedUpdateFromServer(),
                    false,
                    sessionName,
                    m_sessionTimeout,
                    userIdentity,
                    preferredLocales,
                    cancellationToken).ConfigureAwait(false);

                if (session is Opc.Ua.Client.ManagedSession managedSession)
                {
                    managedSession.ConnectionStateChanged += Client_ConnectionStateChanged;
                }

                lock (m_clientsLock)
                {
                    clientSession.Session = session;
                }

                if (context == null)
                {
                    m_root.BrowseName = new QualifiedName(m_endpoint.Description.Server.ApplicationName.Text, NamespaceIndex);
                    m_root.DisplayName = new LocalizedText(m_root.BrowseName.Name);
                    await m_root.ClearChangeMasksAsync(SystemContext, false, cancellationToken).ConfigureAwait(false);

                    ReportConnectionState(StatusCodes.Good, DateTimeUtc.Now);
                }
                return clientSession;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not connect to server.");

                lock (m_clientsLock)
                {
                    m_clients.Remove(sessionId);
                }

                if (context == null)
                {
                    const int ErrorMessageLength = 30;
                    string message = e.InnerException?.Message ?? e.Message;
                    var trimmedMessage = message.Substring(0, Math.Min(message.Length, ErrorMessageLength));
                    if (message.Length > ErrorMessageLength)
                    {
                        trimmedMessage += "...";
                    }
                    m_root.DisplayName = new LocalizedText(m_endpoint.EndpointUrl.ToString() + $" Status: ({trimmedMessage})");
                    await m_root.ClearChangeMasksAsync(SystemContext, false, CancellationToken.None).ConfigureAwait(false);

                    ReportConnectionState(StatusCodes.BadNotConnected, DateTimeUtc.MinValue);
                }

                throw new ServiceResultException(StatusCodes.BadNotConnected, "Server not connected.");
            }
        }

        /// <summary>
        /// Builds the user identity used for the internal metadata session based
        /// on the endpoint's selected user token policy.
        /// </summary>
        /// <remarks>
        /// The metadata session is opened by the aggregation server itself and
        /// therefore has no incoming client identity. When the remote endpoint
        /// only offers a certificate user token policy, connecting anonymously
        /// fails with "Endpoint does not support the user identity type provided"
        /// (issue #658). In that case the aggregation server's own application
        /// certificate is used as the user certificate. All other cases fall
        /// back to an anonymous identity.
        /// </remarks>
        private async Task<IUserIdentity> GetMetadataUserIdentityAsync(CancellationToken cancellationToken)
        {
            try
            {
                UserTokenPolicy policy = m_endpoint.SelectedUserTokenPolicy;

                if (policy != null && policy.TokenType == UserTokenType.Certificate)
                {
                    CertificateIdentifier applicationCertificate =
                        m_configuration.SecurityConfiguration.ApplicationCertificate;

                    if (applicationCertificate != null)
                    {
                        return await UserIdentity.CreateAsync(
                            applicationCertificate,
                            m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                            m_configuration.CertificateManager.CertificateProvider,
                            cancellationToken).ConfigureAwait(false);
                    }

                    m_logger.LogWarning(
                        "Endpoint {Endpoint} requires a certificate user token but no application certificate is configured; falling back to an anonymous identity.",
                        m_endpoint);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not create the user identity for the metadata session; falling back to an anonymous identity.");
            }

            // default to an anonymous identity
            return new UserIdentity();
        }

        /// <summary>
        /// Connects to the aggregated server and refreshes its metadata until the
        /// manager is disposed. While the connection is down the loop retries at
        /// the initial delay; once the type cache is loaded it refreshes at the
        /// regular metadata refresh period.
        /// </summary>
        private async Task MetadataUpdateLoopAsync(CancellationToken cancellationToken)
        {
            int delay = DefaultMetadataInitDelay;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                bool success = await DoMetadataUpdateAsync(cancellationToken).ConfigureAwait(false);
                delay = success ? DefaultMetadataRefresh : DefaultMetadataInitDelay;
            }
        }

        /// <summary>
        /// Updates the metadata of the aggregated server: connects the internal
        /// session, maps the remote namespaces into the local namespace table and
        /// loads the remote type tree into the type cache.
        /// </summary>
        private async Task<bool> DoMetadataUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(null, cancellationToken).ConfigureAwait(false);
                Opc.Ua.Client.ISession client = clientSession.Session;

                if (client == null)
                {
                    return false;
                }

                string[] typeSystemNamespaceUris = new string[]
                {
                    "http://opcfoundation.org/UA/Diagnostics"
                };

                var mapper = new NamespaceMapper();
                mapper.TypeSystemNamespaceUris = typeSystemNamespaceUris;
                mapper.Initialize(Server.NamespaceUris, client.NamespaceUris, m_endpoint.Description.Server.ApplicationUri);

                // set the namespace indexes.
                var namespaceIndexes = new ushort[mapper.LocalNamespaceIndexes.Length + ((m_ownsTypeModel) ? 1 : 0)];

                int index = 0;
                namespaceIndexes[index++] = (ushort)Server.NamespaceUris.GetIndex(Namespaces.Aggregation);

                if (m_ownsTypeModel)
                {
                    namespaceIndexes[index++] = (ushort)Server.NamespaceUris.GetIndex(AggregationModel.Namespaces.Aggregation);
                }

                for (int ii = 1; ii < mapper.LocalNamespaceIndexes.Length; ii++)
                {
                    namespaceIndexes[index++] = (ushort)mapper.LocalNamespaceIndexes[ii];
                }

                m_mapper = mapper;
                SetNamespaceIndexes(namespaceIndexes);

                // re-register node manager for the namespaces mapped from the remote server.
                for (int ii = 0; ii < namespaceIndexes.Length; ii++)
                {
                    Server.NodeManager.RegisterNamespaceManager(Server.NamespaceUris.GetString(namespaceIndexes[ii]), this);
                }

                AggregatedTypeCache cache = new AggregatedTypeCache();
                await cache.LoadTypesAsync(client, Server, m_mapper, cancellationToken).ConfigureAwait(false);

                // update cache.
                if (m_typeCache == null)
                {
                    m_typeCache = cache;
                }

                m_typeCache.TypeNodes = cache.TypeNodes;
                m_typeCacheInitialized = true;
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating event type cache.");
                return false;
            }
        }

        /// <summary>
        /// Returns the subscription this client session forwards to the aggregated
        /// server, creating it on first use. The caller must hold the subscription
        /// lock of the client session.
        /// </summary>
        /// <remarks>
        /// The handler is created here rather than cached anywhere: it closes over the
        /// client session whose items it resolves, and a session which went stale is
        /// replaced by a new object, so a cached handler would resolve notifications
        /// against the maps of the session that is gone.
        /// </remarks>
        private RemoteSubscription GetOrCreateSubscription(AggregationClientSession clientSession)
        {
            if (clientSession.Subscription != null)
            {
                return clientSession.Subscription;
            }

            if (!clientSession.Session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The downstream session does not use the V2 subscription engine.");
            }

            clientSession.SubscriptionOptions = new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions {
                PublishingInterval = TimeSpan.FromMilliseconds(250),
                KeepAliveCount = 100,
                LifetimeCount = 1000,
                MaxNotificationsPerPublish = 10000,
                Priority = 1,
                PublishingEnabled = true,
            });

            clientSession.Subscription = manager.Add(
                new ForwardingHandler(this, clientSession),
                clientSession.SubscriptionOptions);

            return clientSession.Subscription;
        }

        /// <summary>
        /// The name the forwarded item is registered under in the V2 engine.
        /// </summary>
        /// <remarks>
        /// The engine keys its items by name, so the id of the local monitored item is
        /// what pairs the two - the role the client handle played when the item was
        /// constructed with it.
        /// </remarks>
        private static string ForwardedItemName(uint localId)
        {
            return localId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Waits until the V2 engine has applied the monitored item changes.
        /// </summary>
        /// <remarks>
        /// The engine applies added, modified and removed items on its own worker rather
        /// than on an ApplyChanges call, so the operation results a service has to report
        /// only exist once that worker has caught up.
        /// </remarks>
        private static async Task WaitForPendingChangesAsync(RemoteSubscription subscription, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(kApplyTimeout);

            while (HasPendingChanges(subscription))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return;
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool HasPendingChanges(RemoteSubscription subscription)
        {
            foreach (RemoteMonitoredItem monitoredItem in subscription.MonitoredItems.Items)
            {
                if (monitoredItem is IMonitoredItemApplyState applyState && applyState.HasPendingChanges)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes a forwarded item from the engine and from the pairing maps, and drops
        /// the subscription once the last item is gone.
        /// </summary>
        private static async Task RemoveForwardedItemAsync(
            AggregationClientSession clientSession,
            ForwardedMonitoredItem forwarded,
            CancellationToken cancellationToken)
        {
            clientSession.ItemsByLocalId.TryRemove(forwarded.Local.Id, out _);
            clientSession.ItemsByClientHandle.TryRemove(forwarded.Remote.ClientHandle, out _);

            clientSession.Subscription.MonitoredItems.TryRemove(forwarded.Remote.ClientHandle);

            await WaitForPendingChangesAsync(clientSession.Subscription, cancellationToken).ConfigureAwait(false);

            if (clientSession.ItemsByLocalId.IsEmpty)
            {
                // DisposeAsync deletes the subscription on the server and drops it from
                // the manager; there is no RemoveSubscription in the V2 engine.
                RemoteSubscription subscription = clientSession.Subscription;
                clientSession.Subscription = null;
                clientSession.SubscriptionOptions = null;
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Closes the downstream session opened for a client session when that
        /// client session is closed on the aggregation server, so connections to
        /// the underlying servers are not leaked when clients disconnect (issue #26).
        /// </summary>
        /// <remarks>
        /// The master node manager tells every node manager about the closing
        /// session, so there is no session manager event to subscribe to.
        /// </remarks>
        public override ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            AggregationClientSession clientSession = null;

            if (!sessionId.IsNull)
            {
                lock (m_clientsLock)
                {
                    // never tear down the internal metadata session.
                    if (m_clients.TryGetValue(sessionId, out clientSession) &&
                        clientSession != null &&
                        !clientSession.IsMetaDataSession)
                    {
                        m_clients.Remove(sessionId);
                    }
                    else
                    {
                        clientSession = null;
                    }
                }
            }

            if (clientSession != null)
            {
                // the close of the downstream session is bounded but may still take
                // seconds, and the master node manager waits for every node manager
                // before the client session is gone, so it runs on a background task
                // which outlives the service call and its token.
                _ = Task.Run(() => CloseDownstreamSessionAsync(sessionId, clientSession), CancellationToken.None);
            }

            return base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken);
        }

        /// <summary>
        /// Closes and disposes the downstream session associated with a client session.
        /// </summary>
        private async Task CloseDownstreamSessionAsync(NodeId clientSessionId, AggregationClientSession clientSession)
        {
            try
            {
                clientSession.ItemsByLocalId.Clear();
                clientSession.ItemsByClientHandle.Clear();

                // the subscription goes with the session, so it only has to be dropped
                // from the engine - closing the session deletes it on the server.
                RemoteSubscription subscription = clientSession.Subscription;
                clientSession.Subscription = null;
                clientSession.SubscriptionOptions = null;

                if (subscription != null)
                {
                    try
                    {
                        await subscription.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogDebug(e, "Error disposing the forwarding subscription.");
                    }
                }

                Opc.Ua.Client.ISession session = clientSession.Session;
                if (session != null)
                {
                    if (session is Opc.Ua.Client.ManagedSession managedSession)
                    {
                        managedSession.ConnectionStateChanged -= Client_ConnectionStateChanged;
                    }

                    // a managed session whose close runs into a reconnect attempt waits it
                    // out, so bound the close and let the dispose cancel what is in flight.
                    using var closeTimeout = new CancellationTokenSource(kCloseTimeout);
                    try
                    {
                        await session.CloseAsync(closeTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        m_logger.LogWarning("Timed out closing the downstream session for client {SessionId}.", clientSessionId);
                    }
                    finally
                    {
                        session.Dispose();
                    }
                }

                clientSession.SubscriptionLock.Dispose();

                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("Closed downstream session for client {SessionId}.", clientSessionId);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error closing downstream session for client {SessionId}.", clientSessionId);
            }
        }

        /// <summary>
        /// Reports the connection state changes of a downstream session.
        /// </summary>
        /// <remarks>
        /// The managed session detects the broken connection, reconnects on its own
        /// schedule and keeps the same <see cref="Opc.Ua.Client.ISession"/> instance while
        /// it does, so there is nothing to reconnect or swap out here - only to report.
        /// This replaces the keep-alive handler which used to drive a
        /// <c>SessionReconnectHandler</c> itself, and with it the case where a
        /// synchronously failing keep-alive never incremented the request counters the
        /// reconnect was gated on (issue #312).
        /// </remarks>
        private void Client_ConnectionStateChanged(object sender, Opc.Ua.Client.ConnectionStateChangedEventArgs e)
        {
            if (sender is not Opc.Ua.Client.ISession session || session.SessionId.IsNull)
            {
                return;
            }

            NodeId clientSessionId;
            lock (m_clientsLock)
            {
                AggregationClientSession clientSession = m_clients
                    .Where(c => c.Value?.SessionSessionId == session.SessionId)
                    .FirstOrDefault().Value;

                if (clientSession == null)
                {
                    m_logger.LogWarning("--- {State} for stale session --- SessionId: {SessionId}", e.NewState, session.SessionId);
                    return;
                }

                clientSessionId = clientSession.ClientSessionId;
            }

#pragma warning disable CA1873 // Justification: Sample logging keeps existing message formatting.
            switch (e.NewState)
            {
                case Opc.Ua.Client.ConnectionState.Reconnecting:
                case Opc.Ua.Client.ConnectionState.Failover:
                    m_logger.LogInformation("--- RECONNECTING (attempt {Attempt}) --- SessionId: {SessionId}", e.ReconnectAttempt, clientSessionId);
                    break;

                case Opc.Ua.Client.ConnectionState.Connected:
                    if (e.PreviousState is Opc.Ua.Client.ConnectionState.Reconnecting or Opc.Ua.Client.ConnectionState.Failover)
                    {
                        m_logger.LogInformation("--- RECONNECTED --- SessionId: {SessionId}", clientSessionId);
                    }
                    break;

                case Opc.Ua.Client.ConnectionState.Disconnected:
                    m_logger.LogInformation("--- DISCONNECTED ({Error}) --- SessionId: {SessionId}", e.Error, clientSessionId);
                    break;
            }
#pragma warning restore CA1873
        }

        /// <summary>
        /// Reports the state of the connection to the aggregated server through
        /// the status object.
        /// </summary>
        /// <remarks>
        /// The endpoint url is reported again on every change because the
        /// endpoint may have been updated from the server while connecting.
        /// </remarks>
        private void ReportConnectionState(StatusCode status, DateTimeUtc connectTime)
        {
            m_endpointUrl.SetValue(m_endpoint.EndpointUrl.ToString());
            m_connectionStatus.SetValue(status);
            m_connectTime.SetValue(connectTime);
        }

        /// <summary>
        /// Generates a new node id.
        /// </summary>
        private NodeId GenerateNodeId()
        {
            return new NodeId(Guid.NewGuid(), NamespaceIndex);
        }
        #endregion

        #region Private Fields
        private readonly bool m_ownsTypeModel;
        private readonly ApplicationConfiguration m_configuration;
        private readonly ConfiguredEndpoint m_endpoint;
        private readonly Opc.Ua.Client.ReverseConnectManager m_reverseConnectManager;
        private readonly Dictionary<NodeId, AggregationClientSession> m_clients;
        private readonly object m_clientsLock;
        private readonly uint m_sessionTimeout;
        private AggregatedTypeCache m_typeCache;
        private volatile bool m_typeCacheInitialized;
        private CancellationTokenSource m_metadataUpdateCancellation;
        private NamespaceMapper m_mapper;
        // Justification: the nodes are owned by the predefined node table and
        // released by DeleteAddressSpaceAsync of the base class.
#pragma warning disable CA2213
        private FolderState m_root;
        private AggregatedServerStatusState m_status;
#pragma warning restore CA2213
        private IValueUpdater<string> m_endpointUrl;
        private IValueUpdater<StatusCode> m_connectionStatus;
        private IValueUpdater<DateTimeUtc> m_connectTime;
        #endregion
    }

    /// <summary>
    /// Creates the node manager for one aggregated server endpoint.
    /// </summary>
    /// <remarks>
    /// The server registers one factory per configured endpoint through
    /// <c>StandardServer.AddNodeManager</c>; only the first one publishes the
    /// aggregation type model.
    /// </remarks>
    public sealed class AggregationNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly ConfiguredEndpoint m_endpoint;
        private readonly Opc.Ua.Client.ReverseConnectManager m_reverseConnectManager;
        private readonly bool m_ownsTypeModel;

        /// <summary>
        /// Initializes the factory.
        /// </summary>
        public AggregationNodeManagerFactory(
            ConfiguredEndpoint endpoint,
            Opc.Ua.Client.ReverseConnectManager reverseConnectManager,
            bool ownsTypeModel)
        {
            m_endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            m_reverseConnectManager = reverseConnectManager;
            m_ownsTypeModel = ownsTypeModel;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => new ArrayOf<string>(new string[] { Namespaces.Aggregation, AggregationModel.Namespaces.Aggregation });

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: node manager ownership is transferred to the master node manager.
            return new ValueTask<IAsyncNodeManager>(
                new AggregationNodeManager(server, configuration, m_endpoint, m_reverseConnectManager, m_ownsTypeModel));
#pragma warning restore CA2000
        }
    }
}
