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
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;

namespace Quickstarts.MethodsClient
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62557/Quickstarts/MethodsServer";
            this.Text = m_configuration.ApplicationName;

            // the V2 engine takes the notification handler when the subscription is created,
            // so the form owns one for its whole lifetime and points it at its own methods.
            m_callbacks.DataChangeCallback = OnDataChanges;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The name which identifies the state item within its subscription.
        /// </summary>
        private const string kStateItemName = "State";

        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed asynchronously by DeleteSubscriptionAsync.")]
        private ISubscription m_subscription;
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();
        private NodeId m_objectNode;
        private NodeId m_methodNode;
        private bool m_connectedOnce;
        private readonly ITelemetryContext m_telemetry;
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await DeleteSubscriptionAsync();
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Deletes the subscription on the server and drops it from the subscription manager.
        /// </summary>
        /// <remarks>
        /// Done before the session is closed: closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async Task DeleteSubscriptionAsync()
        {
            ISubscription subscription = m_subscription;

            m_subscription = null;

            if (subscription != null)
            {
                await subscription.DisposeAsync();
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    m_subscription = null;
                    StartBTN.Enabled = false;
                    return;
                }

                // set a suitable initial state.
                if (!m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                // this client has built-in knowledge of the information model used by the server.
                NamespaceTable wellKnownNamespaceUris = new NamespaceTable();
                wellKnownNamespaceUris.Append(Namespaces.Methods);

                string[] browsePaths = new string[]
                {
                    "1:My Process/1:State",
                    "1:My Process",
                    "1:My Process/1:Start"
                };

                List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                    m_session,
                    ObjectIds.ObjectsFolder,
                    wellKnownNamespaceUris,
                    default,
                    browsePaths);

                // subscribe to the state if available.
                if (nodes.Count > 0 && !(nodes[0]).IsNull)
                {
                    await DeleteSubscriptionAsync();

                    // the V2 engine takes the settings through an options monitor and creates
                    // the subscription on the server on its own worker.
                    var options = new OptionsMonitor<SubscriptionOptions>(
                        ClientUtils.DefaultSubscriptionOptions with { Priority = 1, LifetimeCount = 20 });

                    m_subscription = ClientUtils.AddSubscription(m_session, m_callbacks, options);

                    // adding the item to the collection is the create request: the engine
                    // applies it on its own worker, there is no ApplyChanges to call.
                    m_subscription.MonitoredItems.TryAdd(
                        kStateItemName,
                        new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                            StartNodeId = nodes[0],
                            AttributeId = Attributes.Value,
                        }),
                        out IMonitoredItem _);
                }

                // save the object/method
                if (nodes.Count > 2)
                {
                    m_objectNode = nodes[1];
                    m_methodNode = nodes[2];
                }

                InitialStateTB.Text = "1";
                FinalStateTB.Text = "100";
                StartBTN.Enabled = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                StartBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                // a V2 subscription belongs to the subscription manager of the session and
                // survives the reconnect together with its monitored items, so there is
                // nothing to re-attach here.
                m_session = ConnectServerCTRL.Session;

                StartBTN.Enabled = true;
                StartBTN_ClickAsync(this, null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            DeleteSubscriptionAsync().GetAwaiter().GetResult();
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Event Handlers
        private async void StartBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                uint initialState = Convert.ToUInt32(InitialStateTB.Text);
                uint finalState = Convert.ToUInt32(FinalStateTB.Text);

                RevisedInitialStateTB.Text = String.Empty;
                RevisedFinalStateTB.Text = String.Empty;

                ArrayOf<Variant> outputArguments = await m_session.CallAsync(
                    m_objectNode,
                    m_methodNode,
                    default,
                    initialState,
                    finalState);

                if (outputArguments != null && outputArguments.Count > 1)
                {
                    RevisedInitialStateTB.Text = outputArguments[0].ToString();
                    RevisedFinalStateTB.Text = outputArguments[1].ToString();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display with the new value of the state variable.
        /// </summary>
        /// <remarks>
        /// The V2 engine calls this on a publish worker instead of on the UI thread, and it
        /// reports the whole notification instead of one value per item.
        /// </remarks>
        private void OnDataChanges(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] notifications,
            PublishState publishState)
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(
                    () => OnDataChanges(subscription, sequenceNumber, publishTime, notifications, publishState)));
                return;
            }

            try
            {
                foreach (DataValueChange change in notifications)
                {
                    if (change.MonitoredItem?.Name == kStateItemName)
                    {
                        CurrentStateTB.Text = change.Value.WrappedValue.ToString();
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
