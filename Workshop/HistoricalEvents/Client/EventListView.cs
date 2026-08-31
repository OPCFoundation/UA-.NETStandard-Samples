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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Quickstarts.HistoricalEvents.Client
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Shows the events of one area, from history and - while subscribed - live.
    /// </summary>
    /// <remarks>
    /// The live half uses the streaming API of the V2 subscription engine:
    /// <see cref="IStreamingSubscription"/> hands the notifications out as an
    /// <see cref="IAsyncEnumerable{T}"/>, creates the monitored item when the enumeration
    /// starts and removes it again when it ends. That suits this control, because neither the
    /// node an item monitors nor its event filter can be modified afterwards - picking another
    /// area or another filter simply restarts the enumeration.
    /// </remarks>
    public partial class EventListView : UserControl
    {
        public EventListView()
        {
            InitializeComponent();
        }

        #region Private Methods
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        #pragma warning disable CA2213 // Justification: disposed asynchronously by DeleteSubscriptionAsync.
        private StreamingSubscription m_streaming;
        #pragma warning restore CA2213
        #pragma warning disable CA2213 // Justification: disposed by StopPumpAsync.
        private CancellationTokenSource m_cts;
        #pragma warning restore CA2213
        private EventFilter m_eventFilter;
        private FilterDeclaration m_filter;
        private NodeId m_areaId;
        private bool m_isSubscribed;
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
            m_telemetry = telemetry;

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
        /// The streaming subscription belongs to the subscription manager of the session and
        /// survives the reconnect together with its monitored item, so the enumeration keeps
        /// running and there is nothing to re-create here.
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

            // the node an item monitors cannot be changed, so the enumeration is restarted:
            // that removes the old monitored item and creates one for the new area.
            await RestartPumpAsync();
        }

        /// <summary>
        /// Changes the filter used to select the events.
        /// </summary>
        public async Task ChangeFilterAsync(FilterDeclaration filter, bool fetchRecent, CancellationToken ct = default)
        {
            m_filter = filter;
            EventsLV.Items.Clear();

            int index = 0;

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

            // the event filter of an item cannot be changed, so the enumeration is restarted:
            // that removes the old monitored item and creates one with the new filter.
            await RestartPumpAsync();
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
                ListViewItem item = await CreateListItemAsync(m_filter, events.Events[ii].EventFields.ToList(), ct);
                EventsLV.Items.Add(item);
            }

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private async Task CreateSubscriptionAsync(CancellationToken ct = default)
        {
            await DeleteSubscriptionAsync(ct);

            // the underlying OPC UA subscription is created when the first enumeration starts.
            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, ClientUtils.DefaultSubscriptionOptions);

            // rebuilding the columns from the filter is what also starts the enumeration.
            await ChangeFilterAsync(m_filter, false, ct);
        }

        /// <summary>
        /// Stops the stream and deletes the subscription on the server.
        /// </summary>
        private async Task DeleteSubscriptionAsync(CancellationToken ct = default)
        {
            StreamingSubscription streaming = m_streaming;

            m_streaming = null;

            await StopPumpAsync();

            if (streaming == null)
            {
                return;
            }

            try
            {
                await streaming.DisposeAsync();
            }
            catch (Exception exception)
            {
                // this also runs when the session has already gone away, and then the
                // subscription cannot be deleted on the server any more.
                m_telemetry?.CreateLogger<EventListView>()
                    .LogError(exception, "Failed to delete the event subscription.");
            }
        }

        /// <summary>
        /// Ends the current enumeration, which removes the monitored item it created.
        /// </summary>
        private async Task StopPumpAsync()
        {
            CancellationTokenSource cts = m_cts;

            m_cts = null;

            if (cts != null)
            {
                await cts.CancelAsync();
                cts.Dispose();
            }
        }

        /// <summary>
        /// Restarts the enumeration for the current area and filter.
        /// </summary>
        private async Task RestartPumpAsync()
        {
            await StopPumpAsync();

            if (m_streaming == null || m_filter == null || m_areaId.IsNull)
            {
                return;
            }

            // the fields of a notification line up with the select clauses of this filter, so
            // the control keeps it: the engine does not report the filter of an item back.
            m_eventFilter = m_filter.GetFilter();
            m_cts = new CancellationTokenSource();

            // nothing is awaited here on purpose: the enumeration runs until the area, the
            // filter or the session changes.
            _ = PumpEventsAsync(m_areaId, m_eventFilter, m_cts.Token);
        }

        /// <summary>
        /// Reads the events of one area off the streaming subscription.
        /// </summary>
        private async Task PumpEventsAsync(NodeId areaId, EventFilter filter, CancellationToken ct)
        {
            IStreamingSubscription streaming = m_streaming;

            var options = new MonitoredItemOptions {
                StartNodeId = areaId,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(areaId, filter, options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    // without a window there is nothing to update, and the enumeration keeps
                    // running rather than ending for good.
                    if (!IsHandleCreated)
                    {
                        continue;
                    }

                    // the enumeration runs on a publish worker, so the display is updated on
                    // the UI thread.
                    BeginInvoke(new Action<EventNotification>(DisplayEventAsync), notification);
                }
            }
            catch (OperationCanceledException)
            {
                // the area, the filter or the session changed.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown.
                m_telemetry?.CreateLogger<EventListView>().LogError(exception, "Failed to read the events.");
            }
        }

        /// <summary>
        /// Creates list item for an event.
        /// </summary>
        private async Task<ListViewItem> CreateListItemAsync(FilterDeclaration filter, List<Variant> fieldValues, CancellationToken ct = default)
        {
            ListViewItem item = new ListViewItem();
            item.Tag = fieldValues;

            for (int ii = 1; ii < fieldValues.Count; ii++)
            {
                if (!filter.Fields[ii - 1].DisplayInList)
                {
                    continue;
                }

                string text = null;

                // check for missing fields.
                if (fieldValues[ii].IsNull)
                {
                    text = String.Empty;
                }

                // display the name of a node instead of the node id.
                else if (fieldValues[ii].TryGetValue(out NodeId fieldNodeId))
                {
                    INode node = await m_session.NodeCache.FindAsync(fieldNodeId, ct);

                    if (node != null)
                    {
                        text = node.ToString();
                    }
                }

                // display local time for any time fields.
                else if (fieldValues[ii].TryGetValue(out DateTimeUtc fieldTime))
                {
                    DateTime value = fieldTime.ToLocalTime();

                    if (m_filter.Fields[ii - 1].InstanceDeclaration.DisplayName.Contains("Time", StringComparison.Ordinal))
                    {
                        text = value.ToString("HH:mm:ss.fff");
                    }
                    else
                    {
                        text = value.ToString("yyyy-MM-dd");
                    }
                }

                // use default string format.
                else
                {
                    text = fieldValues[ii].ToString();
                }

                // update subitem text.
                if (string.IsNullOrEmpty(item.Text))
                {
                    item.Text = text;
                    item.SubItems[0].Text = text;
                }
                else
                {
                    item.SubItems.Add(text);
                }
            }

            return item;
        }

        /// <summary>
        /// Updates the display with an event read off the stream.
        /// </summary>
        private async void DisplayEventAsync(EventNotification eventNotification)
        {
            try
            {
                // check if the filter has changed while this event was on its way.
                if (eventNotification.Fields.Count != m_filter.Fields.Count + 1)
                {
                    return;
                }

                // create an item and add to top of list.
                ListViewItem item = await CreateListItemAsync(m_filter, eventNotification.Fields.ToList());
                EventsLV.Items.Insert(0, item);

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
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                ct);

            var results = response.Results.ToList();
            var diagnosticInfos = response.DiagnosticInfos.ToList();

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
                    TimestampsToReturn.Source,
                    true,
                    nodesToRead,
                    ct);

                results = response.Results.ToList();
                diagnosticInfos = response.DiagnosticInfos.ToList();
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
                    e[index].TryGetValue(out eventId);
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

            var results = response.Results.ToList();
            var diagnosticInfos = response.DiagnosticInfos.ToList();

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
                    using ViewEventDetailsDlg dialog = new ViewEventDetailsDlg();
                    dialog.ShowDialog(m_filter, fields);
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
