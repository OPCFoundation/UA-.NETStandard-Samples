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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using System.Threading.Tasks;
using System.Threading;

namespace Quickstarts.DataAccessClient
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// The main form for a simple Data Access Client application.
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
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62548/Quickstarts/DataAccessServer";
            this.Text = m_configuration.ApplicationName;

            // the V2 engine takes the notification handler when the subscription is created,
            // so the form owns one for its whole lifetime and points it at its own methods.
            m_callbacks.DataChangeCallback = OnDataChanges;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// How long the form waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private bool m_connectedOnce;
#pragma warning disable CA2213 // Justification: disposed asynchronously by DeleteSubscriptionAsync.
        private ISubscription m_subscription;
#pragma warning restore CA2213
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();
        private readonly Dictionary<string, ListViewItem> m_rows = new Dictionary<string, ListViewItem>(StringComparer.Ordinal);
        private int m_nextItemId;
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
                await ConnectServerCTRL.ConnectAsync(m_telemetry).ConfigureAwait(false);
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
            m_rows.Clear();

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
                    m_rows.Clear();
                    MonitoredItemsLV.Items.Clear();
                    BrowseNodesTV.Nodes.Clear();
                    BrowseNodesTV.Enabled = false;
                    MonitoredItemsLV.Enabled = false;
                    return;
                }

                // set a suitable initial state.
#pragma warning disable CA1508 // Justification: Analyzer does not account for session state changes in UI callbacks.
                if (m_session != null && !m_connectedOnce)
#pragma warning restore CA1508
                {
                    m_connectedOnce = true;
                }

                // populate the browse view.
                await PopulateBranchAsync(ObjectIds.ObjectsFolder, BrowseNodesTV.Nodes);

                BrowseNodesTV.Enabled = true;
                MonitoredItemsLV.Enabled = true;
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
                BrowseNodesTV.Enabled = false;
                MonitoredItemsLV.Enabled = false;
                AttributesLV.Items.Clear();
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

                BrowseNodesTV.Enabled = true;
                MonitoredItemsLV.Enabled = true;
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

        #region Private Methods
        /// <summary>
        /// Populates the branch in the tree view.
        /// </summary>
        /// <param name="sourceId">The NodeId of the Node to browse.</param>
        /// <param name="nodes">The node collect to populate.</param>
        private async Task PopulateBranchAsync(NodeId sourceId, TreeNodeCollection nodes, CancellationToken ct = default)
        {
            try
            {
                nodes.Clear();

                // find all of the components of the node.
                BrowseDescription nodeToBrowse1 = new BrowseDescription();

                nodeToBrowse1.NodeId = sourceId;
                nodeToBrowse1.BrowseDirection = BrowseDirection.Forward;
                nodeToBrowse1.ReferenceTypeId = ReferenceTypeIds.Aggregates;
                nodeToBrowse1.IncludeSubtypes = true;
                nodeToBrowse1.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
                nodeToBrowse1.ResultMask = (uint)BrowseResultMask.All;

                // find all nodes organized by the node.
                BrowseDescription nodeToBrowse2 = new BrowseDescription();

                nodeToBrowse2.NodeId = sourceId;
                nodeToBrowse2.BrowseDirection = BrowseDirection.Forward;
                nodeToBrowse2.ReferenceTypeId = ReferenceTypeIds.Organizes;
                nodeToBrowse2.IncludeSubtypes = true;
                nodeToBrowse2.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
                nodeToBrowse2.ResultMask = (uint)BrowseResultMask.All;

                List<BrowseDescription> nodesToBrowse = new List<BrowseDescription>();
                nodesToBrowse.Add(nodeToBrowse1);
                nodesToBrowse.Add(nodeToBrowse2);

                // fetch references from the server.
                var references = await FormUtils.BrowseAsync(m_session, nodesToBrowse, false, ct);

                // process results.
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription target = references[ii];

                    // add node.
                    TreeNode child = new TreeNode(Utils.Format("{0}", target));
                    child.Tag = target;
                    child.Nodes.Add(new TreeNode());
                    nodes.Add(child);
                }

                // update the attributes display.
                await DisplayAttributesAsync(sourceId, ct);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Displays the attributes and properties in the attributes view.
        /// </summary>
        /// <param name="sourceId">The NodeId of the Node to browse.</param>
        private async Task DisplayAttributesAsync(NodeId sourceId, CancellationToken ct = default)
        {
            try
            {
                AttributesLV.Items.Clear();

                List<ReadValueId> nodesToRead = new List<ReadValueId>();

                // attempt to read all possible attributes.
                for (uint ii = Attributes.NodeClass; ii <= Attributes.UserExecutable; ii++)
                {
                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = sourceId;
                    nodeToRead.AttributeId = ii;
                    nodesToRead.Add(nodeToRead);
                }

                int startOfProperties = nodesToRead.Count;

                // find all of the pror of the node.
                BrowseDescription nodeToBrowse1 = new BrowseDescription();

                nodeToBrowse1.NodeId = sourceId;
                nodeToBrowse1.BrowseDirection = BrowseDirection.Forward;
                nodeToBrowse1.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                nodeToBrowse1.IncludeSubtypes = true;
                nodeToBrowse1.NodeClassMask = 0;
                nodeToBrowse1.ResultMask = (uint)BrowseResultMask.All;

                List<BrowseDescription> nodesToBrowse = new List<BrowseDescription>();
                nodesToBrowse.Add(nodeToBrowse1);

                // fetch property references from the server.
                var references = await FormUtils.BrowseAsync(m_session, nodesToBrowse, false, ct);

                if (references == null)
                {
                    return;
                }

                for (int ii = 0; ii < references.Count; ii++)
                {
                    // ignore external references.
                    if (references[ii].NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)references[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodesToRead.Add(nodeToRead);
                }

                // read all values.
                ReadResponse response = await m_session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct);

                var results = response.Results.ToList();
                var diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // process results.
                for (int ii = 0; ii < results.Count; ii++)
                {
                    string name = null;
                    string datatype = null;
                    string value = null;

                    // process attribute value.
                    if (ii < startOfProperties)
                    {
                        // ignore attributes which are invalid for the node.
                        if (results[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                        {
                            continue;
                        }

                        // get the name of the attribute.
                        name = Attributes.GetBrowseName(nodesToRead[ii].AttributeId);

                        // display any unexpected error.
                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            datatype = Utils.Format("{0}", Attributes.GetDataTypeId(nodesToRead[ii].AttributeId));
                            value = Utils.Format("{0}", results[ii].StatusCode);
                        }

                        // display the value.
                        else
                        {
                            TypeInfo typeInfo = results[ii].WrappedValue.TypeInfo;

                            datatype = typeInfo.BuiltInType.ToString();

                            if (typeInfo.ValueRank >= ValueRanks.OneOrMoreDimensions)
                            {
                                datatype += "[]";
                            }

                            value = results[ii].WrappedValue.ToString();
                        }
                    }

                    // process property value.
                    else
                    {
                        // ignore properties which are invalid for the node.
                        if (results[ii].StatusCode == StatusCodes.BadNodeIdUnknown)
                        {
                            continue;
                        }

                        // get the name of the property.
                        name = Utils.Format("{0}", references[ii - startOfProperties]);

                        // display any unexpected error.
                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            datatype = String.Empty;
                            value = Utils.Format("{0}", results[ii].StatusCode);
                        }

                        // display the value.
                        else
                        {
                            TypeInfo typeInfo = results[ii].WrappedValue.TypeInfo;

                            datatype = typeInfo.BuiltInType.ToString();

                            if (typeInfo.ValueRank >= ValueRanks.OneOrMoreDimensions)
                            {
                                datatype += "[]";
                            }

                            value = results[ii].WrappedValue.ToString();
                        }
                    }

                    // add the attribute name/value to the list view.
                    ListViewItem item = new ListViewItem(name);
                    item.SubItems.Add(datatype);
                    item.SubItems.Add(value);
                    AttributesLV.Items.Add(item);
                }

                // adjust width of all columns.
                for (int ii = 0; ii < AttributesLV.Columns.Count; ii++)
                {
                    AttributesLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Converts a monitoring filter to text for display.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The deadback formatted as a string.</returns>
        private string DeadbandFilterToText(MonitoringFilter filter)
        {
            DataChangeFilter datachangeFilter = filter as DataChangeFilter;

            if (datachangeFilter != null)
            {
                if (datachangeFilter.DeadbandType == (uint)DeadbandType.Absolute)
                {
                    return Utils.Format("{0:##.##}", datachangeFilter.DeadbandValue);
                }

                if (datachangeFilter.DeadbandType == (uint)DeadbandType.Percent)
                {
                    return Utils.Format("{0:##.##}%", datachangeFilter.DeadbandValue);
                }
            }

            return "None";
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the Click event of the Help_ContentsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\daclientoverview.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Fetches the children for a node the first time the node is expanded in the tree view.
        /// </summary>
        private async void BrowseNodesTV_BeforeExpandAsync(object sender, TreeViewCancelEventArgs e)
        {
            try
            {
                // check if node has already been expanded once.
                if (e.Node.Nodes.Count != 1 || !string.IsNullOrEmpty(e.Node.Nodes[0].Text))
                {
                    return;
                }

                // get the source for the node.
                ReferenceDescription reference = e.Node.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId.IsAbsolute)
                {
                    e.Cancel = true;
                    return;
                }

                // populate children.
                await PopulateBranchAsync((NodeId)reference.NodeId, e.Node.Nodes);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after a node is selected.
        /// </summary>
        private async void BrowseNodesTV_AfterSelectAsync(object sender, TreeViewEventArgs e)
        {
            try
            {
                // get the source for the node.
                ReferenceDescription reference = e.Node.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                // populate children.
                await PopulateBranchAsync((NodeId)reference.NodeId, e.Node.Nodes);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Ensures the correct node is selected before displaying the context menu.
        /// </summary>
        private void BrowseNodesTV_MouseDown(object sender, MouseEventArgs e)
        {
            try
            {
                BrowseNodesTV.SelectedNode = BrowseNodesTV.GetNodeAt(e.X, e.Y);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Browse_MonitorMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Browse_MonitorMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || BrowseNodesTV.SelectedNode == null)
                {
                    return;
                }

                // can only subscribe to local variables.
                ReferenceDescription reference = (ReferenceDescription)BrowseNodesTV.SelectedNode.Tag;

                if (reference.NodeId.IsAbsolute || reference.NodeClass != NodeClass.Variable)
                {
                    return;
                }

                ListViewItem item = await CreateMonitoredItemAsync((NodeId)reference.NodeId, Utils.Format("{0}", reference));

                // the V2 engine has no ApplyChanges: adding the item to the collection is the
                // request and the engine applies it on its own worker, so the form waits for
                // that worker before it shows the revised values.
                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout);

                UpdateRevisedValues(item, (MonitoredItemHandle)item.Tag);

                MonitoredItemsLV.Columns[0].Width = -2;
                MonitoredItemsLV.Columns[1].Width = -2;
                MonitoredItemsLV.Columns[8].Width = -2;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Creates the monitored item.
        /// </summary>
        private Task<ListViewItem> CreateMonitoredItemAsync(NodeId nodeId, string displayName, CancellationToken ct = default)
        {
            if (m_subscription == null)
            {
                // the V2 engine takes the settings through an options monitor, and
                // reconfiguring that monitor is what modifies the subscription later on.
                var options = new OptionsMonitor<SubscriptionOptions>(
                    ClientUtils.DefaultSubscriptionOptions with { Priority = 100 });

                m_subscription = ClientUtils.AddSubscription(m_session, m_callbacks, options);
            }

            // the item is identified by a name which is unique within the subscription. Its
            // settings live in the handle, because the engine only reports revised values.
            var handle = new MonitoredItemHandle(
                Utils.Format("Item{0}", ++m_nextItemId),
                new MonitoredItemOptions {
                    StartNodeId = nodeId,
                    AttributeId = Attributes.Value,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = TimeSpan.FromMilliseconds(1000),
                    QueueSize = 0,
                    DiscardOldest = true,
                }) {
                DisplayName = displayName,
            };

            // add the attribute name/value to the list view.
            ListViewItem item = new ListViewItem(String.Empty);

            item.SubItems.Add(handle.DisplayName);
            item.SubItems.Add(handle.Settings.MonitoringMode.ToString());
            item.SubItems.Add(handle.Settings.SamplingInterval.TotalMilliseconds.ToString(CultureInfo.CurrentCulture));
            item.SubItems.Add(DeadbandFilterToText(handle.Settings.Filter));
            item.SubItems.Add(String.Empty);
            item.SubItems.Add(String.Empty);
            item.SubItems.Add(String.Empty);
            item.SubItems.Add(String.Empty);

            item.Tag = handle;
            MonitoredItemsLV.Items.Add(item);
            m_rows[handle.Name] = item;

            // adding the item to the collection is the request; the engine applies it.
            m_subscription.MonitoredItems.TryAdd(handle.Name, handle.Options, out IMonitoredItem monitoredItem);
            handle.Item = monitoredItem;

            return Task.FromResult(item);
        }

        /// <summary>
        /// Shows the values the engine reports for an item after it applied the changes.
        /// </summary>
        private void UpdateRevisedValues(ListViewItem item, MonitoredItemHandle handle)
        {
            IMonitoredItem monitoredItem = handle.Item;

            if (monitoredItem == null)
            {
                return;
            }

            item.SubItems[0].Text = monitoredItem.ClientHandle.ToString(CultureInfo.CurrentCulture);
            item.SubItems[2].Text = monitoredItem.CurrentMonitoringMode.ToString();
            item.SubItems[3].Text = monitoredItem.CurrentSamplingInterval.TotalMilliseconds.ToString(CultureInfo.CurrentCulture);

            // the engine reports no revised filter, only whether the server accepted the one
            // which was requested, so the requested filter is what the list shows.
            item.SubItems[4].Text = DeadbandFilterToText(handle.Settings.Filter);

            item.SubItems[8].Text = ServiceResult.IsBad(monitoredItem.Error)
                ? monitoredItem.Error.StatusCode.ToString()
                : String.Empty;
        }

        /// <summary>
        /// Prompts the use to write the value of a varible.
        /// </summary>
        private async void Browse_WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || BrowseNodesTV.SelectedNode == null)
                {
                    return;
                }

                // can only subscribe to local variables.
                ReferenceDescription reference = (ReferenceDescription)BrowseNodesTV.SelectedNode.Tag;

                if (reference.NodeId.IsAbsolute || reference.NodeClass != NodeClass.Variable)
                {
                    return;
                }

                using (var dialog = new WriteValueDlg())
                {
                    await dialog.ShowDialogAsync(m_session, (NodeId)reference.NodeId, Attributes.Value);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Browse_ReadHistoryMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Browse_ReadHistoryMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || BrowseNodesTV.SelectedNode == null)
                {
                    return;
                }

                // can only subscribe to local variables.
                ReferenceDescription reference = (ReferenceDescription)BrowseNodesTV.SelectedNode.Tag;

                if (reference.NodeId.IsAbsolute || reference.NodeClass != NodeClass.Variable)
                {
                    return;
                }

                using (var dialog = new ReadHistoryDlg())
                {
                    await dialog.ShowDialogAsync(m_session, (NodeId)reference.NodeId);
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

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(
                    () => OnDataChanges(subscription, sequenceNumber, publishTime, notifications, publishState)));
                return;
            }

            try
            {
                if (m_session == null)
                {
                    return;
                }

                foreach (DataValueChange change in notifications)
                {
                    if (change.MonitoredItem == null || !m_rows.TryGetValue(change.MonitoredItem.Name, out ListViewItem item))
                    {
                        continue;
                    }

                    item.SubItems[5].Text = Utils.Format("{0}", change.Value.WrappedValue);
                    item.SubItems[6].Text = Utils.Format("{0}", change.Value.StatusCode);
                    item.SubItems[7].Text = Utils.Format("{0:HH:mm:ss.fff}", change.Value.SourceTimestamp.ToLocalTime());
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Returns the handles of the currently selected monitored items.
        /// </summary>
        private List<MonitoredItemHandle> GetSelectedHandles()
        {
            var handles = new List<MonitoredItemHandle>();

            for (int ii = 0; ii < MonitoredItemsLV.SelectedItems.Count; ii++)
            {
                if (MonitoredItemsLV.SelectedItems[ii].Tag is MonitoredItemHandle handle)
                {
                    handles.Add(handle);
                }
            }

            return handles;
        }

        /// <summary>
        /// Changes the monitoring mode for the currently selected monitored items.
        /// </summary>
        private async void Monitoring_MonitoringMode_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || m_subscription == null || MonitoredItemsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                // determine the monitoring mode being requested.
                MonitoringMode monitoringMode = MonitoringMode.Disabled;

                if (sender == Monitoring_MonitoringMode_ReportingMI)
                {
                    monitoringMode = MonitoringMode.Reporting;
                }

                if (sender == Monitoring_MonitoringMode_SamplingMI)
                {
                    monitoringMode = MonitoringMode.Sampling;
                }

                // reconfiguring the options of an item is what modifies it; the engine picks
                // the change up on its own worker.
                List<MonitoredItemHandle> itemsToChange = GetSelectedHandles();

                foreach (MonitoredItemHandle handle in itemsToChange)
                {
                    handle.Configure(options => options with { MonitoringMode = monitoringMode });
                }

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout);

                // update the display.
                foreach (MonitoredItemHandle handle in itemsToChange)
                {
                    if (m_rows.TryGetValue(handle.Name, out ListViewItem item))
                    {
                        UpdateRevisedValues(item, handle);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Changes the sampling interval for the currently selected monitored items.
        /// </summary>
        private async void Monitoring_SamplingInterval_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || m_subscription == null || MonitoredItemsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                // determine the sampling interval being requested.
                double samplingInterval = 0;

                if (sender == Monitoring_SamplingInterval_1000MI)
                {
                    samplingInterval = 1000;
                }
                else if (sender == Monitoring_SamplingInterval_2500MI)
                {
                    samplingInterval = 2500;
                }
                else if (sender == Monitoring_SamplingInterval_5000MI)
                {
                    samplingInterval = 5000;
                }

                // update the sampling interval.
                List<MonitoredItemHandle> itemsToChange = GetSelectedHandles();

                foreach (MonitoredItemHandle handle in itemsToChange)
                {
                    handle.Configure(options => options with {
                        SamplingInterval = TimeSpan.FromMilliseconds(samplingInterval),
                    });
                }

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout);

                // update the display.
                foreach (MonitoredItemHandle handle in itemsToChange)
                {
                    if (m_rows.TryGetValue(handle.Name, out ListViewItem item))
                    {
                        UpdateRevisedValues(item, handle);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Changes the deadband for the currently selected monitored items.
        /// </summary>
        private async void Monitoring_Deadband_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || m_subscription == null || MonitoredItemsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                // determine the filter being requested.
                DataChangeFilter filter = new DataChangeFilter();
                filter.Trigger = DataChangeTrigger.StatusValue;

                if (sender == Monitoring_Deadband_Absolute_5MI)
                {
                    filter.DeadbandType = (uint)DeadbandType.Absolute;
                    filter.DeadbandValue = 5.0;
                }
                else if (sender == Monitoring_Deadband_Absolute_10MI)
                {
                    filter.DeadbandType = (uint)DeadbandType.Absolute;
                    filter.DeadbandValue = 10.0;
                }
                else if (sender == Monitoring_Deadband_Absolute_25MI)
                {
                    filter.DeadbandType = (uint)DeadbandType.Absolute;
                    filter.DeadbandValue = 25.0;
                }
                else if (sender == Monitoring_Deadband_Percentage_1MI)
                {
                    filter.DeadbandType = (uint)DeadbandType.Percent;
                    filter.DeadbandValue = 1.0;
                }
                else if (sender == Monitoring_Deadband_Percentage_5MI)
                {
                    filter.DeadbandType = (uint)DeadbandType.Percent;
                    filter.DeadbandValue = 5.0;
                }
                else if (sender == Monitoring_Deadband_Percentage_10MI)
                {
                    filter.DeadbandType = (uint)DeadbandType.Percent;
                    filter.DeadbandValue = 10.0;
                }
                else
                {
                    filter = null;
                }

                // update the deadband.
                List<MonitoredItemHandle> itemsToChange = GetSelectedHandles();

                foreach (MonitoredItemHandle handle in itemsToChange)
                {
                    handle.Configure(options => options with { Filter = filter });
                }

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout);

                // update the display, and drop a filter the server would not take.
                foreach (MonitoredItemHandle handle in itemsToChange)
                {
                    if (handle.Item != null && ServiceResult.IsBad(handle.Item.Error))
                    {
                        handle.Configure(options => options with { Filter = null });
                    }

                    if (m_rows.TryGetValue(handle.Name, out ListViewItem item))
                    {
                        UpdateRevisedValues(item, handle);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Monitoring_DeleteMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Monitoring_DeleteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (MonitoredItemsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                // collect the items to delete.
                List<ListViewItem> itemsToDelete = new List<ListViewItem>();

                for (int ii = 0; ii < MonitoredItemsLV.SelectedItems.Count; ii++)
                {
                    if (MonitoredItemsLV.SelectedItems[ii].Tag is MonitoredItemHandle handle)
                    {
                        itemsToDelete.Add(MonitoredItemsLV.SelectedItems[ii]);

                        // removing the item from the collection is the delete request.
                        if (m_subscription != null && handle.Item != null)
                        {
                            m_subscription.MonitoredItems.TryRemove(handle.Item.ClientHandle);
                        }

                        m_rows.Remove(handle.Name);
                    }
                }

                // update the server.
                if (m_subscription != null)
                {
                    await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout);
                }

                // remove the items.
                for (int ii = 0; ii < itemsToDelete.Count; ii++)
                {
                    itemsToDelete[ii].Remove();
                }

                MonitoredItemsLV.Columns[0].Width = -2;
                MonitoredItemsLV.Columns[1].Width = -2;
                MonitoredItemsLV.Columns[8].Width = -2;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void BrowsingMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Browse_MonitorMI.Enabled = true;
            Browse_ReadHistoryMI.Enabled = true;
            Browse_WriteMI.Enabled = true;

            if (m_session == null || BrowseNodesTV.SelectedNode == null)
            {
                Browse_MonitorMI.Enabled = false;
                Browse_ReadHistoryMI.Enabled = false;
                Browse_WriteMI.Enabled = false;
                return;
            }

            ReferenceDescription reference = (ReferenceDescription)BrowseNodesTV.SelectedNode.Tag;

            if (reference.NodeId.IsAbsolute || reference.NodeClass != NodeClass.Variable)
            {
                Browse_MonitorMI.Enabled = false;
                Browse_ReadHistoryMI.Enabled = false;
                Browse_WriteMI.Enabled = false;
                return;
            }
        }

        private async void Monitoring_WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // check if operation is currently allowed.
                if (m_session == null || m_subscription == null || MonitoredItemsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                if (MonitoredItemsLV.SelectedItems[0].Tag is MonitoredItemHandle handle)
                {
                    using (var dialog = new WriteValueDlg())
                    {
                        await dialog.ShowDialogAsync(m_session, handle.Settings.StartNodeId, Attributes.Value);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Creates monitored items from a saved list of node ids.
        /// </summary>
        private void File_LoadMI_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Saves the current monitored items.
        /// </summary>
        private void File_SaveMI_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Sets the locale to use.
        /// </summary>
        private async void Server_SetLocaleMI_ClickAsync(object sender, EventArgs e)
        {

            try
            {
                if (m_session == null)
                {
                    return;
                }

                string locale;
                using (var dialog = new SelectLocaleDlg())
                {
                    locale = await dialog.ShowDialogAsync(m_session);
                }

                if (locale == null)
                {
                    return;
                }

                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                await m_session.ChangePreferredLocalesAsync(new List<string>(ConnectServerCTRL.PreferredLocales));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void Server_SetUserMI_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit the application?", "UA Sample Client", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }
    }
}
