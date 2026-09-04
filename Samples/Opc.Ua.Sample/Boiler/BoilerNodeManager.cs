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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Boiler
{
    /// <summary>
    /// A node manager for the boilers exposed by the server.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>BoilerDesign.xml</c> - Boiler #1 comes out of it as a typed
    /// <see cref="BoilerState"/> - and calls <see cref="Configure"/> once the
    /// address space is in place. It also emits the <c>BoilerNodeManagerFactory</c>
    /// the server registers to create this node manager. The instance namespace
    /// named by <c>AdditionalNamespaceUris</c> is reported by the generated
    /// constructor and factory alongside the namespace of the type model, so the
    /// master node manager routes the dynamically created nodes here from the start.
    /// </remarks>
    [NodeManager(
        NamespaceUri = "http://opcfoundation.org/UA/Boiler/",
        AdditionalNamespaceUris = new[] { "http://opcfoundation.org/UA/Boiler/Instance" })]
    public partial class BoilerNodeManager
    {
        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            uint id = Utils.IncrementIdentifier(ref m_lastUsedId);
            return new NodeId(id, NamespaceIndexes[1]);
        }
        #endregion

        #region Configure
        /// <summary>
        /// Builds the dynamic part of the address space and wires the behaviour of
        /// the sample once the predefined nodes are in place.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            m_boilers = new List<BoilerState>();

            // the boiler the type model declares came out of the generated load as
            // a typed node; its simulation used to be started when the passive node
            // was replaced by the typed one, which the generated load made obsolete.
            BoilerState boiler1 = FindPredefinedNode<BoilerState>(
                new NodeId(Objects.Boilers_Boiler1, NamespaceIndexes[0]));

            m_boilers.Add(boiler1);

            StartSimulation(boiler1);

            // create a second boiler dynamically.
            CreateBoiler(builder, 2);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a boiler and adds it to the address space.
        /// </summary>
        /// <param name="builder">The builder of the node manager.</param>
        /// <param name="unitNumber">The unit number for the boiler.</param>
        private void CreateBoiler(INodeManagerBuilder builder, int unitNumber)
        {
            string name = Utils.Format("Boiler #{0}", unitNumber);

            // the boilers folder the type model declares.
            NodeId boilersFolder = new NodeId(Objects.Boilers, NamespaceIndexes[0]);

            // create a boiler from the type model. the builder materializes the
            // children the type declares, assigns unique node ids to all of them
            // through New and registers the instance with the node manager. the
            // typed create also runs the OnAfterCreate hook the sample uses to wire
            // the state machine of the simulation. the inverse Organizes reference
            // places the boiler below the boilers folder; the matching forward
            // reference on the folder is added when Configure completes.
            BoilerState boiler = builder
                .CreateInstance(
                    new QualifiedName(name, NamespaceIndexes[1]),
                    parent => new BoilerState(parent))
                .Configure(node => node.OrganizedBy(boilersFolder))
                .Node;

            string unitLabel = Utils.Format("{0}0", unitNumber);

            UpdateDisplayName(boiler.InputPipe, unitLabel);
            UpdateDisplayName(boiler.Drum, unitLabel);
            UpdateDisplayName(boiler.OutputPipe, unitLabel);
            UpdateDisplayName(boiler.LevelController, unitLabel);
            UpdateDisplayName(boiler.FlowController, unitLabel);
            UpdateDisplayName(boiler.CustomController, unitLabel);

            m_boilers.Add(boiler);

            StartSimulation(boiler);
        }

        /// <summary>
        /// Autostarts the boiler simulation state machine.
        /// </summary>
        private void StartSimulation(BoilerState boiler)
        {
            // the clock of the server, so that the simulation runs on the same time source
            // as the rest of the server and a test can drive it with a FakeTimeProvider.
            // ITimeProviderProvider is the opt-in seam for reaching it; an IServerInternal
            // which does not implement it falls back to the system clock.
            boiler.TimeProvider = (Server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

            MethodState start = boiler.Simulation.Start;
            ArrayOf<Variant> inputArguments = ArrayOf<Variant>.Empty;
            List<Variant> outputArguments = new List<Variant>();
            List<ServiceResult> errors = new List<ServiceResult>();
            start.Call(SystemContext, boiler.NodeId, inputArguments, errors, outputArguments);
        }

        /// <summary>
        /// Updates the display name for an instance with the unit label name.
        /// </summary>
        /// <param name="instance">The instance to update.</param>
        /// <param name="unitLabel">The label to apply.</param>
        /// <remarks>This method assumes the DisplayName has the form NameX001 where X0 is the unit label placeholder.</remarks>
        private static void UpdateDisplayName(BaseInstanceState instance, string unitLabel)
        {
            LocalizedText displayName = instance.DisplayName;

            if (!displayName.IsNull)
            {
                string text = displayName.Text;

                if (text != null)
                {
                    text = text.Replace("X0", unitLabel, StringComparison.Ordinal);
                }

                displayName = new LocalizedText(displayName.Locale, text);
            }

            instance.DisplayName = displayName;
        }
        #endregion

        #region Private Fields
        private uint m_lastUsedId;
        private List<BoilerState> m_boilers;
        #endregion
    }
}
