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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// A control which displays a list of events.
    /// </summary>
    public partial class EventListView : UserControl
    {
        /// <summary>
        /// How long the control waits for the subscription engine to apply a monitored item change.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Initializes the object.
        /// </summary>
        public EventListView()
        {
            InitializeComponent();
            m_callbacks.EventCallback = OnEvents;
        }

        #region Private Methods
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        #pragma warning disable CA2213 // Justification: the subscription is deleted in DeleteSubscriptionAsync, which the owner drives.
        private ISubscription m_subscription;
        #pragma warning restore CA2213
        private IMonitoredItem m_monitoredItem;
        private OptionsMonitor<MonitoredItemOptions> m_monitoredItemOptions;
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();
        private int m_nextItemId;
        private FilterDeclaration m_filter;
        private NodeId m_areaId;
        private bool m_isSubscribed;
        private bool m_displayConditions;
        #endregion

        #region Public Members
        /// <summary>
        /// Whether the control subscribes for new events.
        /// </summary>
        public bool IsSubscribed => m_isSubscribed;

        public async Task SetSubscribedAsync(bool subscribed, CancellationToken ct = default)
        {
            if (m_isSubscribed != subscribed)
            {
                m_isSubscribed = subscribed;

                if (m_session != null)
                {
                    if (m_isSubscribed)
                    {
                        await CreateSubscriptionAsync(ct);
                    }
                    else
                    {
                        await DeleteSubscriptionAsync(ct);
                    }
                }
            }
        }

        /// <summary>
        /// Whether to display the events as conditions.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DisplayConditions
        {
            get { return m_displayConditions; }
            set { m_displayConditions = value; }
        }

        /// <summary>
        /// The context menu to use.
        /// </summary>
        public override ContextMenuStrip ContextMenuStrip
        {
            get { return this.EventsLV.ContextMenuStrip; }
            set { this.EventsLV.ContextMenuStrip = value; }
        }

        /// <summary>
        /// The event area displayed in the control.
        /// </summary>
        public NodeId AreaId
        {
            get { return m_areaId; }
        }

        /// <summary>
        /// The event filter applied to the control.
        /// </summary>
        public FilterDeclaration Filter
        {
            get { return m_filter; }
        }

        /// <summary>
        /// Changes the session.
        /// </summary>
        public async Task ChangeSessionAsync(ISession session, bool fetchRecent, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            if (m_session != null)
            {
                await DeleteSubscriptionAsync(ct);
                m_session = null;
            }

            m_session = session;
            m_telemetry = telemetry;
            EventsLV.Items.Clear();

            if (m_session != null && m_isSubscribed)
            {
                await CreateSubscriptionAsync(ct);

                if (fetchRecent)
                {
                    await ReadRecentHistoryAsync(ct);
                }
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
        /// Changes the area monitored by the control.
        /// </summary>
        public async Task ChangeAreaAsync(NodeId areaId, bool fetchRecent, CancellationToken ct = default)
        {
            m_areaId = areaId;
            EventsLV.Items.Clear();

            if (fetchRecent)
            {
                await ReadRecentHistoryAsync(ct);
            }

            if (m_subscription != null)
            {
                // the node a monitored item watches cannot be modified, so the item is
                // replaced by one for the new area.
                MonitoredItemOptions options = m_monitoredItemOptions.CurrentValue with { StartNodeId = areaId };

                m_subscription.MonitoredItems.TryRemove(m_monitoredItem.ClientHandle);
                AddMonitoredItem(options);

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout, ct);
            }
        }

        /// <summary>
        /// Changes the filter used to select the events.
        /// </summary>
        public async Task ChangeFilterAsync(FilterDeclaration filter, bool fetchRecent, CancellationToken ct = default)
        {
            m_filter = filter;
            EventsLV.Items.Clear();

            int index = 0;

            if (m_filter != null)
            {
                // add or update existing columns.
                for (int ii = 0; ii < m_filter.Fields.Count; ii++)
                {
                    if (m_filter.Fields[ii].DisplayInList)
                    {
                        if (index >= EventsLV.Columns.Count)
                        {
                            EventsLV.Columns.Add(new ColumnHeader());
                        }

                        EventsLV.Columns[index].Text = m_filter.Fields[ii].InstanceDeclaration.DisplayName;
                        EventsLV.Columns[index].TextAlign = HorizontalAlignment.Left;
                        index++;
                    }
                }
            }

            // remove extra columns.
            while (index < EventsLV.Columns.Count)
            {
                EventsLV.Columns.RemoveAt(EventsLV.Columns.Count - 1);
            }

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }

            // fetch recent history.
            if (fetchRecent)
            {
                await ReadRecentHistoryAsync(ct);
            }

            // update subscription.
            if (m_monitoredItemOptions != null && m_filter != null)
            {
                EventFilter eventFilter = m_filter.GetFilter();
                m_monitoredItemOptions.Configure(options => options with { Filter = eventFilter });
                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout, ct);
            }
        }

        /// <summary>
        /// Clears the event history in the control.
        /// </summary>
        public void ClearEventHistory()
        {
            EventsLV.Items.Clear();

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Adds the event history to the control.
        /// </summary>
        public async Task AddEventHistoryAsync(HistoryEvent events, CancellationToken ct = default)
        {
            for (int ii = 0; ii < events.Events.Count; ii++)
            {
                ListViewItem item = await CreateListItemAsync(m_filter, new List<Variant>(events.Events[ii].EventFields.ToArray()), ct);
                EventsLV.Items.Add(item);
            }

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Refreshes the conditions displayed.
        /// </summary>
        public async Task ConditionRefreshAsync(CancellationToken ct = default)
        {
            if (m_subscription != null)
            {
                await m_subscription.ConditionRefreshAsync(ct);
            }
        }

        /// <summary>
        /// Returns the currently selected event at the specified index (null index is not valid).
        /// </summary>
        public IList<Variant> GetSelectedEvent(int index)
        {
            if (EventsLV.SelectedItems.Count > index)
            {
                return EventsLV.SelectedItems[index].Tag as List<Variant>;
            }

            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private async Task CreateSubscriptionAsync(CancellationToken ct = default)
        {
            m_subscription = ClientUtils.AddSubscription(
                m_session,
                m_callbacks,
                new OptionsMonitor<Opc.Ua.Client.Subscriptions.SubscriptionOptions>(ClientUtils.DefaultSubscriptionOptions));

            // builds the columns for the current filter. There is no item to reconfigure yet,
            // so the filter has to go into the options the item is created with below.
            await ChangeFilterAsync(m_filter, false, ct);

            AddMonitoredItem(new MonitoredItemOptions {
                StartNodeId = m_areaId,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
                TimestampsToReturn = TimestampsToReturn.Both,
                Filter = m_filter?.GetFilter(),
            });
        }

        /// <summary>
        /// Adds the monitored item which watches the current area to the subscription.
        /// </summary>
        private void AddMonitoredItem(MonitoredItemOptions options)
        {
            m_monitoredItemOptions = new OptionsMonitor<MonitoredItemOptions>(options);

            // the name has to be unique within the subscription, and an item which was just
            // removed may not have been reaped yet, so every item gets its own name.
            m_subscription.MonitoredItems.TryAdd(
                Utils.Format("Events{0}", ++m_nextItemId),
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
        }

        /// <summary>
        /// Creates list item for an event.
        /// </summary>
        private async Task<ListViewItem> CreateListItemAsync(FilterDeclaration filter, List<Variant> fieldValues, CancellationToken ct = default)
        {
            ListViewItem item = null;

            if (m_displayConditions)
            {
                NodeId conditionId = NodeId.Null;

                if (fieldValues[0].AsBoxedObject() is NodeId nodeId)
                {
                    conditionId = nodeId;
                }

                if (!conditionId.IsNull)
                {
                    for (int ii = 0; ii < EventsLV.Items.Count; ii++)
                    {
                        List<Variant> fields = EventsLV.Items[ii].Tag as List<Variant>;

                        if (fields != null && Utils.IsEqual(conditionId, fields[0].AsBoxedObject()))
                        {
                            item = EventsLV.Items[ii];
                            break;
                        }
                    }
                }
            }

            if (item == null)
            {
                item = new ListViewItem();
            }

            item.Tag = fieldValues;
            int position = -1;

            for (int ii = 1; ii < filter.Fields.Count; ii++)
            {
                if (!filter.Fields[ii].DisplayInList)
                {
                    continue;
                }

                position++;

                string text = null;
                Variant value = fieldValues[ii + 1];

                // check for missing fields.
                if (value.AsBoxedObject() == null)
                {
                    text = String.Empty;
                }

                // display the name of a node instead of the node id.
                else if (value.TypeInfo.BuiltInType == BuiltInType.NodeId)
                {
                    INode node = await m_session.NodeCache.FindAsync((NodeId)value.AsBoxedObject(), ct);

                    if (node != null)
                    {
                        text = node.ToString();
                    }
                }

                // display local time for any time fields.
                else if (value.TypeInfo.BuiltInType == BuiltInType.DateTime)
                {
                    DateTime datetime = (DateTime)value.AsBoxedObject();

                    if (m_filter.Fields[ii].InstanceDeclaration.DisplayName.Contains("Time", StringComparison.Ordinal))
                    {
                        text = datetime.ToLocalTime().ToString("HH:mm:ss.fff");
                    }
                    else
                    {
                        text = datetime.ToLocalTime().ToString("yyyy-MM-dd");
                    }
                }

                // use default string format.
                else
                {
                    text = value.ToString();
                }

                // update subitem text.
                if (string.IsNullOrEmpty(item.Text))
                {
                    item.Text = text;
                    item.SubItems[0].Text = text;
                }
                else
                {
                    if (item.SubItems.Count <= position)
                    {
                        item.SubItems.Add(text);
                    }
                    else
                    {
                        item.SubItems[position].Text = text;
                    }
                }
            }

            return item;
        }

        /// <summary>
        /// Updates the display with a new value for a monitored variable.
        /// </summary>
        private async void OnEvents(ISubscription subscription, uint sequenceNumber, DateTime publishTime, EventNotification[] notifications, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, uint, DateTime, EventNotification[], PublishState>(OnEvents), subscription, sequenceNumber, publishTime, notifications, publishStateMask);
                return;
            }

            try
            {
                foreach (EventNotification notification in notifications)
                {
                    // check if monitored item has changed.
                    if (!Object.ReferenceEquals(m_monitoredItem, notification.MonitoredItem))
                    {
                        continue;
                    }

                    // check if the filter has changed.
                    if (notification.Fields.Count != m_filter.Fields.Count + 1)
                    {
                        continue;
                    }

                    var fields = new List<Variant>(notification.Fields.ToArray());

                    if (m_displayConditions)
                    {
                        NodeId eventTypeId = m_filter.GetValue<NodeId>(new QualifiedName(Opc.Ua.BrowseNames.EventType), fields, NodeId.Null);

                        if (eventTypeId == Opc.Ua.ObjectTypeIds.RefreshStartEventType)
                        {
                            EventsLV.Items.Clear();
                        }

                        if (eventTypeId == Opc.Ua.ObjectTypeIds.RefreshEndEventType)
                        {
                            continue;
                        }
                    }

                    // create an item and add to top of list.
                    ListViewItem item = await CreateListItemAsync(m_filter, fields);

                    if (item.ListView == null)
                    {
                        EventsLV.Items.Insert(0, item);
                    }
                }

                // adjust the width of the columns.
                for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
                {
                    EventsLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private async Task ReadRecentHistoryAsync(CancellationToken ct = default)
        {
            // check if session is active.
            if (m_session != null)
            {
                // check if area supports history.
                IObject area = await m_session.NodeCache.FindAsync(m_areaId, ct) as IObject;

                if (area != null && ((area.EventNotifier & EventNotifiers.HistoryRead) != 0))
                {
                    // get the last hour or 10 events.
                    ReadEventDetails details = new ReadEventDetails();
                    details.StartTime = DateTime.UtcNow.AddSeconds(30);
                    details.EndTime = ((DateTime)details.StartTime).AddHours(-1);
                    details.NumValuesPerNode = 10;
                    details.Filter = m_filter.GetFilter();

                    // read the history.
                    await ReadHistoryAsync(details, m_areaId, ct);
                }
            }
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private async Task ReadHistoryAsync(ReadEventDetails details, NodeId areaId, CancellationToken ct = default)
        {
            List<HistoryReadValueId> nodesToRead = new List<HistoryReadValueId>();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = areaId;
            nodesToRead.Add(nodeToRead);

            HistoryReadResponse response = await m_session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Neither,
                false,
                nodesToRead,
                ct);

            List<HistoryReadResult> results = new List<HistoryReadResult>(response.Results.ToArray());
            List<DiagnosticInfo> diagnosticInfos = new List<DiagnosticInfo>(response.DiagnosticInfos.ToArray());

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            HistoryEvent events = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;
            await AddEventHistoryAsync(events, ct);

            // release continuation points.
            if (!results[0].ContinuationPoint.IsNull && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                response = await m_session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Neither,
                    true,
                    nodesToRead,
                    ct);

                results = new List<HistoryReadResult>(response.Results.ToArray());
                diagnosticInfos = new List<DiagnosticInfo>(response.DiagnosticInfos.ToArray());
            }
        }

        /// <summary>
        /// Deletes the recent history.
        /// </summary>
        private async Task DeleteHistoryAsync(NodeId areaId, List<List<Variant>> events, FilterDeclaration filter, CancellationToken ct = default)
        {
            // find the event id.
            int index = 0;

            foreach (FilterDeclarationField field in filter.Fields)
            {
                if (field.InstanceDeclaration.BrowseName == Opc.Ua.BrowseNames.EventId)
                {
                    break;
                }

                index++;
            }

            // can't delete events if no event id.
            if (index >= filter.Fields.Count)
            {
                throw ServiceResultException.Create(StatusCodes.BadEventIdUnknown, "Cannot delete events if EventId was not selected.");
            }

            // build list of nodes to delete.
            DeleteEventDetails details = new DeleteEventDetails();
            details.NodeId = areaId;

            foreach (List<Variant> e in events)
            {
                ByteString eventId = default;

                if (e.Count > index)
                {
                    if (e[index].AsBoxedObject() is ByteString byteString)
                    {
                        eventId = byteString;
                    }
                    else if (e[index].AsBoxedObject() is byte[] bytes)
                    {
                        eventId = bytes.ToByteString();
                    }
                }

                details.EventIds = details.EventIds.AddItem(eventId);
            }

            // delete the events.
            List<ExtensionObject> nodesToUpdate = new List<ExtensionObject>();
            nodesToUpdate.Add(new ExtensionObject(details));

            HistoryUpdateResponse response = await m_session.HistoryUpdateAsync(
                null,
                nodesToUpdate,
                ct);

            List<HistoryUpdateResult> results = new List<HistoryUpdateResult>(response.Results.ToArray());
            List<DiagnosticInfo> diagnosticInfos = new List<DiagnosticInfo>(response.DiagnosticInfos.ToArray());

            ClientBase.ValidateResponse(results, nodesToUpdate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToUpdate);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            // check for item level errors.
            if (results[0].OperationResults.Count > 0)
            {
                int count = 0;

                for (int ii = 0; ii < results[0].OperationResults.Count; ii++)
                {
                    if (StatusCode.IsBad(results[0].OperationResults[ii]))
                    {
                        count++;
                    }
                }

                // raise an error.
                if (count > 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEventIdUnknown,
                        "Error deleting events. Only {0} of {1} deletes succeeded.",
                        events.Count - count,
                        events.Count);
                }
            }
        }

        private void ViewDetailsMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                List<Variant> fields = EventsLV.SelectedItems[0].Tag as List<Variant>;

                if (fields != null)
                {
                    // new ViewEventDetailsDlg().ShowDialog(m_filter, fields);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void DeleteHistoryMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                List<List<Variant>> events = new List<List<Variant>>();

                foreach (ListViewItem item in EventsLV.SelectedItems)
                {
                    List<Variant> fields = item.Tag as List<Variant>;

                    if (fields != null)
                    {
                        events.Add(fields);
                    }
                }

                if (events.Count > 0)
                {
                    await DeleteHistoryAsync(m_areaId, events, m_filter);

                    foreach (ListViewItem item in EventsLV.SelectedItems)
                    {
                        List<Variant> fields = item.Tag as List<Variant>;

                        if (fields != null)
                        {
                            item.Font = new Font(item.Font, FontStyle.Strikeout);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
