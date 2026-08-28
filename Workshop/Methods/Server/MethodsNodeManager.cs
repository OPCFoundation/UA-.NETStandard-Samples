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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.MethodsServer
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class MethodsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new MethodsNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.Methods];
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class MethodsNodeManager : AsyncCustomNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public MethodsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.Methods)
        {
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
                m_processTimer?.Dispose();
                m_processTimer = null;
                m_stateNode = null;
            }

            base.Dispose(disposing);
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

            // create a object to represent the process being controlled.
#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            BaseObjectState process = new BaseObjectState(null);
#pragma warning restore CA2000

            process.NodeId = new NodeId(1, NamespaceIndex);
            process.BrowseName = new QualifiedName("My Process", NamespaceIndex);
            process.DisplayName = new LocalizedText(process.BrowseName.Name);
            process.TypeDefinitionId = ObjectTypeIds.BaseObjectType;

            // ensure the process object can be found via the server object.
            IList<IReference> references = null;

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

            process.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, process.NodeId));

            // a property to report the process state.
            PropertyState<uint> state = m_stateNode = PropertyState<uint>.With<VariantBuilder>(process);

            state.NodeId = new NodeId(2, NamespaceIndex);
            state.BrowseName = new QualifiedName("State", NamespaceIndex);
            state.DisplayName = new LocalizedText(state.BrowseName.Name);
            state.TypeDefinitionId = VariableTypeIds.PropertyType;
            state.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            state.DataType = DataTypeIds.UInt32;
            state.ValueRank = ValueRanks.Scalar;

            process.AddChild(state);

            // a method to start the process.
#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            MethodState start = new MethodState(process);
#pragma warning restore CA2000

            start.NodeId = new NodeId(3, NamespaceIndex);
            start.BrowseName = new QualifiedName("Start", NamespaceIndex);
            start.DisplayName = new LocalizedText(start.BrowseName.Name);
            start.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            start.UserExecutable = true;
            start.Executable = true;

            // add input arguments.
            start.InputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(start);
            start.InputArguments.NodeId = new NodeId(4, NamespaceIndex);
            start.InputArguments.BrowseName = new QualifiedName(BrowseNames.InputArguments);
            start.InputArguments.DisplayName = new LocalizedText(start.InputArguments.BrowseName.Name);
            start.InputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
            start.InputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            start.InputArguments.DataType = DataTypeIds.Argument;
            start.InputArguments.ValueRank = ValueRanks.OneDimension;

            Argument[] args = new Argument[2];
            args[0] = new Argument();
            args[0].Name = "Initial State";
            args[0].Description = new LocalizedText("The initialize state for the process.");
            args[0].DataType = DataTypeIds.UInt32;
            args[0].ValueRank = ValueRanks.Scalar;

            args[1] = new Argument();
            args[1].Name = "Final State";
            args[1].Description = new LocalizedText("The final state for the process.");
            args[1].DataType = DataTypeIds.UInt32;
            args[1].ValueRank = ValueRanks.Scalar;

            start.InputArguments.Value = args.ToArrayOf();

            // add output arguments.
            start.OutputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(start);
            start.OutputArguments.NodeId = new NodeId(5, NamespaceIndex);
            start.OutputArguments.BrowseName = new QualifiedName(BrowseNames.OutputArguments);
            start.OutputArguments.DisplayName = new LocalizedText(start.OutputArguments.BrowseName.Name);
            start.OutputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
            start.OutputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            start.OutputArguments.DataType = DataTypeIds.Argument;
            start.OutputArguments.ValueRank = ValueRanks.OneDimension;

            args = new Argument[2];
            args[0] = new Argument();
            args[0].Name = "Revised Initial State";
            args[0].Description = new LocalizedText("The revised initialize state for the process.");
            args[0].DataType = DataTypeIds.UInt32;
            args[0].ValueRank = ValueRanks.Scalar;

            args[1] = new Argument();
            args[1].Name = "Revised Final State";
            args[1].Description = new LocalizedText("The revised final state for the process.");
            args[1].DataType = DataTypeIds.UInt32;
            args[1].ValueRank = ValueRanks.Scalar;

            start.OutputArguments.Value = args.ToArrayOf();

            process.AddChild(start);

            // save in dictionary.
            await AddPredefinedNodeAsync(SystemContext, process, cancellationToken).ConfigureAwait(false);

            // set up method handlers.
            start.OnCallMethod2Async = OnStartAsync;
        }

        private object m_processLock = new object();
        private uint m_state;
        private uint m_finalState;
        private Timer m_processTimer;
        private PropertyState<uint> m_stateNode;

        /// <summary>
        /// Called when the Start method is called.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="method">The method.</param>
        /// <param name="objectId">The id of the object the method belongs to.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "GenericMethodCalledEventHandler2Async target: the SDK hands the handler a mutable List<Variant> to fill, because ArrayOf<Variant> is immutable.")]
        public async ValueTask<ServiceResult> OnStartAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            // check the data type of the input arguments.
            uint? initialState = inputArguments[0].AsBoxedObject() as uint?;
            uint? finalState = inputArguments[1].AsBoxedObject() as uint?;

            if (initialState == null || finalState == null)
            {
                return StatusCodes.BadTypeMismatch;
            }

            StartProcess(initialState.Value, finalState.Value, outputArguments);

            // signal update to state node.
            m_stateNode.Value = initialState.Value;
            await m_stateNode.ClearChangeMasksAsync(SystemContext, true, cancellationToken).ConfigureAwait(false);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Starts the process, replacing one which is still running.
        /// </summary>
        private void StartProcess(uint initialState, uint finalState, List<Variant> outputArguments)
        {
            lock (m_processLock)
            {
                // check if the process is running.
                if (m_processTimer != null)
                {
                    m_processTimer.Dispose();
                    m_processTimer = null;
                }

                // start the process.
                m_state = initialState;
                m_finalState = finalState;
                m_processTimer = new Timer(OnUpdateProcess, null, 1000, 1000);

                // the calling function sets default values for all output arguments.
                // only need to update them here.
                outputArguments[0] = Variant.From(m_state);
                outputArguments[1] = Variant.From(m_finalState);
            }
        }

        /// <summary>
        /// Called when updating the process.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnUpdateProcess(object state)
        {
            try
            {
                uint currentState;

                lock (m_processLock)
                {
                    // check if increasing.
                    if (m_state < m_finalState)
                    {
                        m_state++;
                    }

                    // check if decreasing.
                    else if (m_state > m_finalState)
                    {
                        m_state--;
                    }

                    // check if all done.
                    else
                    {
                        m_processTimer.Dispose();
                        m_processTimer = null;
                    }

                    currentState = m_state;
                }

                // signal update to state node. the synchronous flush is safe on a timer
                // thread: it drives the asynchronous notification sinks inline.
                m_stateNode.Value = currentState;
                m_stateNode.ClearChangeMasks(SystemContext, true);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating process.");
            }
        }
        #endregion
    }
}
