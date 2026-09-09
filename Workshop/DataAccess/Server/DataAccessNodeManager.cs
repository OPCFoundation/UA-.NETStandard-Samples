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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.DataAccessServer
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class DataAccessServerNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new DataAccessServerNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.DataAccess];
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plant hierarchy this sample serves lives in an underlying system, not in an
    /// address space: a segment or a block exists only as a path, and the node which
    /// represents it is built from a string node id for the duration of one operation
    /// and discarded again. That is a virtual node family, registered on the fluent
    /// builder with <see cref="VirtualNodeBuilderExtensions.ResolveNodes"/> - the
    /// predicate recognizes the shape of the identifier, the resolver asks the
    /// underlying system what is at that path, and the base node manager owns the
    /// handle, the operation cache and the validation.
    /// </para>
    /// <para>
    /// A block which somebody monitors is the exception to being discarded: the same
    /// instance has to serve every monitored item of that block, because it is what
    /// the underlying system reports its changes to. The family therefore takes part
    /// in the monitored item lifecycle, and the resolver hands out the retained block
    /// while it is monitored.
    /// </para>
    /// <para>
    /// There is no information model: a manager whose whole address space is computed
    /// has nothing to declare in one. It therefore drives the fluent builder itself in
    /// <see cref="CreateAddressSpaceAsync"/>, in the sequence the node manager
    /// generated from a ModelDesign follows.
    /// </para>
    /// </remarks>
    public class DataAccessServerNodeManager : FluentNodeManagerBase
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DataAccessServerNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(
                server,
                configuration,
                server.Telemetry.CreateLogger<DataAccessServerNodeManager>(),
                Namespaces.DataAccess)
        {
            this.AliasRoot = "DA";

            // the clock of the server, so that the simulation and the timestamps it writes
            // run on the same time source as the rest of the server and a test can drive
            // them with a FakeTimeProvider. ITimeProviderProvider is the opt-in seam for
            // reaching it; an IServerInternal which does not implement it falls back to
            // the system clock.
            SystemContext.SystemHandle = m_system = new UnderlyingSystem(
                server.Telemetry,
                (server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System);

            // create the table to store the cached blocks.
            m_blocks = new NodeIdDictionary<BlockState>();
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
                m_system.Dispose();
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
            return ModelUtils.ConstructIdForComponent(node, NamespaceIndex);
        }
        #endregion

        #region IAsyncNodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// The top level segments are not nodes of this manager, so their references to the
        /// Objects folder are written by hand rather than mirrored from a registered node.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // find the top level segments and link them to the ObjectsFolder.
            IList<UnderlyingSystemSegment> segments = m_system.FindSegments(null);

            for (int ii = 0; ii < segments.Count; ii++)
            {
                // Top level areas need a reference from the Server object.
                // These references are added to a list that is returned to the caller.
                // The caller will update the Objects folder node.
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                // construct the NodeId of a segment.
                NodeId segmentId = ModelUtils.ConstructIdForSegment(segments[ii].Id, NamespaceIndex);

                // add an organizes reference from the ObjectsFolder to the area.
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, segmentId));
            }

            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndex);
            Configure(builder);

            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            await SealConfigurationAsync(builder, cancellationToken).ConfigureAwait(false);

            // start the simulation.
            m_system.StartSimulation(Server.Telemetry);
        }

        /// <summary>
        /// Registers the segments and blocks as a virtual node family.
        /// </summary>
        private void Configure(INodeManagerBuilder builder)
        {
            builder
                .ResolveNodes(IsSegmentOrBlockId, ResolveSegmentOrBlockAsync)
                .OnMonitoredItemCreated(OnBlockMonitoredItemCreated)
                .OnMonitoredItemDeleted(OnBlockMonitoredItemDeletedAsync);
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            m_system.StopSimulation();
            m_blocks.Clear();

            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Virtual Segments and Blocks
        /// <summary>
        /// Recognizes the identifier of a segment or a block.
        /// </summary>
        /// <remarks>
        /// Both are string identifiers which name a root type, a path in the underlying
        /// system and optionally a component within it. Whether the path names anything
        /// is the business of the resolver.
        /// </remarks>
        private static bool IsSegmentOrBlockId(NodeId nodeId)
        {
            return nodeId.IdType == IdType.String;
        }

        /// <summary>
        /// Builds the segment, block or component of a block a node id names, or
        /// returns nothing when the underlying system has nothing at that path.
        /// </summary>
        private ValueTask<NodeState> ResolveSegmentOrBlockAsync(
            ISystemContext context,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            // check if the node id has been parsed.
            ParsedNodeId parsedNodeId = ParsedNodeId.Parse(nodeId);

            if (parsedNodeId == null)
            {
                return default;
            }

            NodeState root;

            // validate a segment.
            if (parsedNodeId.RootType == ModelUtils.Segment)
            {
                UnderlyingSystemSegment segment = m_system.FindSegment(parsedNodeId.RootId);

                // segment does not exist.
                if (segment == null)
                {
                    return default;
                }

                NodeId rootId = ModelUtils.ConstructIdForSegment(segment.Id, NamespaceIndex);

                // create a temporary object to use for the operation.
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node handle/cache.
                root = new SegmentState(context, rootId, segment);
#pragma warning restore CA2000
            }

            // validate a block.
            else if (parsedNodeId.RootType == ModelUtils.Block)
            {
                // validate the block.
                UnderlyingSystemBlock block = m_system.FindBlock(parsedNodeId.RootId);

                // block does not exist.
                if (block == null)
                {
                    return default;
                }

                NodeId rootId = ModelUtils.ConstructIdForBlock(block.Id, NamespaceIndex);

                // check for blocks that are being currently monitored: every monitored
                // item of a block has to see the same instance, because that is the one
                // the underlying system reports its changes to.
                if (m_blocks.TryGetValue(rootId, out BlockState node))
                {
                    root = node;
                }

                // create a temporary object to use for the operation.
                else
                {
#pragma warning disable CA2000 // Justification: NodeState ownership is transferred to the node handle/cache.
                    root = new BlockState(this, rootId, block);
#pragma warning restore CA2000
                }
            }

            // unknown root type.
            else
            {
                return default;
            }

            // all done if no components to validate.
            if (String.IsNullOrEmpty(parsedNodeId.ComponentPath))
            {
                return new ValueTask<NodeState>(root);
            }

            // validate component.
            return new ValueTask<NodeState>(
                root.FindChildBySymbolicName(context, parsedNodeId.ComponentPath));
        }
        #endregion

        #region Monitoring
        /// <summary>
        /// Starts the block a monitored item was created for and retains it.
        /// </summary>
        private void OnBlockMonitoredItemCreated(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (source.GetHierarchyRoot() is BlockState block)
            {
                block.StartMonitoring((ServerSystemContext)context);

                // need to save the block to ensure that multiple monitored items use the same instance.
                m_blocks[block.NodeId] = block;
            }
        }

        /// <summary>
        /// Stops the block again once nothing monitors it any more.
        /// </summary>
        private ValueTask OnBlockMonitoredItemDeletedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            if (source.GetHierarchyRoot() is BlockState block &&
                !block.StopMonitoring((ServerSystemContext)context))
            {
                // can remove the block since all monitored items for the block are gone.
                m_blocks.TryRemove(block.NodeId, out _);
            }

            return default;
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        private NodeIdDictionary<BlockState> m_blocks;
        #endregion
    }
}
