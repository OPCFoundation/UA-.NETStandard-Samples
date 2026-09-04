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
    /// <c>FluentNodeManagerBase</c>, loads the predefined nodes generated from
    /// <c>ModelDesign.xml</c> - Boiler #1 comes out of it as a typed
    /// <see cref="BoilerState"/> - and calls <see cref="Configure"/> once the
    /// address space is in place. It also emits the <c>BoilerNodeManagerFactory</c>
    /// the server registers to create this node manager.
    /// <para>
    /// The nodes the sample creates in code live in a second namespace next to the
    /// one of the type model. <c>AdditionalNamespaceUris</c> hands that namespace
    /// to the generated constructor and factory, so the master node manager routes
    /// requests for it here from the start and the <see cref="New"/> override can
    /// mint node ids in it. The uri is spelled out because the generator reads the
    /// attribute before the <c>Namespaces</c> constants it emits itself exist.
    /// </para>
    /// </remarks>
    [NodeManager(AdditionalNamespaceUris = new[] { "http://opcfoundation.org/Quickstarts/Boiler/Instance" })]
    public partial class BoilerNodeManager
    {
        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <remarks>
        /// The builder mints the node ids of every instance it creates through this
        /// method, the root as well as all of its children, so the whole subtree of
        /// Boiler #2 ends up in the instance namespace.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            // generate a new numeric id in the instance namespace.
            return new NodeId(++m_nodeIdCounter, NamespaceIndexes[1]);
        }
        #endregion

        #region Configure
        /// <summary>
        /// Builds the dynamic part of the address space and wires the behaviour of
        /// the sample once the predefined nodes are in place.
        /// </summary>
        /// <remarks>
        /// The generator also emits a typed <c>IBoilerNodeManagerBuilder</c> with a
        /// <c>Boiler1</c> accessor, but its proxy is not the builder the simulation
        /// extension expects and it declares the node by its node class rather than
        /// by its type, so the untyped builder is used and Boiler #1 is resolved by
        /// its node id as the typed boiler the model loader created.
        /// </remarks>
        partial void Configure(INodeManagerBuilder builder)
        {
            // the typed Boiler1 node was created when the model was loaded.
            m_boiler1 = builder
                .Node<BoilerState>(new NodeId(Objects.Boiler1, NamespaceIndexes[0]))
                .Node;

            // create a second boiler from the type model. the builder materializes the
            // children the type declares, assigns unique node ids through New and
            // registers the whole subtree with this node manager. the inverse Organizes
            // reference places it below the Objects folder, which another node manager
            // owns: the generated partial publishes the forward edge once Configure
            // returns, the same route the model takes for Boiler #1.
            m_boiler2 = builder
                .CreateInstance(
                    new QualifiedName("Boiler #2", NamespaceIndexes[1]),
                    parent => new BoilerState(parent))
                .Configure(node => node.UnderObjectsFolder())
                .Node;

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
        private BoilerState m_boiler1;
        private BoilerState m_boiler2;
        private uint m_nodeIdCounter;
        #endregion
    }
}
