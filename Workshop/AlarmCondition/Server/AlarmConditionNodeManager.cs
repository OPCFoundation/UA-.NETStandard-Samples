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
using Opc.Ua.Server.Alarms;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class AlarmConditionServerNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new AlarmConditionServerNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.AlarmCondition];
    }

    /// <summary>
    /// A node manager for a simple server that exposes several Areas, Sources and Conditions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This node manager presumes that the information model consists of a hierachy of predefined
    /// Areas with a number of Sources contained within them. Each individual Source is
    /// identified by a fully qualified path. The underlying system knows how to access the source
    /// configuration when it is provided the fully qualified path.
    /// </para>
    /// <para>
    /// The node manager is a hand-written <see cref="FluentNodeManagerBase"/>: it drives the
    /// fluent builder itself in <see cref="CreateAddressSpaceAsync"/>, the way the generated
    /// node manager of a ModelDesign does. The area tree is built from the configuration and
    /// registered as predefined nodes, so the services of the server find an area, a source
    /// or an alarm by its NodeId without the node manager resolving anything on demand. The
    /// builder adds what the tree cannot say on its own: the notifier link from the server
    /// object to the root areas, which <see cref="FluentNodeManagerBase.CompleteConfigureAsync"/>
    /// publishes to the node manager which owns the server object, and the one second cycle
    /// which drives the simulation and reports the system events.
    /// </para>
    /// </remarks>
    public class AlarmConditionServerNodeManager : FluentNodeManagerBase
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public AlarmConditionServerNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(
                server,
                configuration,
                server.Telemetry.CreateLogger<AlarmConditionServerNodeManager>(),
                Namespaces.AlarmCondition)
        {
            // the clock of the server, so that the simulation and the timestamps it writes
            // run on the same time source as the rest of the server and a test can drive
            // them with a FakeTimeProvider. ITimeProviderProvider is the opt-in seam for
            // reaching it; an IServerInternal which does not implement it falls back to
            // the system clock.
            SystemContext.SystemHandle = m_system = new UnderlyingSystem(
                server.Telemetry,
                (server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System);

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<AlarmConditionServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new AlarmConditionServerConfiguration();
            }

            // create the table to store the available sources.
            m_sources = new Dictionary<string, SourceState>();

            // one engine decides the group suppression of every source of the server. It
            // has to exist before the first source is created, because a source registers
            // its group with it as soon as its alarms exist.
            m_suppressionEngine = new AlarmSuppressionEngine();
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// The engine which applies the Part 9 suppression patterns to the alarm groups.
        /// </summary>
        public AlarmSuppressionEngine SuppressionEngine => m_suppressionEngine;
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <remarks>
        /// The simulation cycle belongs to the builder, and the base class stops it.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_system != null)
                {
                    m_system.Dispose();
                }

                m_suppressionEngine?.Dispose();
                m_suppressionEngine = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        /// <remarks>
        /// This method is called by the NodeState.Create() method which initializes a Node from
        /// the type model. During initialization a number of child nodes are created and need to
        /// have NodeIds assigned to them. This implementation constructs NodeIds by constructing
        /// strings. Other implementations could assign unique integers or Guids and save the new
        /// Node in a dictionary for later lookup.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return ModelUtils.ConstructIdForComponent(node, NamespaceIndex);
        }
        #endregion

        #region IAsyncNodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The sequence is the one the generated node manager of a ModelDesign follows: the
        /// predefined nodes first, then the builder, then
        /// <see cref="FluentNodeManagerBase.CompleteConfigureAsync"/>, which publishes the
        /// references written in <see cref="Configure"/> to the node managers which own their
        /// targets, and finally <see cref="NodeManagerBuilder.Seal"/>, which starts the
        /// simulation cycle.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            var rootAreas = new List<AreaState>();

            if (m_configuration.Areas != null)
            {
                for (int ii = 0; ii < m_configuration.Areas.Count; ii++)
                {
                    // recursively process each area. An area and the sub-areas below it are
                    // registered together, because a sub-area is a child of its area.
                    AreaState area = CreateAndIndexAreas(null, m_configuration.Areas[ii]);
                    await AddPredefinedNodeAsync(SystemContext, area, cancellationToken).ConfigureAwait(false);
                    rootAreas.Add(area);
                }
            }

            // a source is not a child of its areas: it hangs below them with HasEventSource,
            // and more than one area may declare it. It is registered on its own, and once,
            // along with the dialog, the alarms and the group it created for itself.
            foreach (SourceState source in m_sources.Values)
            {
                await AddPredefinedNodeAsync(SystemContext, source, cancellationToken).ConfigureAwait(false);
            }

            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndex)
                .Configure(nodeManager => Configure(nodeManager, rootAreas));

            // the inverse HasNotifier references Configure wrote to the Server object belong
            // to the node manager which owns it. This pass hands them over, and registers
            // each root area as a root notifier on the strength of its reference.
            await CompleteConfigureAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // start the simulation.
            m_system.StartSimulation();
            builder.Seal();
        }

        /// <summary>
        /// Wires what the builder can express: whom the root areas notify, and the cycle
        /// which drives the simulation.
        /// </summary>
        private void Configure(INodeManagerBuilder builder, IReadOnlyList<AreaState> rootAreas)
        {
            // Top level areas need a reference from the Server object. Declaring the inverse
            // reference on the area is enough: the forward reference is published to the
            // caller, which updates the Server object.
            foreach (AreaState area in rootAreas)
            {
                builder.Node(area.NodeId)
                    .AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
            }

            // the same cycle drives what Part 9 leaves to the application - the re-alarm
            // reminders and the alarm metrics of every source - and reports the system and
            // audit events of the server.
            builder.Simulation(TimeSpan.FromSeconds(1)).OnTick(OnSimulationTickAsync);
        }

        /// <summary>
        /// Runs one cycle of the simulation and reports the system events of the server.
        /// </summary>
        private async ValueTask OnSimulationTickAsync(
            ISystemContext context,
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            // the simulation cycle runs in its own try because the events below are dropped
            // by a server which has auditing turned off, and a sample must not lose its
            // simulation over that.
            await RunSimulationCycleAsync().ConfigureAwait(false);

            try
            {
#pragma warning disable CA2000 // Justification: Event state ownership is transferred to Server.ReportEventAsync.
                SystemEventState e = new SystemEventState(null);
#pragma warning restore CA2000

                e.Initialize(
                    SystemContext,
                    null,
                    EventSeverity.Medium,
                    new LocalizedText("Raising Events"));

                e.SetChildValue(SystemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(SystemContext, BrowseNames.SourceName, "Internal", false);

                await Server.ReportEventAsync(SystemContext, e, cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2000 // Justification: Event state ownership is transferred to Server.ReportEventAsync.
                AuditEventState ae = new AuditEventState(null);
#pragma warning restore CA2000

                ae.Initialize(
                    SystemContext,
                    null,
                    EventSeverity.Medium,
                    new LocalizedText("Events Raised"),
                    true,
                    DateTime.UtcNow);

                ae.SetChildValue(SystemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                ae.SetChildValue(SystemContext, BrowseNames.SourceName, "Internal", false);

                await Server.ReportEventAsync(SystemContext, ae, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                m_logger.LogError(e, "Unexpected error raising the system events");
            }
        }

        /// <summary>
        /// Gives every source the tick it needs to run its re-alarm reminders and to
        /// refresh its alarm metrics.
        /// </summary>
        private async Task RunSimulationCycleAsync()
        {
            try
            {
                SourceState[] sources;

                lock (m_sources)
                {
                    sources = new SourceState[m_sources.Count];
                    m_sources.Values.CopyTo(sources, 0);
                }

                foreach (SourceState source in sources)
                {
                    await source.OnSimulationTickAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error running the alarm simulation cycle");
            }
        }

        #region CreateAddressSpace Support Functions
        /// <summary>
        /// Creates and indexes an area defined for the server.
        /// </summary>
        private AreaState CreateAndIndexAreas(AreaState parent, AreaConfiguration configuration)
        {
            // create a unique path to the area.
            string areaPath = Utils.Format("{0}/{1}", (parent != null) ? parent.SymbolicName : String.Empty, configuration.Name);
            NodeId areaId = ModelUtils.ConstructIdForArea(areaPath, NamespaceIndex);

            // create the object that will be used to access the area and any variables contained within it.
            AreaState area = new AreaState(SystemContext, parent, areaId, configuration);

            if (parent != null)
            {
                parent.AddChild(area);
            }

            // create an index any sub-areas defined for the area.
            if (configuration.SubAreas != null)
            {
                for (int ii = 0; ii < configuration.SubAreas.Count; ii++)
                {
                    CreateAndIndexAreas(area, configuration.SubAreas[ii]);
                }
            }

            // add references to sources.
            if (configuration.SourcePaths != null)
            {
                for (int ii = 0; ii < configuration.SourcePaths.Count; ii++)
                {
                    string sourcePath = configuration.SourcePaths[ii];

                    // check if the source already exists because it is referenced by another area.
                    SourceState source = null;

                    if (!m_sources.TryGetValue(sourcePath, out source))
                    {
                        NodeId sourceId = ModelUtils.ConstructIdForSource(sourcePath, NamespaceIndex);
                        m_sources[sourcePath] = source = new SourceState(this, sourceId, sourcePath);
                    }

                    // HasEventSource and HasNotifier control the propagation of event notifications so
                    // they are not like other references. These calls set up a link between the source
                    // and area that will cause events produced by the source to be automatically
                    // propagated to the area.
                    source.AddNotifier(SystemContext, ReferenceTypeIds.HasEventSource, true, area);
                    area.AddNotifier(SystemContext, ReferenceTypeIds.HasEventSource, false, source);
                }
            }

            return area;
        }
        #endregion

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            m_system.StopSimulation();
            m_sources.Clear();

            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        private AlarmConditionServerConfiguration m_configuration;
        private Dictionary<string, SourceState> m_sources;
        private AlarmSuppressionEngine m_suppressionEngine;
        #endregion
    }
}
