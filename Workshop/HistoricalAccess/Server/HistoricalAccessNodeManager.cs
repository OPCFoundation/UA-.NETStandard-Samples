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
using Opc.Ua.Server.Historian.InMemory;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// A node manager for a server that exposes a file based archive of recorded
    /// values through the history services, next to a handful of live variables
    /// whose history the SDK captures for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The address space is built by hand. The history services themselves are not
    /// implemented here: the <see cref="AsyncCustomNodeManager"/> base class routes
    /// every HistoryRead and HistoryUpdate through the historian dispatcher of the
    /// SDK, which resolves a provider for the node the request names.
    /// </para>
    /// <para>
    /// Two providers serve this one namespace, which is what makes the resolution
    /// order visible. The <c>Sample</c> and <c>Dynamic</c> folders hold archive items
    /// backed by files and served by the <see cref="ArchiveHistorianProvider"/> of the
    /// sample, registered for the namespace. The <c>Live</c> folder holds variables
    /// backed by nothing but the in-memory engine which ships with the SDK, registered
    /// as the default of the server and, because they share the namespace of the
    /// archive, for each of them by node id - a node binding wins over a namespace
    /// binding, which wins over the default. Before the registry is even asked, the
    /// <see cref="GetHistorianProvider"/> override of this manager answers for the
    /// nodes it recognises as its own archive items.
    /// </para>
    /// <para>
    /// The live variables are the quick-start path of the SDK: nothing is written for
    /// them but the variable itself. <c>HistorizeAsync</c> installs their
    /// <c>HistoricalDataConfiguration</c> companion object from the capabilities the
    /// provider reports, and every value the simulation publishes - or a client
    /// writes - is captured into the archive on its way through
    /// <see cref="NodeState.ClearChangeMasks"/>. One of them stores structured
    /// history: several readings at one instant, told apart by a key inside the
    /// value.
    /// </para>
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
        /// <remarks>
        /// The historian builders are not disposed here. They registered themselves
        /// with the server when they were created, and the server drains their
        /// capture pipelines - the samples still queued for the live variables - when
        /// it shuts down.
        /// </remarks>
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
        ///
        /// The HistoricalDataConfiguration objects the SDK installs beside the historized
        /// variables - and the properties below them - come through here as well.
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

            // the file archive, registered for every node of the namespace. the base
            // class resolves it through this registry when it dispatches the history
            // services, and the diagnostics node manager rolls the capabilities of
            // every registered provider up into the HistoryServerCapabilities node.
            // this has to happen here: once every address space exists the server
            // reconciles what each variable advertises against the providers which
            // are registered, and clears the history bits of a variable nobody
            // answers for.
            m_archiveHistorian = Server.UseHistorian()
                .UseProvider(m_historian)
                .RegisterForNamespace(Namespaces.HistoricalAccess);

            // the in-memory engine of the SDK, registered as the default of the server:
            // it answers for any historizing node no other binding claims. the live
            // variables below share the namespace of the archive, so each of them is
            // also bound by node id, which is the binding which wins.
            m_liveHistorian = Server.UseHistorian()
                .UseInMemoryProvider(new InMemoryHistorianOptions {
                    RawDataRetentionPeriod = TimeSpan.FromHours(1),
                    DefaultCapabilities = s_liveCapabilities
                })
                .RegisterAsDefault();

            m_liveProvider = (InMemoryHistorianProvider)m_liveHistorian.Provider;

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

            await CreateFolderFromResourcesAsync(root, "Sample", cancellationToken).ConfigureAwait(false);
            await CreateFolderFromResourcesAsync(root, "Dynamic", cancellationToken).ConfigureAwait(false);
            await CreateLiveFolderAsync(root, cancellationToken).ConfigureAwait(false);

            // the simulation runs for as long as the server does: the live variables
            // publish whether or not anybody is watching, which is what makes their
            // captured history worth reading when a client turns up.
            m_simulationTimer = m_timeProvider.CreateTimer(
                DoSimulation,
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Creates a folder, linked below the root of the archive.
        /// </summary>
        private FolderState CreateFolder(NodeState root, string folderName)
        {
#pragma warning disable CA2000 // Justification: ownership is transferred to the address space/predefined node collection.
            FolderState folder = new FolderState(root);
#pragma warning restore CA2000
            folder.ReferenceTypeId = ReferenceTypeIds.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;
            folder.NodeId = new NodeId(folderName, NamespaceIndex);
            folder.BrowseName = new QualifiedName(folderName, NamespaceIndex);
            folder.DisplayName = new LocalizedText(folder.BrowseName.Name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;
            root.AddChild(folder);
            AddPredefinedNodeSynchronously(root);

            return folder;
        }

        /// <summary>
        /// Creates items from embedded resources.
        /// </summary>
        private async ValueTask CreateFolderFromResourcesAsync(NodeState root, string folderName, CancellationToken cancellationToken)
        {
            FolderState dataFolder = CreateFolder(root, folderName);

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

                // install the companion object which describes the recording, before
                // the item and its children are added to the address space.
                await HistorizeArchiveItemAsync(node, cancellationToken).ConfigureAwait(false);

                dataFolder.AddReference(ReferenceTypeIds.Organizes, false, node.NodeId);
                node.AddReference(ReferenceTypeIds.Organizes, true, dataFolder.NodeId);

                AddPredefinedNodeSynchronously(node);
            }
        }

        /// <summary>
        /// Hands an archive item to the historian builder of the SDK.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What the builder does with it: it makes sure the item advertises its
        /// history, and it installs the <c>HistoricalDataConfigurationType</c>
        /// companion object from what the provider reports for the item - the
        /// stepped flag, the sampling interval, the start of the archive and the
        /// aggregate configuration, all of which come from the archive file.
        /// </para>
        /// <para>
        /// What it deliberately does not do: it leaves the Historizing attribute
        /// alone (<c>setHistorizing: false</c>) because the archive owns it - a
        /// finished recording says false, a recording still being appended to says
        /// true - and it attaches no automatic capture, because the archive fills
        /// itself from its files. The fluent builder of a source-generated manager
        /// spells the same choice <c>historizing: null</c>.
        /// </para>
        /// </remarks>
        private async ValueTask HistorizeArchiveItemAsync(ArchiveItemState item, CancellationToken cancellationToken)
        {
            if (item.IsHistorized)
            {
                return;
            }

            await m_archiveHistorian.HistorizeAsync(
                item,
                SystemContext,
                setHistorizing: false,
                autoCapture: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            item.IsHistorized = true;
        }

        /// <summary>
        /// Creates the live variables, whose history the SDK keeps for the sample.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Temperature</c> is what the simulation publishes once a second, and is
        /// the plain case: a scalar with a history the capture pipeline fills.
        /// <c>Setpoint</c> is written by clients, and shows that a Write through the
        /// service is captured the same way. <c>LabSample</c> holds structured
        /// history: every few seconds the simulation records three readings of one
        /// sample at the same instant, each a <see cref="KeyValuePair"/>, and the
        /// key inside the value is what tells them apart in the archive.
        /// </para>
        /// <para>
        /// Each of them is historized with the capabilities the provider should
        /// report for it. That is where the companion object gets its values from,
        /// and where the dispatcher reads what it may let a client do.
        /// </para>
        /// </remarks>
        private async ValueTask CreateLiveFolderAsync(NodeState root, CancellationToken cancellationToken)
        {
            FolderState liveFolder = CreateFolder(root, "Live");

            m_temperature = CreateLiveVariable(liveFolder, "Temperature", DataTypeIds.Double, Variant.From(20.0));
            m_temperature.Description = new LocalizedText("A simulated reading; every published value is captured into the history.");

            await HistorizeLiveVariableAsync(
                m_temperature,
                s_liveCapabilities with {
                    Definition = "Simulated, captured from the published value",
                    StartOfOnlineArchive = m_timeProvider.GetUtcNow().UtcDateTime
                },
                cancellationToken).ConfigureAwait(false);

            m_setpoint = CreateLiveVariable(liveFolder, "Setpoint", DataTypeIds.Double, Variant.From(50.0));
            m_setpoint.Description = new LocalizedText("Written by clients; every write is captured into the history.");
            m_setpoint.AccessLevel |= AccessLevels.CurrentWrite;
            m_setpoint.UserAccessLevel |= AccessLevels.CurrentWrite;

            await HistorizeLiveVariableAsync(
                m_setpoint,
                s_liveCapabilities with {
                    Definition = "Captured from the writes of clients",
                    StartOfOnlineArchive = m_timeProvider.GetUtcNow().UtcDateTime
                },
                cancellationToken).ConfigureAwait(false);

            m_labSample = CreateLiveVariable(liveFolder, "LabSample", DataTypeIds.KeyValuePair, Variant.Null);
            m_labSample.Description = new LocalizedText("Several readings per instant, told apart by the key of the value.");

            // the archive of a structured variable keys its entries by the source
            // timestamp and the Key of the KeyValuePair, so that three readings of
            // one sample can share the instant it was taken at.
            m_liveProvider.RegisterStructured(
                m_labSample.NodeId,
                KeyValuePairStructuredDataKeySelector.Instance,
                s_structuredCapabilities);

            await HistorizeLiveVariableAsync(
                m_labSample,
                s_structuredCapabilities with {
                    StartOfOnlineArchive = m_timeProvider.GetUtcNow().UtcDateTime
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates one live variable with the Annotations property beside it.
        /// </summary>
        /// <remarks>
        /// The builder would create the Annotations property itself for a provider
        /// which accepts annotations, but it names it after the node id of the
        /// variable; building it here gives it a node id of the sample's own scheme,
        /// and the builder reuses what it finds.
        /// </remarks>
        private BaseDataVariableState CreateLiveVariable(NodeState parent, string name, NodeId dataType, Variant initialValue)
        {
#pragma warning disable CA2000 // Justification: ownership is transferred to the address space/predefined node collection.
            BaseDataVariableState variable = new BaseDataVariableState(parent);
#pragma warning restore CA2000
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.SymbolicName = name;
            variable.NodeId = new NodeId("Live/" + name, NamespaceIndex);
            variable.BrowseName = new QualifiedName(name, NamespaceIndex);
            variable.DisplayName = new LocalizedText(name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.DataType = dataType;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            variable.WrappedValue = initialValue;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = m_timeProvider.GetUtcNow().UtcDateTime;

            PropertyState annotations = new PropertyState(variable);
            annotations.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            annotations.TypeDefinitionId = VariableTypeIds.PropertyType;
            annotations.SymbolicName = Opc.Ua.BrowseNames.Annotations;
            annotations.BrowseName = new QualifiedName(Opc.Ua.BrowseNames.Annotations);
            annotations.DisplayName = new LocalizedText(annotations.BrowseName.Name);
            annotations.DataType = DataTypeIds.Annotation;
            annotations.ValueRank = ValueRanks.OneDimension;
            annotations.AccessLevel = AccessLevels.HistoryReadOrWrite;
            annotations.UserAccessLevel = AccessLevels.HistoryReadOrWrite;
            annotations.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            annotations.Historizing = false;
            variable.AddChild(annotations);
            annotations.NodeId = NodeTypes.ConstructIdForComponent(annotations, NamespaceIndex);

            parent.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Historizes a live variable on the in-memory engine.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the whole of what the quick-start path asks of a server. The
        /// builder registers the variable with the engine, sets its history access
        /// bits and its Historizing attribute (<c>setHistorizing: true</c>, the
        /// default), installs the <c>HistoricalDataConfiguration</c> companion object
        /// from the capabilities, and - because <c>autoCapture</c> is left on -
        /// attaches the handler which forwards every value change into the capture
        /// sink. The sink batches the samples of all the variables of this builder
        /// and flushes them to the engine through its bulk insert interface; the
        /// options make it flush after a handful of samples or a tenth of a second,
        /// whichever comes first, so a reader sees a value in the history soon after
        /// it was published.
        /// </para>
        /// <para>
        /// The node binding after it is what routes the variable to this engine
        /// rather than to the archive provider which is registered for the whole
        /// namespace.
        /// </para>
        /// </remarks>
        private async ValueTask HistorizeLiveVariableAsync(
            BaseDataVariableState variable,
            HistorianNodeCapabilities capabilities,
            CancellationToken cancellationToken)
        {
            await m_liveHistorian.HistorizeAsync(
                variable,
                SystemContext,
                capabilities: capabilities,
                captureOptions: s_captureOptions,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            m_liveHistorian.RegisterForNode(variable.NodeId);

            AddPredefinedNodeSynchronously(variable);
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

            // an item behind a file in the archive root is materialized on its first
            // use, and gets its companion object then. the provider takes the archive
            // lock itself, so this runs outside of it.
            if (target is ArchiveItemState created && !created.IsHistorized)
            {
                await HistorizeArchiveItemAsync(created, cancellationToken).ConfigureAwait(false);
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
        /// Answers for the history of the nodes this manager recognises as its own,
        /// before the registry of the server is asked.
        /// </summary>
        /// <remarks>
        /// This is the first step of the resolution order of the dispatcher: a
        /// non-null answer here is final, null falls through to the registry - the
        /// node bindings of the live variables, then the namespace binding of the
        /// archive, then the default. An archive item can only ever be served by
        /// the archive provider, so the manager says so itself and saves the
        /// registry the lookup; everything else is left to the registry, which is
        /// where the live variables find their engine.
        /// </remarks>
        protected override IHistorianProvider GetHistorianProvider(NodeState node)
        {
            return node is ArchiveItemState ? m_historian : null;
        }

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

        // an aggregate filter on a monitored item is revised by the base class from
        // what the historian of the node reports: the stepped flag, the sampling
        // interval as the smallest processing interval, and the aggregate
        // configuration the node is computed with. a start time in the past is
        // primed from the raw history of the node before live values follow. the
        // sample used to do both by hand, and no longer has to.

        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        /// <remarks>
        /// The simulation of an archive item only appends to it while somebody is
        /// monitoring it; the live variables publish regardless.
        /// </remarks>
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
                }
            }
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        protected override ValueTask OnMonitoredItemDeletedAsync(ServerSystemContext context, NodeHandle handle, ISampledDataChangeMonitoredItem monitoredItem, CancellationToken cancellationToken = default)
        {
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
                }
            }

            return default;
        }

        // a processed read for an aggregate the server has no calculator for is
        // refused by the dispatcher with BadAggregateNotSupported before any provider
        // is reached, so the manager no longer filters those requests itself.
        #endregion

        #region Private Methods
        /// <summary>
        /// Runs the simulation.
        /// </summary>
        /// <remarks>
        /// The archive items append their next samples to their own data sets. The
        /// live variables just publish: setting the value, the timestamp and the
        /// status and clearing the change masks is what fires the state change the
        /// capture pipeline of the SDK listens to, and nothing here writes to their
        /// history directly.
        /// </remarks>
        private void DoSimulation(object state)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    if (m_monitoredItems != null)
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

                PublishLiveValues();
            }
            catch (Exception e)
            {
                m_logger.LogError("Unexpected error during simulation: {Message}", e.Message);
            }
        }

        /// <summary>
        /// Publishes the next values of the live variables.
        /// </summary>
        private void PublishLiveValues()
        {
            long tick = Interlocked.Increment(ref m_ticks);
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;

            // a slow sine around room temperature, one sample per tick.
            Publish(m_temperature, Variant.From(Math.Round(20.0 + (5.0 * Math.Sin(tick / 30.0)), 2)), now);

            // one lab sample every five ticks: three readings at the same instant,
            // each with its own key. the capture pipeline inserts them as three
            // entries of the structured history, and the key is what keeps them
            // apart there.
            if (tick % 5 == 0)
            {
                double phase = tick / 50.0;

                Publish(m_labSample, LabReading("pH", Math.Round(7.0 + (0.3 * Math.Sin(phase)), 2)), now);
                Publish(m_labSample, LabReading("Temperature", Math.Round(20.0 + (5.0 * Math.Sin(phase)), 2)), now);
                Publish(m_labSample, LabReading("Conductivity", Math.Round(500.0 + (50.0 * Math.Cos(phase)), 1)), now);
            }
        }

        /// <summary>
        /// Sets the value of a live variable and reports the change, which is what
        /// the capture pipeline listens to.
        /// </summary>
        private void Publish(BaseDataVariableState variable, Variant value, DateTime timestamp)
        {
            variable.WrappedValue = value;
            variable.Timestamp = timestamp;
            variable.StatusCode = StatusCodes.Good;
            variable.ClearChangeMasks(SystemContext, false);
        }

        /// <summary>
        /// One reading of a lab sample: a KeyValuePair whose key names the quantity.
        /// </summary>
        private static Variant LabReading(string quantity, double reading)
        {
            return Variant.From(new ExtensionObject(new Opc.Ua.KeyValuePair {
                Key = new QualifiedName(quantity),
                Value = Variant.From(reading)
            }));
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// What the in-memory engine offers for a plain live variable: the full
        /// read and write surface of Part 11 data history, annotations included, at
        /// the one second cadence of the simulation.
        /// </summary>
        private static readonly HistorianNodeCapabilities s_liveCapabilities = HistorianNodeCapabilities.DataReadWrite with {
            InsertAnnotation = true,
            Stepped = false,
            MinTimeInterval = 1000,
            MaxTimeInterval = 1000
        };

        /// <summary>
        /// What the engine offers for the structured variable: the structured
        /// read and update surface, plus the two deletes so a client can clear what
        /// it wrote. A plain data insert is not among them - entries of a structured
        /// history are written through UpdateStructureData, which carries the key.
        /// </summary>
        private static readonly HistorianNodeCapabilities s_structuredCapabilities = HistorianNodeCapabilities.StructuredReadWrite with {
            DeleteRaw = true,
            DeleteAtTime = true,
            InsertAnnotation = true,
            Stepped = true,
            MinTimeInterval = 5000,
            MaxTimeInterval = 5000,
            Definition = "Three readings per sample, keyed by quantity"
        };

        /// <summary>
        /// How the capture sink batches: a flush after sixteen samples or a tenth
        /// of a second, so a value shows up in the history soon after it was
        /// published; the defaults favour throughput over latency.
        /// </summary>
        private static readonly HistorianCaptureOptions s_captureOptions = new HistorianCaptureOptions {
            BatchTarget = 16,
            BatchWindow = TimeSpan.FromMilliseconds(100)
        };

        private UnderlyingSystem m_system;
        private HistoricalAccessServerConfiguration m_configuration;
        private ArchiveHistorianProvider m_historian;
        private HistorianBuilder m_archiveHistorian;
        private HistorianBuilder m_liveHistorian;
        private InMemoryHistorianProvider m_liveProvider;
        private BaseDataVariableState m_temperature;
        private BaseDataVariableState m_setpoint;
        private BaseDataVariableState m_labSample;
        private long m_ticks;
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
