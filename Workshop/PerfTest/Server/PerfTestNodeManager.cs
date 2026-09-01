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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.PerfTestServer
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class PerfTestNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new PerfTestNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.PerfTest];
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class PerfTestNodeManager : AsyncCustomNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public PerfTestNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(
                server,
                configuration,
                server.Telemetry.CreateLogger<PerfTestNodeManager>(),
                Namespaces.PerfTest)
        {
            SystemContext.SystemHandle = m_system = new UnderlyingSystem();
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId;
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
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            m_system.Initialize(Server.Telemetry);

            IList<MemoryRegister> registers = m_system.GetRegisters();

            for (int ii = 0; ii < registers.Count; ii++)
            {
                NodeId targetId = ModelUtils.GetRegisterId(registers[ii], NamespaceIndex);

                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, targetId));
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        /// <remarks>
        /// The node manager stores no node objects: the register number and the index of
        /// the variable within it are encoded in the numeric identifier, and a node is
        /// synthesized for the duration of the operation. There is no shared state to
        /// protect, so reads run in parallel without any lock.
        /// </remarks>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // quickly exclude nodes that are not in the namespace.
            if (!IsNodeIdInNamespace(nodeId))
            {
                return new ValueTask<NodeHandle>();
            }

            if (!nodeId.TryGetValue(out uint id))
            {
                return new ValueTask<NodeHandle>();
            }

            NodeHandle handle = new NodeHandle();
            handle.NodeId = nodeId;
            handle.Validated = true;

            // find register
            int registerId = (int)((id & 0xFF000000) >> 24);
            int index = (int)(id & 0x00FFFFFF);

            if (registerId == 0)
            {
                MemoryRegister register = m_system.GetRegister(index);

                if (register == null)
                {
                    return new ValueTask<NodeHandle>();
                }

                handle.Node = ModelUtils.GetRegister(register, NamespaceIndex);
            }

            // find register variable.
            else
            {
                MemoryRegister register = m_system.GetRegister(registerId);

                if (register == null)
                {
                    return new ValueTask<NodeHandle>();
                }

                // find register variable.
                BaseDataVariableState variable = ModelUtils.GetRegisterVariable(register, index, NamespaceIndex);

                if (variable == null)
                {
                    return new ValueTask<NodeHandle>();
                }

                handle.Node = variable;
            }

            return new ValueTask<NodeHandle>(handle);
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // not valid if no root.
            if (handle == null)
            {
                return new ValueTask<NodeState>();
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return new ValueTask<NodeState>(handle.Node);
            }

            // TBD

            return new ValueTask<NodeState>();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Called when a batch of monitored items has been created. The items are wired
        /// straight into the register, which queues the changed values from its own timer
        /// and bypasses the sampling machinery of the server.
        /// </summary>
        protected override void OnCreateMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                NodeHandle handle = monitoredItems[ii].ManagerHandle as NodeHandle;

                if (handle == null)
                {
                    continue;
                }

                MemoryRegister register = handle.Node.Handle as MemoryRegister;
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (register != null)
                {
                    register.Subscribe((int)variable.NumericId, (IDataChangeMonitoredItem2)monitoredItems[ii]);
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has been deleted.
        /// </summary>
        protected override ValueTask OnDeleteMonitoredItemsCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                NodeHandle handle = monitoredItems[ii].ManagerHandle as NodeHandle;

                if (handle == null)
                {
                    continue;
                }

                MemoryRegister register = handle.Node.Handle as MemoryRegister;
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (register != null)
                {
                    register.Unsubscribe((int)variable.NumericId, (IDataChangeMonitoredItem2)monitoredItems[ii]);
                }
            }

            return default;
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        #endregion
    }
}
