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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.SimpleEvents.Server
{
    /// <summary>
    /// A node manager for a server that reports events of its own event types.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>ModelDesign.xml</c>, and calls <see cref="Configure"/> once the address
    /// space is in place. It also emits the <c>SimpleEventsNodeManagerFactory</c>
    /// the server registers to create this node manager.
    /// </remarks>
    [NodeManager]
    public partial class SimpleEventsNodeManager
    {
        #region Configure
        /// <summary>
        /// Wires the behaviour of the sample once the address space exists.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            // register the encodeable type for the cycle steps so clients can decode them.
            Server.Factory.Builder.AddQuickstartsSimpleEvents().Commit();

            // start a simulation that reports events for the system cycles. the loop is
            // owned by the node manager and stops when the node manager is disposed.
            builder.Simulation(TimeSpan.FromSeconds(3))
                .OnTick((context, elapsed, cancellationToken) => DoSimulationAsync(cancellationToken));
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        private async ValueTask DoSimulationAsync(CancellationToken cancellationToken)
        {
            for (int ii = 1; ii < 3; ii++)
            {
                // construct translation object with default text.
                TranslationInfo info = new TranslationInfo(
                    "SystemCycleStarted",
                    "en-US",
                    "The system cycle '{0}' has started.",
                    ++m_cycleId);

                // construct the event.
#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
                SystemCycleStartedEventState e = new SystemCycleStartedEventState(null);
#pragma warning restore CA2000

                // Build the event from its type model first. A freshly constructed event
                // is an empty shell: the fields this event type declares are created only
                // by Create, which is also what gives them their browse names. A client
                // selects an event field by browse path, so without this the event
                // arrives with every one of the sample's own fields empty.
                e.Create(
                    SystemContext,
                    NodeId.Null,
                    new QualifiedName(BrowseNames.SystemCycleStartedEventType, NamespaceIndex),
                    LocalizedText.Null,
                    false);

                e.Initialize(
                    SystemContext,
                    null,
                    (EventSeverity)ii,
                    new LocalizedText(info));

                e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceName, "System", false);
                e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceNode, Opc.Ua.ObjectIds.Server, false);

                var cycleId = new QualifiedName(BrowseNames.CycleId, NamespaceIndex);
                var currentStep = new QualifiedName(BrowseNames.CurrentStep, NamespaceIndex);
                var steps = new QualifiedName(BrowseNames.Steps, NamespaceIndex);

                e.SetChildValue(SystemContext, cycleId, m_cycleId.ToString(), false);

                CycleStepDataType step = new CycleStepDataType();
                step.Name = "Step 1";
                step.Duration = 1000;

                e.SetChildValue(SystemContext, currentStep, step, false);
                e.SetChildValue(SystemContext, steps, new[] { step, step }.ToArrayOf(), false);

                await Server.ReportEventAsync(SystemContext, e, cancellationToken).ConfigureAwait(false);
            }
        }
        #endregion

        #region Private Fields
        private int m_cycleId;
        #endregion
    }
}
