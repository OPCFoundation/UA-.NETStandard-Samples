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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace AggregationServer
{
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
        public Opc.Ua.Client.SessionReconnectHandler ReconnectHandler { get; set; }
        public DateTime LastUsed { get; set; }

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
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>Model/ModelDesign.xml</c> and calls <see cref="Configure"/> once the
    /// address space is in place. The factory stays hand written
    /// (<c>GenerateFactory = false</c>) because the server creates one manager
    /// per aggregated endpoint, with arguments a generated factory cannot know.
    /// </remarks>
    [NodeManager(GenerateFactory = false)]
    public partial class AggregationNodeManager
    {
        const uint DefaultSessionTimeout = 60000;
        const int DefaultMetadataRefresh = 300000;
        const int DefaultMetadataInitDelay = 5000;
        const int DefaultReconnectPeriod = 5000;

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
                if (m_sessionManager != null)
                {
                    m_sessionManager.SessionClosing -= SessionManager_SessionClosing;
                    m_sessionManager = null;
                }
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
        /// instance. The external reference dictionary is remembered so
        /// <see cref="Configure"/> can link the dynamically created root to the
        /// Objects folder and the Server object: the master node manager
        /// distributes the dictionary only after every node manager created its
        /// address space, so entries added during Configure are honoured.
        /// </remarks>
        protected override async ValueTask LoadPredefinedNodesAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            m_externalReferences = externalReferences;

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

            // link root to objects folder and to the server object.
            root.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            AddExternalReference(ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, false, root.NodeId, m_externalReferences);

            root.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
            AddExternalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, root.NodeId, m_externalReferences);

            AddPredefinedNodeSynchronously(root);

            // create the status object which reports the connection to the aggregated server.
            AggregationModel.AggregatedServerStatusState status = m_status = new AggregationModel.AggregatedServerStatusState(null);

            status.Create(
                SystemContext,
                GenerateNodeId(),
                new QualifiedName("Status", NamespaceIndex),
                LocalizedText.Null,
                true);

            status.EndpointUrl.Value = m_endpoint.EndpointUrl.ToString();
            status.Status.Value = StatusCodes.BadNotConnected;
            status.ConnectTime.Value = DateTime.MinValue;

            status.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, root.NodeId);
            root.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, status.NodeId);

            AddPredefinedNodeSynchronously(status);

            // Close the downstream session that was opened for a client session
            // as soon as that client session is closed, so connections to the
            // underlying servers are not leaked when clients disconnect (issue #26).
            m_sessionManager = Server.SessionManager;
            if (m_sessionManager != null)
            {
                m_sessionManager.SessionClosing += SessionManager_SessionClosing;
            }

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

                ReadValueId nodeToRead = nodesToRead[handle.Index];

                // read local nodes directly.
                if (PredefinedNodes.ContainsKey(source.NodeId))
                {
                    DataValue value = values[handle.Index];
                    (errors[handle.Index], value) = await source.ReadAttributeAsync(
                        context,
                        nodeToRead.AttributeId,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        value,
                        cancellationToken).ConfigureAwait(false);
                    values[handle.Index] = value;

                    continue;
                }

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

                WriteValue nodeToWrite = nodesToWrite[handle.Index];

                // write local nodes directly.
                if (PredefinedNodes.ContainsKey(source.NodeId))
                {
                    await m_writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        // write the attribute value.
                        errors[handle.Index] = await source.WriteAttributeAsync(
                            context,
                            nodeToWrite.AttributeId,
                            nodeToWrite.ParsedIndexRange,
                            nodeToWrite.Value,
                            cancellationToken).ConfigureAwait(false);

                        // updates to source finished - report changes to monitored items.
                        await source.ClearChangeMasksAsync(context, false, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_writeSemaphore.Release();
                    }

                    continue;
                }

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

            List<Opc.Ua.Client.MonitoredItem> requests = new List<Opc.Ua.Client.MonitoredItem>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                if (!IsOwnMonitoredItem(monitoredItem))
                {
                    continue;
                }

                // local nodes report their own changes.
                if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                {
                    continue;
                }

                // create a request.
                Opc.Ua.Client.MonitoredItem request = new Opc.Ua.Client.MonitoredItem(monitoredItem.Id, Server.Telemetry);

                request.StartNodeId = m_mapper.ToRemoteId(monitoredItem.NodeId);
                request.MonitoringMode = monitoredItem.MonitoringMode;
                request.SamplingInterval = (int)(monitoredItem.SamplingInterval / 2);
                request.Handle = monitoredItem;

                requests.Add(request);
            }

            if (requests.Count == 0)
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
                    Opc.Ua.Client.Subscription target = await GetOrCreateSubscriptionAsync(clientSession, cancellationToken).ConfigureAwait(false);

                    for (int ii = 0; ii < requests.Count; ii++)
                    {
                        target.AddItem(requests[ii]);
                    }

                    await target.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                    // check status.
                    foreach (Opc.Ua.Client.MonitoredItem request in requests)
                    {
                        if (ServiceResult.IsBad(request.Status.Error))
                        {
                            ((MonitoredItem)request.Handle).QueueValue(DataValue.Null, request.Status.Error);
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

                foreach (Opc.Ua.Client.MonitoredItem request in requests)
                {
                    ((MonitoredItem)request.Handle).QueueValue(DataValue.Null, error);
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
            List<Opc.Ua.Client.MonitoredItem> remoteItems = new List<Opc.Ua.Client.MonitoredItem>();

            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Opc.Ua.Client.Subscription target = FindSubscription(clientSession.Session);

                    if (target == null)
                    {
                        return;
                    }

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (!IsOwnMonitoredItem(monitoredItem))
                        {
                            continue;
                        }

                        // determine if a local node.
                        if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                        {
                            continue;
                        }

                        // find matching item.
                        Opc.Ua.Client.MonitoredItem remoteItem = target.FindItemByClientHandle(monitoredItem.Id);

                        if (remoteItem == null)
                        {
                            continue;
                        }

                        //  update item.
                        remoteItem.MonitoringMode = monitoredItem.MonitoringMode;
                        remoteItem.SamplingInterval = (int)(monitoredItem.SamplingInterval / 2);
                        remoteItems.Add(remoteItem);
                    }

                    if (remoteItems.Count == 0)
                    {
                        return;
                    }

                    await target.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                    // check status.
                    foreach (Opc.Ua.Client.MonitoredItem monitoredItem in remoteItems)
                    {
                        if (ServiceResult.IsBad(monitoredItem.Status.Error))
                        {
                            ((MonitoredItem)monitoredItem.Handle).QueueValue(DataValue.Null, monitoredItem.Status.Error);
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

                foreach (Opc.Ua.Client.MonitoredItem monitoredItem in remoteItems)
                {
                    ((MonitoredItem)monitoredItem.Handle).QueueValue(DataValue.Null, error);
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
                    Opc.Ua.Client.Subscription target = FindSubscription(clientSession.Session);

                    if (target == null)
                    {
                        return;
                    }

                    bool changed = false;

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (!IsOwnMonitoredItem(monitoredItem))
                        {
                            continue;
                        }

                        // determine if a local node.
                        if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                        {
                            continue;
                        }

                        // find matching item.
                        Opc.Ua.Client.MonitoredItem remoteItem = target.FindItemByClientHandle(monitoredItem.Id);

                        if (remoteItem == null)
                        {
                            continue;
                        }

                        target.RemoveItem(remoteItem);
                        changed = true;
                    }

                    if (!changed)
                    {
                        return;
                    }

                    await target.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                    if (target.MonitoredItemCount == 0)
                    {
                        await clientSession.Session.RemoveSubscriptionAsync(target, cancellationToken).ConfigureAwait(false);
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
            List<Opc.Ua.Client.MonitoredItem> remoteItems = new List<Opc.Ua.Client.MonitoredItem>();

            try
            {
                AggregationClientSession clientSession = await GetClientSessionAsync(context, cancellationToken).ConfigureAwait(false);

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Opc.Ua.Client.Subscription target = FindSubscription(clientSession.Session);

                    if (target == null)
                    {
                        return;
                    }

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (!IsOwnMonitoredItem(monitoredItem))
                        {
                            continue;
                        }

                        // determine if a local node.
                        if (PredefinedNodes.ContainsKey(monitoredItem.NodeId))
                        {
                            continue;
                        }

                        // find matching item.
                        Opc.Ua.Client.MonitoredItem remoteItem = target.FindItemByClientHandle(monitoredItem.Id);

                        if (remoteItem == null)
                        {
                            continue;
                        }

                        remoteItem.MonitoringMode = monitoredItem.MonitoringMode;
                        remoteItems.Add(remoteItem);
                    }

                    if (remoteItems.Count == 0)
                    {
                        return;
                    }

                    await target.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                    // check status.
                    foreach (Opc.Ua.Client.MonitoredItem monitoredItem in remoteItems)
                    {
                        if (ServiceResult.IsBad(monitoredItem.Status.Error))
                        {
                            ((MonitoredItem)monitoredItem.Handle).QueueValue(DataValue.Null, monitoredItem.Status.Error);
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

                foreach (Opc.Ua.Client.MonitoredItem monitoredItem in remoteItems)
                {
                    ((MonitoredItem)monitoredItem.Handle).QueueValue(DataValue.Null, error);
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
                        // get the subscription.
                        Opc.Ua.Client.Subscription target = FindSubscription(clientSession.Session);

                        if (target == null)
                        {
                            return ServiceResult.Good;
                        }

                        // find matching item.
                        Opc.Ua.Client.MonitoredItem remoteItem = target.FindItemByClientHandle(monitoredItem.Id);

                        if (remoteItem == null)
                        {
                            return ServiceResult.Good;
                        }

                        // apply changes.
                        target.RemoveItem(remoteItem);
                        await target.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                        if (target.MonitoredItemCount == 0)
                        {
                            await clientSession.Session.RemoveSubscriptionAsync(target, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        clientSession.SubscriptionLock.Release();
                    }

                    return ServiceResult.Good;
                }

                // create a request.
                Opc.Ua.Client.MonitoredItem request = new Opc.Ua.Client.MonitoredItem(localItem.Id, Server.Telemetry);

                if (localItem.NodeId == ObjectIds.Server || localItem.NodeId == m_root.NodeId)
                {
                    request.StartNodeId = ObjectIds.Server;
                }
                else
                {
                    request.StartNodeId = m_mapper.ToRemoteId(localItem.NodeId);
                }

                request.AttributeId = Attributes.EventNotifier;
                request.MonitoringMode = localItem.MonitoringMode;
                request.SamplingInterval = (int)localItem.SamplingInterval;
                request.QueueSize = localItem.QueueSize;
                request.DiscardOldest = true;
                request.Filter = localItem.Filter;
                request.Handle = localItem;

                await clientSession.SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Opc.Ua.Client.Subscription target = await GetOrCreateSubscriptionAsync(clientSession, cancellationToken).ConfigureAwait(false);

                    target.AddItem(request);
                    await target.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                    if (ServiceResult.IsBad(request.Status.Error))
                    {
                        m_logger.LogError("Could not create event item. {Error}", request.Status.Error.ToLongString());
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
        /// The delegate used to receive data change notifications via a direct function call instead of a .NET Event.
        /// </summary>
        public void OnDataChangeNotification(Opc.Ua.Client.Subscription subscription, DataChangeNotification notification, ArrayOf<string> stringTable)
        {
            for (int ii = 0; ii < notification.MonitoredItems.Count; ii++)
            {
                MonitoredItemNotification remoteValue = notification.MonitoredItems[ii];

                Opc.Ua.Client.MonitoredItem monitoredItem = subscription.FindItemByClientHandle(remoteValue.ClientHandle);
                MonitoredItem localItem = monitoredItem?.Handle as MonitoredItem;

                if (localItem == null)
                {
                    continue;
                }

                ServiceResult error = null;

                if (remoteValue.Value.StatusCode != StatusCodes.Good)
                {
                    error = new ServiceResult(remoteValue.Value.StatusCode, remoteValue.DiagnosticInfo, stringTable);
                }

                DataValue value = new DataValue(
                    m_mapper.ToLocalVariant(remoteValue.Value.WrappedValue),
                    remoteValue.Value.StatusCode,
                    remoteValue.Value.SourceTimestamp,
                    DateTime.UtcNow,
                    remoteValue.Value.SourcePicoseconds,
                    remoteValue.Value.ServerPicoseconds);

                localItem.QueueValue(value, error);
            }
        }

        /// <summary>
        /// The delegate used to receive event notifications via a direct function call instead of a .NET Event.
        /// </summary>
        public void OnEventNotification(Opc.Ua.Client.Subscription subscription, EventNotificationList notification, ArrayOf<string> stringTable)
        {
            for (int ii = 0; ii < notification.Events.Count; ii++)
            {
                EventFieldList e = notification.Events[ii];

                Opc.Ua.Client.MonitoredItem monitoredItem = subscription.FindItemByClientHandle(e.ClientHandle);
                MonitoredItem localItem = monitoredItem?.Handle as MonitoredItem;

                if (localItem == null)
                {
                    continue;
                }

                List<Variant> eventFields = new List<Variant>();
                for (int jj = 0; jj < e.EventFields.Count; jj++)
                {
                    eventFields.Add(m_mapper.ToLocalVariant(e.EventFields[jj]));
                }
                e.EventFields = eventFields.ToArrayOf();
                e.ClientHandle = localItem.ClientHandle;

                localItem.QueueEvent(e);
            }
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
                        var reconnectHandler = clientSession.ReconnectHandler;
                        if (reconnectHandler == null)
                        {
                            // the client session is stale and not reconnecting
                            m_clients.Remove(sessionId);
                            staleSession = session;
                            clientSession = null;
                        }
                        else
                        {
                            // the client session is busy reconnecting
                            throw new ServiceResultException(StatusCodes.BadNotConnected, "Reconnecting server!");
                        }
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
                try
                {
                    await staleSession.CloseAsync(cancellationToken).ConfigureAwait(false);
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
                Opc.Ua.Client.ISession session = await new Opc.Ua.Client.DefaultSessionFactory(Server.Telemetry).CreateAsync(
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

                session.KeepAlive += Client_KeepAlive;
                lock (m_clientsLock)
                {
                    clientSession.Session = session;
                }

                if (context == null)
                {
                    m_root.BrowseName = new QualifiedName(m_endpoint.Description.Server.ApplicationName.Text, NamespaceIndex);
                    m_root.DisplayName = new LocalizedText(m_root.BrowseName.Name);
                    await m_root.ClearChangeMasksAsync(SystemContext, false, cancellationToken).ConfigureAwait(false);

                    m_status.EndpointUrl.Value = m_endpoint.EndpointUrl.ToString();
                    m_status.Status.Value = StatusCodes.Good;
                    m_status.ConnectTime.Value = DateTime.UtcNow;
                    await m_status.ClearChangeMasksAsync(SystemContext, true, cancellationToken).ConfigureAwait(false);
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

                    m_status.EndpointUrl.Value = m_endpoint.EndpointUrl.ToString();
                    m_status.Status.Value = StatusCodes.BadNotConnected;
                    m_status.ConnectTime.Value = DateTime.MinValue;
                    await m_status.ClearChangeMasksAsync(SystemContext, true, CancellationToken.None).ConfigureAwait(false);
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
        /// Returns the subscription used to forward monitored items to the
        /// aggregated server, creating it on first use. The caller must hold the
        /// subscription lock of the client session.
        /// </summary>
        private async Task<Opc.Ua.Client.Subscription> GetOrCreateSubscriptionAsync(AggregationClientSession clientSession, CancellationToken cancellationToken)
        {
            Opc.Ua.Client.Subscription target = FindSubscription(clientSession.Session);

            if (target != null)
            {
                return target;
            }

#pragma warning disable CA2000 // Justification: Subscription ownership is transferred to the client session.
            Opc.Ua.Client.Subscription subscription = new Opc.Ua.Client.Subscription(Server.Telemetry);
#pragma warning restore CA2000

            subscription.PublishingInterval = 250;
            subscription.KeepAliveCount = 100;
            subscription.LifetimeCount = 1000;
            subscription.MaxNotificationsPerPublish = 10000;
            subscription.Priority = 1;
            subscription.PublishingEnabled = true;
            subscription.TimestampsToReturn = TimestampsToReturn.Both;
            subscription.DisableMonitoredItemCache = true;
            subscription.FastDataChangeCallback = OnDataChangeNotification;
            subscription.FastEventCallback = OnEventNotification;

            clientSession.Session.AddSubscription(subscription);
            await subscription.CreateAsync(cancellationToken).ConfigureAwait(false);

            return subscription;
        }

        /// <summary>
        /// Determines whether a monitored item was created by this node manager.
        /// </summary>
        /// <remarks>
        /// The base class hands the monitored items the synchronous facade of the
        /// node manager wrapped in an async adapter rather than the node manager
        /// itself, so a reference comparison against <c>this</c> is not enough.
        /// </remarks>
        private bool IsOwnMonitoredItem(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                return false;
            }

            object nodeManager = monitoredItem.NodeManager;

            if (ReferenceEquals(nodeManager, this) || ReferenceEquals(nodeManager, SyncNodeManager))
            {
                return true;
            }

            return nodeManager is AsyncNodeManagerAdapter adapter &&
                ReferenceEquals(adapter.SyncNodeManager, SyncNodeManager);
        }

        /// <summary>
        /// Returns the forwarding subscription of the session, or null.
        /// </summary>
        private static Opc.Ua.Client.Subscription FindSubscription(Opc.Ua.Client.ISession client)
        {
            foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
            {
                return current;
            }

            return null;
        }

        /// <summary>
        /// Closes the downstream session opened for a client session when that
        /// client session is closed on the aggregation server (issue #26).
        /// </summary>
        private void SessionManager_SessionClosing(Opc.Ua.Server.ISession session, Opc.Ua.Server.SessionEventReason reason)
        {
            NodeId clientSessionId = session?.Id ?? NodeId.Null;
            if (clientSessionId.IsNull)
            {
                return;
            }

            AggregationClientSession clientSession;
            lock (m_clientsLock)
            {
                if (!m_clients.TryGetValue(clientSessionId, out clientSession) || clientSession == null)
                {
                    return;
                }

                // never tear down the internal metadata session.
                if (clientSession.IsMetaDataSession)
                {
                    return;
                }

                m_clients.Remove(clientSessionId);
            }

            // Event sinks must not block the session manager thread, so close the
            // downstream session on a background task.
            _ = Task.Run(() => CloseDownstreamSessionAsync(clientSessionId, clientSession));
        }

        /// <summary>
        /// Closes and disposes the downstream session associated with a client session.
        /// </summary>
        private async Task CloseDownstreamSessionAsync(NodeId clientSessionId, AggregationClientSession clientSession)
        {
            try
            {
                clientSession.ReconnectHandler?.Dispose();

                Opc.Ua.Client.ISession session = clientSession.Session;
                if (session != null)
                {
                    session.KeepAlive -= Client_KeepAlive;
                    try
                    {
                        await session.CloseAsync().ConfigureAwait(false);
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

        private void Client_KeepAlive(Opc.Ua.Client.ISession session, Opc.Ua.Client.KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                if (m_logger.IsEnabled(LogLevel.Debug))
                {
                    m_logger.LogDebug("{Status} {OutstandingRequestCount}/{DefunctRequestCount}", e.Status, session.OutstandingRequestCount, session.DefunctRequestCount);
                }
                Opc.Ua.Client.SessionReconnectHandler reconnectHandler;
                // Any not-good keep-alive status means the connection to the downstream
                // server is broken, so start reconnecting immediately. Previously this was
                // gated on OutstandingRequestCount + DefunctRequestCount >= 3, but when a
                // keep-alive read fails synchronously (e.g. BadConnectionClosed after the
                // downstream server is restarted or the network is lost) those counters are
                // never incremented, so the reconnect logic never triggered (issue #312).
                if (!session.SessionId.IsNull)
                {
                    lock (m_clientsLock)
                    {
                        AggregationClientSession clientSession = m_clients.Where(c => c.Value?.SessionSessionId == session.SessionId).FirstOrDefault().Value;
                        if (clientSession != null && clientSession.ReconnectHandler == null)
                        {
#pragma warning disable CA1873 // Justification: Sample logging keeps existing message formatting.
                            m_logger.LogInformation("--- RECONNECTING --- SessionId: {SessionId}", clientSession.ClientSessionId);
#pragma warning restore CA1873
                            reconnectHandler = new Opc.Ua.Client.SessionReconnectHandler(Server.Telemetry, true);
                            reconnectHandler.BeginReconnect(session, m_reverseConnectManager, DefaultReconnectPeriod, Client_ReconnectComplete);
                            clientSession.ReconnectHandler = reconnectHandler;
                            e.CancelKeepAlive = true;
                        }
                        else if (clientSession == null)
                        {
                            m_logger.LogWarning("--- KEEP ALIVE for stale session --- SessionId: {SessionId}", session.SessionId);
                        }
                    }
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            Opc.Ua.Client.SessionReconnectHandler reconnectHandler = sender as Opc.Ua.Client.SessionReconnectHandler;
            if (reconnectHandler == null)
            {
                return;
            }

            lock (m_clientsLock)
            {
                var session = reconnectHandler.Session;
                AggregationClientSession clientSession = m_clients.Where(c => Object.ReferenceEquals(reconnectHandler, c.Value?.ReconnectHandler)).FirstOrDefault().Value;
                if (clientSession == null)
                {
#pragma warning disable CA1873 // Justification: Sample logging keeps existing message formatting.
                    m_logger.LogInformation("--- RECONNECTED --- SessionId: {SessionId} but client session was not found.", session?.SessionId);
#pragma warning restore CA1873
                    return;
                }

                clientSession.ReconnectHandler = null;
                Opc.Ua.Client.Session newSession = session as Opc.Ua.Client.Session;
                if (newSession != null && !ReferenceEquals(newSession, clientSession.Session))
                {
                    var oldSession = clientSession.Session;
                    oldSession.KeepAlive -= Client_KeepAlive;
                    newSession.KeepAlive += Client_KeepAlive;
                    clientSession.Session = newSession;
                    oldSession?.Dispose();
                }
                reconnectHandler.Dispose();
#pragma warning disable CA1873 // Justification: Sample logging keeps existing message formatting.
                m_logger.LogInformation("--- RECONNECTED --- SessionId: {SessionId}", clientSession.ClientSessionId);
#pragma warning restore CA1873
            }
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
        private Opc.Ua.Server.ISessionManager m_sessionManager;
        private AggregatedTypeCache m_typeCache;
        private volatile bool m_typeCacheInitialized;
        private CancellationTokenSource m_metadataUpdateCancellation;
        private IDictionary<NodeId, IList<IReference>> m_externalReferences;
        private NamespaceMapper m_mapper;
        // Justification: the nodes are owned by the predefined node table and
        // released by DeleteAddressSpaceAsync of the base class.
#pragma warning disable CA2213
        private FolderState m_root;
        private AggregationModel.AggregatedServerStatusState m_status;
#pragma warning restore CA2213
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
