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
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

    public partial class SubscriptionDlg : SampleForm
    {
        /// <summary>
        /// How long the dialog waits for the subscription engine to apply changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        #region Constructors
        public SubscriptionDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_DataChangeCallback = new Action<ISubscription, uint, DateTime, DataValueChange[], PublishState>(OnDataChangeNotification);
            m_EventCallback = new Action<ISubscription, uint, DateTime, EventNotification[], PublishState>(OnEventNotification);
            m_KeepAliveCallback = new Action<ISubscription, uint, DateTime, PublishState>(OnKeepAliveNotification);
            m_StateChangedCallback = new Action<ISubscription, SubscriptionState, PublishState>(OnSubscriptionStateChanged);
        }
        #endregion

        #region Private Fields
        private ITelemetryContext m_telemetry;
        private SubscriptionHandle m_subscription;
        private readonly Action<ISubscription, uint, DateTime, DataValueChange[], PublishState> m_DataChangeCallback;
        private readonly Action<ISubscription, uint, DateTime, EventNotification[], PublishState> m_EventCallback;
        private readonly Action<ISubscription, uint, DateTime, PublishState> m_KeepAliveCallback;
        private readonly Action<ISubscription, SubscriptionState, PublishState> m_StateChangedCallback;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA2213:Disposable fields should be disposed", Justification = "Sample code preserves existing public API and behavior.")]
        private CreateMonitoredItemsDlg m_createDialog;
        private uint m_lastSequenceNumber;
        private DateTime m_lastPublishTime;
        private static uint m_Counter = 0;
        #endregion

        #region Public Interface
        /// <summary>
        /// Creates a new subscription with the V2 subscription engine of the session.
        /// </summary>
        public SubscriptionHandle New(ISession session, ITelemetryContext telemetry)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_telemetry = telemetry;

            SubscriptionHandle subscription = new SubscriptionHandle(
                session,
                Utils.Format("Subscription {0}", Utils.IncrementIdentifier(ref m_Counter)),
                ClientUtils.DefaultSubscriptionOptions);

            #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
            if (!Windows.Create<SubscriptionEditDlg>().ShowDialog(subscription))
            #pragma warning restore CA2000
            {
                return null;
            }

            // registering the subscription is the request, the engine creates it on the
            // server from its own worker.
            subscription.Create();

            Show(subscription);

            return subscription;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(SubscriptionHandle subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            if (m_telemetry == null)
            {
                m_telemetry = subscription.Session?.MessageContext?.Telemetry;
            }

            Show();
            BringToFront();

            // stop receiving notifications from the previous subscription.
            Detach();

            // start receiving notifications from the new subscription.
            m_subscription = subscription;

            subscription.Callbacks.DataChangeCallback += m_DataChangeCallback;
            subscription.Callbacks.EventCallback += m_EventCallback;
            subscription.Callbacks.KeepAliveCallback += m_KeepAliveCallback;
            subscription.Callbacks.StateChangedCallback += m_StateChangedCallback;

            MonitoredItemsCTRL.Initialize(subscription, m_telemetry);
            EventsCTRL.Initialize(subscription, null);
            DataChangesCTRL.Initialize(subscription, null);

            WindowMI_Click(WindowDataChangesMI, null);

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
        /// Updates the controls displaying the status of the subscription.
        /// </summary>
        private void UpdateStatus()
        {
            PublishingEnabledTB.Text = String.Empty;

            if (m_subscription != null)
            {
                bool publishingEnabled = (m_subscription.Subscription != null)
                    ? m_subscription.Subscription.CurrentPublishingEnabled
                    : m_subscription.Settings.PublishingEnabled;

                PublishingEnabledTB.Text = (publishingEnabled) ? "Enabled" : "Disabled";
            }

            LastUpdateTimeTB.Text = String.Empty;
            LastMessageIdTB.Text = String.Empty;

            if (m_lastPublishTime != DateTime.MinValue)
            {
                LastUpdateTimeTB.Text = String.Format("{0:HH:mm:ss}", m_lastPublishTime.ToLocalTime());
                LastMessageIdTB.Text = String.Format("{0}", m_lastSequenceNumber);
            }

            // determine what window to show.
            bool hasEvents = false;
            bool hasDatachanges = false;

            if (m_subscription != null)
            {
                foreach (MonitoredItemHandle monitoredItem in m_subscription.Items)
                {
                    if (monitoredItem.Settings.Filter is EventFilter)
                    {
                        hasEvents = true;
                    }

                    if (monitoredItem.NodeClass == NodeClass.Variable)
                    {
                        hasDatachanges = true;
                    }
                }
            }

            // enable appropriate windows.
            WindowEventsMI.Enabled = hasEvents;
            WindowDataChangesMI.Enabled = hasDatachanges;

            // show the datachange window if there are no event items.
            if (hasDatachanges && !hasEvents)
            {
                WindowMI_Click(WindowDataChangesMI, null);
            }

            // show events window if there are no datachange items.
            if (hasEvents && !hasDatachanges)
            {
                WindowMI_Click(WindowEventsMI, null);
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

                m_lastSequenceNumber = sequenceNumber;
                m_lastPublishTime = publishTime;

                // notify controls of the change.
                await DataChangesCTRL.NotificationReceivedAsync(notifications, publishStateMask);

                // update subscription status.
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

                m_lastSequenceNumber = sequenceNumber;
                m_lastPublishTime = publishTime;

                // notify controls of the change.
                EventsCTRL.NotificationReceived(notifications);

                // update subscription status.
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

                m_lastSequenceNumber = sequenceNumber;
                m_lastPublishTime = publishTime;

                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Handles a change to the state of the subscription, which is also what reports that
        /// the engine finished applying changes.
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

        private void SubscriptionMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                SubscriptionEnablePublishingMI.Checked = (m_subscription.Subscription != null)
                    ? m_subscription.Subscription.CurrentPublishingEnabled
                    : m_subscription.Settings.PublishingEnabled;
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
                if (sender == WindowMonitoredItemsMI)
                {
                    WindowMonitoredItemsMI.Checked = !WindowMonitoredItemsMI.Checked;
                    WindowEventsMI.Checked = false;
                    MonitoredItemsCTRL.Visible = true;
                    SplitterPN.Panel1Collapsed = !WindowMonitoredItemsMI.Checked;
                }

                else if (sender == WindowDataChangesMI)
                {
                    WindowDataChangesMI.Checked = true;
                    WindowEventsMI.Checked = false;
                    MonitoredItemsCTRL.Visible = true;
                    EventsCTRL.Visible = false;
                    DataChangesCTRL.Visible = true;

                    Text = String.Format("{0} - {1}", m_subscription.DisplayName, "Data Changes");
                }

                else if (sender == WindowEventsMI)
                {
                    WindowDataChangesMI.Checked = false;
                    WindowEventsMI.Checked = true;
                    MonitoredItemsCTRL.Visible = true;
                    EventsCTRL.Visible = true;
                    DataChangesCTRL.Visible = false;

                    Text = String.Format("{0} - {1}", m_subscription.DisplayName, "Events");
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SubscriptionEnablePublishingMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // reconfiguring the options is the request, the engine applies it on its own
                // worker.
                bool enabled = SubscriptionEnablePublishingMI.Checked;
                m_subscription.Configure(options => options with { PublishingEnabled = enabled });
                await m_subscription.WaitForPendingChangesAsync(kApplyTimeout);
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (m_createDialog != null)
                {
                    m_createDialog.Close();
                }

                MonitoredItemsCTRL.FormClosing();

                Detach();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                if (!Windows.Create<SubscriptionEditDlg>().ShowDialog(m_subscription))
                #pragma warning restore CA2000
                {
                    return;
                }

                // the engine applies the new options on its own worker, the state changed
                // callback refreshes the display once it did.
                await m_subscription.WaitForPendingChangesAsync(kApplyTimeout);
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionCreateItemMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_createDialog == null)
                {
                    m_createDialog = Windows.Create<CreateMonitoredItemsDlg>();
                    m_createDialog.FormClosing += new FormClosingEventHandler(CreateDialog_FormClosing);
                }

                m_createDialog.Show(m_subscription, false);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        void CreateDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (m_createDialog == sender)
                {
                    m_createDialog = null;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionCreateItemFromTypeMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_createDialog == null)
                {
                    m_createDialog = Windows.Create<CreateMonitoredItemsDlg>();
                    m_createDialog.FormClosing += new FormClosingEventHandler(CreateDialog_FormClosing);
                }

                m_createDialog.Show(m_subscription, true);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ConditionRefreshMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription.Subscription != null)
                {
                    await m_subscription.Subscription.ConditionRefreshAsync(CancellationToken.None);
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
