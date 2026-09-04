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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.DataAccessClient.Model;

namespace Quickstarts.DataAccessClient
{
    /// <summary>
    /// The main form for a simple Data Access Client application.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="DataAccessClientModel"/>, which browses, reads, writes and monitors. The
    /// window fills the tree and the lists from what the model returns, tells it which
    /// nodes the user picked, and writes the values the model reports into the rows.
    /// </remarks>
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
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="model">The client model of the sample, from the container.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry, DataAccessClientModel model)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62548/Quickstarts/DataAccessServer";
            this.Text = configuration.ApplicationName;

            // created by the container while this constructor runs, so on the thread of
            // the window: that is the context the model captures for its events, and it is
            // why the handlers below can touch the controls directly
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_model.ValueChanged += Model_ValueChanged;
            m_model.Error += Model_Error;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly DataAccessClientModel m_model;

        /// <summary>
        /// The row of the monitored item list which shows each item, by the name the
        /// model reports the item under.
        /// </summary>
        private readonly Dictionary<string, ListViewItem> m_rows = new Dictionary<string, ListViewItem>(StringComparer.Ordinal);
        #endregion

        #region Overrides
        /// <summary>
        /// Releases the resources of the window, and with them the model it owns.
        /// </summary>
        /// <remarks>
        /// This is hand written and therefore lives here rather than in the designer
        /// partial: the model is disposed with the window. The synchronous Dispose of
        /// the model runs its detach on a thread pool thread and waits for it, which is
        /// what a Dispose that cannot await needs. The closing handler has normally
        /// detached already by the time this runs, and a second detach returns at once.
        /// </remarks>
        /// <param name="disposing">True if managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                m_model?.Dispose();
            }

            base.Dispose(disposing);
        }
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
        /// <remarks>
        /// The model is detached first: it deletes its subscription before the control
        /// closes the session, because closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.DetachAsync();
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
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
                ISession session = ConnectServerCTRL.Session;

                if (session == null)
                {
                    await m_model.DetachAsync();
                    m_rows.Clear();
                    MonitoredItemsLV.Items.Clear();
                    BrowseNodesTV.Nodes.Clear();
                    BrowseNodesTV.Enabled = false;
                    MonitoredItemsLV.Enabled = false;
                    return;
                }

                await m_model.AttachAsync(session);

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
                m_model.NotifyReconnectStarting();
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
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                // a V2 subscription belongs to the subscription manager of the session and
                // survives the reconnect together with its monitored items, so the model
                // has nothing to re-create.
                await m_model.NotifyReconnectCompletedAsync();
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
        /// <remarks>
        /// FormClosing cannot await, so the model is detached on a thread pool thread and
        /// waited for; only then does the control close the session.
        /// </remarks>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClientUtils.WaitForTeardown(m_model.DetachAsync);
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

                IReadOnlyList<BrowseNode> children = await m_model.BrowseChildrenAsync(sourceId, ct);

                foreach (BrowseNode child in children)
                {
                    // the placeholder child is what gives the node its expand button; it is
                    // replaced by the real children the first time the node is expanded.
                    var node = new TreeNode(child.Text) { Tag = child };
                    node.Nodes.Add(new TreeNode());
                    nodes.Add(node);
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

                IReadOnlyList<AttributeRow> rows = await m_model.ReadAttributesAsync(sourceId, ct);

                foreach (AttributeRow row in rows)
                {
                    var item = new ListViewItem(row.Name);
                    item.SubItems.Add(row.DataType);
                    item.SubItems.Add(row.Value);
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
        /// The node the selected tree node stands for, or null when nothing local is selected.
        /// </summary>
        private BrowseNode SelectedBrowseNode()
        {
            return BrowseNodesTV.SelectedNode?.Tag as BrowseNode;
        }

        /// <summary>
        /// Returns the names of the currently selected monitored items.
        /// </summary>
        private List<string> GetSelectedNames()
        {
            var names = new List<string>();

            foreach (ListViewItem item in MonitoredItemsLV.SelectedItems)
            {
                if (item.Tag is MonitoredItemRow row)
                {
                    names.Add(row.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// Adds a row for a monitored item the model created.
        /// </summary>
        private void AddRow(MonitoredItemRow row)
        {
            var item = new ListViewItem(string.Empty);

            for (int ii = 1; ii < MonitoredItemsLV.Columns.Count; ii++)
            {
                item.SubItems.Add(string.Empty);
            }

            MonitoredItemsLV.Items.Add(item);
            m_rows[row.Name] = item;

            UpdateRow(item, row);
        }

        /// <summary>
        /// Shows the settings and the last value the model reports for an item.
        /// </summary>
        private static void UpdateRow(ListViewItem item, MonitoredItemRow row)
        {
            item.Tag = row;
            item.SubItems[0].Text = row.ClientHandle?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            item.SubItems[1].Text = row.DisplayName;
            item.SubItems[2].Text = row.MonitoringMode.ToString();
            item.SubItems[3].Text = row.SamplingIntervalMs.ToString(CultureInfo.CurrentCulture);
            item.SubItems[4].Text = row.DeadbandText;
            item.SubItems[8].Text = row.Error;

            if (row.Value is DataValue value)
            {
                UpdateValue(item, value);
            }
        }

        /// <summary>
        /// Shows a value of an item.
        /// </summary>
        private static void UpdateValue(ListViewItem item, DataValue value)
        {
            item.SubItems[5].Text = Utils.Format("{0}", value.WrappedValue);
            item.SubItems[6].Text = Utils.Format("{0}", value.StatusCode);
            item.SubItems[7].Text = Utils.Format("{0:HH:mm:ss.fff}", value.SourceTimestamp.ToLocalTime());
        }

        /// <summary>
        /// Shows the revised settings of items after the model changed them.
        /// </summary>
        private void UpdateRows(IReadOnlyList<MonitoredItemRow> rows)
        {
            foreach (MonitoredItemRow row in rows)
            {
                if (m_rows.TryGetValue(row.Name, out ListViewItem item))
                {
                    UpdateRow(item, row);
                }
            }
        }

        /// <summary>
        /// Adjusts the columns of the monitored item list whose width depends on the rows.
        /// </summary>
        private void AdjustMonitoredItemColumns()
        {
            MonitoredItemsLV.Columns[0].Width = -2;
            MonitoredItemsLV.Columns[1].Width = -2;
            MonitoredItemsLV.Columns[8].Width = -2;
        }

        /// <summary>
        /// Creates the monitored item and adds its row to the list.
        /// </summary>
        private async Task CreateMonitoredItemAsync(NodeId nodeId, string displayName, CancellationToken ct = default)
        {
            MonitoredItemRow row = await m_model.MonitorAsync(nodeId, displayName, ct);
            AddRow(row);
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
                if (e.Node.Tag is not BrowseNode node || !node.IsLocal)
                {
                    e.Cancel = true;
                    return;
                }

                // populate children.
                await PopulateBranchAsync(node.NodeId, e.Node.Nodes);
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
                if (e.Node.Tag is not BrowseNode node || !node.IsLocal)
                {
                    return;
                }

                // populate children.
                await PopulateBranchAsync(node.NodeId, e.Node.Nodes);
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
                // can only subscribe to local variables.
                BrowseNode node = SelectedBrowseNode();

                if (!m_model.IsConnected || node == null || !node.IsLocalVariable)
                {
                    return;
                }

                await CreateMonitoredItemAsync(node.NodeId, node.Text);

                AdjustMonitoredItemColumns();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the use to write the value of a varible.
        /// </summary>
        private async void Browse_WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // can only write local variables.
                BrowseNode node = SelectedBrowseNode();

                if (!m_model.IsConnected || node == null || !node.IsLocalVariable)
                {
                    return;
                }

                await WriteValueAsync(node.NodeId);
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
                // can only read the history of local variables.
                BrowseNode node = SelectedBrowseNode();

                if (!m_model.IsConnected || node == null || !node.IsLocalVariable)
                {
                    return;
                }

                using (var dialog = new ReadHistoryDlg())
                {
                    await dialog.ShowDialogAsync(m_model, node.NodeId, node.Text);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display with the new value for a monitored variable.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window, so the row is written
        /// directly. A value can still arrive after the window was closed.
        /// </remarks>
        private void Model_ValueChanged(object sender, MonitoredItemValueChangedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (m_rows.TryGetValue(e.Name, out ListViewItem item))
            {
                UpdateValue(item, e.Value);
            }
        }

        /// <summary>
        /// Reports a failure on a background path of the model.
        /// </summary>
        private void Model_Error(object sender, ModelErrorEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ClientUtils.HandleException(m_telemetry, this.Text, e.Exception);
        }

        /// <summary>
        /// Changes the monitoring mode for the currently selected monitored items.
        /// </summary>
        private async void Monitoring_MonitoringMode_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<string> names = GetSelectedNames();

                if (!m_model.IsConnected || names.Count == 0)
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

                UpdateRows(await m_model.SetMonitoringModeAsync(names, monitoringMode));
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
                List<string> names = GetSelectedNames();

                if (!m_model.IsConnected || names.Count == 0)
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

                UpdateRows(await m_model.SetSamplingIntervalAsync(names, samplingInterval));
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
                List<string> names = GetSelectedNames();

                if (!m_model.IsConnected || names.Count == 0)
                {
                    return;
                }

                // determine the filter being requested.
                DeadbandType deadbandType = DeadbandType.None;
                double deadbandValue = 0;

                if (sender == Monitoring_Deadband_Absolute_5MI)
                {
                    deadbandType = DeadbandType.Absolute;
                    deadbandValue = 5.0;
                }
                else if (sender == Monitoring_Deadband_Absolute_10MI)
                {
                    deadbandType = DeadbandType.Absolute;
                    deadbandValue = 10.0;
                }
                else if (sender == Monitoring_Deadband_Absolute_25MI)
                {
                    deadbandType = DeadbandType.Absolute;
                    deadbandValue = 25.0;
                }
                else if (sender == Monitoring_Deadband_Percentage_1MI)
                {
                    deadbandType = DeadbandType.Percent;
                    deadbandValue = 1.0;
                }
                else if (sender == Monitoring_Deadband_Percentage_5MI)
                {
                    deadbandType = DeadbandType.Percent;
                    deadbandValue = 5.0;
                }
                else if (sender == Monitoring_Deadband_Percentage_10MI)
                {
                    deadbandType = DeadbandType.Percent;
                    deadbandValue = 10.0;
                }

                // the model drops a filter the server would not take, so the rows it
                // returns show what the server applies.
                UpdateRows(await m_model.SetDeadbandAsync(names, deadbandType, deadbandValue));
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
                List<string> names = GetSelectedNames();

                if (names.Count == 0)
                {
                    return;
                }

                if (m_model.IsConnected)
                {
                    await m_model.RemoveAsync(names);
                }

                // remove the rows.
                foreach (string name in names)
                {
                    if (m_rows.Remove(name, out ListViewItem item))
                    {
                        item.Remove();
                    }
                }

                AdjustMonitoredItemColumns();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void BrowsingMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            BrowseNode node = SelectedBrowseNode();

            bool enabled = m_model.IsConnected && node != null && node.IsLocalVariable;

            Browse_MonitorMI.Enabled = enabled;
            Browse_ReadHistoryMI.Enabled = enabled;
            Browse_WriteMI.Enabled = enabled;
        }

        private async void Monitoring_WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected || MonitoredItemsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                if (MonitoredItemsLV.SelectedItems[0].Tag is MonitoredItemRow row)
                {
                    await WriteValueAsync(row.NodeId);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user for a new value of a variable and writes it.
        /// </summary>
        /// <remarks>
        /// The dialog only knows the current value and how to write a new one: the model
        /// reads the one and does the other.
        /// </remarks>
        private async Task WriteValueAsync(NodeId nodeId)
        {
            DataValue current = await m_model.ReadAttributeAsync(nodeId, Attributes.Value);

            using (var dialog = new WriteValueDlg())
            {
                dialog.ShowDialog(
                    current,
                    (value, ct) => m_model.WriteAsync(nodeId, Attributes.Value, value, ct),
                    m_telemetry);
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
                if (!m_model.IsConnected)
                {
                    return;
                }

                string locale;

                // a shared dialog which browses the locales of the server itself.
                using (var dialog = new SelectLocaleDlg())
                {
                    locale = await dialog.ShowDialogAsync(m_model.Session);
                }

                if (locale == null)
                {
                    return;
                }

                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                await m_model.SetLocaleAsync(locale);
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
