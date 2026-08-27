/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Quickstarts
{
    /// <summary>
    /// The <see cref="IAsyncNodeManager"/> half of the <see cref="QuickstartNodeManager"/>.
    /// </summary>
    /// <remarks>
    /// The Quickstart address space is held entirely in memory, so none of these entry points has
    /// anything to await. They complete synchronously and forward to the virtual synchronous
    /// methods declared in QuickstartNodeManager.cs, which keeps every extension point that the
    /// Workshop server samples override working unchanged. Implementing the interface on the node
    /// manager itself - rather than letting the SDK wrap it in an AsyncNodeManagerAdapter - also
    /// means the MasterNodeManager and the MonitoredItems created by this node manager all refer
    /// to the same instance, which matters because the SDK compares node managers by reference
    /// when it groups monitored items by their owner. A derived node manager that talks to a real,
    /// slow underlying system should override the methods below with genuinely asynchronous
    /// implementations.
    /// </remarks>
    public partial class QuickstartNodeManager
    {
        /// <summary>
        /// The synchronous view of this node manager. The Quickstart node manager implements both
        /// interfaces natively, so no SyncNodeManagerAdapter is required.
        /// </summary>
        public INodeManager SyncNodeManager => this;

        /// <inheritdoc cref="INodeManager.CreateAddressSpace"/>
        public virtual ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            CreateAddressSpace(externalReferences);
            return default;
        }

        /// <inheritdoc cref="INodeManager.DeleteAddressSpace"/>
        public virtual ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            DeleteAddressSpace();
            return default;
        }

        /// <inheritdoc cref="INodeManager.GetManagerHandle"/>
        public virtual ValueTask<object> GetManagerHandleAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<object>(GetManagerHandle(nodeId));
        }

        /// <inheritdoc cref="INodeManager.AddReferences"/>
        public virtual ValueTask AddReferencesAsync(
            IDictionary<NodeId, IList<IReference>> references,
            CancellationToken cancellationToken = default)
        {
            AddReferences(references);
            return default;
        }

        /// <inheritdoc cref="INodeManager.DeleteReference"/>
        public virtual ValueTask<ServiceResult> DeleteReferenceAsync(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(
                DeleteReference(sourceHandle, referenceTypeId, isInverse, targetId, deleteBidirectional));
        }

        /// <inheritdoc cref="INodeManager.GetNodeMetadata"/>
        public virtual ValueTask<NodeMetadata> GetNodeMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeMetadata>(GetNodeMetadata(context, targetHandle, resultMask));
        }

        /// <summary>
        /// Returns the metadata needed to validate the permissions of a node.
        /// </summary>
        /// <remarks>
        /// The Quickstart node manager does not cache permission metadata. Returning null hands the
        /// request back to the MasterNodeManager, which then falls back to
        /// <see cref="GetNodeMetadataAsync"/>.
        /// </remarks>
        public virtual ValueTask<NodeMetadata> GetPermissionMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, Variant[]> uniqueNodesServiceAttributesCache,
            bool permissionsOnly,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeMetadata>((NodeMetadata)null);
        }

        /// <inheritdoc cref="INodeManager.Browse"/>
        public virtual ValueTask<ContinuationPoint> BrowseAsync(
            OperationContext context,
            ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references,
            CancellationToken cancellationToken = default)
        {
            Browse(context, ref continuationPoint, references);
            return new ValueTask<ContinuationPoint>(continuationPoint);
        }

        /// <inheritdoc cref="INodeManager.TranslateBrowsePath"/>
        public virtual ValueTask TranslateBrowsePathAsync(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds,
            CancellationToken cancellationToken = default)
        {
            TranslateBrowsePath(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds);
            return default;
        }

        /// <inheritdoc cref="INodeManager.Read"/>
        public virtual ValueTask ReadAsync(
            OperationContext context,
            double maxAge,
            ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            Read(context, maxAge, nodesToRead, values, errors);
            return default;
        }

        /// <inheritdoc cref="INodeManager.Write"/>
        public virtual ValueTask WriteAsync(
            OperationContext context,
            ArrayOf<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            Write(context, nodesToWrite, errors);
            return default;
        }

        /// <inheritdoc cref="INodeManager.HistoryRead"/>
        public virtual ValueTask HistoryReadAsync(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            HistoryRead(
                context,
                details,
                timestampsToReturn,
                releaseContinuationPoints,
                nodesToRead,
                results,
                errors);

            return default;
        }

        /// <inheritdoc cref="INodeManager.HistoryUpdate"/>
        public virtual ValueTask HistoryUpdateAsync(
            OperationContext context,
            Type detailsType,
            ArrayOf<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            HistoryUpdate(context, detailsType, nodesToUpdate, results, errors);
            return default;
        }

        /// <inheritdoc cref="INodeManager.Call"/>
        public virtual ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            Call(context, methodsToCall, results, errors);
            return default;
        }

        /// <summary>
        /// Resolves the method state for a call request.
        /// </summary>
        /// <remarks>
        /// The Quickstart node manager resolves methods inside <see cref="Call"/>, so it does not
        /// expose them to the MasterNodeManager ahead of the call.
        /// </remarks>
        public virtual ValueTask<MethodState> FindMethodStateAsync(
            OperationContext context,
            CallMethodRequest methodToCall,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<MethodState>((MethodState)null);
        }

        /// <inheritdoc cref="INodeManager.SubscribeToEvents"/>
        public virtual ValueTask<ServiceResult> SubscribeToEventsAsync(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(
                SubscribeToEvents(context, sourceId, subscriptionId, monitoredItem, unsubscribe));
        }

        /// <inheritdoc cref="INodeManager.SubscribeToAllEvents"/>
        public virtual ValueTask<ServiceResult> SubscribeToAllEventsAsync(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(
                SubscribeToAllEvents(context, subscriptionId, monitoredItem, unsubscribe));
        }

        /// <inheritdoc cref="INodeManager.ConditionRefresh"/>
        public virtual ValueTask<ServiceResult> ConditionRefreshAsync(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(ConditionRefresh(context, monitoredItems));
        }

        /// <inheritdoc cref="INodeManager.CreateMonitoredItems"/>
        public virtual ValueTask CreateMonitoredItemsAsync(
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
            CreateMonitoredItems(
                context,
                subscriptionId,
                publishingInterval,
                timestampsToReturn,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                createDurable,
                monitoredItemIdFactory);

            return default;
        }

        /// <inheritdoc cref="INodeManager.ModifyMonitoredItems"/>
        public virtual ValueTask ModifyMonitoredItemsAsync(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            CancellationToken cancellationToken = default)
        {
            ModifyMonitoredItems(
                context,
                timestampsToReturn,
                monitoredItems,
                itemsToModify,
                errors,
                filterErrors);

            return default;
        }

        /// <inheritdoc cref="INodeManager.DeleteMonitoredItems"/>
        public virtual ValueTask DeleteMonitoredItemsAsync(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            DeleteMonitoredItems(context, monitoredItems, processedItems, errors);
            return default;
        }

        /// <inheritdoc cref="INodeManager.TransferMonitoredItems"/>
        public virtual ValueTask TransferMonitoredItemsAsync(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors,
            MonitoredItemTransferOptions transferOptions,
            CancellationToken cancellationToken = default)
        {
            TransferMonitoredItems(
                context,
                sendInitialValues,
                monitoredItems,
                processedItems,
                errors,
                transferOptions);

            return default;
        }

        /// <inheritdoc cref="INodeManager.SetMonitoringMode"/>
        public virtual ValueTask SetMonitoringModeAsync(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            SetMonitoringMode(context, monitoringMode, monitoredItems, processedItems, errors);
            return default;
        }

        /// <inheritdoc cref="INodeManager.RestoreMonitoredItems"/>
        public virtual ValueTask RestoreMonitoredItemsAsync(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity,
            CancellationToken cancellationToken = default)
        {
            RestoreMonitoredItems(itemsToRestore, monitoredItems, savedOwnerIdentity);
            return default;
        }

        /// <inheritdoc cref="SessionClosing"/>
        public virtual ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            SessionClosing(context, sessionId, deleteSubscriptions);
            return default;
        }

        /// <summary>
        /// Called when a session is activated and the user identity has changed.
        /// </summary>
        /// <remarks>
        /// The Quickstart node manager does not cache role permissions, so there is nothing to
        /// invalidate.
        /// </remarks>
        public virtual ValueTask SessionActivatedAsync(
            OperationContext context,
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <summary>
        /// Returns true if the node identified by the handle is in the view.
        /// </summary>
        public virtual ValueTask<bool> IsNodeInViewAsync(
            OperationContext context,
            NodeId viewId,
            object nodeHandle,
            CancellationToken cancellationToken = default)
        {
            if (nodeHandle is NodeHandle handle && handle.Node != null)
            {
                return new ValueTask<bool>(IsNodeInView(context, viewId, handle.Node));
            }

            return new ValueTask<bool>(false);
        }

        /// <summary>
        /// Checks if the node is in the view.
        /// </summary>
        /// <remarks>
        /// The base class only knows about the views that are part of its own predefined nodes.
        /// </remarks>
        protected virtual bool IsNodeInView(OperationContext context, NodeId viewId, NodeState node)
        {
            return FindPredefinedNode(viewId, typeof(ViewState)) != null;
        }

        /// <summary>
        /// Validates whether an event monitored item may receive the specified event.
        /// </summary>
        /// <remarks>
        /// The Quickstart samples do not restrict events by role.
        /// </remarks>
        public virtual ValueTask<ServiceResult> ValidateEventRolePermissionsAsync(
            IEventMonitoredItem monitoredItem,
            IFilterTarget filterTarget,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>
        /// Validates the role permissions for the specified node.
        /// </summary>
        /// <remarks>
        /// The Quickstart samples do not restrict access by role.
        /// </remarks>
        public virtual ValueTask<ServiceResult> ValidateRolePermissionsAsync(
            OperationContext operationContext,
            NodeId nodeId,
            PermissionType requestedPermission,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>
        /// Returns true if the node has opted into multiple event consumer task handling.
        /// </summary>
        public virtual bool IsMultipleEventConsumerNode(NodeId nodeId)
        {
            return false;
        }

        /// <summary>
        /// The Quickstart node manager does not support the NodeManagement service set.
        /// </summary>
        public virtual bool AllowNodeManagement => false;

        /// <summary>
        /// Adds a node. Not supported, see <see cref="AllowNodeManagement"/>.
        /// </summary>
        public virtual ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<(ServiceResult, NodeId)>(
                (new ServiceResult(StatusCodes.BadServiceUnsupported), NodeId.Null));
        }

        /// <summary>
        /// Deletes a node. Not supported, see <see cref="AllowNodeManagement"/>.
        /// </summary>
        public virtual ValueTask<ServiceResult> DeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadServiceUnsupported));
        }

        /// <summary>
        /// Adds a reference. Not supported, see <see cref="AllowNodeManagement"/>.
        /// </summary>
        public virtual ValueTask<ServiceResult> AddReferenceAsync(
            OperationContext context,
            AddReferencesItem item,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadServiceUnsupported));
        }

        /// <summary>
        /// Deletes a reference. Not supported, see <see cref="AllowNodeManagement"/>.
        /// </summary>
        public virtual ValueTask<ServiceResult> DeleteReferenceAsync(
            OperationContext context,
            DeleteReferencesItem item,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadServiceUnsupported));
        }
    }
}
