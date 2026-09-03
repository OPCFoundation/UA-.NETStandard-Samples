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
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;

namespace Quickstarts.HistoricalEvents.Server
{
    /// <summary>
    /// A node manager for a server that keeps a history of events and serves it
    /// through the history services.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>Model\ModelDesign.xml</c>, and calls <see cref="Configure"/> once the
    /// address space is in place. It also emits the
    /// <c>HistoricalEventsNodeManagerFactory</c> the server registers to create
    /// this node manager.
    ///
    /// The history services themselves are not implemented here at all. The event
    /// history lives behind an <see cref="Opc.Ua.Server.Historian.IHistorianEventProvider"/>
    /// - the <see cref="WellReportHistorianProvider"/> registered below - and the
    /// base class routes every HistoryRead and HistoryUpdate for the notifiers of
    /// this namespace to it through the historian dispatcher.
    /// </remarks>
    [NodeManager]
    public partial class HistoricalEventsNodeManager
    {
        #region Configure
        /// <summary>
        /// Builds the dynamic part of the address space and wires the behaviour of
        /// the sample once the predefined nodes are in place.
        /// </summary>
        /// <remarks>
        /// The platforms folder comes from the model, which also declares it as an
        /// event notifier below the server object, so the base class has already
        /// registered it as a root notifier while the predefined nodes were loaded.
        /// The history bits are added here because the model cannot express them.
        /// </remarks>
        partial void Configure(INodeManagerBuilder builder)
        {
            // initialize the report generator that owns the event history.
            m_generator = new ReportGenerator();
            m_generator.Initialize();

            // register the historian for every node of the namespace. the base class
            // resolves it through this registry when it dispatches the history
            // services, so no history override is needed on this node manager.
            Server.UseHistorian()
                .UseProvider(new WellReportHistorianProvider(Server, m_generator, NamespaceIndex))
                .RegisterForNamespace(Namespaces.HistoricalEvents);

            BaseObjectState platforms = FindPredefinedNode<BaseObjectState>(new NodeId(Objects.Plaforms, NamespaceIndex));
            platforms.EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;

            foreach (string areaName in m_generator.GetAreas())
            {
#pragma warning disable CA2000 // Justification: ownership is transferred to the predefined node collection.
                BaseObjectState area = CreateArea(SystemContext, platforms, areaName);
#pragma warning restore CA2000

                foreach (ReportGenerator.WellInfo well in m_generator.GetWells(areaName))
                {
                    CreateWell(SystemContext, area, well.Id, well.Name);
                }
            }

            // start a simulation that reports new events on the wells. the loop is
            // owned by the node manager and stops when the node manager is disposed.
            builder.Simulation(TimeSpan.FromSeconds(10))
                .OnTick((context, elapsed, cancellationToken) => DoSimulationAsync(cancellationToken));
        }

        /// <summary>
        /// Creates a new area.
        /// </summary>
        private FolderState CreateArea(ServerSystemContext context, BaseObjectState platforms, string areaName)
        {
            FolderState area = new FolderState(null);

            area.NodeId = new NodeId(areaName, NamespaceIndex);
            area.BrowseName = new QualifiedName(areaName, NamespaceIndex);
            area.DisplayName = new LocalizedText(area.BrowseName.Name);
            area.EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
            area.TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType;

            platforms.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, false, area);
            area.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, true, platforms);

            AddPredefinedNodeSynchronously(area);

            return area;
        }

        /// <summary>
        /// Creates a new well.
        /// </summary>
        private void CreateWell(ServerSystemContext context, BaseObjectState area, string wellId, string wellName)
        {
#pragma warning disable CA2000 // Justification: ownership is transferred to the predefined node collection.
            WellState well = new WellState(null);
#pragma warning restore CA2000

            well.NodeId = new NodeId(wellId, NamespaceIndex);
            well.BrowseName = new QualifiedName(wellName, NamespaceIndex);
            well.DisplayName = new LocalizedText(wellName);
            well.EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
            well.TypeDefinitionId = new NodeId(ObjectTypes.WellType, NamespaceIndex);

            area.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, false, well);
            well.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, true, area);

            AddPredefinedNodeSynchronously(well);
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
                m_generator?.Dispose();
                m_generator = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        /// <remarks>
        /// Exceptions do not need to be caught here: the simulation loop logs a
        /// handler failure and carries on with the next tick.
        ///
        /// The report tables are shared with the historian provider, which serves
        /// them to clients on their own threads, so writing a report and reading it
        /// back out happens under the lock of the generator. Reporting the event is
        /// not: it reaches the monitored items of every session and has no business
        /// holding the archive while it does.
        /// </remarks>
        private async ValueTask DoSimulationAsync(CancellationToken cancellationToken)
        {
            foreach (ReportType reportType in new[] { ReportType.FluidLevelTest, ReportType.InjectionTest })
            {
                BaseObjectState well;
                BaseEventState report;

                lock (m_generator.SyncRoot)
                {
                    DataRow row = reportType == ReportType.FluidLevelTest
                        ? m_generator.GenerateFluidLevelTestReport()
                        : m_generator.GenerateInjectionTestReport();

                    well = FindPredefinedNode<BaseObjectState>(new NodeId((string)row[BrowseNames.UidWell], NamespaceIndex));

                    if (well == null || !well.AreEventsMonitored)
                    {
                        continue;
                    }

#pragma warning disable CA2000 // Justification: ownership is transferred to ReportEventAsync.
                    report = m_generator.GetReport(SystemContext, NamespaceIndex, reportType, row);
#pragma warning restore CA2000
                }

                await well.ReportEventAsync(SystemContext, report, cancellationToken).ConfigureAwait(false);
            }
        }
        #endregion

        #region Private Fields
        private ReportGenerator m_generator;
        #endregion
    }
}
