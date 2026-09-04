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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// A node manager for a server that exposes a file based archive of recorded
    /// values through the history services.
    /// </summary>
    /// <remarks>
    /// The address space is built by hand from the archive folders and files. The
    /// history services themselves are not implemented here any more: the manager
    /// registers an <see cref="ArchiveHistorianProvider"/> for its namespace, and
    /// the <see cref="AsyncCustomNodeManager"/> base class routes every HistoryRead
    /// and HistoryUpdate through the SDK's historian dispatcher to that provider.
    /// </remarks>
    public class HistoricalAccessServerNodeManager : AsyncCustomNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public HistoricalAccessServerNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.HistoricalAccess)
        {
            this.AliasRoot = "HDA";

            // the clock of the server, so that the simulated history and the timestamps it
            // writes run on the same time source as the rest of the server and a test can
            // drive them with a FakeTimeProvider. ITimeProviderProvider is the opt-in seam
            // for reaching it; an IServerInternal which does not implement it falls back
            // to the system clock.
            m_timeProvider = (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<HistoricalAccessServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new HistoricalAccessServerConfiguration();
            }

            SystemContext.SystemHandle = m_system = new UnderlyingSystem(m_configuration, NamespaceIndex);
            SystemContext.NodeIdFactory = this;

            // the provider serves the archive through the SDK's native historian
            // interfaces; the address space registers it when it is created.
            m_historian = new ArchiveHistorianProvider(server, m_system);
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
                m_simulationTimer?.Dispose();
                m_simulationTimer = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        /// <remarks>
        /// This method is called by the NodeState.Create() method which initializes a Node from
        /// the type model. During initialization a number of child nodes are created and need to
        /// have NodeIds assigned to them. This implementation constructs NodeIds by constructing
        /// strings. Other implementations could assign unique integers or Guids and save the new
        /// Node in a dictionary for later lookup.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                return NodeTypes.ConstructIdForComponent(instance, instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        public override async ValueTask CreateAddressSpaceAsync(IDictionary<NodeId, IList<IReference>> externalReferences, CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // register the historian for every node of the namespace. the base class
            // resolves it through this registry when it dispatches the history
            // services, and the diagnostics node manager rolls the provider
            // capabilities up into the HistoryServerCapabilities node - which this
            // method used to populate by hand - once every address space exists.
            Server.UseHistorian()
                .UseProvider(m_historian)
                .RegisterForNamespace(Namespaces.HistoricalAccess);

            IList<IReference> references = null;

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

#pragma warning disable CA2000 // Justification: ownership is transferred to the address space/predefined node collection.
            ArchiveFolderState root = m_system.GetFolderState(SystemContext, String.Empty);
#pragma warning restore CA2000
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

            CreateFolderFromResources(root, "Sample");
            CreateFolderFromResources(root, "Dynamic");
        }

        /// <summary>
        /// Creates items from embedded resources.
        /// </summary>
        private void CreateFolderFromResources(NodeState root, string folderName)
        {
#pragma warning disable CA2000 // Justification: ownership is transferred to the address space/predefined node collection.
            FolderState dataFolder = new FolderState(root);
#pragma warning restore CA2000
            dataFolder.ReferenceTypeId = ReferenceTypeIds.Organizes;
            dataFolder.TypeDefinitionId = ObjectTypeIds.FolderType;
            dataFolder.NodeId = new NodeId(folderName, NamespaceIndex);
            dataFolder.BrowseName = new QualifiedName(folderName, NamespaceIndex);
            dataFolder.DisplayName = new LocalizedText(dataFolder.BrowseName.Name);
            dataFolder.WriteMask = AttributeWriteMask.None;
            dataFolder.UserWriteMask = AttributeWriteMask.None;
            dataFolder.EventNotifier = EventNotifiers.None;
            root.AddChild(dataFolder);
            AddPredefinedNodeSynchronously(root);

            foreach (string resourcePath in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (!resourcePath.StartsWith("Quickstarts.HistoricalAccessServer.Data." + folderName, StringComparison.Ordinal))
                {
                    continue;
                }

                ArchiveItem item = new ArchiveItem(resourcePath, Assembly.GetExecutingAssembly(), resourcePath);
#pragma warning disable CA2000 // Justification: ownership is transferred to the address space/predefined node collection.
                ArchiveItemState node = new ArchiveItemState(SystemContext, item, NamespaceIndex);
#pragma warning restore CA2000
                node.ReloadFromSource(SystemContext, Server.Telemetry);

                // register with the underlying system so the historian resolves the
                // item - and its capabilities - by node id like any other.
                m_system.RegisterItemState(node);

                dataFolder.AddReference(ReferenceTypeIds.Organizes, false, node.NodeId);
                node.AddReference(ReferenceTypeIds.Organizes, true, dataFolder.NodeId);

                AddPredefinedNodeSynchronously(node);
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override async ValueTask<NodeHandle> GetManagerHandleAsync(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache, CancellationToken cancellationToken = default)
        {
            // check for predefined nodes.
            NodeHandle handle = await base.GetManagerHandleAsync(context, nodeId, cache, cancellationToken).ConfigureAwait(false);

            if (handle != null)
            {
                return handle;
            }

            // quickly exclude nodes that are not in the namespace.
            if (!IsNodeIdInNamespace(nodeId))
            {
                return null;
            }

            // check for nodes that are being currently monitored.
            if (MonitoredNodes.TryGetValue(nodeId, out MonitoredNode2 monitoredNode))
            {
                return new NodeHandle {
                    NodeId = nodeId,
                    Validated = true,
                    Node = monitoredNode.Node
                };
            }

            // parse the identifier.
            ParsedNodeId parsedNodeId = ParsedNodeId.Parse(nodeId);

            if (parsedNodeId != null)
            {
                return new NodeHandle {
                    NodeId = nodeId,
                    Validated = false,
                    Node = null,
                    ParsedNodeId = parsedNodeId
                };
            }

            return null;
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the node cache/handle for the operation.")]
        protected override async ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            if (handle == null)
            {
                return null;
            }

            // lookup in cache.
            NodeState target = await FindNodeInCacheAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            ParsedNodeId pnd = handle.ParsedNodeId as ParsedNodeId;

            if (pnd == null)
            {
                return null;
            }

            // check for a new node.
            try
            {
                lock (m_system.SyncRoot)
                {
                    switch (pnd.RootType)
                    {
                        case NodeTypes.Folder:
                        {
                            target = m_system.GetFolderState(SystemContext, pnd.RootId);
                            break;
                        }

                        case NodeTypes.Item:
                        {
                            ArchiveItemState item = m_system.GetItemState(SystemContext, pnd);
                            item.LoadConfiguration(context, Server.Telemetry);
                            target = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // a node id can parse as an item without a file behind it.
                m_logger.LogError(e, "Could not load the archive behind {NodeId}.", handle.NodeId);
                return null;
            }

            // root is not valid.
            if (target == null)
            {
                return null;
            }

            // validate component.
            if (!String.IsNullOrEmpty(pnd.ComponentPath))
            {
                NodeState component = target.FindChildBySymbolicName(context, pnd.ComponentPath);

                // component does not exist.
                if (component == null)
                {
                    return null;
                }

                target = component;
            }

            // put root into cache.
            if (cache != null)
            {
                cache[handle.NodeId] = target;
            }

            handle.Node = target;
            handle.Validated = true;
            return handle.Node;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Validates the nodes and reads the values from the underlying source.
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
                DataValue value = values[handle.Index];

                lock (m_system.SyncRoot)
                {
                    // check if the node needs to be initialized from disk.
                    ArchiveItemState item = source.GetHierarchyRoot() as ArchiveItemState;

                    if (item != null && item.ArchiveItem.LastLoadTime.AddMinutes(10) < m_timeProvider.GetUtcNow().UtcDateTime)
                    {
                        item.LoadConfiguration(context, Server.Telemetry);
                    }

                    // update the attribute value. the read happens under the archive
                    // lock so the simulation cannot change the value fields halfway
                    // through it.
#pragma warning disable CA1849 // Justification: the lock cannot be held across an await and the node lives in memory, so the synchronous read does not block.
                    errors[handle.Index] = source.ReadAttribute(
                        context,
                        nodeToRead.AttributeId,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        ref value);
#pragma warning restore CA1849
                }

                values[handle.Index] = value;
            }
        }

        /// <summary>
        /// Reads the initial value for a monitored item.
        /// </summary>
        /// <remarks>
        /// A monitored item with an aggregate filter whose start time lies in the
        /// past is primed with the recorded values so the aggregates cover the
        /// requested window; everything else takes the current value as usual.
        /// </remarks>
        protected override ServiceResult ReadInitialValue(
            ISystemContext context,
            NodeHandle handle,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null || monitoredItem.AttributeId != Attributes.Value)
            {
                return base.ReadInitialValue(context, handle, monitoredItem);
            }

            MonitoredItem sampledItem = monitoredItem as MonitoredItem;
            AggregateFilter filter = sampledItem?.Filter as AggregateFilter;

            if (filter == null || filter.StartTime >= m_timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(-filter.ProcessingInterval))
            {
                return base.ReadInitialValue(context, handle, monitoredItem);
            }

            try
            {
                foreach (DataValue value in m_historian.ReadRawWindow(SystemContext, item, (DateTime)filter.StartTime, m_timeProvider.GetUtcNow().UtcDateTime))
                {
                    sampledItem.QueueValue(value, ServiceResult.Good);
                }

                return StatusCodes.Good;
            }
            catch (Exception e)
            {
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error fetching initial values.");
                sampledItem.QueueValue(DataValue.Null, error);
                return error;
            }
        }

        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        protected override void OnMonitoredItemCreated(ServerSystemContext context, NodeHandle handle, ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_system.SyncRoot)
            {
                if (handle.Node.GetHierarchyRoot() is ArchiveItemState item)
                {
                    if (m_monitoredItems == null)
                    {
                        m_monitoredItems = new Dictionary<string, ArchiveItemState>();
                    }

                    m_monitoredItems.TryAdd(item.ArchiveItem.UniquePath, item);
                    item.SubscribeCount++;

                    if (m_simulationTimer == null)
                    {
                        m_simulationTimer = m_timeProvider.CreateTimer(
                            DoSimulation,
                            null,
                            TimeSpan.FromMilliseconds(500),
                            TimeSpan.FromMilliseconds(500));
                    }
                }
            }
        }

        /// <summary>
        /// Revises an aggregate filter (may require knowledge of the variable being used).
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="samplingInterval">The sampling interval for the monitored item.</param>
        /// <param name="queueSize">The queue size for the monitored item.</param>
        /// <param name="filterToUse">The filter to revise.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Good if the filter is acceptable.</returns>
        protected override ValueTask<StatusCode> ReviseAggregateFilterAsync(
            ServerSystemContext context,
            NodeHandle handle,
            double samplingInterval,
            uint queueSize,
            ServerAggregateFilter filterToUse,
            CancellationToken cancellationToken = default)
        {
            // a processing interval of zero would keep the start-time alignment
            // below spinning forever.
            if (filterToUse.ProcessingInterval <= 0)
            {
                filterToUse.ProcessingInterval = Math.Max(samplingInterval, 1000);
            }

            // use the sampling interval to limit the processing interval.
            if (filterToUse.ProcessingInterval < samplingInterval)
            {
                filterToUse.ProcessingInterval = samplingInterval;
            }

            // check if an archive item.
            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null)
            {
                // no historial data so must start in the future.
                while (filterToUse.StartTime < m_timeProvider.GetUtcNow().UtcDateTime)
                {
                    filterToUse.StartTime = filterToUse.StartTime.AddMilliseconds(filterToUse.ProcessingInterval);
                }

                // use suitable defaults for values which are are not archived items.
                filterToUse.AggregateConfiguration.UseServerCapabilitiesDefaults = false;
                filterToUse.AggregateConfiguration.UseSlopedExtrapolation = false;
                filterToUse.AggregateConfiguration.TreatUncertainAsBad = false;
                filterToUse.AggregateConfiguration.PercentDataBad = 100;
                filterToUse.AggregateConfiguration.PercentDataGood = 100;
                filterToUse.Stepped = true;

                return new ValueTask<StatusCode>((StatusCode)StatusCodes.Good);
            }

            // the item settings this reads are rewritten whenever the item reloads
            // from its source, so they are read under the archive lock.
            lock (m_system.SyncRoot)
            {
                // use the archive acquisition sampling interval to limit the processing interval.
                if (filterToUse.ProcessingInterval < item.ArchiveItem.SamplingInterval)
                {
                    filterToUse.ProcessingInterval = item.ArchiveItem.SamplingInterval;
                }

                // ensure the buffer does not get overfilled.
                while (filterToUse.StartTime.AddMilliseconds(queueSize * filterToUse.ProcessingInterval) < m_timeProvider.GetUtcNow().UtcDateTime)
                {
                    filterToUse.StartTime = filterToUse.StartTime.AddMilliseconds(filterToUse.ProcessingInterval);
                }

                filterToUse.Stepped = item.ArchiveItem.Stepped;

                // revise the configration.
                m_historian.ReviseAggregateConfiguration(item, filterToUse.AggregateConfiguration);
            }

            return new ValueTask<StatusCode>((StatusCode)StatusCodes.Good);
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        protected override async ValueTask OnMonitoredItemDeletedAsync(ServerSystemContext context, NodeHandle handle, ISampledDataChangeMonitoredItem monitoredItem, CancellationToken cancellationToken = default)
        {
            ITimer timerToDispose = null;

            lock (m_system.SyncRoot)
            {
                if (handle.Node.GetHierarchyRoot() is ArchiveItemState item &&
                    m_monitoredItems != null &&
                    m_monitoredItems.TryGetValue(item.ArchiveItem.UniquePath, out ArchiveItemState monitoredItemState))
                {
                    monitoredItemState.SubscribeCount--;

                    if (monitoredItemState.SubscribeCount == 0)
                    {
                        m_monitoredItems.Remove(item.ArchiveItem.UniquePath);
                    }

                    if (m_monitoredItems.Count == 0)
                    {
                        timerToDispose = m_simulationTimer;
                        m_simulationTimer = null;
                    }
                }
            }

            if (timerToDispose != null)
            {
                await timerToDispose.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Refuses aggregates the server has no calculator for.
        /// </summary>
        /// <remarks>
        /// The historian provider computes aggregates itself so the stepped flag and
        /// the aggregate configuration of each archive item are honoured, but a
        /// provider read has no way to report a per-operation error, so unsupported
        /// aggregates are refused here before the dispatch.
        /// </remarks>
        protected override ValueTask HistoryReadProcessedAsync(
            ServerSystemContext context,
            ReadProcessedDetails details,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            List<NodeHandle> supported = new List<NodeHandle>(nodesToProcess.Count);

            foreach (NodeHandle handle in nodesToProcess)
            {
                if (!Server.AggregateManager.IsSupported(details.AggregateType[handle.Index]))
                {
                    errors[handle.Index] = StatusCodes.BadAggregateNotSupported;
                    continue;
                }

                supported.Add(handle);
            }

            return base.HistoryReadProcessedAsync(context, details, timestampsToReturn, nodesToRead, results, errors, supported, cache, cancellationToken);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Runs the simulation.
        /// </summary>
        private void DoSimulation(object state)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    foreach (ArchiveItemState item in m_monitoredItems.Values)
                    {
                        if (item.ArchiveItem.LastLoadTime.AddSeconds(10) < m_timeProvider.GetUtcNow().UtcDateTime)
                        {
                            item.LoadConfiguration(SystemContext, Server.Telemetry);
                        }

                        foreach (DataValue value in item.NewSamples(SystemContext))
                        {
                            item.WrappedValue = value.WrappedValue;
                            item.Timestamp = value.SourceTimestamp;
                            item.StatusCode = value.StatusCode;
                            item.ClearChangeMasks(SystemContext, true);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError("Unexpected error during simulation: {Message}", e.Message);
            }
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        private HistoricalAccessServerConfiguration m_configuration;
        private ArchiveHistorianProvider m_historian;
        private readonly TimeProvider m_timeProvider;
        private ITimer m_simulationTimer;
        private Dictionary<string, ArchiveItemState> m_monitoredItems;
        #endregion
    }

    /// <summary>
    /// The factory the server registers to create the node manager on startup.
    /// </summary>
    public class HistoricalAccessNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => new ArrayOf<string>(new string[] { Namespaces.HistoricalAccess });

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership is transferred to the master node manager.
            return new ValueTask<IAsyncNodeManager>(
                new HistoricalAccessServerNodeManager(server, configuration));
#pragma warning restore CA2000
        }
    }
}
