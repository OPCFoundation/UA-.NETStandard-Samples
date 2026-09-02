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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using System.Threading.Tasks;
using System.Threading;

namespace Quickstarts.Boiler.Client
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

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62567/Quickstarts/BoilerServer";
            this.Text = m_configuration.ApplicationName;
            m_telemetry = telemetry;

            // the V2 engine takes the notification handler when the subscription is created,
            // so the form owns one for its whole lifetime and points it at its own methods.
            m_callbacks.DataChangeCallback = OnDataChanges;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed asynchronously by DeleteSubscriptionAsync.")]
        private ISubscription m_subscription;
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        /// <summary>
        /// The control which displays each monitored item, by the name of the item.
        /// </summary>
        /// <remarks>
        /// The V2 engine identifies an item by a name which is unique within its subscription
        /// and reports that name with every notification, which replaces the Handle the
        /// classic monitored item carried.
        /// </remarks>
        private readonly Dictionary<string, Control> m_displays = new Dictionary<string, Control>(StringComparer.Ordinal);
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
                m_session = null;
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
            m_displays.Clear();

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
                    m_displays.Clear();
                    BoilerCB.Enabled = false;
                    return;
                }

                // set a suitable initial state.
                if (!m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                BoilerCB.Enabled = true;

                // update list of boilers
                await GetBoilersAsync();
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
                BoilerCB.Enabled = false;
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

                BoilerCB.Enabled = true;
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
            ClientUtils.WaitForTeardown(DeleteSubscriptionAsync);
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the boilers.
        /// </summary>
        private async Task GetBoilersAsync(CancellationToken ct = default)
        {
            BoilerCB.Items.Clear();

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = Opc.Ua.ObjectIds.ObjectsFolder;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object);
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.All);

            List<ReferenceDescription> references = await ClientUtils.BrowseAsync(
                m_session,
                nodeToBrowse,
                false,
                ct);

            if (references != null)
            {
                NodeId boilerTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.BoilerType, m_session.NamespaceUris);

                for (int ii = 0; ii < references.Count; ii++)
                {
                    if (boilerTypeId == references[ii].TypeDefinition)
                    {
                        BoilerCB.Items.Add(references[ii]);
                    }
                }

                if (BoilerCB.Items.Count > 0)
                {
                    BoilerCB.SelectedIndex = 0;
                }
            }
        }
        #endregion

        #region Event Handlers
        private async void BoilerCB_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                // the previous boiler is dropped with its subscription: disposing it deletes
                // the subscription on the server and removes it from the manager.
                await DeleteSubscriptionAsync();

                ReferenceDescription boiler = (ReferenceDescription)BoilerCB.SelectedItem;

                if (boiler == null)
                {
                    return;
                }

                // the V2 engine takes the settings through an options monitor and creates the
                // subscription on the server on its own worker.
                var options = new OptionsMonitor<SubscriptionOptions>(
                    ClientUtils.DefaultSubscriptionOptions with { Priority = 1, LifetimeCount = 20 });

                m_subscription = ClientUtils.AddSubscription(m_session, m_callbacks, options);

                NamespaceTable wellKnownNamespaceUris = new NamespaceTable();
                wellKnownNamespaceUris.Append(Namespaces.Boiler);

                string[] browsePaths = new string[]
                {
                    "1:PipeX001/1:FTX001/1:Output",
                    "1:DrumX001/1:LIX001/1:Output",
                    "1:PipeX002/1:FTX002/1:Output",
                    "1:LCX001/1:SetPoint",
                };

                List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                    m_session,
                    (NodeId)boiler.NodeId,
                    wellKnownNamespaceUris,
                    default,
                    browsePaths);

                Control[] controls = new Control[]
                {
                    InputPipeFlowTB,
                    DrumLevelTB,
                    OutputPipeFlowTB,
                    DrumLevelSetPointTB
                };

                for (int ii = 0; ii < nodes.Count; ii++)
                {
                    controls[ii].Text = "---";

                    if (!nodes[ii].IsNull)
                    {
                        // adding the item to the collection is the create request: the engine
                        // applies it on its own worker, there is no ApplyChanges to call.
                        string name = browsePaths[ii];

                        m_displays[name] = controls[ii];

                        m_subscription.MonitoredItems.TryAdd(
                            name,
                            new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                                StartNodeId = nodes[ii],
                                AttributeId = Attributes.Value,
                            }),
                            out IMonitoredItem _);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display with the new values for the monitored variables.
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
                    if (change.MonitoredItem == null || !m_displays.TryGetValue(change.MonitoredItem.Name, out Control control))
                    {
                        continue;
                    }

                    control.Text = change.Value.WrappedValue.ToString();
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
