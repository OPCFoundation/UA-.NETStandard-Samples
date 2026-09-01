/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.NodeManagement.Server
{
    /// <summary>
    /// A node manager which lets clients build its address space over the OPC UA
    /// NodeManagement service set (OPC 10000-4 5.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole opt-in is <see cref="AllowNodeManagement"/>. Everything else in this file is
    /// the part a real server has to decide for itself, and a sample which only flipped the
    /// property would leave a reader believing there is nothing to decide:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///   <see cref="New"/> chooses what a server assigned NodeId looks like. A client may
    ///   leave <c>RequestedNewNodeId</c> empty, and then this is what answers.
    ///   </item>
    ///   <item>
    ///   <see cref="AddNodeAsync"/> and <see cref="DeleteNodeAsync"/> are where a server says
    ///   which part of its address space is open. Here the model of the sample is read-only
    ///   and everything below <c>Devices</c> is not, so a client can neither delete
    ///   <c>Plant</c> nor hang a node off it.
    ///   </item>
    ///   <item>
    ///   The same two methods are the only place the sample keeps a derived value
    ///   (<c>DeviceCount</c>) in step with an address space it does not control.
    ///   </item>
    /// </list>
    /// <para>
    /// AddReferences and DeleteReferences are left exactly as the SDK implements them. The
    /// <c>Commissioned</c> folder exists so that there is something to point a reference at
    /// without adding a node, which is the difference between the two pairs of services.
    /// </para>
    /// </remarks>
    [NodeManager]
    public partial class NodeManagementNodeManager
    {
        #region INodeManagementAsyncNodeManager Members
        /// <summary>
        /// Accepts AddNodes, DeleteNodes, AddReferences and DeleteReferences requests.
        /// </summary>
        /// <remarks>
        /// This one property is the entire server side opt-in. Without it the master node
        /// manager answers BadUserAccessDenied to every item routed here, which is the
        /// default of every node manager and the reason the four services are safe to have
        /// implemented in the SDK for all of them.
        /// </remarks>
        public override bool AllowNodeManagement => true;

        /// <summary>
        /// Chooses the NodeId of a node the client did not name one for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A client may send an empty <c>RequestedNewNodeId</c> and leave the identifier to
        /// the server, which is what <c>INodeIdFactory.New</c> is for. The base
        /// implementation counts up a numeric identifier seeded from the clock; this sample
        /// builds a string identifier from the browse name instead, so that a node which a
        /// client created is recognisable as such in a browse, and so that a server assigned
        /// identifier can never collide with the numeric identifiers of the model.
        /// </para>
        /// <para>
        /// A node whose NodeId is already set - every node of the model, while it is being
        /// loaded - keeps it. Overriding this without that check renumbers the model.
        /// </para>
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node == null || !node.NodeId.IsNull || node.BrowseName.IsNull)
            {
                return base.New(context, node);
            }

            return new NodeId(
                Utils.Format(
                    "{0}-{1}",
                    node.BrowseName.Name,
                    Utils.IncrementIdentifier(ref m_lastAddedNode)),
                NamespaceIndex);
        }

        /// <summary>
        /// Adds one node, if it belongs below the folder this sample opens to its clients.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The master node manager has already established that the request is well formed,
        /// that this node manager owns the namespace the new node goes into, and that the
        /// Session carries the AddNode and AddReference permissions. What is left for the
        /// node manager is the question no generic code can answer: whether this particular
        /// parent is a place where clients are allowed to build.
        /// </para>
        /// <para>
        /// The answer here is "below Devices, at any depth". A client can therefore add a
        /// device and then add variables to the device it just added, but cannot attach
        /// anything to <c>Plant</c>, to <c>Commissioned</c>, or to <c>DeviceCount</c>.
        /// </para>
        /// </remarks>
        public override async ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken = default)
        {
            if (item == null)
            {
                return await base.AddNodeAsync(context, item, cancellationToken).ConfigureAwait(false);
            }

            NodeId parentId = ExpandedNodeId.ToNodeId(item.ParentNodeId, Server.NamespaceUris, false);

            if (!IsOpenToClients(parentId))
            {
                return (
                    ServiceResult.Create(
                        StatusCodes.BadParentNodeIdInvalid,
                        "This server only accepts nodes below '{0}'. '{1}' is part of its own model.",
                        m_devices.DisplayName,
                        parentId),
                    NodeId.Null);
            }

            (ServiceResult result, NodeId addedNodeId) = await base
                .AddNodeAsync(context, item, cancellationToken)
                .ConfigureAwait(false);

            if (ServiceResult.IsGood(result))
            {
                await UpdateDeviceCountAsync(cancellationToken).ConfigureAwait(false);
            }

            return (result, addedNodeId);
        }

        /// <summary>
        /// Deletes one node, unless it is part of the model the sample ships.
        /// </summary>
        /// <remarks>
        /// A server which opts in to NodeManagement and protects nothing can be emptied by
        /// its first client: DeleteNodes on <c>Plant</c> would take the folders and the
        /// counter with it and there would be no way back short of a restart. Which nodes
        /// are fixed is a decision only the node manager can make, so this is the shape a
        /// real server needs, not a detail of the sample.
        /// </remarks>
        public override async ValueTask<ServiceResult> DeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken = default)
        {
            if (item != null && m_modelNodes.Contains(item.NodeId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "'{0}' belongs to the model of this server and cannot be deleted.",
                    item.NodeId);
            }

            ServiceResult result = await base
                .DeleteNodeAsync(context, item, cancellationToken)
                .ConfigureAwait(false);

            if (ServiceResult.IsGood(result))
            {
                await UpdateDeviceCountAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        #endregion

        #region Configure
        /// <summary>
        /// Records which nodes the model brought, and switches model change tracking on.
        /// </summary>
        partial void Configure(INodeManagementNodeManagerBuilder builder)
        {
            m_plant = builder.Plant.Node;
            m_devices = builder.Plant.Devices.Node;
            m_commissioned = builder.Plant.Commissioned.Node;
            m_deviceCount = builder.Plant.DeviceCount.Node;

            // everything which exists at this point is the model of the sample, and is what
            // DeleteNodeAsync refuses to delete. Taking the snapshot here rather than listing
            // the four nodes by hand keeps the rule true if the model ever grows.
            m_modelNodes = new HashSet<NodeId>(PredefinedNodes.Keys);

            m_deviceCount.Value = Variant.From(0u);
            m_deviceCount.Timestamp = DateTimeUtc.Now;
            m_deviceCount.ClearChangeMasks(SystemContext, false);

            // A client which is not the one making the changes still has to find out about
            // them, and AddNodes and DeleteNodes raise a GeneralModelChangeEvent by
            // themselves once the affected node is eligible. Part 5 9.32.2 makes eligibility
            // a NodeVersion property on the node, which this attaches - so the three folders
            // each grow a NodeVersion which clients can see when they browse. The properties
            // belong to the model rather than to the clients, so they join the set above.
            m_modelNodes.Add(EnableModelChangeTrackingFor(m_plant, null).NodeId);
            m_modelNodes.Add(EnableModelChangeTrackingFor(m_commissioned, null).NodeId);

            m_devicesNodeVersion = EnableModelChangeTrackingFor(m_devices, null).NodeId;
            m_modelNodes.Add(m_devicesNodeVersion);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// True when a node is <c>Devices</c> or one of its descendants.
        /// </summary>
        /// <remarks>
        /// Walked over the parent chain rather than over references: a node added through
        /// AddNodes becomes a child of its parent in the node state tree, so the chain is
        /// exactly the set of nodes a client built below the folder.
        /// </remarks>
        private bool IsOpenToClients(NodeId nodeId)
        {
            NodeState node = FindPredefinedNode<NodeState>(nodeId);

            while (node != null)
            {
                if (node.NodeId == m_devices.NodeId)
                {
                    return true;
                }

                node = (node as BaseInstanceState)?.Parent;
            }

            return false;
        }

        /// <summary>
        /// Reports how many nodes clients have added to the Devices folder.
        /// </summary>
        /// <remarks>
        /// The only piece of state in the sample which is derived from an address space the
        /// server does not control, and therefore the one thing which shows where a server
        /// has to hook into AddNodes and DeleteNodes even when the SDK does the work.
        /// </remarks>
        private async ValueTask UpdateDeviceCountAsync(CancellationToken cancellationToken)
        {
            var children = new List<BaseInstanceState>();

            m_devices.GetChildren(SystemContext, children);

            // the NodeVersion property of the folder is a child like any other, and is the
            // one child which is not something a client put there
            uint count = (uint)children.Count(child => child.NodeId != m_devicesNodeVersion);

            m_deviceCount.Value = Variant.From(count);
            m_deviceCount.Timestamp = DateTimeUtc.Now;

            await m_deviceCount
                .ClearChangeMasksAsync(SystemContext, false, cancellationToken)
                .ConfigureAwait(false);
        }
        #endregion

        #region Private Fields
        private BaseObjectState m_plant;
        private BaseObjectState m_devices;
        private BaseObjectState m_commissioned;
        private BaseVariableState m_deviceCount;
        private HashSet<NodeId> m_modelNodes;
        private NodeId m_devicesNodeVersion;
        private uint m_lastAddedNode;
        #endregion
    }
}
