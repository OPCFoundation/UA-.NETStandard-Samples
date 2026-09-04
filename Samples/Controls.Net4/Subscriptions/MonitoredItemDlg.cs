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
using System.Windows.Forms;
using System.Reflection;
using Opc.Ua.Samples.WinForms;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

    public partial class MonitoredItemDlg : SampleForm
    {
        #region Constructors
        public MonitoredItemDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_DataChangeCallback = new Action<ISubscription, uint, DateTime, DataValueChange[], PublishState>(OnDataChangeNotification);
            m_EventCallback = new Action<ISubscription, uint, DateTime, EventNotification[], PublishState>(OnEventNotification);
            m_KeepAliveCallback = new Action<ISubscription, uint, DateTime, PublishState>(OnKeepAliveNotification);
            m_StateChangedCallback = new Action<ISubscription, SubscriptionState, PublishState>(OnSubscriptionStateChanged);

            FormClosing += new FormClosingEventHandler(MonitoredItemDlg_FormClosing);
        }
        #endregion

        #region Private Fields
        private SubscriptionHandle m_subscription;
        private MonitoredItemHandle m_monitoredItem;
        private readonly Action<ISubscription, uint, DateTime, DataValueChange[], PublishState> m_DataChangeCallback;
        private readonly Action<ISubscription, uint, DateTime, EventNotification[], PublishState> m_EventCallback;
        private readonly Action<ISubscription, uint, DateTime, PublishState> m_KeepAliveCallback;
        private readonly Action<ISubscription, SubscriptionState, PublishState> m_StateChangedCallback;
        private ITelemetryContext m_telemetry;
        private uint m_lastSequenceNumber;
        private DateTime m_lastPublishTime;
        private bool m_publishingStopped;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(SubscriptionHandle subscription, MonitoredItemHandle monitoredItem)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

            Show();
            BringToFront();

            // stop receiving notifications from the previous subscription.
            Detach();

            // start receiving notifications from the new subscription.
            m_subscription = subscription;
            m_monitoredItem = monitoredItem;
            m_telemetry = subscription.Session?.MessageContext?.Telemetry;

            subscription.Callbacks.DataChangeCallback += m_DataChangeCallback;
            subscription.Callbacks.EventCallback += m_EventCallback;
            subscription.Callbacks.KeepAliveCallback += m_KeepAliveCallback;
            subscription.Callbacks.StateChangedCallback += m_StateChangedCallback;

            WindowMI_Click(WindowStatusMI, null);
            WindowMI_Click(WindowLatestValueMI, null);

            MonitoredItemsCTRL.Initialize(m_subscription, m_monitoredItem);
            EventsCTRL.Initialize(m_subscription, m_monitoredItem);
            DataChangesCTRL.Initialize(m_subscription, m_monitoredItem);
            LatestValueCTRL.Telemetry = m_telemetry;
            UpdateStatus();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Stops receiving notifications from the current subscription.
        /// </summary>
        private void Detach()
        {
            if (m_subscription != null)
            {
                m_subscription.Callbacks.DataChangeCallback -= m_DataChangeCallback;
                m_subscription.Callbacks.EventCallback -= m_EventCallback;
                m_subscription.Callbacks.KeepAliveCallback -= m_KeepAliveCallback;
                m_subscription.Callbacks.StateChangedCallback -= m_StateChangedCallback;
            }
        }

        /// <summary>
        /// Updates the controls displaying the status of the monitored item.
        /// </summary>
        private void UpdateStatus()
        {
            MonitoringModeTB.Text = String.Empty;
            MonitoringModeTB.ForeColor = Color.Empty;
            MonitoringModeTB.Font = new Font(MonitoringModeTB.Font, FontStyle.Regular);

            if (m_monitoredItem != null)
            {
                MonitoringMode monitoringMode = (m_monitoredItem.Item != null && m_monitoredItem.Item.Created)
                    ? m_monitoredItem.Item.CurrentMonitoringMode
                    : m_monitoredItem.Settings.MonitoringMode;

                MonitoringModeTB.Text = String.Format("{0}", monitoringMode);
            }

            if (m_publishingStopped)
            {
                MonitoringModeTB.Text = String.Format("BadNoCommunication");
                MonitoringModeTB.ForeColor = Color.Red;
                MonitoringModeTB.Font = new Font(MonitoringModeTB.Font, FontStyle.Bold);
            }

            LastUpdateTimeTB.Text = String.Empty;
            LastMessageIdTB.Text = String.Empty;

            if (m_lastPublishTime != DateTime.MinValue)
            {
                LastUpdateTimeTB.Text = String.Format("{0:HH:mm:ss}", m_lastPublishTime.ToLocalTime());
                LastMessageIdTB.Text = String.Format("{0}", m_lastSequenceNumber);
            }
        }

        /// <summary>
        /// Remembers the publish state the engine reported with a notification.
        /// </summary>
        private void UpdatePublishState(uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            m_lastSequenceNumber = sequenceNumber;
            m_lastPublishTime = publishTime;

            if ((publishStateMask & PublishState.Stopped) != 0)
            {
                m_publishingStopped = true;
            }
            else if ((publishStateMask & PublishState.Recovered) != 0)
            {
                m_publishingStopped = false;
            }
            else if (publishStateMask == PublishState.None)
            {
                m_publishingStopped = false;
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Processes the data changes of a publish response from the server.
        /// </summary>
        private async void OnDataChangeNotification(ISubscription subscription, uint sequenceNumber, DateTime publishTime, DataValueChange[] notifications, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_DataChangeCallback, subscription, sequenceNumber, publishTime, notifications, publishStateMask);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                // ignore notifications for other subscriptions.
                if (m_subscription == null || !Object.ReferenceEquals(m_subscription.Subscription, subscription))
                {
                    return;
                }

                UpdatePublishState(sequenceNumber, publishTime, publishStateMask);

                // notify controls of the change.
                await DataChangesCTRL.NotificationReceivedAsync(notifications, publishStateMask);
                MonitoredItemsCTRL.NotificationReceived(notifications);

                // show the latest value of the item this dialog monitors.
                foreach (DataValueChange change in notifications)
                {
                    if (Object.ReferenceEquals(m_monitoredItem.Item, change.MonitoredItem))
                    {
                        LatestValueCTRL.Telemetry = m_telemetry;
                        await LatestValueCTRL.ShowValueAsync(change.Value, true);
                    }
                }

                // update item status.
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Processes the events of a publish response from the server.
        /// </summary>
        private void OnEventNotification(ISubscription subscription, uint sequenceNumber, DateTime publishTime, EventNotification[] notifications, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_EventCallback, subscription, sequenceNumber, publishTime, notifications, publishStateMask);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                // ignore notifications for other subscriptions.
                if (m_subscription == null || !Object.ReferenceEquals(m_subscription.Subscription, subscription))
                {
                    return;
                }

                UpdatePublishState(sequenceNumber, publishTime, publishStateMask);

                // notify controls of the change.
                EventsCTRL.NotificationReceived(notifications);
                MonitoredItemsCTRL.NotificationReceived(notifications);

                // update item status.
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Processes a keep alive notification from the server.
        /// </summary>
        private void OnKeepAliveNotification(ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_KeepAliveCallback, subscription, sequenceNumber, publishTime, publishStateMask);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                // ignore notifications for other subscriptions.
                if (m_subscription == null || !Object.ReferenceEquals(m_subscription.Subscription, subscription))
                {
                    return;
                }

                UpdatePublishState(sequenceNumber, publishTime, publishStateMask);
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Handles a change to the state of the subscription.
        /// </summary>
        private void OnSubscriptionStateChanged(ISubscription subscription, SubscriptionState state, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_StateChangedCallback, subscription, state, publishStateMask);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                // ignore notifications for other subscriptions.
                if (m_subscription == null || !Object.ReferenceEquals(m_subscription.Subscription, subscription))
                {
                    return;
                }

                // notify controls of the change.
                EventsCTRL.SubscriptionChanged();
                DataChangesCTRL.SubscriptionChanged();
                MonitoredItemsCTRL.SubscriptionChanged();

                // update subscription status.
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MonitoredItemDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                Detach();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void WindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (sender == WindowStatusMI)
                {
                    WindowStatusMI.Checked = !WindowStatusMI.Checked;
                    WindowLatestValueMI.Checked = false;
                    MonitoredItemsCTRL.Visible = true;
                    SplitterPN.Panel1Collapsed = !WindowStatusMI.Checked;
                }

                else if (sender == WindowHistoryMI)
                {
                    WindowHistoryMI.Checked = true;
                    WindowLatestValueMI.Checked = false;
                    MonitoredItemsCTRL.Visible = true;
                    EventsCTRL.Visible = m_monitoredItem.NodeClass != NodeClass.Variable;
                    DataChangesCTRL.Visible = !EventsCTRL.Visible;
                    LatestValueCTRL.Visible = false;

                    Text = String.Format("{0} - {1} - {2}", m_subscription.DisplayName, m_monitoredItem.DisplayName, "Recent Values");
                }

                else if (sender == WindowLatestValueMI)
                {
                    WindowHistoryMI.Checked = false;
                    WindowLatestValueMI.Checked = true;
                    MonitoredItemsCTRL.Visible = true;
                    EventsCTRL.Visible = false;
                    DataChangesCTRL.Visible = false;
                    LatestValueCTRL.Visible = true;

                    Text = String.Format("{0} - {1} - {2}", m_subscription.DisplayName, m_monitoredItem.DisplayName, "Latest Value");
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void MonitoringModeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                MonitoringMode monitoringMode = m_monitoredItem.Settings.MonitoringMode;

                if (sender == MonitoringModeReportingMI)
                {
                    monitoringMode = MonitoringMode.Reporting;
                }

                else if (sender == MonitoringModeSamplingMI)
                {
                    monitoringMode = MonitoringMode.Sampling;
                }

                else if (sender == MonitoringModeDisabledMI)
                {
                    monitoringMode = MonitoringMode.Disabled;
                }

                // reconfiguring the options is the request, the engine applies it on its own
                // worker.
                m_monitoredItem.Configure(options => options with { MonitoringMode = monitoringMode });
                await m_subscription.WaitForPendingChangesAsync(TimeSpan.FromSeconds(10));

                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MonitoringModeMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                MonitoringModeReportingMI.Checked = false;
                MonitoringModeSamplingMI.Checked = false;
                MonitoringModeDisabledMI.Checked = false;

                MonitoringMode monitoringMode = (m_monitoredItem.Item != null && m_monitoredItem.Item.Created)
                    ? m_monitoredItem.Item.CurrentMonitoringMode
                    : m_monitoredItem.Settings.MonitoringMode;

                switch (monitoringMode)
                {
                    case MonitoringMode.Reporting: { MonitoringModeReportingMI.Checked = true; break; }
                    case MonitoringMode.Sampling: { MonitoringModeSamplingMI.Checked = true; break; }
                    case MonitoringMode.Disabled: { MonitoringModeDisabledMI.Checked = true; break; }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
