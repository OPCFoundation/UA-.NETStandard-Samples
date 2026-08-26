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
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;
using System.Linq;
using Microsoft.Extensions.Logging;

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

        private Opc.Ua.Client.ISession m_session;
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class AggregationNodeManager : CustomNodeManager2
    {
        const uint DefaultSessionTimeout = 60000;
        const int DefaultMetadataRefresh = 300000;
        const int DefaultMetadataInitDelay = 5000;
        const int DefaultReconnectPeriod = 5000;

        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public AggregationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Opc.Ua.Client.ReverseConnectManager reverseConnectManager,
            bool ownsTypeModel)
        :
            base(server, configuration, Namespaces.Aggregation, AggregationModel.Namespaces.Aggregation)
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
                if (m_sessionManager != null)
                {
                    m_sessionManager.SessionClosing -= SessionManager_SessionClosing;
                    m_sessionManager = null;
                }
                m_metadataUpdateTimer?.Dispose();
                m_metadataUpdateTimer = null;
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

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                if (m_ownsTypeModel)
                {
                    LoadPredefinedNodes(SystemContext, externalReferences);
                }

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

                AddPredefinedNode(SystemContext, root);

                // link root to objects folder.
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
                root.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                // link root to server object.
                if (!externalReferences.TryGetValue(ObjectIds.Server, out references))
                {
                    externalReferences[ObjectIds.Server] = references = new List<IReference>();
                }

                references.Add(new NodeStateReference(ReferenceTypeIds.HasNotifier, false, root.NodeId));
                root.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);

                // create status object.
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

                AddPredefinedNode(SystemContext, status);

                StartMetadataUpdates(DoMetadataUpdateAsync, null, DefaultMetadataInitDelay, DefaultMetadataRefresh);

                // Close the downstream session that was opened for a client session
                // as soon as that client session is closed, so connections to the
                // underlying servers are not leaked when clients disconnect (issue #26).
                m_sessionManager = Server.SessionManager;
                if (m_sessionManager != null)
                {
                    m_sessionManager.SessionClosing += SessionManager_SessionClosing;
                }
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                // check cache (the cache is used because the same node id can appear many times in a single request).
                if (cache != null)
                {
                    if (cache.TryGetValue(nodeId, out node))
                    {
                        return new NodeHandle(nodeId, node);
                    }
                }

                // look up predefined node.
                if (PredefinedNodes != null)
                {
                    if (PredefinedNodes.TryGetValue(nodeId, out node))
                    {
                        NodeHandle handle = new NodeHandle(nodeId, node);

                        if (cache != null)
                        {
                            cache.Add(nodeId, node);
                        }

                        return handle;
                    }
                }

                // check for shared namespaces.
                if (nodeId.NamespaceIndex == NamespaceIndex)
                {
                    return null;
                }

                // possible node.
                return new NodeHandle() { NodeId = nodeId, Validated = false };
            }
        }

        /// <summary>
        /// Handles a read operations that fetch data from an external source.
        /// </summary>
        protected override void Read(
            ServerSystemContext context,
            ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache)
        {
            List<ReadValueId> requests = new List<ReadValueId>();
            List<int> indexes = new List<int>();

            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];
                ReadValueId nodeToRead = nodesToRead[ii];
                DataValue value = values[ii];

                lock (Lock)
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // determine if a local node.
                    if (PredefinedNodes.ContainsKey(source.NodeId))
                    {
                        NumericRange indexRange = nodeToRead.ParsedIndexRange;
                        DataValue localValue = value;
                        errors[handle.Index] = source.ReadAttribute(
                            context,
                            nodeToRead.AttributeId,
                            indexRange,
                            nodeToRead.DataEncoding,
                            ref localValue);
                        values[handle.Index] = localValue;

                        continue;
                    }

                    ReadValueId request = (ReadValueId)nodeToRead.MemberwiseClone();
                    request.NodeId = m_mapper.ToRemoteId(nodeToRead.NodeId);
                    request.DataEncoding = m_mapper.ToRemoteName(nodeToRead.DataEncoding);
                    requests.Add(request);
                    indexes.Add(ii);
                }
            }

            // send request to external system.
            try
            {
                Opc.Ua.Client.ISession client = GetClientSession(context);

                ReadResponse response = client.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    requests,
                    default).AsTask().Result;

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

                    if (results[ii].StatusCode != StatusCodes.Good)
                    {
                        errors[indexes[ii]] = new ServiceResult(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
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
        /// Handles a write operation.
        /// </summary>
        protected override void Write(
            ServerSystemContext context,
            ArrayOf<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache)
        {
            List<WriteValue> requests = new List<WriteValue>();
            List<int> indexes = new List<int>();

            // validates the nodes and constructs requests for external nodes.
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                WriteValue nodeToWrite = nodesToWrite[ii];
                NodeHandle handle = nodesToValidate[ii];

                lock (Lock)
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // determine if a local node.
                    if (PredefinedNodes.ContainsKey(source.NodeId))
                    {
                        // write the attribute value.
                        errors[handle.Index] = source.WriteAttribute(
                            context,
                            nodeToWrite.AttributeId,
                            nodeToWrite.ParsedIndexRange,
                            nodeToWrite.Value);

                        // updates to source finished - report changes to monitored items.
                        source.ClearChangeMasks(context, false);
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
                    indexes.Add(ii);
                }
            }

            // send request to external system.
            try
            {
                Opc.Ua.Client.ISession client = GetClientSession(context);

                WriteResponse response = client.WriteAsync(
                    null,
                    requests,
                    default).AsTask().Result;

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
        /// Handles a call operation.
        /// </summary>
        public override void Call(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors)
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

                MethodState method = null;

                lock (Lock)
                {
                    // check for valid handle.
                    NodeHandle handle = GetManagerHandle(systemContext, methodToCall.ObjectId, operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    methodToCall.Processed = true;

                    // validate the source node.
                    NodeState source = ValidateNode(systemContext, handle, operationCache);

                    if (source == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }

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
                }

                if (method != null)
                {
                    // call the method.
                    CallMethodResult result = results[ii] = new CallMethodResult();

                    errors[ii] = Call(
                        systemContext,
                        methodToCall,
                        method,
                        result);

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

            // send request to external system.
            try
            {
                Opc.Ua.Client.ISession client = GetClientSession(systemContext);

                CallResponse response = client.CallAsync(
                    null,
                    requests,
                    default).AsTask().Result;

                ResponseHeader responseHeader = response.ResponseHeader;
                ArrayOf<CallMethodResult> results2 = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                // these do sanity checks on the result - make sure response matched the request.
                ClientBase.ValidateResponse<CallMethodRequest, CallMethodResult>((IReadOnlyList<CallMethodResult>)results2.ToArray(), (IReadOnlyList<CallMethodRequest>)requests.ToArray());
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos.ToArray(), requests);

                // set results.
                for (int ii = 0; ii < requests.Count; ii++)
                {
                    results[indexes[ii]] = results2[ii];
                    errors[indexes[ii]] = ServiceResult.Good;

                    if (results2[ii].StatusCode != StatusCodes.Good)
                    {
                        errors[indexes[ii]] = new ServiceResult(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    }
                    else
                    {
                        List<Variant> outputArguments = new List<Variant>();
                        for (int jj = 0; jj < results2[ii].OutputArguments.Count; jj++)
                        {
                            outputArguments.Add(m_mapper.ToLocalVariant(results2[ii].OutputArguments[jj]));
                        }
                        results2[ii].OutputArguments = outputArguments.ToArrayOf();
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
        /// Called when a batch of monitored items has been created.
        /// </summary>
        protected override void OnCreateMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            List<Opc.Ua.Client.MonitoredItem> requests = new List<Opc.Ua.Client.MonitoredItem>();
            List<int> indexes = new List<int>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                if (monitoredItem == null || !Object.ReferenceEquals(monitoredItem.NodeManager, this))
                {
                    continue;
                }

                lock (Lock)
                {
                    // determine if a local node.
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
                    indexes.Add(ii);
                }
            }

            // send request to external system.
            try
            {
                Opc.Ua.Client.ISession client = GetClientSession(context);

                lock (client)
                {
                    // create subscription.
                    if (client.SubscriptionCount == 0)
                    {
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

                        client.AddSubscription(subscription);
                        subscription.CreateAsync().GetAwaiter().GetResult();
                    }

                    // add items.
                    Opc.Ua.Client.Subscription target = null;

                    foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
                    {
                        target = current;
                        break;
                    }

                    for (int ii = 0; ii < requests.Count; ii++)
                    {
                        target.AddItem(requests[ii]);
                    }

                    target.ApplyChangesAsync().GetAwaiter().GetResult();

                    // check status.
                    int index = 0;

                    foreach (Opc.Ua.Client.MonitoredItem monitoredItem in target.MonitoredItems)
                    {
                        if (ServiceResult.IsBad(monitoredItem.Status.Error))
                        {
                            ((MonitoredItem)monitoredItems[indexes[index++]]).QueueValue(DataValue.Null, monitoredItem.Status.Error);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // handle unexpected communication error.
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not access external system.");

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    ((MonitoredItem)monitoredItems[indexes[ii]]).QueueValue(DataValue.Null, error);
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has been modify.
        /// </summary>
        protected override void OnModifyMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            Opc.Ua.Client.ISession client = GetClientSession(context);
            List<Opc.Ua.Client.MonitoredItem> remoteItems = new List<Opc.Ua.Client.MonitoredItem>();

            lock (client)
            {
                Opc.Ua.Client.Subscription target = null;

                foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
                {
                    target = current;
                    break;
                }

                if (target == null)
                {
                    return;
                }

                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                    if (monitoredItem == null || !Object.ReferenceEquals(monitoredItem.NodeManager, this))
                    {
                        continue;
                    }

                    lock (Lock)
                    {
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
                }

                // send request to external system.
                try
                {
                    target.ApplyChangesAsync().GetAwaiter().GetResult();

                    // check status.
                    foreach (Opc.Ua.Client.MonitoredItem monitoredItem in remoteItems)
                    {
                        if (ServiceResult.IsBad(monitoredItem.Status.Error))
                        {
                            ((MonitoredItem)monitoredItem.Handle).QueueValue(DataValue.Null, monitoredItem.Status.Error);
                        }
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
        }

        /// <summary>
        /// Called when a batch of monitored items has been modify.
        /// </summary>
        protected override void OnDeleteMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            Opc.Ua.Client.ISession client = GetClientSession(context);

            lock (client)
            {
                Opc.Ua.Client.Subscription target = null;

                foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
                {
                    target = current;
                    break;
                }

                if (target == null)
                {
                    return;
                }

                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                    if (monitoredItem == null || !Object.ReferenceEquals(monitoredItem.NodeManager, this))
                    {
                        continue;
                    }

                    lock (Lock)
                    {
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
                    }
                }

                // send request to external system.
                try
                {
                    target.ApplyChangesAsync().GetAwaiter().GetResult();

                    if (target.MonitoredItemCount == 0)
                    {
                        client.RemoveSubscriptionAsync(target).GetAwaiter().GetResult();
                    }
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Could not access external system.");
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has their monitoring mode changed.
        /// </summary>
        protected override void OnSetMonitoringModeComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            Opc.Ua.Client.ISession client = GetClientSession(context);
            List<Opc.Ua.Client.MonitoredItem> remoteItems = new List<Opc.Ua.Client.MonitoredItem>();

            lock (client)
            {
                Opc.Ua.Client.Subscription target = null;

                foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
                {
                    target = current;
                    break;
                }

                if (target == null)
                {
                    return;
                }

                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                    if (monitoredItem == null || !Object.ReferenceEquals(monitoredItem.NodeManager, this))
                    {
                        continue;
                    }

                    lock (Lock)
                    {
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
                }

                // send request to external system.
                try
                {
                    target.ApplyChangesAsync().GetAwaiter().GetResult();

                    // check status.
                    foreach (Opc.Ua.Client.MonitoredItem monitoredItem in remoteItems)
                    {
                        if (ServiceResult.IsBad(monitoredItem.Status.Error))
                        {
                            ((MonitoredItem)monitoredItem.Handle).QueueValue(DataValue.Null, monitoredItem.Status.Error);
                        }
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
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced by all event sources.
        /// </summary>
        public override ServiceResult SubscribeToAllEvents(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            return SubscribeToEvents(context, null, subscriptionId, monitoredItem, unsubscribe);
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced an event source.
        /// </summary>
        public override ServiceResult SubscribeToEvents(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
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

                Opc.Ua.Client.ISession client = GetClientSession(systemContext);

                if (unsubscribe)
                {
                    lock (client)
                    {
                        // get the subscription.
                        Opc.Ua.Client.Subscription target = null;

                        foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
                        {
                            target = current;
                            break;
                        }

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
                        target.ApplyChangesAsync().GetAwaiter().GetResult();

                        if (target.MonitoredItemCount == 0)
                        {
                            target.Session.RemoveSubscriptionAsync(target).GetAwaiter().GetResult();
                        }
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

                lock (client)
                {
                    // create subscription.
                    if (client.SubscriptionCount == 0)
                    {
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

                        client.AddSubscription(subscription);
                        subscription.CreateAsync().GetAwaiter().GetResult();
                    }

                    // get the subscription.
                    Opc.Ua.Client.Subscription target = null;

                    foreach (Opc.Ua.Client.Subscription current in client.Subscriptions)
                    {
                        target = current;
                        break;
                    }

                    if (target == null)
                    {
                        return ServiceResult.Good;
                    }

                    target.AddItem(request);
                    target.ApplyChangesAsync().GetAwaiter().GetResult();

                    if (ServiceResult.IsBad(request.Status.Error))
                    {
                        m_logger.LogError("Could not create event item. {Error}", request.Status.Error.ToLongString());
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not access external system.");
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// The delegate used to receive data change notifications via a direct function call instead of a .NET Event.
        /// </summary>
        public void OnDataChangeNotification(Opc.Ua.Client.Subscription subscription, DataChangeNotification notification, ArrayOf<string> stringTable)
        {
            for (int ii = 0; ii < notification.MonitoredItems.Count; ii++)
            {
                MonitoredItem localItem = null;
                DataValue value = DataValue.Null;
                ServiceResult error = null;

                lock (subscription.Session)
                {
                    Opc.Ua.Client.MonitoredItem monitoredItem = subscription.FindItemByClientHandle(notification.MonitoredItems[ii].ClientHandle);

                    if (monitoredItem != null)
                    {
                        MonitoredItemNotification value2 = notification.MonitoredItems[ii];

                        if (value2.Value.StatusCode != StatusCodes.Good)
                        {
                            error = new ServiceResult(value2.Value.StatusCode, value2.DiagnosticInfo, stringTable);
                        }

                        value = new DataValue(
                            m_mapper.ToLocalVariant(value2.Value.WrappedValue),
                            value2.Value.StatusCode,
                            value2.Value.SourceTimestamp,
                            DateTime.UtcNow,
                            value2.Value.SourcePicoseconds,
                            value2.Value.ServerPicoseconds);

                        localItem = (MonitoredItem)monitoredItem.Handle;
                    }
                }

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
                MonitoredItem localItem = null;

                EventFieldList e = null;

                lock (subscription.Session)
                {
                    Opc.Ua.Client.MonitoredItem monitoredItem = subscription.FindItemByClientHandle(notification.Events[ii].ClientHandle);

                    if (monitoredItem != null)
                    {
                        e = notification.Events[ii];

                        List<Variant> eventFields = new List<Variant>();
                        for (int jj = 0; jj < e.EventFields.Count; jj++)
                        {
                            eventFields.Add(m_mapper.ToLocalVariant(e.EventFields[jj]));
                        }
                        e.EventFields = eventFields.ToArrayOf();

                        localItem = (MonitoredItem)monitoredItem.Handle;
                        e.ClientHandle = localItem.ClientHandle;
                    }
                }

                localItem.QueueEvent(e);
            }
        }

        /// <summary>
        /// Get a cached client session or create a new one per server connection.
        /// </summary>
        Opc.Ua.Client.ISession GetClientSession(ServerSystemContext context)
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
                userIdentity = GetMetadataUserIdentity();
            }

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
                            return session;
                        }
                        var reconnectHandler = clientSession.ReconnectHandler;
                        if (reconnectHandler == null)
                        {
                            // the client session is stale and not reconnecting
                            m_clients.Remove(sessionId);
                            session.CloseAsync().GetAwaiter().GetResult();
                            session.Dispose();
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

            try
            {
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("Create Connect Session: {Endpoint} for {SessionName}", m_endpoint, sessionName);
                }
                Opc.Ua.Client.ISession session = new Opc.Ua.Client.DefaultSessionFactory(Server.Telemetry).CreateAsync(
                    m_configuration,
                    m_reverseConnectManager,
                    m_endpoint,
                    m_endpoint.NeedUpdateFromServer(),
                    false,
                    sessionName,
                    m_sessionTimeout,
                    userIdentity,
                    preferredLocales,
                    default(CancellationToken)).Result;

                session.KeepAlive += Client_KeepAlive;
                lock (m_clientsLock)
                {
                    clientSession.Session = session;
                }

                if (context == null)
                {
                    lock (Lock)
                    {
                        m_root.BrowseName = new QualifiedName(m_endpoint.Description.Server.ApplicationName.Text, NamespaceIndex);
                        m_root.DisplayName = new LocalizedText(m_root.BrowseName.Name);
                        m_root.ClearChangeMasks(SystemContext, false);

                        m_status.EndpointUrl.Value = m_endpoint.EndpointUrl.ToString();
                        m_status.Status.Value = StatusCodes.Good;
                        m_status.ConnectTime.Value = DateTime.UtcNow;
                        m_status.ClearChangeMasks(SystemContext, true);
                    }
                }
                return session;
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
                    lock (Lock)
                    {
                        const int ErrorMessageLength = 30;
                        var messageLength = e.InnerException.Message.Length;
                        var trimmedMessage = e.InnerException.Message.Substring(0, Math.Min(messageLength, ErrorMessageLength));
                        if (messageLength > ErrorMessageLength)
                        {
                            trimmedMessage += "...";
                        }
                        m_root.DisplayName = new LocalizedText(m_endpoint.EndpointUrl.ToString() + $" Status: ({trimmedMessage})");
                        m_root.ClearChangeMasks(SystemContext, false);

                        m_status.EndpointUrl.Value = m_endpoint.EndpointUrl.ToString();
                        m_status.Status.Value = StatusCodes.BadNotConnected;
                        m_status.ConnectTime.Value = DateTime.MinValue;
                        m_status.ClearChangeMasks(SystemContext, true);
                    }
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
        private IUserIdentity GetMetadataUserIdentity()
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
                        return UserIdentity.CreateAsync(
                            applicationCertificate,
                            m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                            m_configuration.CertificateManager.CertificateProvider,
                            default(CancellationToken)).GetAwaiter().GetResult();
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
        /// Starts updating the metadata.
        /// </summary>
        private void StartMetadataUpdates(WaitCallback callback, object callbackData, int initialDelay, int period)
        {
            lock (Lock)
            {
                CleanupTimer();
                m_metadataUpdateCallback = callback;
                m_timerPeriod = period;
                m_initialDelay = initialDelay;
                m_metadataUpdateTimer = new Timer(DoMetadataUpdateAsync, callbackData, initialDelay, -1);
            }
        }

        /// <summary>
        /// Cleanup the meta data timer.
        /// </summary>
        private void CleanupTimer()
        {
            lock (Lock)
            {
                if (m_metadataUpdateTimer != null)
                {
                    m_metadataUpdateTimer.Dispose();
                    m_metadataUpdateTimer = null;
                }
            }
        }

        /// <summary>
        /// Updates the metadata.
        /// </summary>
        private async void DoMetadataUpdateAsync(object state)
        {
            int nextTimerPeriod = m_initialDelay;
            Opc.Ua.Client.ISession client = null;
            try
            {
                if (!Server.IsRunning)
                {
                    return;
                }

                client = GetClientSession(null);

                if (client == null)
                {
                    return;
                }

                string[] TypeSystemNamespaceUris = new string[]
                {
                    "http://opcfoundation.org/UA/Diagnostics"
                };

                // The server owns its diagnostics lock and no longer exposes it; this
                // section does not touch the diagnostics summary it guarded.
                ushort[] namespaceIndexes = null;
                lock (Lock)
                {
                    var mapper = new NamespaceMapper();
                    mapper.TypeSystemNamespaceUris = TypeSystemNamespaceUris;
                    mapper.Initialize(Server.NamespaceUris, client.NamespaceUris, m_endpoint.Description.Server.ApplicationUri);

                    // set the namespace indexes.
                    namespaceIndexes = new ushort[mapper.LocalNamespaceIndexes.Length + ((m_ownsTypeModel) ? 1 : 0)];

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
                }

                // re-register node manager.
                for (int ii = 0; ii < namespaceIndexes.Length; ii++)
                {
                    Server.NodeManager.RegisterNamespaceManager(Server.NamespaceUris.GetString(namespaceIndexes[ii]), this);
                }

                AggregatedTypeCache cache = new AggregatedTypeCache();
                await cache.LoadTypesAsync(client, Server, m_mapper);

                lock (Lock)
                {
                    // update cache.
                    if (m_typeCache == null)
                    {
                        m_typeCache = cache;
                    }

                    m_typeCache.TypeNodes = cache.TypeNodes;
                }

                nextTimerPeriod = m_timerPeriod;
                m_typeCacheInitialized = true;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating event type cache.");
            }
            finally
            {
                lock (Lock)
                {
                    m_metadataUpdateTimer.Change(nextTimerPeriod, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
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
            NodeState target = FindNodeInCache(context, handle, cache);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            try
            {
                Opc.Ua.Client.ISession client = GetClientSession(context);

                // get remote node.
                NodeId targetId = m_mapper.ToRemoteId(handle.NodeId);
                ILocalNode node = Opc.Ua.Client.SessionClientExtensions.ReadNodeAsync(client, targetId).GetAwaiter().GetResult();

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
        /// Used to receive notifications when a node browser is created.
        /// </summary>
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
            Browser browser = new Browser(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                null,
                false,
                GetClientSession(context as ServerSystemContext),
                m_mapper,
                Object.ReferenceEquals(node, m_root) ? null : node,
                m_root.NodeId);

            return browser;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            var assy = this.GetType().GetTypeInfo().Assembly;
            var name = assy.GetName().Name + ".Model.AggregationModel.PredefinedNodes.uanodes";
            predefinedNodes.LoadFromBinaryResource(context, name, assy, true);
            return predefinedNodes;
        }
        #endregion

        #region Private Methods
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
            _ = Task.Run(() => CloseDownstreamSession(clientSessionId, clientSession));
        }

        /// <summary>
        /// Closes and disposes the downstream session associated with a client session.
        /// </summary>
        private void CloseDownstreamSession(NodeId clientSessionId, AggregationClientSession clientSession)
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
                        session.CloseAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        session.Dispose();
                    }
                }

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
                    m_logger.LogInformation("--- RECONNECTED --- SessionId: {SessionId} but client session was not found.", clientSession.ClientSessionId);
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
        private bool m_ownsTypeModel;
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpoint m_endpoint;
        private Opc.Ua.Client.ReverseConnectManager m_reverseConnectManager;
        private Dictionary<NodeId, AggregationClientSession> m_clients;
        private object m_clientsLock;
        private Opc.Ua.Server.ISessionManager m_sessionManager;
        private AggregatedTypeCache m_typeCache;
        private bool m_typeCacheInitialized;
        // Justification: disposed via Utils.SilentDispose in Dispose(bool); analyzer does not recognize the helper.
#pragma warning disable CA2213
        private Timer m_metadataUpdateTimer;
        private int m_timerPeriod;
        private int m_initialDelay;
        private uint m_sessionTimeout;
        private WaitCallback m_metadataUpdateCallback;
        private NamespaceMapper m_mapper;
        private FolderState m_root;
        private AggregationModel.AggregatedServerStatusState m_status;
#pragma warning restore CA2213
        #endregion
    }
}
