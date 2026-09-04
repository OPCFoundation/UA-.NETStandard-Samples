/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Samples.Client;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "WinForms designer/owner lifetime manages this sample field.")]
    public partial class HistoryDataListView : SampleUserControl
    {
        /// <summary>
        /// How long the control waits for the subscription engine to apply a monitored item change.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How many values the control shows at a time when neither the client nor
        /// the server puts a number on it.
        /// </summary>
        private const uint kMaxRowsPerPage = 1000;

        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public HistoryDataListView()
        {
            InitializeComponent();
            m_callbacks.DataChangeCallback = OnDataChanges;
            ResultsDV.AutoGenerateColumns = false;
            LeftPN.Enabled = false;

            ReadTypeCB.Items.Add(HistoryReadType.Raw);
            ReadTypeCB.Items.Add(HistoryReadType.Processed);
            ReadTypeCB.Items.Add(HistoryReadType.Modified);
            ReadTypeCB.Items.Add(HistoryReadType.AtTime);
            ReadTypeCB.Items.Add(HistoryReadType.Subscribe);
            ReadTypeCB.Items.Add(HistoryReadType.Insert);
            ReadTypeCB.Items.Add(HistoryReadType.InsertReplace);
            ReadTypeCB.Items.Add(HistoryReadType.Replace);
            ReadTypeCB.Items.Add(HistoryReadType.Remove);
            ReadTypeCB.Items.Add(HistoryReadType.DeleteRaw);
            ReadTypeCB.Items.Add(HistoryReadType.DeleteModified);
            ReadTypeCB.Items.Add(HistoryReadType.DeleteAtTime);
            ReadTypeCB.SelectedIndex = 0;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Results");

            m_dataset.Tables[0].Columns.Add("Index", typeof(int));
            m_dataset.Tables[0].Columns.Add("SourceTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("StatusCode", typeof(StatusCode));
            m_dataset.Tables[0].Columns.Add("HistoryInfo", typeof(string));
            m_dataset.Tables[0].Columns.Add("UpdateType", typeof(HistoryUpdateType));
            m_dataset.Tables[0].Columns.Add("UpdateTime", typeof(string));
            m_dataset.Tables[0].Columns.Add("UserName", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataValue", typeof(DataValue));
            m_dataset.Tables[0].Columns.Add("UpdateResult", typeof(StatusCode));

            m_dataset.Tables[0].DefaultView.Sort = "Index";

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region HistoryReadType Class
        /// <summary>
        /// The type history read operation.
        /// </summary>
        public enum HistoryReadType
        {
            /// <summary>
            /// Subscribe to data changes.
            /// </summary>
            Subscribe,

            /// <summary>
            /// Read raw data.
            /// </summary>
            Raw,

            /// <summary>
            /// Read modified data.
            /// </summary>
            Modified,

            /// <summary>
            /// Read data at the specified times.
            /// </summary>
            AtTime,

            /// <summary>
            /// Read processed data.
            /// </summary>
            Processed,

            /// <summary>
            /// Insert data.
            /// </summary>
            Insert,

            /// <summary>
            /// Insert or replace data.
            /// </summary>
            InsertReplace,

            /// <summary>
            /// Replace data.
            /// </summary>
            Replace,

            /// <summary>
            /// Remove data.
            /// </summary>
            Remove,

            /// <summary>
            /// Delete raw data.
            /// </summary>
            DeleteRaw,

            /// <summary>
            /// Delete modified data.
            /// </summary>
            DeleteModified,

            /// <summary>
            /// Delete data at the specified times.
            /// </summary>
            DeleteAtTime
        }
        #endregion

        #region AvailableAggregate Class
        /// <summary>
        /// An aggregate supported by server.
        /// </summary>
        private sealed class AvailableAggregate
        {
            public NodeId NodeId { get; set; }
            public string DisplayName { get; set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
        #endregion

        #region AvailableSession Class
        /// <summary>
        /// A session available in the conntrol.
        /// </summary>
        #pragma warning disable CA1812 // Justification: sample type is retained for designer/reflection use.
        private sealed class AvailableSession
        #pragma warning restore CA1812
        {
            public Session Session { get; set; }

            public override string ToString()
            {
                return Session.SessionName;
            }
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private ISubscription m_subscription;
        private IMonitoredItem m_monitoredItem;
        private OptionsMonitor<MonitoredItemOptions> m_monitoredItemOptions;
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();
        private int m_nextItemId;
        private NodeId m_nodeId;
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        private int m_nextId;
        private bool m_isSubscribed;
        private HistoryClient m_historian;
        private HistoryServerCapabilitiesInfo m_capabilities;
        private IAsyncEnumerator<HistoryRow> m_reader;
        private bool m_timesChanged;
        private HistoricalDataConfigurationState m_configuration;
        private List<HistoricalProperty> m_properties;
        #endregion

        #region HistoryRow Class
        /// <summary>
        /// One row of an answer: the value, and for a modified read what was done to
        /// it and by whom.
        /// </summary>
        private sealed class HistoryRow
        {
            public HistoryRow(DataValue value, ModificationInfo info)
            {
                Value = value;
                Info = info;
            }

            public DataValue Value { get; }

            public ModificationInfo Info { get; }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The node id to use.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public NodeId NodeId => m_nodeId;

        public void ClearNodeId()
        {
            m_nodeId = NodeId.Null;
            NodeIdTB.Text = String.Empty;
        }

        public async Task SetNodeIdAsync(NodeId value, CancellationToken ct = default)
        {
            m_nodeId = value;

            if (m_session != null)
            {
                NodeIdTB.Text = await m_session.NodeCache.GetDisplayTextAsync(m_nodeId, ct);
            }
            else
            {
                if ((m_nodeId).IsNull)
                {
                    NodeIdTB.Text = String.Empty;
                }
                else
                {
                    NodeIdTB.Text = m_nodeId.ToString();
                }
            }
        }

        /// <summary>
        /// The type of read operation.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HistoryReadType ReadType
        {
            get { return (HistoryReadType)ReadTypeCB.SelectedItem; }
            set { ReadTypeCB.SelectedItem = value; }
        }

        /// <summary>
        /// The start time for the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DateTime StartTime
        {
            get
            {
                if (StartTimeCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return StartTimeDP.Value;
            }

            set
            {
                if (value < Utils.TimeBase)
                {
                    StartTimeCK.Checked = false;
                    return;
                }

                if (value.Kind == DateTimeKind.Local)
                {
                    value = value.ToUniversalTime();
                }

                StartTimeCK.Checked = true;
                StartTimeDP.Value = value;
            }
        }

        /// <summary>
        /// The end time for the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DateTime EndTime
        {
            get
            {
                if (EndTimeCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return EndTimeDP.Value;
            }

            set
            {
                if (value < Utils.TimeBase)
                {
                    EndTimeCK.Checked = false;
                    return;
                }

                if (value.Kind == DateTimeKind.Local)
                {
                    value = value.ToUniversalTime();
                }

                EndTimeCK.Checked = true;
                EndTimeDP.Value = value;
            }
        }

        /// <summary>
        /// THe maximum number of values to return.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public uint MaxReturnValues
        {
            get
            {
                if (MaxReturnValuesCK.Checked)
                {
                    return 0;
                }

                return (uint)MaxReturnValuesNP.Value;
            }

            set
            {
                MaxReturnValuesCK.Checked = value != 0;
                MaxReturnValuesNP.Value = value;
            }
        }

        /// <summary>
        /// If true the bounds are returned in the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ReturnBounds
        {
            get
            {
                return ReturnBoundsCK.Checked;
            }

            set
            {
                ReturnBoundsCK.Checked = value;
            }
        }

        /// <summary>
        /// The aggregate to calculate.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public NodeId Aggregate
        {
            get
            {
                AvailableAggregate aggregate = AggregateCB.SelectedItem as AvailableAggregate;

                if (aggregate == null)
                {
                    return NodeId.Null;
                }

                return aggregate.NodeId;
            }

            set
            {
                if ((value).IsNull)
                {
                    AggregateCB.SelectedIndex = -1;
                    return;
                }

                foreach (AvailableAggregate aggregate in AggregateCB.Items)
                {
                    if (aggregate.NodeId == value)
                    {
                        AggregateCB.SelectedItem = value;
                        return;

                    }
                }

                throw new ArgumentException("Aggregate does match one of the available aggregates.");
            }
        }

        /// <summary>
        /// The processing interval to use.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double ProcessingInterval
        {
            get { return (double)ProcessingIntervalNP.Value; }
            set { ProcessingIntervalNP.Value = (decimal)value; }
        }

        /// <summary>
        /// Changes the session.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "Sample control flow is intentional and analyzer reports a false positive.")]
        public async Task ChangeSessionAsync(ISession session, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            if (m_session != null)
            {
                await DeleteSubscriptionAsync(ct);
                await ReleaseReaderAsync();
                m_session = null;
                m_historian = null;
                m_capabilities = null;
            }

            if (session == null)
            {
                return;
            }

            m_session = session;
            m_telemetry = telemetry;
            m_dataset.Clear();
            LeftPN.Enabled = true;

            // the history services of the session, as one object: it builds the read
            // and update details, follows the continuation points a read leaves
            // behind, and releases the one still open when the caller stops early.
            m_historian = session.Historian();

            // what the server says it can do with history. a client which asks for an
            // operation the server does not have is told so one round trip later, so
            // the control asks once and shapes itself around the answer.
            m_capabilities = await m_historian.GetServerCapabilitiesAsync(ct).ConfigureAwait(true);
            InsertAnnotationMI.Enabled = m_capabilities.InsertAnnotation;

            #pragma warning disable CA1508 // Justification: sample control flow is intentional and analyzer reports a false positive.
            if (m_session != null)
            #pragma warning restore CA1508
            {
                AggregateCB.Items.Clear();

                ILocalNode node = await m_session.NodeCache.FindAsync(ObjectIds.Server_ServerCapabilities_AggregateFunctions, ct) as ILocalNode;

                if (node != null)
                {
                    foreach (IReference reference in node.References.Find(ReferenceTypeIds.HierarchicalReferences, false, true, m_session.TypeTree))
                    {
                        ILocalNode aggregate = await m_session.NodeCache.FindAsync(reference.TargetId, ct) as ILocalNode;

                        if (aggregate != null && aggregate.TypeDefinitionId == ObjectTypeIds.AggregateFunctionType)
                        {
                            AvailableAggregate item = new AvailableAggregate();
                            item.NodeId = aggregate.NodeId;
                            item.DisplayName = await m_session.NodeCache.GetDisplayTextAsync(aggregate, ct);
                            AggregateCB.Items.Add(item);
                        }
                    }

                    if (AggregateCB.Items.Count == 0)
                    {
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Interpolative, DisplayName = BrowseNames.AggregateFunction_Interpolative });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Average, DisplayName = BrowseNames.AggregateFunction_Average });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_TimeAverage, DisplayName = BrowseNames.AggregateFunction_TimeAverage });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Total, DisplayName = BrowseNames.AggregateFunction_Total });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Count, DisplayName = BrowseNames.AggregateFunction_Count });
                    }

                    if (AggregateCB.Items.Count > 0)
                    {
                        AggregateCB.SelectedIndex = 0;
                    }
                }

                SubscriptionStateChanged();
            }
        }

        /// <summary>
        /// Updates the control after the session has reconnected.
        /// </summary>
        /// <remarks>
        /// The V2 subscription engine keeps the subscription and its monitored items alive
        /// across a reconnect, so there is nothing left to look up here.
        /// </remarks>
        public void SessionReconnected(ISession session)
        {
            m_session = session;
        }

        /// <summary>
        /// Changes the node monitored by the control.
        /// </summary>
        public async Task ChangeNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            // whatever is being read is about the node the control is leaving.
            await ReleaseReaderAsync().ConfigureAwait(true);
            ShowReadInProgress(false);

            m_nodeId = nodeId;
            m_configuration = null;
            m_properties = null;
            PropertyCB.Items.Clear();
            m_dataset.Clear();
            NodeIdTB.Text = await m_session.NodeCache.GetDisplayTextAsync(m_nodeId, ct);

            if (!(nodeId).IsNull)
            {
                m_properties = await SampleHistory.FindPropertiesWithHistoryAsync(m_session, m_nodeId, ct);

                if (m_properties == null || m_properties.Count <= 1)
                {
                    PropertyLB.Visible = false;
                    PropertyCB.Visible = false;
                }
                else
                {
                    PropertyCB.Items.AddRange((object[])m_properties.ToArray());
                    PropertyCB.SelectedIndex = 0;
                    PropertyLB.Visible = true;
                    PropertyCB.Visible = true;
                }

                m_configuration = await SampleHistory.ReadConfigurationAsync(m_session, m_nodeId, ct);

                // whether history can be read is what the access level of the node says,
                // not whether it carries a HistoricalDataConfiguration companion object:
                // Part 11 leaves that object optional, and plenty of servers historize a
                // variable without one. Reading the companion object is still worth doing
                // - it is what the archive limits below come from - but a node which does
                // not have one keeps every read type it is entitled to.
                if (!await SampleHistory.IsHistoryReadableAsync(m_session, GetSelectedNode(), ct).ConfigureAwait(true))
                {
                    this.ReadTypeCB.Enabled = false;
                    this.ReadTypeCB.SelectedItem = HistoryReadType.Subscribe;
                }
                else
                {
                    this.ReadTypeCB.Enabled = true;

                    if (!m_timesChanged)
                    {
                        DateTime startTime = await SampleHistory.ReadFirstDateAsync(m_session, GetSelectedNode(), m_configuration, ct);

                        if (startTime != DateTime.MinValue)
                        {
                            StartTimeDP.Value = startTime;
                        }

                        DateTime endTime = await SampleHistory.ReadLastDateAsync(m_session, GetSelectedNode(), ct);

                        if (endTime != DateTime.MinValue)
                        {
                            EndTimeDP.Value = endTime;
                        }
                    }
                }
            }

            if (m_subscription != null)
            {
                // the node a monitored item watches cannot be modified, so the item is
                // replaced by one for the new node.
                MonitoredItemOptions options = m_monitoredItemOptions.CurrentValue with { StartNodeId = nodeId };

                m_subscription.MonitoredItems.TryRemove(m_monitoredItem.ClientHandle);
                AddMonitoredItem(options);

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout, ct);
                SubscriptionStateChanged();
            }
        }

        /// <summary>
        /// Sets the sort order for the control.
        /// </summary>
        /// <param name="mostRecentFirst">If true the most recent entries are displayed first.</param>
        public void SetSortOrder(bool mostRecentFirst)
        {
            if (m_dataset != null && m_dataset.Tables.Count > 0)
            {
                if (mostRecentFirst)
                {
                    m_dataset.Tables[0].DefaultView.Sort = "Index DESC";
                }
                else
                {
                    m_dataset.Tables[0].DefaultView.Sort = "Index";
                }
            }
        }

        /// <summary>
        /// A kludge to get around the stupid designer that keeps setting property values to bogus defaults.
        /// </summary>
        public void Reset()
        {
            ClearNodeId();
            ReadType = HistoryReadType.Raw;
            StartTime = DateTime.MinValue;
            EndTime = DateTime.MinValue;
            Aggregate = NodeId.Null;

            StartTimeCK.Checked = true;
            EndTimeCK.Checked = false;
            MaxReturnValuesCK.Checked = true;
            MaxReturnValuesNP.Value = 10;
            m_timesChanged = false;
            ProcessingIntervalNP.Value = 5000;
        }

        /// <summary>
        /// Shows the configuration.
        /// </summary>
        public async Task ShowConfigurationAsync(CancellationToken ct = default)
        {
            if (m_session != null)
            {
                if (m_configuration != null)
                {
                    await Windows.Create<ViewNodeStateDlg>().ShowDialogAsync(m_session, m_configuration, null, ct);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private async Task CreateSubscriptionAsync(CancellationToken ct = default)
        {
            if (m_session == null)
            {
                return;
            }

            m_subscription = ClientUtils.AddSubscription(
                m_session,
                m_callbacks,
                new OptionsMonitor<Opc.Ua.Client.Subscriptions.SubscriptionOptions>(ClientUtils.DefaultSubscriptionOptions));

            var options = new MonitoredItemOptions {
                StartNodeId = m_nodeId,
                AttributeId = Attributes.Value,
                SamplingInterval = TimeSpan.FromMilliseconds((double)SamplingIntervalNP.Value),
                QueueSize = 1000,
                DiscardOldest = true,
                TimestampsToReturn = TimestampsToReturn.Both,
            };

            // specify aggregate filter.
            if (AggregateCB.SelectedItem != null)
            {
                AggregateFilter filter = new AggregateFilter();

                if (StartTimeCK.Checked)
                {
                    filter.StartTime = StartTimeDP.Value.ToUniversalTime();
                }
                else
                {
                    filter.StartTime = DateTime.UtcNow;
                }

                filter.ProcessingInterval = (double)ProcessingIntervalNP.Value;
                filter.AggregateType = ((AvailableAggregate)AggregateCB.SelectedItem).NodeId;

                if (!filter.AggregateType.IsNull)
                {
                    options = options with { Filter = filter };
                }
            }

            AddMonitoredItem(options);

            await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout, ct);
            SubscriptionStateChanged();
        }

        /// <summary>
        /// Adds the monitored item which watches the current node to the subscription.
        /// </summary>
        private void AddMonitoredItem(MonitoredItemOptions options)
        {
            m_monitoredItemOptions = new OptionsMonitor<MonitoredItemOptions>(options);

            // the name has to be unique within the subscription, and an item which was just
            // removed may not have been reaped yet, so every item gets its own name.
            m_subscription.MonitoredItems.TryAdd(
                Utils.Format("Value{0}", ++m_nextItemId),
                m_monitoredItemOptions,
                out m_monitoredItem);
        }

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        private async Task DeleteSubscriptionAsync(CancellationToken ct = default)
        {
            if (m_subscription != null)
            {
                // disposing the subscription deletes it on the server and drops it from the
                // subscription manager of the session.
                await m_subscription.DisposeAsync();
                m_subscription = null;
                m_monitoredItem = null;
                m_monitoredItemOptions = null;
            }

            SubscriptionStateChanged();
        }

        /// <summary>
        /// Updates the controls after the subscription state changes.
        /// </summary>
        private void SubscriptionStateChanged()
        {
            if (m_monitoredItem != null)
            {
                if (ServiceResult.IsBad(m_monitoredItem.Error))
                {
                    StatusTB.Text = m_monitoredItem.Error.ToString();
                    return;
                }

                StatusTB.Text = "Monitoring started.";
                m_isSubscribed = true;
                GoBTN.Enabled = false;
                GoBTN.Visible = true;
                StopBTN.Enabled = true;
                NextBTN.Visible = false;
            }
            else
            {
                StatusTB.Text = "Monitoring stopped.";
                m_isSubscribed = false;
                GoBTN.Enabled = true;
                GoBTN.Visible = true;
                StopBTN.Enabled = false;
                NextBTN.Visible = false;
            }
        }

        /// <summary>
        /// Adds a value to the grid.
        /// </summary>
        private void AddValue(DataValue value, ModificationInfo modificationInfo)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            m_nextId += 10000;

            row[0] = m_nextId;
            UpdateRow(row, value, modificationInfo);

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Updates a value in the grid.
        /// </summary>
        private void UpdateRow(DataRow row, DataValue value, ModificationInfo modificationInfo)
        {
            row[1] = value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            row[2] = value.ServerTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            row[3] = value.WrappedValue;
            row[4] = new StatusCode(value.StatusCode.Code);
            row[5] = value.StatusCode.AggregateBits.ToString();

            if (modificationInfo != null)
            {
                row[6] = modificationInfo.UpdateType;
                row[7] = modificationInfo.ModificationTime.ToLocalTime().ToString("HH:mm:ss");
                row[8] = modificationInfo.UserName;
            }

            row[9] = value;
        }

        /// <summary>
        /// Updates the display with a new value for a monitored variable.
        /// </summary>
        private void OnDataChanges(ISubscription subscription, uint sequenceNumber, DateTime publishTime, DataValueChange[] changes, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, uint, DateTime, DataValueChange[], PublishState>(OnDataChanges), subscription, sequenceNumber, publishTime, changes, publishStateMask);
                return;
            }

            try
            {
                if (!Object.ReferenceEquals(subscription, m_subscription))
                {
                    return;
                }

                foreach (DataValueChange change in changes)
                {
                    if (!Object.ReferenceEquals(change.MonitoredItem, m_monitoredItem))
                    {
                        continue;
                    }

                    AddValue(change.Value, null);
                }

                m_dataset.AcceptChanges();

                if (ResultsDV.Rows.Count > 0)
                {
                    ResultsDV.FirstDisplayedCell = ResultsDV.Rows[ResultsDV.Rows.Count - 1].Cells[0];
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Returns the currently selected historical variable or property node id.
        /// </summary>
        private NodeId GetSelectedNode()
        {
            if (PropertyCB.SelectedIndex >= 0)
            {
                return ((HistoricalProperty)PropertyCB.SelectedItem).NodeId;
            }

            return m_nodeId;
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        /// <remarks>
        /// A modified read is the one kind the history client of the SDK cannot serve
        /// the control: it yields the values of an answer, and the modification info
        /// beside them - what was done to a value, when and by whom - is the whole
        /// point of reading modified history, so that read keeps its own reader.
        /// </remarks>
        private Task ReadRawAsync(bool isReadModified, CancellationToken ct = default)
        {
            NodeId nodeId = GetSelectedNode();
            DateTime startTime = StartTimeCK.Checked ? StartTimeDP.Value.ToUniversalTime() : DateTime.MinValue;
            DateTime endTime = EndTimeCK.Checked ? EndTimeDP.Value.ToUniversalTime() : DateTime.MinValue;

            if (isReadModified)
            {
                return StartReadAsync(ReadModifiedRowsAsync(nodeId, startTime, endTime, PageSize, ct), true, ct);
            }

            return StartReadAsync(
                AsRows(m_historian.ReadRawAsync(
                    nodeId,
                    startTime,
                    endTime,
                    PageSize,
                    ReturnBoundsCK.Checked,
                    TimestampsToReturn.Both,
                    ct)),
                false,
                ct);
        }

        /// <summary>
        /// Fetches the values recorded at a series of times.
        /// </summary>
        private Task ReadAtTimeAsync(CancellationToken ct = default)
        {
            DateTime startTime = StartTimeDP.Value.ToUniversalTime();
            List<DateTime> times = new List<DateTime>();

            for (int ii = 0; ii < MaxReturnValuesNP.Value; ii++)
            {
                times.Add(startTime.AddMilliseconds((double)(ii * TimeStepNP.Value)));
            }

            return StartReadAsync(
                AsRows(m_historian.ReadAtTimeAsync(
                    GetSelectedNode(),
                    times,
                    UseSimpleBoundsCK.Checked,
                    TimestampsToReturn.Both,
                    ct)),
                false,
                ct);
        }

        /// <summary>
        /// Fetches an aggregate over the recent history.
        /// </summary>
        private Task ReadProcessedAsync(CancellationToken ct = default)
        {
            AvailableAggregate aggregate = (AvailableAggregate)AggregateCB.SelectedItem;

            if (aggregate == null)
            {
                return Task.CompletedTask;
            }

            return StartReadAsync(
                AsRows(m_historian.ReadProcessedAsync(
                    m_nodeId,
                    aggregate.NodeId,
                    StartTimeDP.Value.ToUniversalTime(),
                    EndTimeDP.Value.ToUniversalTime(),
                    (double)ProcessingIntervalNP.Value,
                    null,
                    TimestampsToReturn.Both,
                    ct)),
                false,
                ct);
        }

        /// <summary>
        /// Starts a new read and shows its first page.
        /// </summary>
        /// <remarks>
        /// The history client hands out the answer of a read as a sequence which
        /// spans the whole time range: it issues the requests, carries the
        /// continuation point of one to the next, and releases the one still open
        /// when the caller stops pulling. The control walks that sequence a page at a
        /// time so that Go, Next and Stop keep meaning what they always did - Stop
        /// abandons the sequence, which is what releases the continuation point the
        /// server is holding.
        /// </remarks>
        private async Task StartReadAsync(IAsyncEnumerable<HistoryRow> rows, bool isModified, CancellationToken ct = default)
        {
            await ReleaseReaderAsync().ConfigureAwait(true);

            m_dataset.Tables[0].Rows.Clear();

            ResultsDV.Columns[5].Visible = isModified;
            ResultsDV.Columns[6].Visible = isModified;
            ResultsDV.Columns[7].Visible = isModified;
            ResultsDV.Columns[8].Visible = false;

            m_reader = rows.GetAsyncEnumerator(ct);

            await ReadNextAsync(ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Fetches the next page of the read in progress.
        /// </summary>
        private async Task ReadNextAsync(CancellationToken ct = default)
        {
            if (m_reader == null)
            {
                return;
            }

            uint pageSize = PageSize != 0 ? PageSize : kMaxRowsPerPage;
            bool exhausted = false;

            for (uint ii = 0; ii < pageSize; ii++)
            {
                if (!await m_reader.MoveNextAsync().ConfigureAwait(true))
                {
                    exhausted = true;
                    break;
                }

                AddValue(m_reader.Current.Value, m_reader.Current.Info);
            }

            m_dataset.AcceptChanges();

            if (exhausted)
            {
                await ReleaseReaderAsync().ConfigureAwait(true);
            }

            ShowReadInProgress(m_reader != null);
        }

        /// <summary>
        /// Abandons the read in progress, which releases the continuation point the
        /// server is holding for it.
        /// </summary>
        private async Task ReleaseReaderAsync()
        {
            if (m_reader == null)
            {
                return;
            }

            IAsyncEnumerator<HistoryRow> reader = m_reader;
            m_reader = null;

            await reader.DisposeAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Offers Next and Stop while a read has more to give, Go once it has not.
        /// </summary>
        private void ShowReadInProgress(bool inProgress)
        {
            GoBTN.Visible = !inProgress;
            GoBTN.Enabled = !inProgress;
            NextBTN.Visible = inProgress;
            NextBTN.Enabled = inProgress;
            StopBTN.Enabled = inProgress;
        }

        /// <summary>
        /// The number of values the control asks the server for at a time.
        /// </summary>
        /// <remarks>
        /// A server which caps how much it returns per request is honoured: asking
        /// for more than the cap only earns a smaller answer than the page the
        /// control is about to display.
        /// </remarks>
        private uint PageSize
        {
            get
            {
                uint requested = MaxReturnValuesCK.Checked ? (uint)MaxReturnValuesNP.Value : 0;
                uint limit = m_capabilities?.MaxReturnDataValues ?? 0;

                if (limit == 0)
                {
                    return requested;
                }

                return requested == 0 ? limit : Math.Min(requested, limit);
            }
        }

        /// <summary>
        /// Presents the values of a read as rows without modification info.
        /// </summary>
        private static async IAsyncEnumerable<HistoryRow> AsRows(IAsyncEnumerable<DataValue> values)
        {
            await foreach (DataValue value in values.ConfigureAwait(false))
            {
                yield return new HistoryRow(value, null);
            }
        }

        /// <summary>
        /// Reads the modified history of a node, following the continuation points
        /// the server leaves behind and releasing the one still open when the caller
        /// stops pulling.
        /// </summary>
        /// <remarks>
        /// This is what the history client of the SDK does for every other read; it
        /// is spelled out here because the modification info of an answer does not
        /// survive the sequence of values that client yields.
        /// </remarks>
        private async IAsyncEnumerable<HistoryRow> ReadModifiedRowsAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ExtensionObject details = new ExtensionObject(new ReadRawModifiedDetails {
                IsReadModified = true,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode,
                ReturnBounds = false,
            });

            ByteString continuationPoint = ByteString.Empty;
            ByteString openPoint = ByteString.Empty;

            try
            {
                while (true)
                {
                    HistoryReadValueId nodeToRead = new HistoryReadValueId {
                        NodeId = nodeId,
                        ContinuationPoint = continuationPoint,
                    };

                    HistoryReadResponse response = await m_session.HistoryReadAsync(
                        null,
                        details,
                        TimestampsToReturn.Both,
                        false,
                        new List<HistoryReadValueId> { nodeToRead },
                        ct).ConfigureAwait(false);

                    HistoryReadResult result = response.Results.ToList()[0];

                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode);
                    }

                    openPoint = result.ContinuationPoint;

                    if (ExtensionObject.ToEncodeable(result.HistoryData) is HistoryModifiedData data)
                    {
                        for (int ii = 0; ii < data.DataValues.Count; ii++)
                        {
                            yield return new HistoryRow(
                                data.DataValues[ii],
                                ii < data.ModificationInfos.Count ? data.ModificationInfos[ii] : null);
                        }
                    }

                    if (result.ContinuationPoint.IsNull || result.ContinuationPoint.Length == 0)
                    {
                        openPoint = ByteString.Empty;
                        yield break;
                    }

                    continuationPoint = result.ContinuationPoint;
                }
            }
            finally
            {
                if (!openPoint.IsNull && openPoint.Length > 0)
                {
                    await SampleHistory.ReleaseContinuationPointAsync(m_session, nodeId, details, openPoint).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Writes the values on display back into the history of the node.
        /// </summary>
        /// <remarks>
        /// Insert, replace and update go through the history client of the SDK, which
        /// builds the details of the request and unpacks the status the archive
        /// answered with for each value. Remove is not part of that client - it has
        /// the two deletes of Part 11 instead - so it keeps the service call.
        ///
        /// The rows on display are annotations rather than values when the annotations
        /// property of the variable is the one being read, and an annotation is
        /// written one at a time through the variable it belongs to.
        /// </remarks>
        private async Task InsertReplaceAsync(PerformUpdateType updateType, CancellationToken ct = default)
        {
            List<DataValue> values = new List<DataValue>();

            foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
            {
                DataValue value = (DataValue)row.Row[9];
                values.Add(value);
            }

            HistoricalProperty property = PropertyCB.SelectedItem as HistoricalProperty;

            if (property != null && property.BrowseName == Opc.Ua.BrowseNames.Annotations)
            {
                ShowOperationResults(await WriteAnnotationsAsync(values, updateType, ct));
                return;
            }

            NodeId nodeId = GetSelectedNode();

            IList<StatusCode> results = updateType switch {
                PerformUpdateType.Insert => await m_historian.InsertAsync(nodeId, values, ct),
                PerformUpdateType.Replace => await m_historian.ReplaceAsync(nodeId, values, ct),
                PerformUpdateType.Update => await m_historian.UpdateAsync(nodeId, values, ct),
                _ => await SampleHistory.RemoveAsync(m_session, nodeId, values, ct),
            };

            ShowOperationResults(results);
        }

        /// <summary>
        /// Writes the annotations on display back to the variable they belong to.
        /// </summary>
        private async Task<IList<StatusCode>> WriteAnnotationsAsync(
            IList<DataValue> values,
            PerformUpdateType updateType,
            CancellationToken ct = default)
        {
            List<StatusCode> results = new List<StatusCode>(values.Count);

            foreach (DataValue value in values)
            {
                if (!value.WrappedValue.TryGetValue(out ExtensionObject extension) ||
                    !extension.TryGetValue(out Annotation annotation))
                {
                    results.Add(StatusCodes.BadTypeMismatch);
                    continue;
                }

                results.Add(await m_historian.WriteAnnotationAsync(
                    m_nodeId,
                    (DateTime)annotation.AnnotationTime,
                    annotation.Message,
                    annotation.UserName,
                    updateType,
                    ct));
            }

            return results;
        }

        /// <summary>
        /// Shows what the archive answered for each of the values on display.
        /// </summary>
        private void ShowOperationResults(IList<StatusCode> results)
        {
            ResultsDV.Columns[ResultsDV.Columns.Count - 1].Visible = true;

            for (int ii = 0; ii < m_dataset.Tables[0].DefaultView.Count && ii < results.Count; ii++)
            {
                m_dataset.Tables[0].DefaultView[ii].Row[10] = results[ii];
            }

            m_dataset.AcceptChanges();
        }

        /// <summary>
        /// Deletes the block of data.
        /// </summary>
        private async Task DeleteRawAsync(bool isModified, CancellationToken ct = default)
        {
            StatusCode result = await m_historian.DeleteRawAsync(
                m_nodeId,
                StartTimeDP.Value.ToUniversalTime(),
                EndTimeDP.Value.ToUniversalTime(),
                isModified,
                ct);

            if (StatusCode.IsBad(result))
            {
                throw new ServiceResultException(result);
            }

            ResultsDV.Columns[ResultsDV.Columns.Count - 1].Visible = false;
            m_dataset.Clear();
        }

        /// <summary>
        /// Deletes the history.
        /// </summary>
        /// <remarks>
        /// The row of a value on display keeps the value it was built from, which is
        /// what carries the source timestamp the archive keys it by; the column of
        /// the grid holds the local rendering of it and would not name the same
        /// instant back to the server.
        /// </remarks>
        private async Task DeleteAtTimeAsync(CancellationToken ct = default)
        {
            List<DateTime> times = new List<DateTime>();

            foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
            {
                times.Add((DateTime)((DataValue)row.Row[9]).SourceTimestamp);
            }

            IList<StatusCode> results = await m_historian.DeleteAtTimeAsync(m_nodeId, times, ct);

            ShowOperationResults(results);
        }

        private async void NodeIdBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                ReferenceDescription reference = await Windows.Create<SelectNodeDlg>().ShowDialogAsync(
                    m_session,
                    Opc.Ua.ObjectIds.ObjectsFolder,
                    null,
                    "Select Variable",
                    m_telemetry,
                    default,
                    Opc.Ua.ReferenceTypeIds.Organizes,
                    Opc.Ua.ReferenceTypeIds.Aggregates);

                if (reference == null)
                {
                    return;
                }

                if (reference.NodeId != m_nodeId)
                {
                    await ChangeNodeAsync((NodeId)reference.NodeId);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void SubscribeCK_CheckedChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session != null)
                {
                    if (m_isSubscribed)
                    {
                        await CreateSubscriptionAsync();
                    }
                    else
                    {
                        await DeleteSubscriptionAsync();
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void GoBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // the rows are not cleared here: a read starts by clearing them, and
                // the operations which write history take the values to write from
                // exactly the rows a read left behind.
                switch ((HistoryReadType)ReadTypeCB.SelectedItem)
                {
                    case HistoryReadType.Subscribe:
                    {
                        await CreateSubscriptionAsync();
                        break;
                    }

                    case HistoryReadType.Raw:
                    {
                        await ReadRawAsync(false);
                        break;
                    }

                    case HistoryReadType.Modified:
                    {
                        await ReadRawAsync(true);
                        break;
                    }

                    case HistoryReadType.Processed:
                    {
                        await ReadProcessedAsync();
                        break;
                    }

                    case HistoryReadType.AtTime:
                    {
                        await ReadAtTimeAsync();
                        break;
                    }

                    case HistoryReadType.Insert:
                    {
                        await InsertReplaceAsync(PerformUpdateType.Insert);
                        break;
                    }

                    case HistoryReadType.Replace:
                    {
                        await InsertReplaceAsync(PerformUpdateType.Replace);
                        break;
                    }

                    case HistoryReadType.InsertReplace:
                    {
                        await InsertReplaceAsync(PerformUpdateType.Update);
                        break;
                    }

                    case HistoryReadType.Remove:
                    {
                        await InsertReplaceAsync(PerformUpdateType.Remove);
                        break;
                    }

                    case HistoryReadType.DeleteRaw:
                    {
                        await DeleteRawAsync(false);
                        break;
                    }

                    case HistoryReadType.DeleteModified:
                    {
                        await DeleteRawAsync(true);
                        break;
                    }

                    case HistoryReadType.DeleteAtTime:
                    {
                        await DeleteAtTimeAsync();
                        break;
                    }
                }

            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void NextBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ReadNextAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void StopBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // abandoning the read is what releases the continuation point the
                // server is holding open for it.
                await ReleaseReaderAsync();
                await DeleteSubscriptionAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void ReadTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                HistoryReadType readType = (HistoryReadType)ReadTypeCB.SelectedItem;

                switch (readType)
                {
                    case HistoryReadType.Subscribe:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = true;
                        SamplingIntervalNP.Visible = true;
                        SamplingIntervalUnitsLB.Visible = true;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = false;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = true;
                        AggregateCB.Visible = true;
                        ResampleIntervalLB.Visible = true;
                        ProcessingIntervalNP.Visible = true;
                        ResampleIntervalUnitsLB.Visible = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Raw:
                    {
                        PropertyLB.Visible = (m_properties != null && m_properties.Count > 1);
                        PropertyCB.Visible = (m_properties != null && m_properties.Count > 1);
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = true;
                        MaxReturnValuesCK.Checked = true;
                        ReturnBoundsLB.Visible = true;
                        ReturnBoundsCK.Visible = true;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Modified:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = true;
                        MaxReturnValuesCK.Checked = true;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Processed:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = true;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = true;
                        AggregateCB.Visible = true;
                        ResampleIntervalLB.Visible = true;
                        ProcessingIntervalNP.Visible = true;
                        ResampleIntervalUnitsLB.Visible = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.AtTime:
                    {
                        PropertyLB.Visible = (m_properties != null && m_properties.Count > 1);
                        PropertyCB.Visible = (m_properties != null && m_properties.Count > 1);
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = false;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = true;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = true;
                        TimeStepNP.Visible = true;
                        TimeStepUnitsLB.Visible = true;
                        UseSimpleBoundsLB.Visible = true;
                        UseSimpleBoundsCK.Visible = true;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Insert:
                    case HistoryReadType.InsertReplace:
                    case HistoryReadType.Replace:
                    case HistoryReadType.Remove:
                    {
                        PropertyLB.Visible = (m_properties != null && m_properties.Count > 1);
                        PropertyCB.Visible = (m_properties != null && m_properties.Count > 1);
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = false;
                        StartTimeDP.Visible = false;
                        StartTimeCK.Visible = false;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = false;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = false;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = true;
                        TimeStepNP.Visible = true;
                        TimeStepUnitsLB.Visible = true;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = true;
                        break;
                    }

                    case HistoryReadType.DeleteAtTime:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = false;
                        StartTimeDP.Visible = false;
                        StartTimeCK.Visible = false;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = false;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = false;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.DeleteRaw:
                    case HistoryReadType.DeleteModified:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = true;
                        EndTimeCK.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion

        #region Event Handlers
        private void StartTimeDP_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                m_timesChanged = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void DetectLimitsBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                DateTime startTime = await SampleHistory.ReadFirstDateAsync(m_session, GetSelectedNode(), m_configuration);

                if (startTime != DateTime.MinValue)
                {
                    StartTimeDP.Value = startTime;
                }

                DateTime endTime = await SampleHistory.ReadLastDateAsync(m_session, GetSelectedNode());

                if (endTime != DateTime.MinValue)
                {
                    EndTimeDP.Value = endTime;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void StartTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                StartTimeDP.Enabled = StartTimeCK.Checked;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void EndTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                EndTimeDP.Enabled = EndTimeCK.Checked;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void MaxReturnValuesCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                MaxReturnValuesNP.Enabled = MaxReturnValuesCK.Checked;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void TimeShiftBTN_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
                {
                    DataValue value = (DataValue)row.Row[9];
                    DateTime sourceTs = ((DateTime)value.SourceTimestamp).AddMilliseconds((double)TimeStepNP.Value);
                    DateTime serverTs = ((DateTime)value.ServerTimestamp).AddMilliseconds((double)TimeStepNP.Value);
                    value = new DataValue(value.WrappedValue, value.StatusCode, sourceTs, serverTs);
                    row.Row[9] = value;

                    row[1] = sourceTs.ToLocalTime().ToString("HH:mm:ss.fff");
                    row[2] = serverTs.ToLocalTime().ToString("HH:mm:ss.fff");
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Annotates every selected value with the same note.
        /// </summary>
        /// <remarks>
        /// An annotation is addressed through the variable rather than through its
        /// Annotations property: the history client translates the one to the other,
        /// which is also what the server does with the node id of an annotation
        /// request, so a client never has to find that property for itself.
        /// </remarks>
        private async void InsertAnnotationMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || ResultsDV.SelectedRows.Count == 0)
                {
                    return;
                }

                Annotation annotation = Windows.Create<EditAnnotationDlg>().ShowDialog(m_session, null, null);

                if (annotation == null)
                {
                    return;
                }

                ResultsDV.Columns[ResultsDV.Columns.Count - 1].Visible = true;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    DataValue value = (DataValue)source.Row[9];

                    // the annotation belongs to the instant the value was recorded at.
                    source.Row[10] = await m_historian.WriteAnnotationAsync(
                        m_nodeId,
                        (DateTime)value.SourceTimestamp,
                        annotation.Message,
                        annotation.UserName);
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void EditValueMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    DataValue value = (DataValue)source.Row[9];

                    Variant newValue = Windows.Create<EditDataValueDlg>().ShowDialog(value.WrappedValue, null);

                    if (newValue.IsNull)
                    {
                        return;
                    }

                    UpdateRow(source.Row, new DataValue(newValue), null);
                    m_dataset.AcceptChanges();
                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void ShowServerTimestampMI_CheckedChanged(object sender, EventArgs e)
        {
            ServerTimestampCH.Visible = ShowServerTimestampMI.Checked;
        }
        #endregion
    }
}
