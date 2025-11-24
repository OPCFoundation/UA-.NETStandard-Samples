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

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class SubscribeDataListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public SubscribeDataListViewCtrl()
        {
            InitializeComponent();
            m_PublishStatusChanged = new PublishStateChangedEventHandler(OnPublishStatusChanged);
            ResultsDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Requests");

            m_dataset.Tables[0].Columns.Add("MonitoredItem", typeof(MonitoredItem));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("NodeAttribute", typeof(string));
            m_dataset.Tables[0].Columns.Add("IndexRange", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataEncoding", typeof(QualifiedName));
            m_dataset.Tables[0].Columns.Add("MonitoringMode", typeof(MonitoringMode));
            m_dataset.Tables[0].Columns.Add("SamplingInterval", typeof(double));
            m_dataset.Tables[0].Columns.Add("QueueSize", typeof(uint));
            m_dataset.Tables[0].Columns.Add("DiscardOldest", typeof(bool));
            m_dataset.Tables[0].Columns.Add("Filter", typeof(MonitoringFilter));
            m_dataset.Tables[0].Columns.Add("OperationStatus", typeof(StatusCode));
            m_dataset.Tables[0].Columns.Add("DataValue", typeof(DataValue));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("StatusCode", typeof(StatusCode));
            m_dataset.Tables[0].Columns.Add("SourceTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerTimestamp", typeof(string));

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private Subscription m_subscription;
        private DisplayState m_state;
        private EditComplexValueDlg m_EditComplexValueDlg;
        private PublishStateChangedEventHandler m_PublishStatusChanged;
        #endregion

        #region Stage Enum
        /// <summary>
        /// The diplays state.
        /// </summary>
        private enum DisplayState
        {
            EditItems,
            ApplyChanges,
            ViewUpdates
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session used.
        /// </summary>
        public void ChangeSession(ISession session, ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            if (!Object.ReferenceEquals(session, m_session))
            {
                m_session = session;

                if (m_subscription != null)
                {
                    m_subscription.PublishStatusChanged -= m_PublishStatusChanged;
                    m_subscription.FastDataChangeCallback = null;
                    m_subscription = null;
                }

                if (m_session != null)
                {
                    // find new subscription.
                    foreach (Subscription subscription in m_session.Subscriptions)
                    {
                        if (Object.ReferenceEquals(subscription.Handle, this))
                        {
                            m_subscription = subscription;
                            m_subscription.PublishStatusChanged += m_PublishStatusChanged;
                            m_subscription.FastDataChangeCallback = OnDataChangeAsync;
                            break;
                        }
                    }

                    // update references to monitored items.
                    if (m_subscription != null)
                    {
                        foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                        {
                            DataRow row = (DataRow)monitoredItem.Handle;
                            row[0] = monitoredItem;

                            if (m_EditComplexValueDlg != null)
                            {
                                MonitoredItem oldMonitoredItem = (MonitoredItem)m_EditComplexValueDlg.Tag;

                                if (Object.ReferenceEquals(oldMonitoredItem.Handle, monitoredItem.Handle))
                                {
                                    m_EditComplexValueDlg.Tag = monitoredItem;
                                }
                            }
                        }
                    }
                }

                if (m_EditComplexValueDlg != null)
                {
                    m_EditComplexValueDlg.ChangeSession(session);
                }
            }
        }

        /// <summary>
        /// Returns true if the control has an active subscription assigned.
        /// </summary>
        public bool HasSubscription
        {
            get
            {
                return m_subscription != null;
            }
        }

        /// <summary>
        /// Sets the subscription used with the control.
        /// </summary>
        public void SetSubscription(Subscription subscription)
        {
            if (m_subscription != null)
            {
                m_subscription.PublishStatusChanged -= m_PublishStatusChanged;
                m_subscription.FastDataChangeCallback = null;
                m_subscription = null;
            }

            m_session = null;
            m_subscription = subscription;
            m_subscription.DisableMonitoredItemCache = true;
            m_subscription.PublishStatusChanged += m_PublishStatusChanged;
            m_subscription.FastDataChangeCallback = OnDataChangeAsync;
            m_dataset.Tables[0].Rows.Clear();

            if (m_subscription != null)
            {
                m_session = subscription.Session as Session;
                m_subscription.Handle = this;
            }
        }

        /// <summary>
        /// Adds the monitored items to the subscription.
        /// </summary>
        public async Task AddItemsAsync(CancellationToken ct, params ReadValueId[] itemsToMonitor)
        {
            if (m_subscription == null)
            {
                throw new ServiceResultException(StatusCodes.BadNoSubscription);
            }

            if (itemsToMonitor != null)
            {
                SetDisplayState(DisplayState.EditItems);

                for (int ii = 0; ii < itemsToMonitor.Length; ii++)
                {
                    if (itemsToMonitor[ii] == null)
                    {
                        continue;
                    }

                    DataRow row = m_dataset.Tables[0].NewRow();

                    MonitoredItem monitoredItem = new MonitoredItem(m_subscription.DefaultItem);
                    monitoredItem.StartNodeId = itemsToMonitor[ii].NodeId;
                    monitoredItem.AttributeId = itemsToMonitor[ii].AttributeId;
                    monitoredItem.IndexRange = itemsToMonitor[ii].IndexRange;
                    monitoredItem.Encoding = itemsToMonitor[ii].DataEncoding;
                    monitoredItem.Handle = row;
                    m_subscription.AddItem(monitoredItem);

                    await UpdateRowAsync(row, monitoredItem);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
        }

        /// <summary>
        /// Whether the next command does anything.
        /// </summary>
        public bool CanCallNext
        {
            get
            {
                return m_state != DisplayState.ViewUpdates;
            }
        }

        /// <summary>
        /// Whether the back command does anything.
        /// </summary>
        public bool CanCallBack
        {
            get
            {
                return m_state != DisplayState.EditItems;
            }
        }

        /// <summary>
        /// Moves the grid to the next state.
        /// </summary>
        public async Task NextAsync(CancellationToken ct = default)
        {
            if (m_state == DisplayState.ViewUpdates)
            {
                return;
            }

            SetDisplayState(++m_state);

            // clear any selection.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;
            }

            if (m_subscription != null)
            {
                // apply any changes.
                if (m_state == DisplayState.ApplyChanges)
                {
                    await m_subscription.ApplyChangesAsync(ct);

                    foreach (DataRow row in m_dataset.Tables[0].Rows)
                    {
                        MonitoredItem monitoredItem = (MonitoredItem)row[0];
                        UpdateRow(row, monitoredItem.Status);
                    }
                }
            }
        }

        /// <summary>
        /// Moves the grid back to the edit items state.
        /// </summary>
        public async Task BackAsync(CancellationToken ct = default)
        {
            if (m_state == DisplayState.EditItems)
            {
                return;
            }

            SetDisplayState(DisplayState.EditItems);

            // clear any selection.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;

                // revert to specified parameters.
                DataRowView source = row.DataBoundItem as DataRowView;
                MonitoredItem monitoredItem = (MonitoredItem)source.Row[0];
                await UpdateRowAsync(source.Row, monitoredItem, ct);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets the display state for the control.
        /// </summary>
        private void SetDisplayState(DisplayState state)
        {
            m_state = state;

            switch (m_state)
            {
                case DisplayState.EditItems:
                {
                    SamplingIntervalCH.Visible = true;
                    QueueSizeCH.Visible = true;
                    DiscardOldestCH.Visible = true;
                    FilterCH.Visible = true;
                    OperationStatusCH.Visible = false;
                    DataTypeCH.Visible = false;
                    ValueCH.Visible = false;
                    StatusCodeCH.Visible = false;
                    SourceTimestampCH.Visible = false;
                    ServerTimestampCH.Visible = false;
                    break;
                }

                case DisplayState.ApplyChanges:
                {
                    SamplingIntervalCH.Visible = true;
                    QueueSizeCH.Visible = true;
                    DiscardOldestCH.Visible = true;
                    FilterCH.Visible = false;
                    OperationStatusCH.Visible = true;
                    DataTypeCH.Visible = false;
                    ValueCH.Visible = false;
                    StatusCodeCH.Visible = false;
                    SourceTimestampCH.Visible = false;
                    ServerTimestampCH.Visible = false;
                    break;
                }

                case DisplayState.ViewUpdates:
                {
                    SamplingIntervalCH.Visible = false;
                    QueueSizeCH.Visible = false;
                    DiscardOldestCH.Visible = false;
                    FilterCH.Visible = false;
                    OperationStatusCH.Visible = false;
                    DataTypeCH.Visible = true;
                    ValueCH.Visible = true;
                    StatusCodeCH.Visible = true;
                    SourceTimestampCH.Visible = true;
                    ServerTimestampCH.Visible = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Updates the row with the monitored item.
        /// </summary>
        private async Task UpdateRowAsync(DataRow row, MonitoredItem monitoredItem, CancellationToken ct = default)
        {
            row[0] = monitoredItem;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(monitoredItem.AttributeId, null)];
            row[2] = await m_session.NodeCache.GetDisplayTextAsync(monitoredItem.StartNodeId, ct) + "/" + Attributes.GetBrowseName(monitoredItem.AttributeId);
            row[3] = monitoredItem.IndexRange;
            row[4] = monitoredItem.Encoding;
            row[5] = monitoredItem.MonitoringMode;
            row[6] = monitoredItem.SamplingInterval;
            row[7] = monitoredItem.QueueSize;
            row[8] = monitoredItem.DiscardOldest;
            row[9] = monitoredItem.Filter;
        }

        /// <summary>
        /// Updates the row with the monitored item status.
        /// </summary>
        private void UpdateRow(DataRow row, MonitoredItemStatus status)
        {
            row[5] = status.MonitoringMode;
            row[6] = status.SamplingInterval;
            row[7] = status.QueueSize;
            row[8] = status.DiscardOldest;
            row[9] = status.Filter;

            if (ServiceResult.IsBad(status.Error))
            {
                row[10] = new StatusCode(status.Error.Code);
            }
            else
            {
                row[10] = new StatusCode();
            }
        }

        /// <summary>
        /// Updates the row with the data value.
        /// </summary>
        private void UpdateRow(DataRow row, MonitoredItemNotification notification)
        {
            DataValue value = notification.Value;

            row[11] = value;

            if (value != null)
            {
                row[1] = ImageList.Images[ClientUtils.GetImageIndex(Attributes.Value, value.Value)];
                row[12] = (value.WrappedValue.TypeInfo != null) ? value.WrappedValue.TypeInfo.ToString() : String.Empty;
                row[13] = value.WrappedValue;
                row[14] = value.StatusCode;
                row[15] = value.SourceTimestamp.ToLocalTime().ToString("hh:mm:ss.fff");
                row[16] = value.ServerTimestamp.ToLocalTime().ToString("hh:mm:ss.fff");
            }
        }

        /// <summary>
        /// Gets the display string for the subscription status.
        /// </summary>
        private string GetDisplayString(Subscription subscription)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append((subscription.CurrentPublishingEnabled) ? "Enabled" : "Disabled");
            buffer.Append(" (");
            buffer.Append(subscription.CurrentPublishingInterval);
            buffer.Append("ms/");
            buffer.Append(subscription.CurrentKeepAliveCount);
            buffer.Append('/');
            buffer.Append(subscription.CurrentLifetimeCount);
            buffer.Append('}');

            return buffer.ToString();
        }
        #endregion

        #region Event Handlers
        private void OnPublishStatusChanged(object sender, PublishStateChangedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, m_subscription))
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(m_PublishStatusChanged, sender, e);
                return;
            }

            try
            {
                if ((e.Status & PublishStateChangedMask.Stopped) != 0)
                {
                    SubscriptionStateTB.Text = "STOPPED";
                    SubscriptionStateTB.ForeColor = Color.Red;
                }
                else if ((e.Status & PublishStateChangedMask.Recovered) != 0)
                {
                    SubscriptionStateTB.Text = GetDisplayString(m_subscription);
                    SubscriptionStateTB.ForeColor = Color.Empty;
                }

                SequenceNumberTB.Text = m_subscription.SequenceNumber.ToString();
                LastNotificationTB.Text = m_subscription.LastNotificationTime.ToLocalTime().ToString("hh:mm:ss");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void OnDataChangeAsync(Subscription subscription, DataChangeNotification notification, IList<string> stringTable)
        {
            if (!Object.ReferenceEquals(subscription, m_subscription))
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new FastDataChangeNotificationEventHandler(OnDataChangeAsync), subscription, notification, stringTable);
                return;
            }

            try
            {
                foreach (MonitoredItemNotification itemNotification in notification.MonitoredItems)
                {
                    MonitoredItem monitoredItem = subscription.FindItemByClientHandle(itemNotification.ClientHandle);

                    if (monitoredItem == null)
                    {
                        continue;
                    }

                    DataRow row = (DataRow)monitoredItem.Handle;

                    if (row.RowState == DataRowState.Detached)
                    {
                        continue;
                    }

                    UpdateRow(row, itemNotification);

                    if (m_EditComplexValueDlg != null && Object.ReferenceEquals(m_EditComplexValueDlg.Tag, monitoredItem))
                    {
                        await m_EditComplexValueDlg.UpdateValueAsync(monitoredItem.ResolvedNodeId, monitoredItem.AttributeId, null, itemNotification.Value.Value);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void PopupMenu_Opening(object sender, CancelEventArgs e)
        {
            NewMI.Visible = m_state == DisplayState.EditItems;
            EditMI.Enabled = ResultsDV.SelectedRows.Count > 0;
            DeleteMI.Enabled = ResultsDV.SelectedRows.Count > 0;
            ViewValueMI.Visible = m_state == DisplayState.ViewUpdates;
            SetMonitoringModeMI.Visible = m_state != DisplayState.ApplyChanges;
        }

        private async void NewMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                MonitoredItem monitoredItem = null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItem = (MonitoredItem)source.Row[0];
                    break;
                }

                if (monitoredItem == null)
                {
                    monitoredItem = new MonitoredItem(m_subscription.DefaultItem);
                }
                else
                {
                    monitoredItem = new MonitoredItem(monitoredItem);
                }

                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_session, monitoredItem, false, m_telemetry))
                {
                    m_subscription.AddItem(monitoredItem);
                    DataRow row = m_dataset.Tables[0].NewRow();
                    monitoredItem.Handle = row;
                    await UpdateRowAsync(row, monitoredItem);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                MonitoredItem monitoredItem = null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItem = (MonitoredItem)source.Row[0];
                    break;
                }

                if (monitoredItem == null)
                {
                    return;
                }

                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_session, monitoredItem, false, m_telemetry))
                {
                    DataRow row = (DataRow)monitoredItem.Handle;
                    await UpdateRowAsync(row, monitoredItem);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    MonitoredItem monitoredItem = (MonitoredItem)source.Row[0];
                    m_subscription.RemoveItem(monitoredItem);
                    source.Row.Delete();
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void ViewValueMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                MonitoredItem monitoredItem = null;
                DataValue value = null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItem = (MonitoredItem)source.Row[0];
                    value = (DataValue)source.Row[11];
                    break;
                }

                if (monitoredItem == null)
                {
                    return;
                }

                m_EditComplexValueDlg = new EditComplexValueDlg();
                m_EditComplexValueDlg.Tag = monitoredItem;

                await m_EditComplexValueDlg.ShowDialogAsync(
                    m_session,
                    monitoredItem.ResolvedNodeId,
                    monitoredItem.AttributeId,
                    null,
                    value.Value,
                    true,
                    "View Data Change");

                m_EditComplexValueDlg = null;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void ResultsDV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (m_state == DisplayState.EditItems)
                {
                    EditMI_ClickAsync(sender, e);
                }
                else
                {
                    ViewValueMI_ClickAsync(sender, e);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void SetMonitoringModeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<MonitoredItem> monitoredItems = new List<MonitoredItem>();

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItems.Add((MonitoredItem)source.Row[0]);
                }

                if (monitoredItems.Count == 0)
                {
                    return;
                }

                MonitoringMode oldMonitoringMode = monitoredItems[0].MonitoringMode;
                MonitoringMode newMonitoringMode = new EditMonitoredItemDlg().ShowDialog(oldMonitoringMode);

                if (oldMonitoringMode != newMonitoringMode)
                {
                    List<MonitoredItem> itemsToModify = new List<MonitoredItem>();

                    foreach (MonitoredItem monitoredItem in monitoredItems)
                    {
                        DataRow row = (DataRow)monitoredItem.Handle;
                        row[5] = newMonitoringMode;

                        if (monitoredItem.Created)
                        {
                            itemsToModify.Add(monitoredItem);
                            continue;
                        }

                        monitoredItem.MonitoringMode = newMonitoringMode;
                    }

                    if (itemsToModify.Count != 0)
                    {
                        await m_subscription.SetMonitoringModeAsync(newMonitoringMode, itemsToModify);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void Subscription_EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (new EditSubscriptionDlg().ShowDialog(m_subscription))
                {
                    await m_subscription.ModifyAsync();

                    if (m_subscription.PublishingEnabled != m_subscription.CurrentPublishingEnabled)
                    {
                        await m_subscription.SetPublishingModeAsync(m_subscription.PublishingEnabled);
                    }

                    SubscriptionStateTB.Text = GetDisplayString(m_subscription);
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
