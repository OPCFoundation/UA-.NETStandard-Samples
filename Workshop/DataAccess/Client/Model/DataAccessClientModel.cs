/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Quickstarts.DataAccessClient.Model
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// One child of a node in the address space, the way the browse tree shows it.
    /// </summary>
    /// <param name="NodeId">The node, or <see cref="NodeId.Null"/> when it lives on another server.</param>
    /// <param name="Text">The text the tree shows for it.</param>
    /// <param name="NodeClass">The class of the node.</param>
    /// <param name="IsLocal">False for a reference into another server, which cannot be followed.</param>
    public sealed record BrowseNode(NodeId NodeId, string Text, NodeClass NodeClass, bool IsLocal)
    {
        /// <summary>
        /// True for a variable of this server, the only kind of node which can be
        /// monitored, written or read from history.
        /// </summary>
        public bool IsLocalVariable => IsLocal && NodeClass == NodeClass.Variable;
    }

    /// <summary>
    /// One attribute or property of a node, formatted for the attribute list.
    /// </summary>
    /// <param name="Name">The name of the attribute or property.</param>
    /// <param name="DataType">The built-in type of its value, with [] for an array.</param>
    /// <param name="Value">The value, or the status code when it could not be read.</param>
    public sealed record AttributeRow(string Name, string DataType, string Value);

    /// <summary>
    /// What the monitored item list shows for one item.
    /// </summary>
    /// <param name="Name">The name which identifies the item within the subscription.</param>
    /// <param name="NodeId">The variable the item monitors.</param>
    /// <param name="ClientHandle">The handle the engine assigned, or null before it did.</param>
    /// <param name="DisplayName">The text the user picked the variable by.</param>
    /// <param name="MonitoringMode">The monitoring mode, revised once the server accepted the item.</param>
    /// <param name="SamplingIntervalMs">The sampling interval in milliseconds, revised once the server accepted the item.</param>
    /// <param name="DeadbandText">The deadband filter as text, "None" when there is none.</param>
    /// <param name="Error">The status the server refused the item or its last change with, or empty.</param>
    /// <param name="Value">The last value the item reported, or null before it reported one.</param>
    public sealed record MonitoredItemRow(
        string Name,
        NodeId NodeId,
        uint? ClientHandle,
        string DisplayName,
        MonitoringMode MonitoringMode,
        double SamplingIntervalMs,
        string DeadbandText,
        string Error,
        DataValue? Value);

    /// <summary>
    /// The settings of a raw or modified read of the history of a variable.
    /// </summary>
    /// <param name="StartTime">The start of the range, or <see cref="DateTime.MinValue"/> for no bound.</param>
    /// <param name="EndTime">The end of the range, or <see cref="DateTime.MinValue"/> for no bound.</param>
    /// <param name="MaxValues">The most values one page holds, 0 for no limit.</param>
    /// <param name="ReturnBounds">Whether the values at the bounds of the range are returned too.</param>
    /// <param name="IsReadModified">True to read the values which were modified instead of the raw ones.</param>
    public sealed record RawHistoryRequest(
        DateTime StartTime,
        DateTime EndTime,
        uint MaxValues,
        bool ReturnBounds,
        bool IsReadModified);

    /// <summary>
    /// The settings of an aggregated read of the history of a variable.
    /// </summary>
    /// <param name="StartTime">The start of the range.</param>
    /// <param name="EndTime">The end of the range.</param>
    /// <param name="ProcessingIntervalMs">The width of one aggregation interval in milliseconds.</param>
    /// <param name="AggregateId">The aggregate function, one of the AggregateFunction objects.</param>
    public sealed record ProcessedHistoryRequest(
        DateTime StartTime,
        DateTime EndTime,
        double ProcessingIntervalMs,
        NodeId AggregateId);

    /// <summary>
    /// One page of the history of a variable.
    /// </summary>
    /// <param name="Values">The values of the page, in time order.</param>
    /// <param name="ContinuationPoint">What the server needs to return the next page, null after the last one.</param>
    public sealed record HistoryPage(IReadOnlyList<DataValue> Values, ByteString ContinuationPoint)
    {
        /// <summary>
        /// True while the server holds more values for this read.
        /// </summary>
        public bool HasMore => !ContinuationPoint.IsNull && ContinuationPoint.Length > 0;
    }

    /// <summary>
    /// The payload of <see cref="DataAccessClientModel.ValueChanged"/>.
    /// </summary>
    public sealed class MonitoredItemValueChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public MonitoredItemValueChangedEventArgs(string name, DataValue value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>
        /// The name of the item within the subscription.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Its new value.
        /// </summary>
        public DataValue Value { get; }
    }

    /// <summary>
    /// The client model of the Data Access client: browses the address space, reads and
    /// writes attributes, reads history, and monitors the variables the user picks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscription is created lazily, on the first monitored item, and deleted when
    /// the session is detached. The V2 engine identifies an item by a name which is unique
    /// within its subscription and reports that name with every notification; the model
    /// hands the same name out in every <see cref="MonitoredItemRow"/>, so the window can
    /// key its list on it.
    /// </para>
    /// <para>
    /// The engine has no ApplyChanges: adding an item, or reconfiguring its options, is the
    /// request, and the engine applies it on its own worker. Every method here which changes
    /// an item therefore waits for that worker before it returns the revised settings.
    /// </para>
    /// </remarks>
    public sealed class DataAccessClientModel : SampleClientModel
    {
        /// <summary>
        /// How long the model waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        // the V2 engine takes the notification handler when the subscription is created,
        // so the model owns one for its whole lifetime and points it at its own method.
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        // the items by their name, and the last value each reported. Both are written by
        // the publish worker and read by whichever thread calls the model, hence the lock.
        private readonly Dictionary<string, MonitoredItemEntry> m_entries = new Dictionary<string, MonitoredItemEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, DataValue> m_lastValues = new Dictionary<string, DataValue>(StringComparer.Ordinal);

        private ISubscription m_subscription;
        private int m_nextItemId;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public DataAccessClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_callbacks.DataChangeCallback = OnDataChanges;
        }

        /// <summary>
        /// The items which are being monitored, in the order they were added.
        /// </summary>
        public IReadOnlyList<MonitoredItemRow> MonitoredItems
        {
            get
            {
                lock (m_entries)
                {
                    return m_entries.Values.Select(ToRow).ToList();
                }
            }
        }

        /// <summary>
        /// Raised for every value a monitored item reports.
        /// </summary>
        public event EventHandler<MonitoredItemValueChangedEventArgs> ValueChanged;

        #region Browsing
        /// <summary>
        /// Finds the components of a node and the nodes it organizes, which is what the
        /// browse tree shows below it.
        /// </summary>
        /// <param name="parentId">The node to browse.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<BrowseNode>> BrowseChildrenAsync(NodeId parentId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodesToBrowse = new List<BrowseDescription> {
                // the components of the node.
                new BrowseDescription {
                    NodeId = parentId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.Aggregates,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                    ResultMask = (uint)BrowseResultMask.All,
                },
                // the nodes organized by the node.
                new BrowseDescription {
                    NodeId = parentId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                    ResultMask = (uint)BrowseResultMask.All,
                },
            };

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, nodesToBrowse, false, ct)
                .ConfigureAwait(false);

            var children = new List<BrowseNode>();

            if (references == null)
            {
                return children;
            }

            foreach (ReferenceDescription reference in references)
            {
                // a reference into another server cannot be followed from this session.
                bool isLocal = !reference.NodeId.IsAbsolute;

                children.Add(new BrowseNode(
                    isLocal ? ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris) : NodeId.Null,
                    Utils.Format("{0}", reference),
                    reference.NodeClass,
                    isLocal));
            }

            return children;
        }

        /// <summary>
        /// Reads every attribute a node may have, and the values of its properties.
        /// </summary>
        /// <remarks>
        /// All attributes are asked for; the ones the node class does not have come back as
        /// BadAttributeIdInvalid and are left out, which saves a round trip to find out the
        /// node class first.
        /// </remarks>
        /// <param name="nodeId">The node.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<AttributeRow>> ReadAttributesAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodesToRead = new List<ReadValueId>();

            for (uint ii = Attributes.NodeClass; ii <= Attributes.UserExecutable; ii++)
            {
                nodesToRead.Add(new ReadValueId { NodeId = nodeId, AttributeId = ii });
            }

            int startOfProperties = nodesToRead.Count;

            // the properties of the node are read in the same request as its attributes.
            var nodeToBrowse = new BrowseDescription {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, nodeToBrowse, false, ct)
                .ConfigureAwait(false);

            var rows = new List<AttributeRow>();

            if (references == null)
            {
                return rows;
            }

            var properties = new List<ReferenceDescription>();

            foreach (ReferenceDescription reference in references)
            {
                // a property on another server cannot be read from this session.
                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                properties.Add(reference);

                nodesToRead.Add(new ReadValueId {
                    NodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris),
                    AttributeId = Attributes.Value,
                });
            }

            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct).ConfigureAwait(false);

            List<DataValue> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            for (int ii = 0; ii < results.Count; ii++)
            {
                DataValue result = results[ii];
                string name;
                string dataType;
                string value;

                if (ii < startOfProperties)
                {
                    // an attribute the node class does not have.
                    if (result.StatusCode == StatusCodes.BadAttributeIdInvalid)
                    {
                        continue;
                    }

                    name = Attributes.GetBrowseName(nodesToRead[ii].AttributeId);

                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        dataType = Utils.Format("{0}", Attributes.GetDataTypeId(nodesToRead[ii].AttributeId));
                        value = Utils.Format("{0}", result.StatusCode);
                    }
                    else
                    {
                        dataType = DataTypeText(result);
                        value = result.WrappedValue.ToString();
                    }
                }
                else
                {
                    // a property which went away between the browse and the read.
                    if (result.StatusCode == StatusCodes.BadNodeIdUnknown)
                    {
                        continue;
                    }

                    name = Utils.Format("{0}", properties[ii - startOfProperties]);

                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        dataType = string.Empty;
                        value = Utils.Format("{0}", result.StatusCode);
                    }
                    else
                    {
                        dataType = DataTypeText(result);
                        value = result.WrappedValue.ToString();
                    }
                }

                rows.Add(new AttributeRow(name, dataType, value));
            }

            return rows;
        }

        /// <summary>
        /// Reads one attribute of a node.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        /// <param name="attributeId">The attribute.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<DataValue> ReadAttributeAsync(NodeId nodeId, uint attributeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = attributeId },
            };

            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct).ConfigureAwait(false);

            List<DataValue> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            return results[0];
        }

        /// <summary>
        /// Writes one attribute of a node.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        /// <param name="attributeId">The attribute.</param>
        /// <param name="value">The value, already converted to the type of the attribute.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The status the server answered with.</returns>
        public async Task<StatusCode> WriteAsync(NodeId nodeId, uint attributeId, Variant value, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var valuesToWrite = new List<WriteValue> {
                new WriteValue {
                    NodeId = nodeId,
                    AttributeId = attributeId,
                    Value = new DataValue(value, StatusCodes.Good, DateTime.MinValue, DateTime.MinValue),
                },
            };

            WriteResponse response = await session.WriteAsync(null, valuesToWrite, ct).ConfigureAwait(false);

            List<StatusCode> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, valuesToWrite);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

            return results[0];
        }

        /// <summary>
        /// Changes the locale the server answers in.
        /// </summary>
        /// <param name="locale">The locale.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task SetLocaleAsync(string locale, CancellationToken ct = default)
        {
            return RequireSession().ChangePreferredLocalesAsync(new List<string> { locale }, ct);
        }
        #endregion

        #region History
        /// <summary>
        /// Reads the timestamp of the oldest value in the history of a variable.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The timestamp, or <see cref="DateTime.MinValue"/> when the variable has no history.</returns>
        public async Task<DateTime> ReadFirstTimestampAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var details = new ReadRawModifiedDetails {
                StartTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTime = DateTime.Today.AddDays(1),
                IsReadModified = false,
                NumValuesPerNode = 1,
                ReturnBounds = false,
            };

            var nodeToRead = new HistoryReadValueId { NodeId = nodeId };
            var nodesToRead = new List<HistoryReadValueId> { nodeToRead };

            List<HistoryReadResult> results = await HistoryReadAsync(session, details, false, nodesToRead, ct).ConfigureAwait(false);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                return DateTime.MinValue;
            }

            if (ExtensionObject.ToEncodeable(results[0].HistoryData) is not HistoryData data || data.DataValues.Count == 0)
            {
                return DateTime.MinValue;
            }

            DateTime startTime = (DateTime)data.DataValues[0].SourceTimestamp;

            // one value was asked for, but the server may still hold a continuation point
            // for the rest, and that is released so it does not count against the limit.
            if (results[0].ContinuationPoint.IsNull)
            {
                return startTime;
            }

            nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

            await HistoryReadAsync(session, details, true, nodesToRead, ct).ConfigureAwait(false);

            return startTime;
        }

        /// <summary>
        /// Reads a page of the raw or modified history of a variable.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="request">What to read.</param>
        /// <param name="continuationPoint">The continuation point of the previous page, or null for the first one.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<HistoryPage> ReadRawAsync(
            NodeId nodeId,
            RawHistoryRequest request,
            ByteString continuationPoint = default,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var details = new ReadRawModifiedDetails {
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                IsReadModified = request.IsReadModified,
                NumValuesPerNode = request.MaxValues,
                ReturnBounds = request.ReturnBounds,
            };

            return ReadPageAsync(nodeId, details, continuationPoint, ct);
        }

        /// <summary>
        /// Reads a page of the aggregated history of a variable.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="request">What to read.</param>
        /// <param name="continuationPoint">The continuation point of the previous page, or null for the first one.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<HistoryPage> ReadProcessedAsync(
            NodeId nodeId,
            ProcessedHistoryRequest request,
            ByteString continuationPoint = default,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var details = new ReadProcessedDetails {
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                ProcessingInterval = request.ProcessingIntervalMs,
                AggregateType = new[] { request.AggregateId }.ToArrayOf(),
            };

            return ReadPageAsync(nodeId, details, continuationPoint, ct);
        }

        /// <summary>
        /// Tells the server that the rest of a paged read is not wanted.
        /// </summary>
        /// <param name="nodeId">The variable the read was on.</param>
        /// <param name="continuationPoint">The continuation point of the last page.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ReleaseContinuationPointAsync(NodeId nodeId, ByteString continuationPoint, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (continuationPoint.IsNull || continuationPoint.Length == 0)
            {
                return;
            }

            var nodesToRead = new List<HistoryReadValueId> {
                new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint },
            };

            // the details do not matter for a release, but the request needs some.
            await HistoryReadAsync(session, new ReadRawModifiedDetails(), true, nodesToRead, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads one page of history with the given details.
        /// </summary>
        private async Task<HistoryPage> ReadPageAsync(
            NodeId nodeId,
            HistoryReadDetails details,
            ByteString continuationPoint,
            CancellationToken ct)
        {
            ISession session = RequireSession();

            var nodesToRead = new List<HistoryReadValueId> {
                new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint },
            };

            List<HistoryReadResult> results = await HistoryReadAsync(session, details, false, nodesToRead, ct).ConfigureAwait(false);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            IReadOnlyList<DataValue> values = ExtensionObject.ToEncodeable(results[0].HistoryData) is HistoryData data
                ? data.DataValues.ToList()
                : Array.Empty<DataValue>();

            return new HistoryPage(values, results[0].ContinuationPoint);
        }

        /// <summary>
        /// Sends one HistoryRead request and validates the response.
        /// </summary>
        private static async Task<List<HistoryReadResult>> HistoryReadAsync(
            ISession session,
            HistoryReadDetails details,
            bool releaseContinuationPoints,
            List<HistoryReadValueId> nodesToRead,
            CancellationToken ct)
        {
            HistoryReadResponse response = await session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                releaseContinuationPoints,
                nodesToRead,
                ct).ConfigureAwait(false);

            List<HistoryReadResult> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            return results;
        }
        #endregion

        #region Monitoring
        /// <summary>
        /// Monitors the value of a variable, creating the subscription on the first item.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="displayName">The text the user picked the variable by.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The item with the settings the server revised.</returns>
        public async Task<MonitoredItemRow> MonitorAsync(NodeId nodeId, string displayName, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            ISubscription subscription = m_subscription;

            if (subscription == null)
            {
                // the V2 engine takes the settings through an options monitor, and
                // reconfiguring that monitor is what modifies the subscription later on.
                var options = new OptionsMonitor<SubscriptionOptions>(
                    SampleSession.DefaultSubscriptionOptions with { Priority = 100 });

                subscription = SampleSession.AddSubscription(session, m_callbacks, options);
                m_subscription = subscription;
            }

            // the item is identified by a name which is unique within the subscription. Its
            // settings live in the entry, because the engine only reports revised values.
            var entry = new MonitoredItemEntry(
                Utils.Format("Item{0}", Interlocked.Increment(ref m_nextItemId)),
                new MonitoredItemOptions {
                    StartNodeId = nodeId,
                    AttributeId = Attributes.Value,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = TimeSpan.FromMilliseconds(1000),
                    QueueSize = 0,
                    DiscardOldest = true,
                }) {
                DisplayName = displayName,
            };

            lock (m_entries)
            {
                m_entries[entry.Name] = entry;
            }

            // adding the item to the collection is the request; the engine applies it.
            subscription.MonitoredItems.TryAdd(entry.Name, entry.Options, out IMonitoredItem monitoredItem);
            entry.Item = monitoredItem;

            await SampleSession.WaitForPendingChangesAsync(subscription, kApplyTimeout, ct).ConfigureAwait(false);

            return ToRow(entry);
        }

        /// <summary>
        /// Changes the monitoring mode of items.
        /// </summary>
        /// <param name="names">The names of the items.</param>
        /// <param name="monitoringMode">The new mode.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The items with the settings the server revised.</returns>
        public Task<IReadOnlyList<MonitoredItemRow>> SetMonitoringModeAsync(
            IReadOnlyList<string> names,
            MonitoringMode monitoringMode,
            CancellationToken ct = default)
        {
            return ReconfigureAsync(
                names,
                options => options with { MonitoringMode = monitoringMode },
                false,
                ct);
        }

        /// <summary>
        /// Changes the sampling interval of items.
        /// </summary>
        /// <param name="names">The names of the items.</param>
        /// <param name="samplingIntervalMs">The new interval in milliseconds.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The items with the settings the server revised.</returns>
        public Task<IReadOnlyList<MonitoredItemRow>> SetSamplingIntervalAsync(
            IReadOnlyList<string> names,
            double samplingIntervalMs,
            CancellationToken ct = default)
        {
            return ReconfigureAsync(
                names,
                options => options with { SamplingInterval = TimeSpan.FromMilliseconds(samplingIntervalMs) },
                false,
                ct);
        }

        /// <summary>
        /// Changes the deadband filter of items.
        /// </summary>
        /// <remarks>
        /// A server refuses a deadband on a variable which is not numeric. The engine keeps
        /// the refused filter as the pending settings of the item, so a refused filter is
        /// dropped again: the list then shows "None", which is what the server applies.
        /// </remarks>
        /// <param name="names">The names of the items.</param>
        /// <param name="deadbandType">The kind of deadband, <see cref="DeadbandType.None"/> to remove it.</param>
        /// <param name="deadbandValue">The width of the deadband, absolute or in percent of the range.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The items with the settings the server revised.</returns>
        public Task<IReadOnlyList<MonitoredItemRow>> SetDeadbandAsync(
            IReadOnlyList<string> names,
            DeadbandType deadbandType,
            double deadbandValue,
            CancellationToken ct = default)
        {
            DataChangeFilter filter = null;

            if (deadbandType != DeadbandType.None)
            {
                filter = new DataChangeFilter {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)deadbandType,
                    DeadbandValue = deadbandValue,
                };
            }

            return ReconfigureAsync(
                names,
                options => options with { Filter = filter },
                true,
                ct);
        }

        /// <summary>
        /// Stops monitoring items.
        /// </summary>
        /// <param name="names">The names of the items.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task RemoveAsync(IReadOnlyList<string> names, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(names);

            ISubscription subscription = m_subscription;

            foreach (string name in names)
            {
                MonitoredItemEntry entry;

                lock (m_entries)
                {
                    if (!m_entries.Remove(name, out entry))
                    {
                        continue;
                    }

                    m_lastValues.Remove(name);
                }

                // removing the item from the collection is the delete request.
                if (subscription != null && entry.Item != null)
                {
                    subscription.MonitoredItems.TryRemove(entry.Item.ClientHandle);
                }
            }

            if (subscription != null)
            {
                await SampleSession.WaitForPendingChangesAsync(subscription, kApplyTimeout, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Converts a monitoring filter to text for display.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The deadband formatted as a string, "None" when there is none.</returns>
        public static string DeadbandFilterToText(MonitoringFilter filter)
        {
            if (filter is DataChangeFilter datachangeFilter)
            {
                if (datachangeFilter.DeadbandType == (uint)DeadbandType.Absolute)
                {
                    return Utils.Format("{0:##.##}", datachangeFilter.DeadbandValue);
                }

                if (datachangeFilter.DeadbandType == (uint)DeadbandType.Percent)
                {
                    return Utils.Format("{0:##.##}%", datachangeFilter.DeadbandValue);
                }
            }

            return "None";
        }

        /// <summary>
        /// Reconfigures items, waits for the engine to apply the change and reports the
        /// revised settings.
        /// </summary>
        private async Task<IReadOnlyList<MonitoredItemRow>> ReconfigureAsync(
            IReadOnlyList<string> names,
            Func<MonitoredItemOptions, MonitoredItemOptions> configure,
            bool revertWhenRefused,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(names);

            ISubscription subscription = m_subscription
                ?? throw new InvalidOperationException("There is no subscription to reconfigure.");

            List<MonitoredItemEntry> entries = FindEntries(names);

            // reconfiguring the options of an item is what modifies it; the engine picks
            // the change up on its own worker.
            foreach (MonitoredItemEntry entry in entries)
            {
                entry.Configure(configure);
            }

            await SampleSession.WaitForPendingChangesAsync(subscription, kApplyTimeout, ct).ConfigureAwait(false);

            if (revertWhenRefused)
            {
                bool reverted = false;

                foreach (MonitoredItemEntry entry in entries)
                {
                    if (entry.Item != null && ServiceResult.IsBad(entry.Item.Error))
                    {
                        entry.Configure(options => options with { Filter = null });
                        reverted = true;
                    }
                }

                if (reverted)
                {
                    await SampleSession.WaitForPendingChangesAsync(subscription, kApplyTimeout, ct).ConfigureAwait(false);
                }
            }

            return entries.Select(ToRow).ToList();
        }

        /// <summary>
        /// The entries of the named items which are still monitored.
        /// </summary>
        private List<MonitoredItemEntry> FindEntries(IReadOnlyList<string> names)
        {
            var entries = new List<MonitoredItemEntry>();

            lock (m_entries)
            {
                foreach (string name in names)
                {
                    if (m_entries.TryGetValue(name, out MonitoredItemEntry entry))
                    {
                        entries.Add(entry);
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// What the list shows for an entry: the revised settings once the server accepted
        /// the item, the requested ones before that.
        /// </summary>
        private MonitoredItemRow ToRow(MonitoredItemEntry entry)
        {
            IMonitoredItem item = entry.Item;
            MonitoredItemOptions settings = entry.Settings;
            DataValue? lastValue = null;

            lock (m_entries)
            {
                if (m_lastValues.TryGetValue(entry.Name, out DataValue reported))
                {
                    lastValue = reported;
                }
            }

            // the engine reports no revised filter, only whether the server accepted the one
            // which was requested, so the requested filter is what the list shows.
            return new MonitoredItemRow(
                entry.Name,
                settings.StartNodeId,
                item?.ClientHandle,
                entry.DisplayName,
                entry.Created ? item.CurrentMonitoringMode : settings.MonitoringMode,
                (entry.Created ? item.CurrentSamplingInterval : settings.SamplingInterval).TotalMilliseconds,
                DeadbandFilterToText(settings.Filter),
                item != null && ServiceResult.IsBad(item.Error) ? item.Error.StatusCode.ToString() : string.Empty,
                lastValue);
        }

        /// <summary>
        /// Reports the new values of the monitored items.
        /// </summary>
        /// <remarks>
        /// The V2 engine calls this on a publish worker and reports the whole notification
        /// instead of one value per item. Each change is turned into one event, which the
        /// base class posts to the thread the model was created on. The value is kept as
        /// well, so an item which was just added can show it even when the notification
        /// overtook the row.
        /// </remarks>
        private void OnDataChanges(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] notifications,
            PublishState publishState)
        {
            foreach (DataValueChange change in notifications)
            {
                if (change.MonitoredItem == null)
                {
                    continue;
                }

                string name = change.MonitoredItem.Name;

                lock (m_entries)
                {
                    if (!m_entries.ContainsKey(name))
                    {
                        continue;
                    }

                    m_lastValues[name] = change.Value;
                }

                Raise(ValueChanged, new MonitoredItemValueChangedEventArgs(name, change.Value));
            }
        }
        #endregion

        #region Lifecycle
        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            // done before the session is closed: closing a session which still carries a
            // subscription waits for the publish pipeline to drain.
            ISubscription subscription = m_subscription;

            m_subscription = null;

            lock (m_entries)
            {
                m_entries.Clear();
                m_lastValues.Clear();
            }

            if (subscription != null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so there is nothing to re-attach:
        // the reconnect hooks of the base class are not overridden.
        #endregion

        /// <summary>
        /// The built-in type of a value as text, with [] for an array.
        /// </summary>
        private static string DataTypeText(DataValue value)
        {
            TypeInfo typeInfo = value.WrappedValue.TypeInfo;
            string dataType = typeInfo.BuiltInType.ToString();

            if (typeInfo.ValueRank >= ValueRanks.OneOrMoreDimensions)
            {
                dataType += "[]";
            }

            return dataType;
        }
    }
}
