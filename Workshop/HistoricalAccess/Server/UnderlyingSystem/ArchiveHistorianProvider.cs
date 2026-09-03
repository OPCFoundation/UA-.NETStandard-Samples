/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Serves the file based archive through the SDK's native historian interfaces.
    /// </summary>
    /// <remarks>
    /// The node manager registers one instance of this provider for the sample
    /// namespace. <see cref="Opc.Ua.Server.AsyncCustomNodeManager"/> then routes every
    /// HistoryRead and HistoryUpdate service call through the
    /// <see cref="HistorianDispatcher"/>, which owns the protocol plumbing -
    /// continuation points, timestamps to return, index ranges and data encodings,
    /// the audit events, and the translation between the Annotations property and
    /// the variable it belongs to - and calls the capability interfaces implemented
    /// here with validated, normalised requests.
    ///
    /// Processed and at-time reads have streaming framework fallbacks built on
    /// <see cref="ReadRawAsync"/>, but the provider implements both natively so the
    /// per item settings recorded in the archive files - stepped interpolation and
    /// the aggregate configuration - are honoured, the way the sample always did.
    ///
    /// Two of the interfaces implemented here are offers rather than obligations, and
    /// the framework of this SDK version does not take either up: the atomic update
    /// path of <see cref="IHistorianTransactionalProvider"/> has no caller yet, and
    /// <see cref="IHistorianBulkInsertProvider"/> is reached only from the automatic
    /// value capture pipeline, which this sample does not use because its archive is
    /// filled from files rather than from live values. They are implemented anyway,
    /// because what a store has to do to honour them is the part worth showing: the
    /// batch is what a store with real transactions commits or discards as a whole,
    /// and it is where the cost of a write - the lock and the reload, not the value -
    /// is paid once instead of once per value.
    ///
    /// The provider also feeds the server-wide HistoryServerCapabilities flags: the
    /// diagnostics node manager asks every registered provider for its capabilities
    /// once the address space exists and rolls the answers up into the capability
    /// node, which the sample used to populate by hand.
    ///
    /// A provider read has no per-operation error channel, and nothing between here
    /// and the transport catches an exception, so every operation contains its own
    /// failures: reads answer with an empty page, updates with a bad status per
    /// value, and the error goes to the log.
    /// </remarks>
    public sealed class ArchiveHistorianProvider :
        HistorianProviderBase,
        IHistorianDataProvider,
        IHistorianTransactionalProvider,
        IHistorianBulkInsertProvider,
        IHistorianModifiedProvider,
        IHistorianAtTimeProvider,
        IHistorianProcessedProvider,
        IHistorianAnnotationProvider
    {
        /// <summary>
        /// Creates a provider serving the items of the underlying system.
        /// </summary>
        public ArchiveHistorianProvider(IServerInternal server, UnderlyingSystem system)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_system = system ?? throw new ArgumentNullException(nameof(system));
            m_logger = server.Telemetry.CreateLogger<ArchiveHistorianProvider>();
        }

        #region HistorianProviderBase Members
        /// <inheritdoc/>
        public override ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
        {
            // every node id which addresses an item of the archive has history.
            ParsedNodeId parsedNodeId = ParsedNodeId.Parse(nodeId);

            return new ValueTask<bool>(
                parsedNodeId != null &&
                parsedNodeId.RootType == NodeTypes.Item &&
                String.IsNullOrEmpty(parsedNodeId.ComponentPath));
        }

        /// <inheritdoc/>
        public override ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct)
        {
            lock (m_system.SyncRoot)
            {
                // the null node id is the roll-up query for the server-wide capability
                // flags; items which have not been touched yet get the same answer.
                ArchiveItemState item = m_system.FindItemState(ParsedNodeId.Parse(nodeId));

                if (item == null)
                {
                    return new ValueTask<HistorianNodeCapabilities>(s_capabilities);
                }

                return new ValueTask<HistorianNodeCapabilities>(s_capabilities with {
                    Stepped = item.ArchiveItem.Stepped,
                    MinTimeInterval = item.ArchiveItem.SamplingInterval,
                    MaxTimeInterval = item.ArchiveItem.SamplingInterval,
                    DefaultAggregateConfiguration = item.ArchiveItem.AggregateConfiguration
                        ?? m_server.AggregateManager.GetDefaultConfiguration(NodeId.Null)
                });
            }
        }
        #endregion

        #region IHistorianDataProvider Members
        /// <inheritdoc/>
        public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
            HistorianOperationContext context,
            HistorianRawReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, request.NodeId);

                    if (!IsReadable(item, context, resumeToken.IsEmpty))
                    {
                        return new ValueTask<HistorianPage<HistoricalDataValue>>(HistorianPage<HistoricalDataValue>.Empty);
                    }

                    return new ValueTask<HistorianPage<HistoricalDataValue>>(
                        ReadRawPage(item, request, DecodeTimestamp(resumeToken)));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reading the raw history of {NodeId}.", request.NodeId);
                return new ValueTask<HistorianPage<HistoricalDataValue>>(HistorianPage<HistoricalDataValue>.Empty);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAsync(HistorianOperationContext context, NodeId nodeId, IList<DataValue> values, CancellationToken ct)
        {
            return UpdateDataAsync(context, nodeId, values, PerformUpdateType.Insert);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAsync(HistorianOperationContext context, NodeId nodeId, IList<DataValue> values, CancellationToken ct)
        {
            return UpdateDataAsync(context, nodeId, values, PerformUpdateType.Replace);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAsync(HistorianOperationContext context, NodeId nodeId, IList<DataValue> values, CancellationToken ct)
        {
            return UpdateDataAsync(context, nodeId, values, PerformUpdateType.Update);
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> DeleteRawAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            bool isDeleteModified,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, nodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<StatusCode>((StatusCode)StatusCodes.BadNodeIdUnknown);
                    }

                    return new ValueTask<StatusCode>((StatusCode)item.DeleteHistory(
                        context.SystemContext,
                        (DateTime)startTime,
                        (DateTime)endTime,
                        isDeleteModified));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error deleting the history of {NodeId}.", nodeId);
                return new ValueTask<StatusCode>((StatusCode)StatusCodes.BadUnexpectedError);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> timestamps,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, nodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadNodeIdUnknown, timestamps.Count));
                    }

                    StatusCode[] results = new StatusCode[timestamps.Count];

                    for (int ii = 0; ii < timestamps.Count; ii++)
                    {
                        results[ii] = item.DeleteHistory(context.SystemContext, (DateTime)timestamps[ii]);
                    }

                    return new ValueTask<IList<StatusCode>>(results);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error deleting the history of {NodeId}.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, timestamps.Count));
            }
        }
        #endregion

        #region IHistorianTransactionalProvider Members
        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return UpdateDataAtomicAsync(context, nodeId, values, PerformUpdateType.Insert);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return UpdateDataAtomicAsync(context, nodeId, values, PerformUpdateType.Replace);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return UpdateDataAtomicAsync(context, nodeId, values, PerformUpdateType.Update);
        }
        #endregion

        #region IHistorianBulkInsertProvider Members
        /// <inheritdoc/>
        /// <remarks>
        /// The archive lock and the reload of an item are what an insert costs here,
        /// not the writing of the value, so a batch takes the lock once and reloads
        /// each item once however many values are meant for it.
        /// </remarks>
        public ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>> InsertBatchAsync(
            HistorianOperationContext context,
            IReadOnlyDictionary<NodeId, IList<DataValue>> batch,
            CancellationToken ct)
        {
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            var results = new Dictionary<NodeId, IList<StatusCode>>(batch.Count);

            try
            {
                lock (m_system.SyncRoot)
                {
                    foreach (KeyValuePair<NodeId, IList<DataValue>> entry in batch)
                    {
                        // the batch covers several nodes, so the node the context
                        // carries cannot be the one this entry is about.
                        ArchiveItemState item = ResolveById(context, entry.Key);

                        results[entry.Key] = item == null || !TryReload(item, context)
                            ? RepeatStatus(StatusCodes.BadNodeIdUnknown, entry.Value.Count)
                            : UpdateItem(item, context, entry.Value, PerformUpdateType.Insert);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error inserting a batch of {Count} nodes into the archive.", batch.Count);

                foreach (KeyValuePair<NodeId, IList<DataValue>> entry in batch)
                {
                    results[entry.Key] = RepeatStatus(StatusCodes.BadUnexpectedError, entry.Value.Count);
                }
            }

            return new ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>>(results);
        }
        #endregion

        #region IHistorianModifiedProvider Members
        /// <inheritdoc/>
        public ValueTask<HistorianPage<ModifiedDataValue>> ReadModifiedAsync(
            HistorianOperationContext context,
            HistorianModifiedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, request.NodeId);

                    if (!IsReadable(item, context, resumeToken.IsEmpty))
                    {
                        return new ValueTask<HistorianPage<ModifiedDataValue>>(HistorianPage<ModifiedDataValue>.Empty);
                    }

                    DataView view = item.ReadHistory((DateTime)request.StartTime, (DateTime)request.EndTime, true);

                    return new ValueTask<HistorianPage<ModifiedDataValue>>(ReadPage(
                        view,
                        (DateTime)request.StartTime,
                        (DateTime)request.EndTime,
                        request.MaxValues,
                        request.IsForward,
                        resumeToken,
                        row => new ModifiedDataValue((DataValue)row[2], (ModificationInfo)row[6])));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reading the modified history of {NodeId}.", request.NodeId);
                return new ValueTask<HistorianPage<ModifiedDataValue>>(HistorianPage<ModifiedDataValue>.Empty);
            }
        }
        #endregion

        #region IHistorianAtTimeProvider Members
        /// <inheritdoc/>
        public ValueTask<IList<DataValue>> ReadAtTimeAsync(
            HistorianOperationContext context,
            HistorianAtTimeReadRequest request,
            CancellationToken ct)
        {
            try
            {
                List<DataValue> values = new List<DataValue>(request.RequestedTimes.Count);

                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, request.NodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        foreach (DateTimeUtc requestedTime in request.RequestedTimes)
                        {
                            values.Add(DataValue.FromStatusCode(StatusCodes.BadNoData, requestedTime));
                        }

                        return new ValueTask<IList<DataValue>>(values);
                    }

                    DataView view = item.ReadHistory(DateTime.MinValue, DateTime.MaxValue, false);

                    foreach (DateTimeUtc requestedTime in request.RequestedTimes)
                    {
                        values.Add(ReadValueAtTime(item, view, (DateTime)requestedTime, request.UseSimpleBounds));
                    }
                }

                return new ValueTask<IList<DataValue>>(values);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reading the history of {NodeId} at the requested times.", request.NodeId);

                List<DataValue> errors = new List<DataValue>(request.RequestedTimes.Count);

                foreach (DateTimeUtc requestedTime in request.RequestedTimes)
                {
                    errors.Add(DataValue.FromStatusCode(StatusCodes.BadUnexpectedError, requestedTime));
                }

                return new ValueTask<IList<DataValue>>(errors);
            }
        }
        #endregion

        #region IHistorianProcessedProvider Members
        /// <inheritdoc/>
        public ValueTask<HistorianPage<DataValue>> ReadProcessedAsync(
            HistorianOperationContext context,
            HistorianProcessedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, request.NodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<HistorianPage<DataValue>>(HistorianPage<DataValue>.Empty);
                    }

                    // choose the aggregate configuration: the item settings loaded from the
                    // archive file win over the server-wide defaults.
                    AggregateConfiguration configuration = (AggregateConfiguration)
                        (request.Configuration ?? m_server.AggregateManager.GetDefaultConfiguration(NodeId.Null)).MemberwiseClone();

                    ReviseAggregateConfiguration(item, configuration);

                    // the node manager refuses unsupported aggregates before the dispatch,
                    // so a missing calculator can only be a race with a configuration change.
                    IAggregateCalculator calculator = m_server.AggregateManager.CreateCalculator(
                        request.AggregateId,
                        request.StartTime,
                        request.EndTime,
                        request.ProcessingInterval,
                        item.ArchiveItem.Stepped,
                        configuration);

                    if (calculator == null)
                    {
                        return new ValueTask<HistorianPage<DataValue>>(HistorianPage<DataValue>.Empty);
                    }

                    bool timeFlowsBackward = request.EndTime < request.StartTime;
                    DataView view = item.ReadHistory((DateTime)request.StartTime, (DateTime)request.EndTime, false);
                    List<DataValue> values = new List<DataValue>();

                    int ii = timeFlowsBackward ? view.Count - 1 : 0;

                    while (ii >= 0 && ii < view.Count)
                    {
                        calculator.QueueRawValue((DataValue)view[ii].Row[2]);
                        DrainCalculator(calculator, false, values);
                        ii += timeFlowsBackward ? -1 : 1;
                    }

                    // queue any processed values beyond the end of the data.
                    DrainCalculator(calculator, true, values);

                    return new ValueTask<HistorianPage<DataValue>>(new HistorianPage<DataValue>(values));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error computing an aggregate over the history of {NodeId}.", request.NodeId);
                return new ValueTask<HistorianPage<DataValue>>(HistorianPage<DataValue>.Empty);
            }
        }
        #endregion

        #region IHistorianAnnotationProvider Members
        /// <inheritdoc/>
        public ValueTask<HistorianPage<Annotation>> ReadAnnotationsAsync(
            HistorianOperationContext context,
            HistorianAnnotationReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, request.NodeId);

                    if (!IsReadable(item, context, resumeToken.IsEmpty))
                    {
                        return new ValueTask<HistorianPage<Annotation>>(HistorianPage<Annotation>.Empty);
                    }

                    DataView view = item.ReadHistory(
                        (DateTime)request.StartTime,
                        (DateTime)request.EndTime,
                        false,
                        new QualifiedName(Opc.Ua.BrowseNames.Annotations));

                    return new ValueTask<HistorianPage<Annotation>>(ReadPage(
                        view,
                        (DateTime)request.StartTime,
                        (DateTime)request.EndTime,
                        request.MaxValues,
                        request.IsForward,
                        resumeToken,
                        row => (Annotation)row[5]));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reading the annotations of {NodeId}.", request.NodeId);
                return new ValueTask<HistorianPage<Annotation>>(HistorianPage<Annotation>.Empty);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAnnotationsAsync(HistorianOperationContext context, NodeId nodeId, IList<Annotation> annotations, CancellationToken ct)
        {
            return UpdateAnnotationsAsync(context, nodeId, annotations, PerformUpdateType.Insert);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAnnotationsAsync(HistorianOperationContext context, NodeId nodeId, IList<Annotation> annotations, CancellationToken ct)
        {
            return UpdateAnnotationsAsync(context, nodeId, annotations, PerformUpdateType.Replace);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAnnotationsAsync(HistorianOperationContext context, NodeId nodeId, IList<Annotation> annotations, CancellationToken ct)
        {
            return UpdateAnnotationsAsync(context, nodeId, annotations, PerformUpdateType.Update);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> annotationTimes,
            CancellationToken ct)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, nodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadNodeIdUnknown, annotationTimes.Count));
                    }

                    StatusCode[] results = new StatusCode[annotationTimes.Count];

                    for (int ii = 0; ii < annotationTimes.Count; ii++)
                    {
                        results[ii] = item.DeleteAnnotations((DateTime)annotationTimes[ii]);
                    }

                    return new ValueTask<IList<StatusCode>>(results);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error deleting the annotations of {NodeId}.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, annotationTimes.Count));
            }
        }
        #endregion

        #region Internal Interface
        /// <summary>
        /// Reads the raw values in the given window, including the bounds, for the
        /// node manager to backfill a freshly created monitored item with an
        /// aggregate filter reaching into the past.
        /// </summary>
        internal IList<DataValue> ReadRawWindow(ServerSystemContext context, ArchiveItemState item, DateTime startTime, DateTime endTime)
        {
            List<DataValue> values = new List<DataValue>();

            lock (m_system.SyncRoot)
            {
                item.ReloadFromSource(context, m_server.Telemetry);

                HistorianRawReadRequest request = new HistorianRawReadRequest {
                    NodeId = item.NodeId,
                    StartTime = startTime,
                    EndTime = endTime,
                    MaxValues = 0,
                    IsForward = true,
                    ReturnBounds = true
                };

                DateTime resumeAt = DateTime.MinValue;

                while (true)
                {
                    HistorianPage<HistoricalDataValue> page = ReadRawPage(item, request, resumeAt);

                    foreach (HistoricalDataValue value in page.Values)
                    {
                        values.Add(value.Value);
                    }

                    if (page.IsFinal)
                    {
                        break;
                    }

                    resumeAt = DecodeTimestamp(page.NextToken);
                }
            }

            return values;
        }

        /// <summary>
        /// Revises the aggregate configuration: the settings recorded in the archive
        /// file for the item replace a request for the server defaults, and stepped
        /// items never extrapolate along a slope.
        /// </summary>
        /// <remarks>
        /// Callers hold the archive lock: the item configuration this reads is
        /// rewritten in place whenever the item reloads from its source.
        /// </remarks>
        internal void ReviseAggregateConfiguration(ArchiveItemState item, AggregateConfiguration configurationToUse)
        {
            // set configuration from defaults.
            if (configurationToUse.UseServerCapabilitiesDefaults)
            {
                AggregateConfiguration configuration = item.ArchiveItem.AggregateConfiguration;

                if (configuration == null || configuration.UseServerCapabilitiesDefaults)
                {
                    configuration = m_server.AggregateManager.GetDefaultConfiguration(NodeId.Null);
                }

                configurationToUse.UseSlopedExtrapolation = configuration.UseSlopedExtrapolation;
                configurationToUse.TreatUncertainAsBad = configuration.TreatUncertainAsBad;
                configurationToUse.PercentDataBad = configuration.PercentDataBad;
                configurationToUse.PercentDataGood = configuration.PercentDataGood;
            }

            // override configuration when it does not make sense for the item.
            configurationToUse.UseServerCapabilitiesDefaults = false;

            if (item.ArchiveItem.Stepped)
            {
                configurationToUse.UseSlopedExtrapolation = false;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the archive item addressed by a request.
        /// </summary>
        /// <remarks>
        /// The dispatcher hands the provider the node it validated - for operations
        /// on the Annotations property that is already the owning variable. Any
        /// other node is answered for exactly what it is: a request addressed at a
        /// component of an item must not fall through to the item itself, or a
        /// delete aimed at a property would delete the item's data.
        /// </remarks>
        private ArchiveItemState Resolve(HistorianOperationContext context, NodeId nodeId)
        {
            if (context.Node != null)
            {
                return context.Node as ArchiveItemState;
            }

            return ResolveById(context, nodeId);
        }

        /// <summary>
        /// Returns the archive item with the given node id.
        /// </summary>
        /// <remarks>
        /// An operation which covers several nodes at once cannot go through the one
        /// node the context carries, so it resolves every node id for itself.
        /// </remarks>
        private ArchiveItemState ResolveById(HistorianOperationContext context, NodeId nodeId)
        {
            ParsedNodeId parsedNodeId = ParsedNodeId.Parse(nodeId);

            if (parsedNodeId == null ||
                parsedNodeId.RootType != NodeTypes.Item ||
                !String.IsNullOrEmpty(parsedNodeId.ComponentPath))
            {
                return null;
            }

            return m_system.GetItemState(context.SystemContext, parsedNodeId);
        }

        /// <summary>
        /// Prepares an item for a paged read: the first page brings the archive up
        /// to date with its backing file, continuation pages read what is loaded.
        /// </summary>
        private bool IsReadable(ArchiveItemState item, HistorianOperationContext context, bool firstPage)
        {
            if (item == null)
            {
                return false;
            }

            if (firstPage)
            {
                return TryReload(item, context);
            }

            return item.ArchiveItem.DataSet != null;
        }

        /// <summary>
        /// Brings the archive item up to date with its backing file or resource.
        /// </summary>
        private bool TryReload(ArchiveItemState item, HistorianOperationContext context)
        {
            try
            {
                item.ReloadFromSource(context.SystemContext, m_server.Telemetry);
                return true;
            }
            catch (Exception e)
            {
                // a node id can parse as an item without a file behind it; treat it
                // like an item without history instead of failing the whole service.
                m_logger.LogError(e, "Could not load the archive behind {NodeId}.", item.NodeId);
                return false;
            }
        }

        /// <summary>
        /// Applies the per value insert, replace or update to the archive.
        /// </summary>
        private ValueTask<IList<StatusCode>> UpdateDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            PerformUpdateType performUpdateType)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, nodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadNodeIdUnknown, values.Count));
                    }

                    return new ValueTask<IList<StatusCode>>(
                        UpdateItem(item, context, values, performUpdateType));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating the history of {NodeId}.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, values.Count));
            }
        }

        /// <summary>
        /// Applies a batch of values to one item as a whole: either every value is
        /// in the archive when the call returns, or none of them is.
        /// </summary>
        /// <remarks>
        /// The archive of an item is a DataSet, which keeps every write pending
        /// until it is told to accept it, so a batch is applied value by value and
        /// then either committed or discarded in one go. That is the same guarantee
        /// a store with real transactions would give, reached with what this one has.
        ///
        /// A value which fails answers with the reason it failed; the others answer
        /// with BadHistoryOperationUnsupported, because nothing became of them - the
        /// convention the in memory historian of the SDK uses for a batch it rolled
        /// back.
        /// </remarks>
        private ValueTask<IList<StatusCode>> UpdateDataAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            PerformUpdateType performUpdateType)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, nodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadNodeIdUnknown, values.Count));
                    }

                    StatusCode[] results = UpdateItem(item, context, values, performUpdateType, commit: false);
                    int failed = Array.FindIndex(results, StatusCode.IsBad);

                    if (failed < 0)
                    {
                        item.CommitChanges();
                        return new ValueTask<IList<StatusCode>>(results);
                    }

                    item.RollbackChanges();

                    StatusCode reason = results[failed];
                    Array.Fill(results, (StatusCode)StatusCodes.BadHistoryOperationUnsupported);
                    results[failed] = reason;

                    return new ValueTask<IList<StatusCode>>(results);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating the history of {NodeId} atomically.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, values.Count));
            }
        }

        /// <summary>
        /// Writes the values of one item, one status per value.
        /// </summary>
        private static StatusCode[] UpdateItem(
            ArchiveItemState item,
            HistorianOperationContext context,
            IList<DataValue> values,
            PerformUpdateType performUpdateType,
            bool commit = true)
        {
            StatusCode[] results = new StatusCode[values.Count];

            for (int ii = 0; ii < values.Count; ii++)
            {
                results[ii] = item.UpdateHistory(context.SystemContext, values[ii], performUpdateType, commit);
            }

            return results;
        }

        /// <summary>
        /// Applies the per annotation insert, replace or update to the archive.
        /// </summary>
        private ValueTask<IList<StatusCode>> UpdateAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            PerformUpdateType performUpdateType)
        {
            try
            {
                lock (m_system.SyncRoot)
                {
                    ArchiveItemState item = Resolve(context, nodeId);

                    if (item == null || !TryReload(item, context))
                    {
                        return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadNodeIdUnknown, annotations.Count));
                    }

                    StatusCode[] results = new StatusCode[annotations.Count];

                    for (int ii = 0; ii < annotations.Count; ii++)
                    {
                        // the dispatcher passes null for a value it could not decode.
                        results[ii] = annotations[ii] == null
                            ? StatusCodes.BadTypeMismatch
                            : item.UpdateAnnotations(annotations[ii], performUpdateType);
                    }

                    return new ValueTask<IList<StatusCode>>(results);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating the annotations of {NodeId}.", nodeId);
                return new ValueTask<IList<StatusCode>>(RepeatStatus(StatusCodes.BadUnexpectedError, annotations.Count));
            }
        }

        /// <summary>
        /// Builds one page of a raw read over the current-data table.
        /// </summary>
        /// <remarks>
        /// The window is normalised by the framework: the effective start is the
        /// earlier time, one-sided requests carry a MinValue or MaxValue sentinel,
        /// and <see cref="HistorianRawReadRequest.IsForward"/> holds the direction.
        /// A forward read returns the samples in [start, end) so the sample at the
        /// end becomes the bound; a reverse read mirrors that to (start, end].
        /// The source timestamp is the storage key, so the resume token is simply
        /// the timestamp the previous page ended at.
        /// </remarks>
        private static HistorianPage<HistoricalDataValue> ReadRawPage(
            ArchiveItemState item,
            HistorianRawReadRequest request,
            DateTime resumeAt)
        {
            DataView view = item.ReadHistory((DateTime)request.StartTime, (DateTime)request.EndTime, false);

            DateTime windowStart = (DateTime)request.StartTime;
            DateTime windowEnd = (DateTime)request.EndTime;
            bool hasStart = request.StartTime != DateTimeUtc.MinValue;
            bool hasEnd = request.EndTime != DateTimeUtc.MaxValue;
            bool firstPage = resumeAt == DateTime.MinValue;

            // a one-sided request is capped by the requested count alone, so a full
            // page is the end of it rather than a reason to continue.
            bool sizeLimited = request.MaxValues != 0 && (!hasStart || !hasEnd);
            uint pageSize = request.MaxValues != 0 ? request.MaxValues : kDefaultPageSize;

            List<HistoricalDataValue> values = new List<HistoricalDataValue>();

            // a read at a single point in time returns the sample recorded there,
            // and with bounds requested the samples on each side of the instant.
            if (hasStart && hasEnd && windowStart == windowEnd)
            {
                int exact = view.Find(windowStart);

                if (exact >= 0)
                {
                    if (firstPage)
                    {
                        values.Add(new HistoricalDataValue((DataValue)view[exact].Row[2], request.ReturnBounds));

                        if (request.ReturnBounds && request.MaxValues != 1)
                        {
                            values.Add(FindBoundAfter(view, windowStart));
                        }
                    }

                    return new HistorianPage<HistoricalDataValue>(values);
                }

                if (!request.ReturnBounds)
                {
                    return new HistorianPage<HistoricalDataValue>(values);
                }

                // no sample at the instant: fall through, so the read answers with
                // the bound on each side of it.
            }

            DateTime lastReturned = DateTime.MinValue;
            bool full = false;

            if (request.IsForward)
            {
                // the bound before the window, unless a sample sits exactly on its edge.
                if (firstPage && request.ReturnBounds && hasStart && view.Find(windowStart) < 0)
                {
                    HistoricalDataValue bound = FindBoundBefore(view, windowStart);
                    values.Add(bound);
                    lastReturned = (DateTime)bound.Value.SourceTimestamp;
                    full = values.Count >= pageSize;
                }

                for (int ii = 0; ii < view.Count; ii++)
                {
                    DateTime timestamp = (DateTime)view[ii].Row[0];

                    if (timestamp < windowStart || (!firstPage && timestamp <= resumeAt))
                    {
                        continue;
                    }

                    if (hasEnd && timestamp >= windowEnd)
                    {
                        if (request.ReturnBounds)
                        {
                            if (full)
                            {
                                return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                            }

                            values.Add(new HistoricalDataValue((DataValue)view[ii].Row[2], true));
                        }

                        return new HistorianPage<HistoricalDataValue>(values);
                    }

                    if (full)
                    {
                        return sizeLimited
                            ? new HistorianPage<HistoricalDataValue>(values)
                            : new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                    }

                    values.Add(new HistoricalDataValue((DataValue)view[ii].Row[2]));
                    lastReturned = timestamp;
                    full = values.Count >= pageSize;
                }

                // the data ran out inside the window, so the bound at the end is missing.
                if (request.ReturnBounds && hasEnd)
                {
                    if (full)
                    {
                        return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                    }

                    values.Add(CreateMissingBound(windowEnd));
                }

                return new HistorianPage<HistoricalDataValue>(values);
            }

            // reverse: iterate the sorted view from the back.
            if (firstPage && request.ReturnBounds && hasEnd && view.Find(windowEnd) < 0)
            {
                HistoricalDataValue bound = FindBoundAfter(view, windowEnd);
                values.Add(bound);
                lastReturned = (DateTime)bound.Value.SourceTimestamp;
                full = values.Count >= pageSize;
            }

            for (int ii = view.Count - 1; ii >= 0; ii--)
            {
                DateTime timestamp = (DateTime)view[ii].Row[0];

                if (timestamp > windowEnd || (!firstPage && timestamp >= resumeAt))
                {
                    continue;
                }

                if (hasStart && timestamp <= windowStart)
                {
                    if (request.ReturnBounds)
                    {
                        if (full)
                        {
                            return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                        }

                        values.Add(new HistoricalDataValue((DataValue)view[ii].Row[2], true));
                    }

                    return new HistorianPage<HistoricalDataValue>(values);
                }

                if (full)
                {
                    return sizeLimited
                        ? new HistorianPage<HistoricalDataValue>(values)
                        : new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                }

                values.Add(new HistoricalDataValue((DataValue)view[ii].Row[2]));
                lastReturned = timestamp;
                full = values.Count >= pageSize;
            }

            if (request.ReturnBounds && hasStart)
            {
                if (full)
                {
                    return new HistorianPage<HistoricalDataValue>(values, EncodeTimestamp(lastReturned));
                }

                values.Add(CreateMissingBound(windowStart));
            }

            return new HistorianPage<HistoricalDataValue>(values);
        }

        /// <summary>
        /// Builds one page of a modified or annotation read: the rows of the sorted
        /// view inside the window - [start, end) forward, (start, end] in reverse,
        /// mirroring the raw read - in the requested direction, resuming after the
        /// page which came before.
        /// </summary>
        /// <remarks>
        /// Unlike the raw table, these tables hold several rows per source timestamp
        /// by design - every modification of a sample logs at its timestamp, and two
        /// users may annotate the same instant - so the resume token carries the
        /// count of rows already returned at the boundary timestamp along with the
        /// timestamp itself. A token of only the timestamp would drop the rest of a
        /// group a page boundary lands in.
        /// </remarks>
        private static HistorianPage<T> ReadPage<T>(
            DataView view,
            DateTime windowStart,
            DateTime windowEnd,
            uint maxValues,
            bool isForward,
            HistorianResumeToken resumeToken,
            Func<DataRow, T> select)
        {
            uint pageSize = maxValues != 0 ? maxValues : kDefaultPageSize;
            (DateTime resumeAt, int resumeSkip) = DecodeGroupPosition(resumeToken);
            bool resuming = resumeAt != DateTime.MinValue;

            List<T> values = new List<T>();
            DateTime lastReturned = DateTime.MinValue;
            int returnedAtLast = 0;
            int skipped = 0;

            int ii = isForward ? 0 : view.Count - 1;

            while (ii >= 0 && ii < view.Count)
            {
                DataRow row = view[ii].Row;
                DateTime timestamp = (DateTime)row[0];
                ii += isForward ? 1 : -1;

                if (isForward
                    ? timestamp < windowStart || timestamp >= windowEnd
                    : timestamp <= windowStart || timestamp > windowEnd)
                {
                    continue;
                }

                if (resuming)
                {
                    if (isForward ? timestamp < resumeAt : timestamp > resumeAt)
                    {
                        continue;
                    }

                    if (timestamp == resumeAt && skipped < resumeSkip)
                    {
                        skipped++;
                        continue;
                    }
                }

                values.Add(select(row));

                if (timestamp == lastReturned)
                {
                    returnedAtLast++;
                }
                else
                {
                    lastReturned = timestamp;
                    returnedAtLast = 1;
                }

                if (values.Count >= pageSize)
                {
                    int carriedOver = resuming && lastReturned == resumeAt ? resumeSkip : 0;
                    return new HistorianPage<T>(values, EncodeGroupPosition(lastReturned, carriedOver + returnedAtLast));
                }
            }

            return new HistorianPage<T>(values);
        }

        /// <summary>
        /// Reads the value recorded at the requested time, interpolating between the
        /// neighbouring samples the way the item is configured to when the archive
        /// holds no sample at that exact time.
        /// </summary>
        private static DataValue ReadValueAtTime(ArchiveItemState item, DataView view, DateTime requestedTime, bool useSimpleBounds)
        {
            // find the value at the time.
            int index = item.FindValueAtOrBefore(view, requestedTime, !useSimpleBounds, out bool dataBeforeIgnored);

            if (index < 0)
            {
                return DataValue.FromStatusCode(StatusCodes.BadNoData, requestedTime);
            }

            // nothing more to do if a raw value exists.
            if ((DateTime)view[index].Row[0] == requestedTime)
            {
                return (DataValue)view[index].Row[2];
            }

            DataValue before = (DataValue)view[index].Row[2];
            DataValue value;

            // find the value after the time.
            int afterIndex = item.FindValueAfter(view, index, !useSimpleBounds, out bool dataAfterIgnored);

            // use stepped interpolation if the item is stepped or no end bound exists.
            if (afterIndex < 0 || item.ArchiveItem.Stepped)
            {
                value = AggregateCalculator.SteppedInterpolate(requestedTime, before);

                if (StatusCode.IsNotBad(value.StatusCode) && dataBeforeIgnored)
                {
                    value = MarkAsSubNormal(value);
                }

                return value;
            }

            value = AggregateCalculator.SlopedInterpolate(requestedTime, before, (DataValue)view[afterIndex].Row[2]);

            if (StatusCode.IsNotBad(value.StatusCode) && (dataBeforeIgnored || dataAfterIgnored))
            {
                value = MarkAsSubNormal(value);
            }

            return value;
        }

        /// <summary>
        /// Marks an interpolated value which had to skip over bad neighbours.
        /// </summary>
        private static DataValue MarkAsSubNormal(DataValue value)
        {
            return new DataValue(
                value.WrappedValue,
                value.StatusCode.WithCodeBits(StatusCodes.UncertainDataSubNormal),
                value.SourceTimestamp,
                value.ServerTimestamp,
                value.SourcePicoseconds,
                value.ServerPicoseconds);
        }

        /// <summary>
        /// Returns the last sample before the timestamp as a bound, or the marker
        /// for a bound the archive cannot supply.
        /// </summary>
        private static HistoricalDataValue FindBoundBefore(DataView view, DateTime timestamp)
        {
            for (int ii = view.Count - 1; ii >= 0; ii--)
            {
                if ((DateTime)view[ii].Row[0] < timestamp)
                {
                    return new HistoricalDataValue((DataValue)view[ii].Row[2], true);
                }
            }

            return CreateMissingBound(timestamp);
        }

        /// <summary>
        /// Returns the first sample after the timestamp as a bound, or the marker
        /// for a bound the archive cannot supply.
        /// </summary>
        private static HistoricalDataValue FindBoundAfter(DataView view, DateTime timestamp)
        {
            for (int ii = 0; ii < view.Count; ii++)
            {
                if ((DateTime)view[ii].Row[0] > timestamp)
                {
                    return new HistoricalDataValue((DataValue)view[ii].Row[2], true);
                }
            }

            return CreateMissingBound(timestamp);
        }

        /// <summary>
        /// Creates the placeholder for a requested bound the archive cannot supply.
        /// </summary>
        private static HistoricalDataValue CreateMissingBound(DateTime timestamp)
        {
            return new HistoricalDataValue(
                new DataValue(Variant.Null, StatusCodes.BadBoundNotFound, timestamp, timestamp),
                true);
        }

        /// <summary>
        /// Drains the processed values the calculator has completed.
        /// </summary>
        private static void DrainCalculator(IAggregateCalculator calculator, bool returnPartial, List<DataValue> values)
        {
            while (calculator.TryGetProcessedValue(returnPartial, out DataValue processedValue))
            {
                values.Add(processedValue);
            }
        }

        /// <summary>
        /// Encodes the timestamp a raw page ended at into the resume token the
        /// framework hands back for the next page.
        /// </summary>
        private static HistorianResumeToken EncodeTimestamp(DateTime timestamp)
        {
            byte[] state = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(state, timestamp.Ticks);
            return new HistorianResumeToken(state);
        }

        /// <summary>
        /// Decodes the timestamp the previous raw page ended at; MinValue on the
        /// first page.
        /// </summary>
        private static DateTime DecodeTimestamp(HistorianResumeToken token)
        {
            if (token.IsEmpty || token.State.Length != 8)
            {
                return DateTime.MinValue;
            }

            return new DateTime(BinaryPrimitives.ReadInt64BigEndian(token.State.Span), DateTimeKind.Utc);
        }

        /// <summary>
        /// Encodes where a modified or annotation page ended: the boundary timestamp
        /// and how many rows at that timestamp the pages so far returned.
        /// </summary>
        private static HistorianResumeToken EncodeGroupPosition(DateTime timestamp, int returnedAtTimestamp)
        {
            byte[] state = new byte[12];
            BinaryPrimitives.WriteInt64BigEndian(state, timestamp.Ticks);
            BinaryPrimitives.WriteInt32BigEndian(state.AsSpan(8), returnedAtTimestamp);
            return new HistorianResumeToken(state);
        }

        /// <summary>
        /// Decodes where the previous modified or annotation page ended; MinValue
        /// and zero on the first page.
        /// </summary>
        private static (DateTime Timestamp, int Count) DecodeGroupPosition(HistorianResumeToken token)
        {
            if (token.IsEmpty || token.State.Length != 12)
            {
                return (DateTime.MinValue, 0);
            }

            return (
                new DateTime(BinaryPrimitives.ReadInt64BigEndian(token.State.Span), DateTimeKind.Utc),
                BinaryPrimitives.ReadInt32BigEndian(token.State.Span.Slice(8)));
        }
        #endregion

        #region Private Fields
        private const uint kDefaultPageSize = 1000;

        /// <summary>
        /// What every item of this archive supports; the flags mirror what the
        /// sample advertised in HistoryServerCapabilities before the roll-up took
        /// over: reads of every kind, inserts, replaces, updates, both deletes and
        /// annotations.
        /// </summary>
        private static readonly HistorianNodeCapabilities s_capabilities = new HistorianNodeCapabilities {
            InsertData = true,
            ReplaceData = true,
            UpdateData = true,
            DeleteRaw = true,
            DeleteAtTime = true,
            InsertAnnotation = true,
            ServerTimestampSupported = true
        };

        private readonly IServerInternal m_server;
        private readonly UnderlyingSystem m_system;
        private readonly ILogger m_logger;
        #endregion
    }
}
