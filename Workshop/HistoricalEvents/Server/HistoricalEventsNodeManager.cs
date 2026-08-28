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
    /// this node manager. The history services below override the async history
    /// interface of the base class.
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

        #region Historian Functions
        /// <summary>
        /// Reads history events.
        /// </summary>
        protected override ValueTask HistoryReadEventsAsync(
            ServerSystemContext context,
            ReadEventDetails details,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                HistoryReadRequest request = null;

                // load an exising request. an empty continuation point means the client is
                // starting a new request: HistoryReadValueId hands out an empty ByteString
                // rather than a null one when the client never assigned a continuation point.
                if (!nodeToRead.ContinuationPoint.IsNull && nodeToRead.ContinuationPoint.Length > 0)
                {
                    request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                    if (request == null)
                    {
                        errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                        continue;
                    }
                }

                // create a new request.
                else
                {
#pragma warning disable CA2000 // Justification: ownership is transferred to the session continuation points.
                    request = CreateHistoryReadRequest(
                        context,
                        details,
                        handle,
                        nodeToRead);
#pragma warning restore CA2000
                }

                // process events until the max is reached.
                HistoryEvent events = new HistoryEvent();

                while (request.NumValuesPerNode == 0 || events.Events.Count < request.NumValuesPerNode)
                {
                    if (request.Events.Count == 0)
                    {
                        break;
                    }

                    BaseEventState e = null;

                    if (request.TimeFlowsBackward)
                    {
                        e = request.Events.Last.Value;
                        request.Events.RemoveLast();
                    }
                    else
                    {
                        e = request.Events.First.Value;
                        request.Events.RemoveFirst();
                    }

                    events.Events = events.Events.AddItem(GetEventFields(request, e));
                }

                errors[handle.Index] = ServiceResult.Good;

                // check if a continuation point is requred.
                if (request.Events.Count > 0)
                {
                    // only set if both end time and start time are specified.
                    if (details.StartTime != DateTime.MinValue && details.EndTime != DateTime.MinValue)
                    {
                        result.ContinuationPoint = SaveContinuationPoint(context, request);
                    }
                }

                // check if no data returned.
                else
                {
                    errors[handle.Index] = StatusCodes.GoodNoData;
                }

                // return the data.
                result.HistoryData = new ExtensionObject(events);
            }

            return default;
        }

        /// <summary>
        /// Updates or inserts events.
        /// </summary>
        protected override ValueTask HistoryUpdateEventsAsync(
            ServerSystemContext context,
            ArrayOf<UpdateEventDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                UpdateEventDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // validate the event filter.
                FilterContext filterContext = new FilterContext(context.NamespaceUris, context.TypeTable, context, Server.Telemetry);
                EventFilter.Result filterResult = nodeToUpdate.Filter.Validate(filterContext);

                if (ServiceResult.IsBad(filterResult.Status))
                {
                    errors[handle.Index] = filterResult.Status;
                    continue;
                }

                // all done.
                errors[handle.Index] = StatusCodes.BadNotImplemented;
            }

            return default;
        }

        /// <summary>
        /// Deletes history events.
        /// </summary>
        protected override ValueTask HistoryDeleteEventsAsync(
            ServerSystemContext context,
            ArrayOf<DeleteEventDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                DeleteEventDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // delete events.
                bool failed = false;

                for (int jj = 0; jj < nodeToUpdate.EventIds.Count; jj++)
                {
                    try
                    {
                        string eventId = new Guid(nodeToUpdate.EventIds[jj].ToArray()).ToString();

                        if (!m_generator.DeleteEvent(eventId))
                        {
                            result.OperationResults = result.OperationResults.AddItem(StatusCodes.BadEventIdUnknown);
                            failed = true;
                            continue;
                        }

                        result.OperationResults = result.OperationResults.AddItem(StatusCodes.Good);
                    }
                    catch
                    {
                        result.OperationResults = result.OperationResults.AddItem(StatusCodes.BadEventIdUnknown);
                        failed = true;
                    }
                }

                // check if diagnostics are required.
                if (failed)
                {
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        for (int jj = 0; jj < nodeToUpdate.EventIds.Count; jj++)
                        {
                            if (StatusCode.IsBad(result.OperationResults[jj]))
                            {
                                result.DiagnosticInfos = result.DiagnosticInfos.AddItem(ServerUtils.CreateDiagnosticInfo(Server, context.OperationContext, result.OperationResults[jj], m_logger));
                            }
                        }
                    }
                }

                // clear operation results if all good.
                else
                {
                    result.OperationResults = ArrayOf<StatusCode>.Empty;
                }

                // all done.
                errors[handle.Index] = ServiceResult.Good;
            }

            return default;
        }

        /// <summary>
        /// Releases the history continuation point.
        /// </summary>
        protected override ValueTask HistoryReleaseContinuationPointsAsync(
            ServerSystemContext context,
            ArrayOf<HistoryReadValueId> nodesToRead,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];

                // find the continuation point.
                HistoryReadRequest request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                if (request == null)
                {
                    errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                    continue;
                }

                // all done.
                errors[handle.Index] = StatusCodes.Good;
            }

            return default;
        }

        #region History Helpers
        /// <summary>
        /// Fetches the requested event fields from the event.
        /// </summary>
        private HistoryEventFieldList GetEventFields(HistoryReadRequest request, IFilterTarget instance)
        {
            // fetch the event fields.
            HistoryEventFieldList fields = new HistoryEventFieldList();

            foreach (SimpleAttributeOperand clause in request.Filter.SelectClauses)
            {
                // get the value of the attribute (apply localization).
                Variant value = instance.GetAttributeValue(
                    request.FilterContext,
                    clause.TypeDefinitionId,
                    clause.BrowsePath,
                    clause.AttributeId,
                    clause.ParsedIndexRange);

                // add the value to the list of event fields.
                if (!value.IsNull)
                {
                    // translate any localized text.
                    if (value.AsBoxedObject() is LocalizedText text && !text.IsNullOrEmpty)
                    {
                        value = Variant.From(Server.ResourceManager.Translate(request.FilterContext.PreferredLocales, text));
                    }

                    // add value.
                    fields.EventFields = fields.EventFields.AddItem(value);
                }

                // add a dummy entry for missing values.
                else
                {
                    fields.EventFields = fields.EventFields.AddItem(Variant.Null);
                }
            }

            return fields;
        }

        /// <summary>
        /// Creates a new history request.
        /// </summary>
        private HistoryReadRequest CreateHistoryReadRequest(
            ServerSystemContext context,
            ReadEventDetails details,
            NodeHandle handle,
            HistoryReadValueId nodeToRead)
        {
            FilterContext filterContext = new FilterContext(context.NamespaceUris, context.TypeTable, context.PreferredLocales, Server.Telemetry);
            LinkedList<BaseEventState> events = new LinkedList<BaseEventState>();

            for (ReportType ii = ReportType.FluidLevelTest; ii <= ReportType.InjectionTest; ii++)
            {
                using DataView view = handle.Node is WellState
                    ? m_generator.ReadHistoryForWellId(
                        ii,
                        handle.Node.NodeId.TryGetValue(out string wellId) ? wellId : null,
                        (DateTime)details.StartTime,
                        (DateTime)details.EndTime)
                    : m_generator.ReadHistoryForArea(
                        ii,
                        handle.Node.NodeId.TryGetValue(out string areaId) ? areaId : null,
                        (DateTime)details.StartTime,
                        (DateTime)details.EndTime);

                LinkedListNode<BaseEventState> pos = events.First;
                bool sizeLimited = (details.StartTime == DateTime.MinValue || details.EndTime == DateTime.MinValue);

                foreach (DataRowView row in view)
                {
                    // check if reached max results.
                    if (sizeLimited)
                    {
                        if (events.Count >= details.NumValuesPerNode)
                        {
                            break;
                        }
                    }

#pragma warning disable CA2000 // Justification: ownership is transferred to the event results collection.
                    BaseEventState e = m_generator.GetReport(context, NamespaceIndex, ii, row.Row);
#pragma warning restore CA2000

                    if (details.Filter.WhereClause != null && details.Filter.WhereClause.Elements.Count > 0)
                    {
                        if (!details.Filter.WhereClause.Evaluate(filterContext, e))
                        {
                            continue;
                        }
                    }

                    bool inserted = false;

                    for (LinkedListNode<BaseEventState> jj = pos; jj != null; jj = jj.Next)
                    {
                        if (jj.Value.Time.Value > e.Time.Value)
                        {
                            events.AddBefore(jj, e);
                            pos = jj;
                            inserted = true;
                            break;
                        }
                    }

                    if (!inserted)
                    {
                        events.AddLast(e);
                        pos = null;
                    }
                }
            }

            HistoryReadRequest request = new HistoryReadRequest();
            request.Events = events;
            request.TimeFlowsBackward = details.StartTime == DateTime.MinValue || (details.EndTime != DateTime.MinValue && details.EndTime < details.StartTime);
            request.NumValuesPerNode = details.NumValuesPerNode;
            request.Filter = details.Filter;
            request.FilterContext = filterContext;
            return request;
        }

        /// <summary>
        /// Stores a read history request.
        /// </summary>
        private sealed class HistoryReadRequest : IHistoryContinuationPoint
        {
            public Guid Id { get; set; }
            public ByteString ContinuationPoint;
            public LinkedList<BaseEventState> Events;
            public bool TimeFlowsBackward;
            public uint NumValuesPerNode;
            public EventFilter Filter;
            public FilterContext FilterContext;

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Loads a history continuation point.
        /// </summary>
        private HistoryReadRequest LoadContinuationPoint(
            ServerSystemContext context,
            ByteString continuationPoint)
        {
            ISession session = context.OperationContext.Session;

            if (session == null)
            {
                return null;
            }

            HistoryReadRequest request = session.ContinuationPoints.RestoreHistory(continuationPoint) as HistoryReadRequest;

            if (request == null)
            {
                return null;
            }

            return request;
        }

        /// <summary>
        /// Saves a history continuation point.
        /// </summary>
        private ByteString SaveContinuationPoint(
            ServerSystemContext context,
            HistoryReadRequest request)
        {
            ISession session = context.OperationContext.Session;

            if (session == null)
            {
                return default;
            }

            request.Id = Guid.NewGuid();
            session.ContinuationPoints.SaveHistory(request);
            request.ContinuationPoint = request.Id.ToByteArray().ToByteString();
            return request.ContinuationPoint;
        }
        #endregion
        #endregion

        #region Private Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        /// <remarks>
        /// Exceptions do not need to be caught here: the simulation loop logs a
        /// handler failure and carries on with the next tick.
        /// </remarks>
        private async ValueTask DoSimulationAsync(CancellationToken cancellationToken)
        {
            {
                DataRow row = m_generator.GenerateFluidLevelTestReport();
                BaseObjectState well = FindPredefinedNode<BaseObjectState>(new NodeId((string)row[BrowseNames.UidWell], NamespaceIndex));

                if (well != null && well.AreEventsMonitored)
                {
#pragma warning disable CA2000 // Justification: ownership is transferred to ReportEventAsync.
                    BaseEventState e = m_generator.GetFluidLevelTestReport(SystemContext, NamespaceIndex, row);
#pragma warning restore CA2000
                    await well.ReportEventAsync(SystemContext, e, cancellationToken).ConfigureAwait(false);
                }
            }

            {
                DataRow row = m_generator.GenerateInjectionTestReport();
                BaseObjectState well = FindPredefinedNode<BaseObjectState>(new NodeId((string)row[BrowseNames.UidWell], NamespaceIndex));

                if (well != null && well.AreEventsMonitored)
                {
#pragma warning disable CA2000 // Justification: ownership is transferred to ReportEventAsync.
                    BaseEventState e = m_generator.GetInjectionTestReport(SystemContext, NamespaceIndex, row);
#pragma warning restore CA2000
                    await well.ReportEventAsync(SystemContext, e, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        #endregion

        #region Private Fields
        private ReportGenerator m_generator;
        #endregion
    }
}
