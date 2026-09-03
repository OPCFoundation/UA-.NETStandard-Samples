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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace MemoryBuffer
{
    /// <summary>
    /// The factory to create the node manager for memory buffers.
    /// </summary>
    /// <remarks>
    /// The factory is written by hand because the node manager needs the buffer
    /// configuration from the application configuration, and the generated
    /// constructor is the only place the generated partial sees the configuration:
    /// nothing keeps it for <see cref="MemoryBufferNodeManager.Configure"/>. The
    /// factory parses the extension and hands it to the constructor which chains
    /// to the generated one. It advertises the same two namespaces the generated
    /// constructor reports.
    /// </remarks>
    public class MemoryBufferNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(IServerInternal server, ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            // use suitable defaults if no configuration exists.
            MemoryBufferConfiguration bufferConfiguration =
                configuration.ParseExtension<MemoryBufferConfiguration>() ??
                new MemoryBufferConfiguration();

#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(new MemoryBufferNodeManager(server, configuration, bufferConfiguration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.MemoryBuffer, Namespaces.MemoryBuffer + "/Instance"];
    }

    /// <summary>
    /// A node manager for a variety of memory buffers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>MemoryBufferDesign.xml</c> - the MemoryBuffers folder below the Objects
    /// folder - and calls <see cref="Configure"/> once the address space is in
    /// place. The generated constructor reports the instance namespace named by
    /// <c>AdditionalNamespaceUris</c> next to the namespace of the type model, so
    /// the master node manager routes the buffers and their tags here from the
    /// start. The factory stays hand-written, see
    /// <see cref="MemoryBufferNodeManagerFactory"/> for why.
    /// </para>
    /// <para>
    /// The buffers publish their values straight into the monitored items and the
    /// tags do not exist as nodes: a tag is synthesized from its node id for the
    /// duration of one service call. The fluent surface has no hook for either -
    /// there is no way to resolve a node id which is not a predefined node, and no
    /// way to refuse a monitored item at creation or to take over its
    /// modification, deletion and monitoring mode changes (tracked as SDK issues
    /// OPCFoundation/UA-.NETStandard#4397 and OPCFoundation/UA-.NETStandard#4399).
    /// Until those land, this partial keeps the <see cref="GetManagerHandleAsync"/>
    /// override and the four monitored item overrides, which the generated partial
    /// leaves free: it only owns the predefined node loading, the address space
    /// creation and the node added, node removed and monitored item created hooks.
    /// </para>
    /// </remarks>
    [NodeManager(
        NamespaceUri = "http://samples.org/UA/MemoryBuffer",
        AdditionalNamespaceUris = new[] { "http://samples.org/UA/MemoryBuffer/Instance" },
        GenerateFactory = false)]
    public partial class MemoryBufferNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager with the buffers to expose.
        /// </summary>
        /// <remarks>
        /// Chains to the generated constructor, which reports both namespaces to
        /// the base node manager and installs this node manager as the node id
        /// factory of the system context.
        /// </remarks>
        public MemoryBufferNodeManager(IServerInternal server, ApplicationConfiguration configuration, MemoryBufferConfiguration bufferConfiguration)
        :
            this(server, configuration)
        {
            m_configuration = bufferConfiguration ?? new MemoryBufferConfiguration();
            m_buffers = new Dictionary<string, MemoryBufferState>();
        }
        #endregion

        #region Configure
        /// <summary>
        /// Creates the buffers the configuration declares once the predefined
        /// nodes are in place.
        /// </summary>
        /// <remarks>
        /// The buffers are created imperatively rather than through the builder:
        /// their node ids have to be the buffer names in the instance namespace,
        /// because the tags are addressed as <c>buffer[offset]</c> in that namespace,
        /// and the builder would mint ids of its own.
        /// </remarks>
        partial void Configure(INodeManagerBuilder builder)
        {
            Server.Factory.AddEncodeableTypes(typeof(MemoryBufferNodeManager).Assembly.GetExportedTypes().Where(t => t.FullName.StartsWith(typeof(MemoryBufferNodeManager).Namespace, StringComparison.Ordinal)));

            BaseInstanceState root = FindPredefinedNode<BaseInstanceState>(
                new NodeId(Objects.MemoryBuffers, NamespaceIndexes[0]));

            // create the nodes from configuration.
            ushort namespaceIndex = NamespaceIndexes[1];

            if (m_configuration != null && !m_configuration.Buffers.IsNull)
            {
                for (int ii = 0; ii < m_configuration.Buffers.Count; ii++)
                {
                    MemoryBufferInstance instance = m_configuration.Buffers[ii];

                    // create a new buffer.
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    MemoryBufferState bufferNode = new MemoryBufferState(SystemContext, instance);
                    #pragma warning restore CA2000

                    // assign node ids.
                    bufferNode.Create(
                        SystemContext,
                        new NodeId(bufferNode.SymbolicName, namespaceIndex),
                        new QualifiedName(bufferNode.SymbolicName, namespaceIndex),
                        LocalizedText.Null,
                        true);

                    bufferNode.CreateBuffer(instance.DataType, instance.TagCount);
                    bufferNode.InitializeMonitoring(Server, this);

                    // save the buffers for easy look up later.
                    m_buffers[bufferNode.SymbolicName] = bufferNode;

                    // link to root.
                    root.AddChild(bufferNode);

                    // store it and its properties in the pre-defined nodes dictionary for easy look up.
                    AddPredefinedNodeSynchronously(bufferNode);
                }
            }
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        /// <remarks>
        /// This must efficiently determine whether the node belongs to the node manager. If it does belong to
        /// NodeManager it should return a handle that does not require the NodeId to be validated again when
        /// the handle is passed into other methods such as 'Read' or 'Write'.
        /// </remarks>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache, CancellationToken cancellationToken = default)
        {
            if (!IsNodeIdInNamespace(nodeId))
            {
                return default;
            }

            if (nodeId.TryGetValue(out string id) && id != null)
            {
                // check for a reference to the buffer.
                if (m_buffers.TryGetValue(id, out MemoryBufferState buffer))
                {
                    return new ValueTask<NodeHandle>(new NodeHandle
                    {
                        NodeId = nodeId,
                        Node = buffer,
                        Validated = true
                    });
                }

                // tag ids have the syntax <bufferName>[<address>]
                if (id[id.Length - 1] != ']')
                {
                    return default;
                }

                int index = id.IndexOf('[', StringComparison.Ordinal);

                if (index == -1)
                {
                    return default;
                }

                string bufferName = id.Substring(0, index);

                // verify the buffer.
                if (!m_buffers.TryGetValue(bufferName, out buffer))
                {
                    return default;
                }

                // validate the address.
                string offsetText = id.Substring(index + 1, id.Length - index - 2);

                for (int ii = 0; ii < offsetText.Length; ii++)
                {
                    if (!Char.IsDigit(offsetText[ii]))
                    {
                        return default;
                    }
                }

                // check range on offset.
                uint offset = Convert.ToUInt32(offsetText);

                if (offset >= buffer.SizeInBytes.Value)
                {
                    return default;
                }

                // the tags contain all of the metadata required to support the UA
                // operations and pointers to functions in the buffer object that
                // allow the value to be accessed. These tags are ephemeral and are
                // discarded after the operation completes. This design pattern allows
                // the server to expose potentially millions of UA nodes without
                // creating millions of objects that reside in memory.
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                return new ValueTask<NodeHandle>(new NodeHandle
                {
                    NodeId = nodeId,
                    Node = new MemoryTagState(buffer, offset),
                    Validated = true
                });
                #pragma warning restore CA2000
            }

            return base.GetManagerHandleAsync(context, nodeId, cache, cancellationToken);
        }

        /// <summary>
        /// Creates a new monitored item for a tag and lets the buffer publish into it.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        protected override async ValueTask<(ServiceResult error, MonitoringFilterResult filterResult, IMonitoredItem monitoredItem)> CreateMonitoredItemAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            // use default behavior for non-tag sources.
            if (handle.Node is not MemoryTagState tag)
            {
                return await base.CreateMonitoredItemAsync(
                    context,
                    handle,
                    subscriptionId,
                    publishingInterval,
                    diagnosticsMasks,
                    timestampsToReturn,
                    itemToCreate,
                    createDurable,
                    monitoredItemId,
                    cancellationToken).ConfigureAwait(false);
            }

            // validate parameters.
            MonitoringParameters parameters = itemToCreate.RequestedParameters;

            // no filters supported at this time.
            MonitoringFilter filter = (MonitoringFilter)ExtensionObject.ToEncodeable(parameters.Filter);

            if (filter != null)
            {
                return (StatusCodes.BadFilterNotAllowed, null, null);
            }

            // index range not supported.
            if (!itemToCreate.ItemToMonitor.ParsedIndexRange.IsNull)
            {
                return (StatusCodes.BadIndexRangeInvalid, null, null);
            }

            // data encoding not supported.
            if (!itemToCreate.ItemToMonitor.DataEncoding.IsNull)
            {
                return (StatusCodes.BadDataEncodingUnsupported, null, null);
            }

            // read initial value.
            (ServiceResult error, DataValue initialValue) = await tag.ReadAttributeAsync(
                context,
                itemToCreate.ItemToMonitor.AttributeId,
                itemToCreate.ItemToMonitor.ParsedIndexRange,
                itemToCreate.ItemToMonitor.DataEncoding,
                new DataValue(
                    Variant.Null,
                    StatusCodes.Good,
                    DateTime.MinValue,
                    DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);

            if (ServiceResult.IsBad(error))
            {
                return (error, null, null);
            }

            // get the monitored node for the containing buffer.
            if (tag.Parent is not MemoryBufferState buffer)
            {
                return (StatusCodes.BadInternalError, null, null);
            }

            // create a globally unique identifier.
            uint id = monitoredItemId.GetNextId();

            // determine the sampling interval.
            double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }

            // create the item. the handle is passed on as the manager handle so the
            // service calls route the item back to this node manager.
            MemoryBufferMonitoredItem datachangeItem = buffer.CreateDataChangeItem(
                tag,
                handle,
                subscriptionId,
                id,
                itemToCreate.ItemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                samplingInterval);

            // report the initial value.
            datachangeItem.QueueValue(initialValue, null);

            return (ServiceResult.Good, null, datachangeItem);
        }

        /// <summary>
        /// Modifies the parameters for a monitored item.
        /// </summary>
        protected override async ValueTask<(ServiceResult error, MonitoringFilterResult filterResult)> ModifyMonitoredItemAsync(
            ServerSystemContext context,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            IMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            NodeHandle handle,
            CancellationToken cancellationToken = default)
        {
            // use default behavior for items the buffers do not own.
            if (monitoredItem is not MemoryBufferMonitoredItem datachangeItem)
            {
                return await base.ModifyMonitoredItemAsync(
                    context,
                    diagnosticsMasks,
                    timestampsToReturn,
                    monitoredItem,
                    itemToModify,
                    handle,
                    cancellationToken).ConfigureAwait(false);
            }

            // validate parameters.
            MonitoringParameters parameters = itemToModify.RequestedParameters;

            // no filters supported at this time.
            MonitoringFilter filter = (MonitoringFilter)ExtensionObject.ToEncodeable(parameters.Filter);

            if (filter != null)
            {
                return (StatusCodes.BadFilterNotAllowed, null);
            }

            // modify the monitored item parameters.
            datachangeItem.Modify(
                diagnosticsMasks,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                itemToModify.RequestedParameters.SamplingInterval);

            return (ServiceResult.Good, null);
        }

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        protected override async ValueTask<ServiceResult> DeleteMonitoredItemAsync(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            NodeHandle handle,
            CancellationToken cancellationToken = default)
        {
            // use default behavior for items the buffers do not own.
            if (monitoredItem is not MemoryBufferMonitoredItem datachangeItem)
            {
                return await base.DeleteMonitoredItemAsync(
                    context,
                    monitoredItem,
                    handle,
                    cancellationToken).ConfigureAwait(false);
            }

            if (handle.Node is not MemoryTagState tag || tag.Parent is not MemoryBufferState buffer)
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            // delete the item.
            buffer.DeleteItem(datachangeItem);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Changes the monitoring mode for an item.
        /// </summary>
        protected override async ValueTask<ServiceResult> SetMonitoringModeAsync(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle,
            CancellationToken cancellationToken = default)
        {
            // use default behavior for items the buffers do not own.
            if (monitoredItem is not MemoryBufferMonitoredItem datachangeItem)
            {
                return await base.SetMonitoringModeAsync(
                    context,
                    monitoredItem,
                    monitoringMode,
                    handle,
                    cancellationToken).ConfigureAwait(false);
            }

            MonitoringMode previousMode = datachangeItem.SetMonitoringMode(monitoringMode);

            // need to provide an immediate update after enabling.
            if (previousMode == MonitoringMode.Disabled && monitoringMode != MonitoringMode.Disabled &&
                handle.Node is MemoryTagState tag && tag.Parent is MemoryBufferState buffer)
            {
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                MemoryTagState updateTag = new MemoryTagState(buffer, datachangeItem.Offset);
                #pragma warning restore CA2000

                (ServiceResult error, DataValue initialValue) = await updateTag.ReadAttributeAsync(
                    context,
                    datachangeItem.AttributeId,
                    NumericRange.Null,
                    QualifiedName.Null,
                    new DataValue(
                        Variant.Null,
                        StatusCodes.Good,
                        DateTime.MinValue,
                        DateTime.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                datachangeItem.QueueValue(initialValue, error);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Private Fields
        private MemoryBufferConfiguration m_configuration;
        private Dictionary<string, MemoryBufferState> m_buffers;
        #endregion
    }
}
