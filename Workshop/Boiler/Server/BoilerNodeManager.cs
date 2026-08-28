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
using Opc.Ua.Server.Fluent;

namespace Quickstarts.Boiler.Server
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>ModelDesign.xml</c> - Boiler #1 comes out of it as a typed
    /// <see cref="BoilerState"/> - and calls <see cref="Configure"/> once the
    /// address space is in place. It also emits the <c>BoilerNodeManagerFactory</c>
    /// the server registers to create this node manager.
    /// </remarks>
    [NodeManager]
    public partial class BoilerNodeManager
    {
        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            // generate a new numeric id in the instance namespace.
            return new NodeId(++m_nodeIdCounter, NamespaceIndexes[1]);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Captures the external references collection of the startup for <see cref="Configure"/>.
        /// </summary>
        /// <remarks>
        /// The fluent builder cannot publish references on nodes another node manager
        /// owns, so Configure links the second boiler below the Objects folder through
        /// this collection - the same route the Organizes reference the model declares
        /// for Boiler #1 takes. The master node manager distributes the collected
        /// references once every address space exists.
        /// </remarks>
        protected override ValueTask LoadPredefinedNodesAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            m_externalReferences = externalReferences;
            return base.LoadPredefinedNodesAsync(context, externalReferences, cancellationToken);
        }
        #endregion

        #region Configure
        /// <summary>
        /// Builds the dynamic part of the address space and wires the behaviour of
        /// the sample once the predefined nodes are in place.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            // the generated constructor only registers the namespace of the type
            // model; add a second namespace for the dynamically created nodes. the
            // master node manager built its routing table when this node manager
            // reported one namespace, so the new namespace is registered with it too.
            SetNamespaces(Namespaces.Boiler, Namespaces.Boiler + "/Instance");
            Server.NodeManager.RegisterNamespaceManager(Namespaces.Boiler + "/Instance", this);

            // find the typed Boiler1 node that was created when the model was loaded.
            m_boiler1 = FindPredefinedNode<BoilerState>(new NodeId(Objects.Boiler1, NamespaceIndexes[0]));

            // create a second boiler node.
#pragma warning disable CA2000 // Justification: ownership is transferred to the predefined node collection.
            m_boiler2 = new BoilerState(null);
#pragma warning restore CA2000

            // initialize it from the type model and assign unique node ids.
            m_boiler2.Create(
                SystemContext,
                NodeId.Null,
                new QualifiedName("Boiler #2", NamespaceIndexes[1]),
                LocalizedText.Null,
                true);

            // store it and all of its children in the pre-defined nodes dictionary for easy look up.
            AddPredefinedNodeSynchronously(m_boiler2);

            // link it below the Objects folder, which another node manager owns.
            AddExternalReference(
                Opc.Ua.ObjectIds.ObjectsFolder,
                Opc.Ua.ReferenceTypeIds.Organizes,
                false,
                m_boiler2.NodeId,
                m_externalReferences);

            // start a simulation that changes the values of the nodes. the loop is
            // owned by the node manager and stops when the node manager is disposed.
            builder.Simulation(TimeSpan.FromSeconds(1))
                .OnTick((context, elapsed) => DoSimulation());
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        private void DoSimulation()
        {
            double value1 = m_boiler1.Drum.LevelIndicator.Output.Value;
            value1 = ((int)(++value1)) % 100;
            m_boiler1.Drum.LevelIndicator.Output.Value = value1;
            m_boiler1.ClearChangeMasks(SystemContext, true);

            double value2 = m_boiler2.Drum.LevelIndicator.Output.Value;
            value2 = ((int)(++value2)) % 20;
            m_boiler2.Drum.LevelIndicator.Output.Value = value2;
            m_boiler2.ClearChangeMasks(SystemContext, true);
        }
        #endregion

        #region Private Fields
        private IDictionary<NodeId, IList<IReference>> m_externalReferences;
        private BoilerState m_boiler1;
        private BoilerState m_boiler2;
        private uint m_nodeIdCounter;
        #endregion
    }
}
