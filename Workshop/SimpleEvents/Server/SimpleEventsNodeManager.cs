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
using System.Runtime.CompilerServices;
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
        /// <remarks>
        /// The system object comes from the model, which also declares it as an event
        /// notifier below the server object, so the base class has already registered
        /// it as a root notifier while the predefined nodes were loaded - a client
        /// subscribing to the server object receives its events.
        /// </remarks>
        partial void Configure(ISimpleEventsNodeManagerBuilder builder)
        {
            // register the encodeable type for the cycle steps so clients can decode them.
            Server.Factory.Builder.AddQuickstartsSimpleEvents().Commit();

            // publish the cycle events on the system notifier. the event source registry
            // of the fluent SDK owns the lifecycle: the iterator starts when the first
            // client subscribes to events on the system or the server object, is cancelled
            // when the last interested monitored item disappears, and is disposed with
            // the node manager.
            builder.System.Publish(GenerateSystemCyclesAsync);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reports two started cycles every three seconds while at least one client
        /// is monitoring events on the system notifier.
        /// </summary>
        private async IAsyncEnumerable<SystemCycleStartedEventState> GenerateSystemCyclesAsync(
            BaseObjectState notifier,
            ISystemContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                for (int ii = 1; ii < 3; ii++)
                {
                    // construct translation object with default text.
                    TranslationInfo info = new TranslationInfo(
                        "SystemCycleStarted",
                        "en-US",
                        "The system cycle '{0}' has started.",
                        ++m_cycleId);

                    // construct the event.
#pragma warning disable CA2000 // Justification: Node ownership is transferred to the event source registry.
                    SystemCycleStartedEventState e = new SystemCycleStartedEventState(null);
#pragma warning restore CA2000

                    // Build the event from its type model first. A freshly constructed event
                    // is an empty shell: the fields this event type declares are created only
                    // by Create, which is also what gives them their browse names. A client
                    // selects an event field by browse path, so without this the event
                    // arrives with every one of the sample's own fields empty.
                    e.Create(
                        context,
                        NodeId.Null,
                        new QualifiedName(BrowseNames.SystemCycleStartedEventType, NamespaceIndex),
                        LocalizedText.Null,
                        false);

                    e.Initialize(
                        context,
                        null,
                        (EventSeverity)ii,
                        new LocalizedText(info));

                    // the registry populates SourceNode and SourceName from the notifier on
                    // the way out, so only the sample's own fields are set here.
                    var cycleId = new QualifiedName(BrowseNames.CycleId, NamespaceIndex);
                    var currentStep = new QualifiedName(BrowseNames.CurrentStep, NamespaceIndex);
                    var steps = new QualifiedName(BrowseNames.Steps, NamespaceIndex);

                    e.SetChildValue(context, cycleId, m_cycleId.ToString(), false);

                    CycleStepDataType step = new CycleStepDataType();
                    step.Name = "Step 1";
                    step.Duration = 1000;

                    e.SetChildValue(context, currentStep, step, false);
                    e.SetChildValue(context, steps, new[] { step, step }.ToArrayOf(), false);

                    yield return e;
                }
            }
        }
        #endregion

        #region Private Fields
        private int m_cycleId;
        #endregion
    }
}
