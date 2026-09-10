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
    /// <remarks>
    /// <para>
    /// The node manager stores no node objects at all: the register number and the
    /// index of the variable within it are encoded in the numeric identifier, and a
    /// node is synthesized for the duration of one operation. That is what makes the
    /// sample fast, and it is expressed as two virtual node families registered with
    /// <see cref="VirtualNodeBuilderExtensions.ResolveNodes"/> - one for the register
    /// folders and one for the variables below them. The families are kept apart
    /// because only the variables carry a read handler, and a family which wires one
    /// may only ever resolve variables.
    /// </para>
    /// <para>
    /// The read handler is what keeps a read honest once somebody subscribes: the
    /// server holds on to the node of a monitored variable, but the register never
    /// writes into that node - it queues the changed value straight into the monitored
    /// item, bypassing the sampling machinery of the server. Reading through the
    /// register rather than off the node therefore answers with the current value
    /// instead of the one the variable was synthesized with.
    /// </para>
    /// <para>
    /// There is no information model: a manager whose whole address space is computed
    /// has nothing to declare in one. It therefore drives the fluent builder itself in
    /// <see cref="CreateAddressSpaceAsync"/>, in the sequence the node manager
    /// generated from a ModelDesign follows.
    /// </para>
    /// </remarks>
    public class PerfTestNodeManager : FluentNodeManagerBase
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
        /// The registers are not nodes of this manager either, so their references to the
        /// Objects folder are written by hand rather than mirrored from a registered node.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // the clock of the server, so that the update loop of the simulated registers
            // runs on the same time source as the rest of the server and a test can drive
            // it with a FakeTimeProvider. ITimeProviderProvider is the opt-in seam for
            // reaching it; an IServerInternal which does not implement it falls back to
            // the system clock.
            m_system.Initialize(
                Server.Telemetry,
                (Server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System);

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

            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndex);
            Configure(builder);

            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            await SealConfigurationAsync(builder, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Registers the two virtual node families and the subscription forwarding.
        /// </summary>
        private void Configure(INodeManagerBuilder builder)
        {
            builder.ResolveNodes(IsRegisterId, ResolveRegisterAsync);

            builder
                .ResolveNodes(IsRegisterVariableId, ResolveRegisterVariableAsync)
                .OnRead(ReadRegisterValue);

            builder
                .OnMonitoredItemsCreated(SubscribeToRegistersAsync)
                .OnMonitoredItemsDeleted(UnsubscribeFromRegistersAsync);
        }
        #endregion

        #region Virtual Registers
        /// <summary>
        /// Recognizes the identifier of a register: the high byte names the register a
        /// variable belongs to, so it is zero for the register itself.
        /// </summary>
        private static bool IsRegisterId(NodeId nodeId)
        {
            return nodeId.TryGetValue(out uint id) && (id & 0xFF000000) == 0;
        }

        /// <summary>
        /// Recognizes the identifier of a variable within a register.
        /// </summary>
        private static bool IsRegisterVariableId(NodeId nodeId)
        {
            return nodeId.TryGetValue(out uint id) && (id & 0xFF000000) != 0;
        }

        /// <summary>
        /// Synthesizes the register a node id names.
        /// </summary>
        private ValueTask<NodeState> ResolveRegisterAsync(
            ISystemContext context,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            nodeId.TryGetValue(out uint id);

            MemoryRegister register = m_system.GetRegister((int)(id & 0x00FFFFFF));

            if (register == null)
            {
                return default;
            }

            return new ValueTask<NodeState>(ModelUtils.GetRegister(register, NamespaceIndex));
        }

        /// <summary>
        /// Synthesizes the variable a node id names, or nothing when the index is past
        /// the end of the register.
        /// </summary>
        private ValueTask<NodeState> ResolveRegisterVariableAsync(
            ISystemContext context,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            nodeId.TryGetValue(out uint id);

            MemoryRegister register = m_system.GetRegister((int)((id & 0xFF000000) >> 24));

            if (register == null)
            {
                return default;
            }

            BaseDataVariableState variable = ModelUtils.GetRegisterVariable(
                register, (int)(id & 0x00FFFFFF), NamespaceIndex);

            if (variable == null)
            {
                return default;
            }

            return new ValueTask<NodeState>(variable);
        }

        /// <summary>
        /// Reads the current value out of the register rather than off the node.
        /// </summary>
        /// <remarks>
        /// The register queues its changes into the monitored items directly, so the
        /// node of a subscribed variable would otherwise keep answering with the value
        /// it was synthesized with for as long as the server holds on to it.
        /// </remarks>
        private static ServiceResult ReadRegisterValue(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            if (node.Handle is not MemoryRegister register || node is not BaseVariableState variable)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            value = Variant.From(register.Read((int)variable.NumericId));

            return ServiceResult.Good;
        }
        #endregion

        #region Monitoring
        /// <summary>
        /// Wires the created items straight into their register, which queues the
        /// changed values from its own timer and bypasses the sampling machinery of the
        /// server.
        /// </summary>
        private static ValueTask SubscribeToRegistersAsync(
            ISystemContext context,
            ArrayOf<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                if (Resolve(monitoredItems[ii], out MemoryRegister register, out BaseVariableState variable))
                {
                    register.Subscribe((int)variable.NumericId, (IDataChangeMonitoredItem2)monitoredItems[ii]);
                }
            }

            return default;
        }

        /// <summary>
        /// Takes the deleted items off their register again.
        /// </summary>
        private static ValueTask UnsubscribeFromRegistersAsync(
            ISystemContext context,
            ArrayOf<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                if (Resolve(monitoredItems[ii], out MemoryRegister register, out BaseVariableState variable))
                {
                    register.Unsubscribe((int)variable.NumericId, (IDataChangeMonitoredItem2)monitoredItems[ii]);
                }
            }

            return default;
        }

        /// <summary>
        /// Recovers the register and the variable a monitored item was created for.
        /// </summary>
        private static bool Resolve(
            IMonitoredItem monitoredItem,
            out MemoryRegister register,
            out BaseVariableState variable)
        {
            register = null;
            variable = null;

            if (monitoredItem is not IDataChangeMonitoredItem2 ||
                monitoredItem.ManagerHandle is not NodeHandle handle ||
                handle.Node == null)
            {
                return false;
            }

            register = handle.Node.Handle as MemoryRegister;
            variable = handle.Node as BaseVariableState;

            return register != null && variable != null;
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        #endregion
    }
}
