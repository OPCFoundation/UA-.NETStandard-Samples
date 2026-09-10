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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace MemoryBuffer
{
    /// <summary>
    /// A node manager for a variety of memory buffers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>FluentNodeManagerBase</c>, loads the predefined nodes generated from
    /// <c>MemoryBufferDesign.xml</c> - the MemoryBuffers folder below the Objects
    /// folder - calls <see cref="Configure"/> once the address space is in place,
    /// and emits the factory the server registers. The generated constructor
    /// reports the instance namespace named by <c>AdditionalNamespaceUris</c> next
    /// to the namespace of the type model, so the master node manager routes the
    /// buffers and their tags here from the start.
    /// </para>
    /// <para>
    /// The tags of a buffer do not exist as nodes: a tag is synthesized from its
    /// node id for the duration of one service call, which is what lets the sample
    /// expose potentially millions of UA nodes without keeping millions of objects
    /// in memory. That is a virtual node family, registered on the builder with
    /// <see cref="VirtualNodeBuilderExtensions.ResolveNodes"/>: the predicate
    /// recognizes a tag id cheaply, the resolver materializes the tag for the
    /// operation which asked for it, and the base node manager takes care of the
    /// handle, the cache and the validation.
    /// </para>
    /// <para>
    /// The buffers publish their values straight into the monitored items rather
    /// than letting the server sample the tags, so the family also takes part in
    /// monitored item creation: it refuses what its own publishing cannot honour -
    /// a filter, an index range or a data encoding - and hands the stack a factory
    /// for the item the buffer writes into. The stack registers that item, modifies
    /// it and deletes it like any other; all the sample has left to do is to unhook
    /// it from the monitoring table of its buffer when it goes away.
    /// </para>
    /// </remarks>
    [NodeManager(
        NamespaceUri = "http://samples.org/UA/MemoryBuffer",
        AdditionalNamespaceUris = new[] { "http://samples.org/UA/MemoryBuffer/Instance" })]
    public partial class MemoryBufferNodeManager
    {
        #region Configure
        /// <summary>
        /// Creates the buffers the configuration declares once the predefined
        /// nodes are in place, and registers their tags as a virtual node family.
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

            // use suitable defaults if no configuration exists.
            MemoryBufferConfiguration bufferConfiguration =
                Configuration?.ParseExtension<MemoryBufferConfiguration>() ??
                new MemoryBufferConfiguration();

            BaseInstanceState root = FindPredefinedNode<BaseInstanceState>(
                new NodeId(Objects.MemoryBuffers, NamespaceIndexes[0]));

            // create the nodes from configuration.
            ushort namespaceIndex = NamespaceIndexes[1];

            if (!bufferConfiguration.Buffers.IsNull)
            {
                for (int ii = 0; ii < bufferConfiguration.Buffers.Count; ii++)
                {
                    MemoryBufferInstance instance = bufferConfiguration.Buffers[ii];

                    // create a new buffer.
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    // the clock of the server, so that the scan loop runs on the same time
                    // source as the rest of the server and a test can drive it with a
                    // FakeTimeProvider. ITimeProviderProvider is the opt-in seam for
                    // reaching it; an IServerInternal which does not implement it falls
                    // back to the system clock.
                    MemoryBufferState bufferNode = new MemoryBufferState(
                        SystemContext,
                        instance,
                        (Server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System);
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

            // the tags are not nodes: they are recognized by their id, materialized for
            // the operation which asked for them, and published into by their buffer.
            builder
                .ResolveNodes(IsTagId, ResolveTagAsync)
                .OnCreateMonitoredItem(OnCreatingTagMonitoredItemAsync)
                .OnMonitoredItemDeleted(OnTagMonitoredItemDeletedAsync);
        }
        #endregion

        #region Virtual Tags
        /// <summary>
        /// Recognizes the id of a tag, which has the syntax <c>bufferName[offset]</c>.
        /// </summary>
        /// <remarks>
        /// The predicate runs for every node id of this node manager which is not a
        /// predefined node, so it does no more than look at the shape of the
        /// identifier; whether the buffer exists and the offset is in range is the
        /// business of the resolver.
        /// </remarks>
        private static bool IsTagId(NodeId nodeId)
        {
            return nodeId.TryGetValue(out string id) &&
                id != null &&
                id.Length > 2 &&
                id[id.Length - 1] == ']' &&
                id.IndexOf('[', StringComparison.Ordinal) > 0;
        }

        /// <summary>
        /// Materializes the tag a node id names, or nothing when it names no slot of
        /// a configured buffer.
        /// </summary>
        /// <remarks>
        /// The tags carry all of the metadata required to support the UA operations
        /// and pointers to functions in the buffer object that allow the value to be
        /// accessed. They are discarded again once the operation completes.
        /// </remarks>
        private ValueTask<NodeState> ResolveTagAsync(
            ISystemContext context,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            if (!nodeId.TryGetValue(out string id) || id == null)
            {
                return default;
            }

            int index = id.IndexOf('[', StringComparison.Ordinal);

            // verify the buffer.
            if (!m_buffers.TryGetValue(id.Substring(0, index), out MemoryBufferState buffer))
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
            if (!UInt32.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out uint offset) ||
                offset >= buffer.SizeInBytes.Value)
            {
                return default;
            }

            #pragma warning disable CA2000 // Justification: ownership of the tag transfers to the caller.
            return new ValueTask<NodeState>(new MemoryTagState(buffer, offset));
            #pragma warning restore CA2000
        }
        #endregion

        #region Monitoring
        /// <summary>
        /// Refuses what the publishing of the buffer cannot honour, and hands the
        /// stack the item the buffer writes into.
        /// </summary>
        /// <remarks>
        /// Because the buffer queues the value into the monitored item itself there is
        /// nothing left to apply a filter, an index range or an encoding to, so asking
        /// for one fails at creation rather than being quietly ignored.
        /// </remarks>
        private static ValueTask<MonitoredItemCreateDecision> OnCreatingTagMonitoredItemAsync(
            MonitoredItemCreateContext context,
            CancellationToken cancellationToken)
        {
            // no filters supported at this time.
            if (ExtensionObject.ToEncodeable(context.Request.RequestedParameters.Filter) is MonitoringFilter)
            {
                return new ValueTask<MonitoredItemCreateDecision>(
                    MonitoredItemCreateDecision.Refuse(new ServiceResult(StatusCodes.BadFilterNotAllowed)));
            }

            // index range not supported.
            if (!context.Request.ItemToMonitor.ParsedIndexRange.IsNull)
            {
                return new ValueTask<MonitoredItemCreateDecision>(
                    MonitoredItemCreateDecision.Refuse(new ServiceResult(StatusCodes.BadIndexRangeInvalid)));
            }

            // data encoding not supported.
            if (!context.Request.ItemToMonitor.DataEncoding.IsNull)
            {
                return new ValueTask<MonitoredItemCreateDecision>(
                    MonitoredItemCreateDecision.Refuse(new ServiceResult(StatusCodes.BadDataEncodingUnsupported)));
            }

            return new ValueTask<MonitoredItemCreateDecision>(
                MonitoredItemCreateDecision.Use(CreateTagMonitoredItem, queueInitialValue: true));
        }

        /// <summary>
        /// Creates the monitored item the buffer publishes into.
        /// </summary>
        /// <remarks>
        /// The item is registered by the stack, which is what routes the later modify,
        /// delete and monitoring mode calls back here.
        /// </remarks>
        private static ISampledDataChangeMonitoredItem CreateTagMonitoredItem(MonitoredItemFactoryContext context)
        {
            var tag = (MemoryTagState)context.Handle.Node;
            var buffer = (MemoryBufferState)tag.Parent;

            return buffer.CreateDataChangeItem(
                tag,
                context.Handle,
                context.SubscriptionId,
                context.MonitoredItemId,
                context.Request.ItemToMonitor,
                context.DiagnosticsMasks,
                context.TimestampsToReturn,
                context.Request.MonitoringMode,
                context.Request.RequestedParameters.ClientHandle,
                context.SamplingInterval);
        }

        /// <summary>
        /// Takes a deleted item out of the monitoring table of its buffer.
        /// </summary>
        private static ValueTask OnTagMonitoredItemDeletedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            if (source is MemoryTagState tag &&
                tag.Parent is MemoryBufferState buffer &&
                monitoredItem is MemoryBufferMonitoredItem datachangeItem)
            {
                buffer.DeleteItem(datachangeItem);
            }

            return default;
        }
        #endregion

        #region Private Fields
        private readonly Dictionary<string, MemoryBufferState> m_buffers = [];
        #endregion
    }
}
