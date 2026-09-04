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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class SubscribeDataListViewCtrl : UserControl
    {
        /// <summary>
        /// How long the control waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public SubscribeDataListViewCtrl()
        {
            InitializeComponent();
            m_subscription.Callbacks.DataChangeCallback = OnDataChanges;
            m_subscription.Callbacks.KeepAliveCallback = OnKeepAlive;
            m_subscription.Callbacks.StateChangedCallback = OnSubscriptionStateChanged;
            ResultsDV.AutoGenerateColumns = false;
            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            ImageList = new ClientUtils().ImageList;
            #pragma warning restore CA2000

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Requests");

            m_dataset.Tables[0].Columns.Add("MonitoredItem", typeof(MonitoredItemHandle));
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
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        // the subscription, its items and everything the engine needs said to it; the
        // control below is the grid which shows them.
        private readonly SampleSubscription m_subscription = new SampleSubscription();
        private ITelemetryContext m_telemetry;
        private DisplayState m_state;
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private EditComplexValueDlg m_EditComplexValueDlg;
        #pragma warning restore CA2213
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

            // a V2 subscription belongs to the subscription manager of the session it was
            // created on, and it survives a reconnect together with its monitored items.
            // It only has to be dropped when it does not belong to the new session.
            if (m_subscription.ChangeSession(session))
            {
                m_dataset.Tables[0].Rows.Clear();
            }

            m_EditComplexValueDlg?.ChangeSession(session);
        }

        /// <summary>
        /// Returns true if the control has an active subscription assigned.
        /// </summary>
        public bool HasSubscription => m_subscription.HasSubscription;

        /// <summary>
        /// The handler the control needs when a subscription is created on its behalf.
        /// </summary>
        /// <remarks>
        /// The V2 engine takes the notification handler when the subscription is created, so a
        /// caller which creates the subscription itself has to pass this one.
        /// </remarks>
        public ISubscriptionNotificationHandler NotificationHandler => m_subscription.NotificationHandler;

        /// <summary>
        /// Creates the subscription the control displays on the session.
        /// </summary>
        public ISubscription CreateSubscription(ISession session, SubscriptionOptions options = null)
        {
            ISubscription subscription = m_subscription.Create(session, options);

            m_dataset.Tables[0].Rows.Clear();

            return subscription;
        }

        /// <summary>
        /// Sets the subscription used with the control.
        /// </summary>
        /// <param name="subscription">The subscription, created with <see cref="NotificationHandler"/>.</param>
        /// <param name="session">The session the subscription was created on. A V2 subscription
        /// does not point back at its session, so the control has to be told.</param>
        /// <param name="options">The options monitor the subscription was created with, so the
        /// control can reconfigure it. Optional: without it the subscription cannot be edited.</param>
        public void SetSubscription(ISubscription subscription, ISession session, OptionsMonitor<SubscriptionOptions> options = null)
        {
            m_subscription.Adopt(subscription, session, options);
            m_dataset.Tables[0].Rows.Clear();
        }

        /// <summary>
        /// Adds the monitored items to the subscription.
        /// </summary>
        public async Task AddItemsAsync(CancellationToken ct, params ReadValueId[] itemsToMonitor)
        {
            if (!m_subscription.HasSubscription)
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

                    MonitoredItemHandle handle = m_subscription.Add(new MonitoredItemOptions {
                        StartNodeId = itemsToMonitor[ii].NodeId,
                        AttributeId = itemsToMonitor[ii].AttributeId,
                        IndexRange = itemsToMonitor[ii].IndexRange,
                        Encoding = itemsToMonitor[ii].DataEncoding,
                    });

                    handle.Row = row;

                    await UpdateRowAsync(row, handle, ct);
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

            // apply any changes.
            if (m_subscription.HasSubscription && m_state == DisplayState.ApplyChanges)
            {
                await ApplyChangesAsync(ct);
            }
        }

        /// <summary>
        /// Sends the items which are new to the server and shows what it revised them to.
        /// </summary>
        private async Task ApplyChangesAsync(CancellationToken ct = default)
        {
            await m_subscription.ApplyAsync(kApplyTimeout, ct);

            foreach (DataRow row in m_dataset.Tables[0].Rows)
            {
                UpdateRevisedValues(row, (MonitoredItemHandle)row[0]);
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
                var handle = (MonitoredItemHandle)source.Row[0];
                await UpdateRowAsync(source.Row, handle, ct);
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
        private async Task UpdateRowAsync(DataRow row, MonitoredItemHandle handle, CancellationToken ct = default)
        {
            MonitoredItemOptions settings = handle.Settings;

            row[0] = handle;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(settings.AttributeId, Variant.Null)];
            row[2] = await m_subscription.Session.NodeCache.GetDisplayTextAsync(settings.StartNodeId, ct) + "/" + Attributes.GetBrowseName(settings.AttributeId);
            row[3] = settings.IndexRange;
            row[4] = settings.Encoding ?? QualifiedName.Null;
            row[5] = settings.MonitoringMode;
            row[6] = settings.SamplingInterval.TotalMilliseconds;
            row[7] = settings.QueueSize;
            row[8] = settings.DiscardOldest;
            row[9] = settings.Filter;
        }

        /// <summary>
        /// Updates the row with the values the server revised the monitored item to.
        /// </summary>
        private void UpdateRevisedValues(DataRow row, MonitoredItemHandle handle)
        {
            IMonitoredItem item = handle.Item;

            if (item == null)
            {
                return;
            }

            row[5] = item.CurrentMonitoringMode;
            row[6] = item.CurrentSamplingInterval.TotalMilliseconds;
            row[7] = item.CurrentQueueSize;
            row[8] = handle.Settings.DiscardOldest;
            row[9] = handle.Settings.Filter;

            if (ServiceResult.IsBad(item.Error))
            {
                row[10] = new StatusCode(item.Error.Code);
            }
            else
            {
                row[10] = new StatusCode();
            }
        }

        /// <summary>
        /// Updates the row with the data value.
        /// </summary>
        private void UpdateRow(DataRow row, DataValue value)
        {
            row[11] = value;

            if (!value.IsNull)
            {
                row[1] = ImageList.Images[ClientUtils.GetImageIndex(Attributes.Value, value.WrappedValue)];
                row[12] = (!value.WrappedValue.TypeInfo.IsUnknown) ? value.WrappedValue.TypeInfo.ToString() : String.Empty;
                row[13] = value.WrappedValue;
                row[14] = value.StatusCode;
                row[15] = value.SourceTimestamp.ToLocalTime().ToString("hh:mm:ss.fff");
                row[16] = value.ServerTimestamp.ToLocalTime().ToString("hh:mm:ss.fff");
            }
        }

        #endregion

        #region Event Handlers
        private void OnKeepAlive(ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, uint, DateTime, PublishState>(OnKeepAlive), subscription, sequenceNumber, publishTime, publishStateMask);
                return;
            }

            UpdatePublishStatus(subscription, sequenceNumber, publishTime, publishStateMask);
        }

        private void OnSubscriptionStateChanged(ISubscription subscription, Opc.Ua.Client.Subscriptions.SubscriptionState state, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, Opc.Ua.Client.Subscriptions.SubscriptionState, PublishState>(OnSubscriptionStateChanged), subscription, state, publishStateMask);
                return;
            }

            if (!Object.ReferenceEquals(subscription, m_subscription.Subscription))
            {
                return;
            }

            try
            {
                SubscriptionStateTB.Text = SampleSubscription.Describe(subscription);
                SubscriptionStateTB.ForeColor = Color.Empty;

                // the state change is what reports that the engine applied the pending item
                // changes, so this is where the revised values become visible.
                if (m_state == DisplayState.ApplyChanges)
                {
                    foreach (DataRow row in m_dataset.Tables[0].Rows)
                    {
                        UpdateRevisedValues(row, (MonitoredItemHandle)row[0]);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shows the publish state the engine reported with a notification.
        /// </summary>
        private void UpdatePublishStatus(ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            if (!Object.ReferenceEquals(subscription, m_subscription.Subscription))
            {
                return;
            }

            try
            {
                if ((publishStateMask & PublishState.Stopped) != 0)
                {
                    SubscriptionStateTB.Text = "STOPPED";
                    SubscriptionStateTB.ForeColor = Color.Red;
                }
                else if ((publishStateMask & PublishState.Recovered) != 0)
                {
                    SubscriptionStateTB.Text = SampleSubscription.Describe(subscription);
                    SubscriptionStateTB.ForeColor = Color.Empty;
                }

                SequenceNumberTB.Text = sequenceNumber.ToString();
                LastNotificationTB.Text = publishTime.ToLocalTime().ToString("hh:mm:ss");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void OnDataChanges(ISubscription subscription, uint sequenceNumber, DateTime publishTime, DataValueChange[] changes, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, uint, DateTime, DataValueChange[], PublishState>(OnDataChanges), subscription, sequenceNumber, publishTime, changes, publishStateMask);
                return;
            }

            if (!Object.ReferenceEquals(subscription, m_subscription.Subscription))
            {
                return;
            }

            try
            {
                UpdatePublishStatus(subscription, sequenceNumber, publishTime, publishStateMask);

                foreach (DataValueChange change in changes)
                {
                    MonitoredItemHandle handle = m_subscription.Find(change.MonitoredItem);

                    if (handle?.Row == null || handle.Row.RowState == DataRowState.Detached)
                    {
                        continue;
                    }

                    UpdateRow(handle.Row, change.Value);

                    if (m_EditComplexValueDlg != null && Object.ReferenceEquals(m_EditComplexValueDlg.Tag, handle))
                    {
                        await m_EditComplexValueDlg.UpdateValueAsync(handle.Settings.StartNodeId, handle.Settings.AttributeId, null, change.Value.WrappedValue);
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
                MonitoredItemHandle selected = null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    selected = (MonitoredItemHandle)source.Row[0];
                    break;
                }

                // a new item starts from the settings of the selected one, or from the defaults.
                // It is added straight away because the dialog edits it in place; a cancelled
                // dialog takes it back off again, before it ever reached the server.
                MonitoredItemHandle handle = m_subscription.Add(selected?.Settings ?? new MonitoredItemOptions());

                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_subscription.Session, handle, false, m_telemetry))
                #pragma warning restore CA2000
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    handle.Row = row;
                    await UpdateRowAsync(row, handle);
                    m_dataset.Tables[0].Rows.Add(row);
                }
                else
                {
                    m_subscription.Remove(handle);
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
                MonitoredItemHandle handle = null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    handle = (MonitoredItemHandle)source.Row[0];
                    break;
                }

                if (handle == null)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_subscription.Session, handle, false, m_telemetry))
                #pragma warning restore CA2000
                {
                    await UpdateRowAsync(handle.Row, handle);
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
                    var handle = (MonitoredItemHandle)source.Row[0];

                    m_subscription.Remove(handle);
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
                MonitoredItemHandle handle = null;
                DataValue value = DataValue.Null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    handle = (MonitoredItemHandle)source.Row[0];
                    value = (DataValue)source.Row[11];
                    break;
                }

                if (handle == null)
                {
                    return;
                }

                m_EditComplexValueDlg = new EditComplexValueDlg();
                m_EditComplexValueDlg.Tag = handle;

                await m_EditComplexValueDlg.ShowDialogAsync(
                    m_subscription.Session,
                    handle.Settings.StartNodeId,
                    handle.Settings.AttributeId,
                    null,
                    value.WrappedValue,
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
                var handles = new List<MonitoredItemHandle>();

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    handles.Add((MonitoredItemHandle)source.Row[0]);
                }

                if (handles.Count == 0)
                {
                    return;
                }

                MonitoringMode oldMonitoringMode = handles[0].Settings.MonitoringMode;
                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                MonitoringMode newMonitoringMode = new EditMonitoredItemDlg().ShowDialog(oldMonitoringMode);
                #pragma warning restore CA2000

                if (oldMonitoringMode != newMonitoringMode)
                {
                    foreach (MonitoredItemHandle handle in handles)
                    {
                        handle.Row[5] = newMonitoringMode;
                    }

                    await m_subscription.SetMonitoringModeAsync(handles, newMonitoringMode, kApplyTimeout);
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
                if (!m_subscription.CanEditSubscription)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                if (new EditSubscriptionDlg().ShowDialog(m_subscription.Options, m_telemetry))
                #pragma warning restore CA2000
                {
                    // the engine applies the new options on its own worker, so the revised values
                    // are only there once the pending change settled.
                    await m_subscription.WaitForChangesAsync(kApplyTimeout);
                    SubscriptionStateTB.Text = SampleSubscription.Describe(m_subscription.Subscription);
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
