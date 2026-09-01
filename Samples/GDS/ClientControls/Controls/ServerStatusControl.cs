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
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Gds.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in
    // Opc.Ua.Client, so the two option records are aliased instead of imported.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    public partial class ServerStatusControl : UserControl
    {
        /// <summary>
        /// The name the status item carries inside the subscription. A V2 monitored item is
        /// addressed by name rather than by the object the caller holds.
        /// </summary>
        private const string kServerStatusItemName = "ServerStatus";

        /// <summary>
        /// How often the server is asked to sample its own status.
        /// </summary>
        private static readonly TimeSpan kStatusSamplingInterval = TimeSpan.FromSeconds(1);

        public ServerStatusControl()
        {
            InitializeComponent();
            m_callbacks.DataChangeCallback = OnServerStatusChange;
        }

        private ServerPushConfigurationClient m_server;
        private ITelemetryContext m_telemetry;
        private ISubscription m_subscription;
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        /// <summary>
        /// Shows the address space and the live status of the server the push client is
        /// connected to, or clears the panel when there is no connection.
        /// </summary>
        /// <remarks>
        /// The status used to arrive through <c>ServerPushConfigurationClient.ServerStatusChanged</c>,
        /// which the SDK declares but never raises (OPCFoundation/UA-.NETStandard#4346), so the
        /// fields stayed at their placeholders for the whole session. The control now monitors the
        /// ServerStatus variable itself, through the V2 subscription engine of the session the
        /// push client opened.
        /// </remarks>
        public async Task InitializeAsync(ServerPushConfigurationClient server, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            await StopMonitoringAsync();

            m_server = server;
            m_telemetry = telemetry;

            // the push client hands out an ISession, and a ManagedSession is not a Session:
            // a downcast here compiles and silently leaves the browse control without a session.
            ISession session = server?.Session;

            SetServerStatus(null);

            await ServerBrowseControl.InitializeAsync(session, Opc.Ua.ObjectIds.ObjectsFolder, telemetry, ct, ReferenceTypeIds.HierarchicalReferences);

            if (session != null)
            {
                StartMonitoring(session);
            }
        }

        public void SetServerStatus(ServerStatusDataType status)
        {
            ProductNameTextBox.Text = "---";
            ProductUriTextBox.Text = "---";
            ManufacturerNameTextBox.Text = "---";
            SoftwareVersionTextBox.Text = "---";
            BuildNumberTextBox.Text = "---";
            BuildDateTextBox.Text = "---";
            StartTimeTextBox.Text = "---";
            CurrentTimeTextBox.Text = "---";
            StateTextBox.Text = "---";
            SecondsUntilShutdownTextBox.Text = "---";
            ShutdownReasonTextBox.Text = "---";

            if (status != null)
            {
                if (status.BuildInfo != null)
                {
                    ProductNameTextBox.Text = status.BuildInfo.ProductName;
                    ProductUriTextBox.Text = status.BuildInfo.ProductUri;
                    ManufacturerNameTextBox.Text = status.BuildInfo.ManufacturerName;
                    SoftwareVersionTextBox.Text = status.BuildInfo.SoftwareVersion;
                    BuildNumberTextBox.Text = status.BuildInfo.BuildNumber;
                    BuildDateTextBox.Text = status.BuildInfo.BuildDate.ToLocalTime().ToString("yyyy-MM-dd");
                }

                StartTimeTextBox.Text = status.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                CurrentTimeTextBox.Text = status.CurrentTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                SecondsUntilShutdownTextBox.Text = (status.SecondsTillShutdown > 0) ? status.SecondsTillShutdown.ToString() : "";
                ShutdownReasonTextBox.Text = (status.SecondsTillShutdown > 0) ? String.Format("{0}", status.ShutdownReason) : "";
                StateTextBox.Text = status.State.ToString();
            }
        }

        /// <summary>
        /// Subscribes to the ServerStatus variable of the connected server.
        /// </summary>
        private void StartMonitoring(ISession session)
        {
            m_subscription = ClientUtils.AddSubscription(
                session,
                m_callbacks,
                new OptionsMonitor<SubscriptionOptions>(ClientUtils.DefaultSubscriptionOptions));

            // the engine applies the item on its own worker; there is no ApplyChanges to call.
            _ = m_subscription.MonitoredItems.TryAdd(
                kServerStatusItemName,
                new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                    StartNodeId = Opc.Ua.VariableIds.Server_ServerStatus,
                    AttributeId = Attributes.Value,
                    SamplingInterval = kStatusSamplingInterval,
                    QueueSize = 1,
                }),
                out _);
        }

        /// <summary>
        /// Deletes the status subscription on the server and drops it from the subscription
        /// manager of the session it was created on.
        /// </summary>
        private async Task StopMonitoringAsync()
        {
            ISubscription subscription = m_subscription;
            m_subscription = null;

            if (subscription == null)
            {
                return;
            }

            try
            {
                await subscription.DisposeAsync();
            }
            catch
            {
                // the session it belonged to may already be gone, which is the normal case
                // when the panel is cleared because the connection dropped.
            }
        }

        /// <summary>
        /// Drops the status subscription without waiting for the delete, for the disposal
        /// path, which has nothing left to await on.
        /// </summary>
        private void StopMonitoring()
        {
            _ = StopMonitoringAsync();
        }

        /// <summary>
        /// Takes the status out of a notification of the V2 engine and shows it.
        /// </summary>
        /// <remarks>
        /// This runs on a publish worker rather than on the UI thread, so it marshals itself.
        /// </remarks>
        private void OnServerStatusChange(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] changes,
            PublishState publishStateMask)
        {
            // a notification of a subscription the control has already dropped, which is what
            // is still in flight while the panel is being torn down.
            if (!Object.ReferenceEquals(subscription, m_subscription))
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(
                        new Action<ISubscription, uint, DateTime, DataValueChange[], PublishState>(OnServerStatusChange),
                        subscription, sequenceNumber, publishTime, changes, publishStateMask);
                }
                catch (InvalidOperationException)
                {
                    // the window the control marshals through went away between the check and
                    // the post. Throwing here would leave the publish worker with nowhere to
                    // report it.
                }

                return;
            }

            foreach (DataValueChange change in changes)
            {
                SetServerStatus(change.Value.GetValue<ServerStatusDataType>(null));
            }
        }

        private async void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            if (m_server == null)
            {
                return;
            }

            try
            {
                await m_server.ApplyChangesAsync();
            }
            catch (Exception exception)
            {
                var se = exception as ServiceResultException;

                if (se == null || se.StatusCode != StatusCodes.BadServerHalted)
                {
                    Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Parent.Text, exception);
                }
            }

            try
            {
                await m_server.DisconnectAsync();
            }
            catch
            {
                // ignore.
            }
        }

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.CornflowerBlue;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.MidnightBlue;
        }
    }
}
