/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.NodeManagement.Client.Model;

namespace Quickstarts.NodeManagement.Client
{
    /// <summary>
    /// The main form of the OPC UA NodeManagement Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="NodeManagementClientModel"/>, which resolves the folders of the server,
    /// sends the four services of OPC 10000-4 5.8 and watches the model change events. The
    /// window only reads the name box and the selection, calls one operation of the model
    /// per button, and shows the two folders the model reads.
    /// </para>
    /// <para>
    /// Every button reports the status code the server answered into the status bar rather
    /// than into a dialog, because the refusals are half of what there is to see: a browse
    /// name a sibling already uses, a node id which is taken, a parent the server does not
    /// open to its clients, and a node whose node manager never opted in at all.
    /// </para>
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
        /// <param name="telemetry">The telemetry context of the application.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry, NodeManagementClientModel model)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62575/Quickstarts/NodeManagementServer";
            this.Text = configuration.ApplicationName;

            // created by the container while this constructor runs, so on the thread of
            // the window: that is the context the model captures for its events, and it is
            // why the handlers below can touch the controls directly
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_model.ModelChanged += Model_ModelChangedAsync;
            m_model.Error += Model_Error;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly NodeManagementClientModel m_model;

        /// <summary>
        /// Whether a refresh is filling the lists right now, and whether another one was
        /// asked for while it did.
        /// </summary>
        /// <remarks>
        /// A model change event can arrive while a button's refresh is mid-way, and two
        /// interleaved fills of the same list would leave it half of each. The second
        /// request is remembered and served once the first has finished.
        /// </remarks>
        private bool m_refreshing;
        private bool m_refreshPending;
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
        /// closes the session, because closing a session which still carries one waits for
        /// the publish pipeline to drain. The close is awaited rather than the synchronous
        /// Disconnect, which blocks the UI thread on work that needs the same message loop.
        /// </remarks>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.DetachAsync();
                await ConnectServerCTRL.DisconnectAsync(default);
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
        /// Updates the display after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                ISession session = ConnectServerCTRL.Session;

                if (session == null)
                {
                    await m_model.DetachAsync();
                    NodesLV.Items.Clear();
                    GroupLV.Items.Clear();
                    SetButtonsEnabled(false);
                    return;
                }

                // the model resolves the folders and subscribes to the model change events
                await m_model.AttachAsync(session);

                if (!m_model.IsModelAvailable)
                {
                    // no point in offering the buttons: every browse name they send would go
                    // out in namespace zero and be routed to the standard address space
                    Report(new OperationResult(
                        $"Looking for the namespace {NodeManagementClientModel.NodeManagementNamespaceUri}",
                        StatusCodes.BadNodeIdUnknown));
                    return;
                }

                SetButtonsEnabled(true);

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                m_model.NotifyReconnectStarting();
                SetButtonsEnabled(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                // the subscription survives the reconnect; what the address space holds by
                // now is read again, because other clients were free to change it meanwhile
                await m_model.NotifyReconnectCompletedAsync();

                SetButtonsEnabled(m_model.IsModelAvailable);

                await RefreshAsync();
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

        /// <summary>
        /// Reads the address space again.
        /// </summary>
        private async void RefreshBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Creates an object below the selected node.
        /// </summary>
        private async void AddObjectBTN_ClickAsync(object sender, EventArgs e)
        {
            await AddAsync(NodeClass.Object);
        }

        /// <summary>
        /// Creates a variable below the selected node.
        /// </summary>
        private async void AddVariableBTN_ClickAsync(object sender, EventArgs e)
        {
            await AddAsync(NodeClass.Variable);
        }

        /// <summary>
        /// Deletes the selected node.
        /// </summary>
        private async void DeleteBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeEntry selected = Selected(NodesLV);

                if (!m_model.IsConnected || selected == null)
                {
                    Report(new OperationResult("Deleting a node", StatusCodes.BadNothingToDo));
                    return;
                }

                Report(await m_model.DeleteNodeAsync(selected));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Makes the selected node reachable from the commissioned group as well.
        /// </summary>
        private async void AddReferenceBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeEntry selected = Selected(NodesLV);

                if (!m_model.IsConnected || selected == null)
                {
                    Report(new OperationResult("Adding a reference", StatusCodes.BadNothingToDo));
                    return;
                }

                Report(await m_model.AddReferenceAsync(selected));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Drops the reference which puts the selected node in the group.
        /// </summary>
        private async void DeleteReferenceBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeEntry selected = Selected(GroupLV);

                if (!m_model.IsConnected || selected == null)
                {
                    Report(new OperationResult("Deleting a reference", StatusCodes.BadNothingToDo));
                    return;
                }

                Report(await m_model.DeleteReferenceAsync(selected));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Tries the same delete on a node of the standard address space.
        /// </summary>
        private async void RefusedBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                Report(await m_model.DeleteStandardNodeAsync());
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Reads the address space again after the server reported a change.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window. The event can still arrive
        /// after the window was closed.
        /// </remarks>
        private async void Model_ModelChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed)
                {
                    return;
                }

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
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
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds a node of the given class below the selected node.
        /// </summary>
        private async Task AddAsync(NodeClass nodeClass)
        {
            try
            {
                string name = NewNameTB.Text?.Trim();

                if (!m_model.IsConnected || string.IsNullOrEmpty(name))
                {
                    Report(new OperationResult("Adding a node", StatusCodes.BadBrowseNameInvalid));
                    return;
                }

                NodeId parentId = ParentForNewNodes();

                if (parentId.IsNull)
                {
                    Report(new OperationResult("Adding a node", StatusCodes.BadParentNodeIdInvalid));
                    return;
                }

                Report(await m_model.AddNodeAsync(nodeClass, name, parentId));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// The node a new node is attached to.
        /// </summary>
        /// <remarks>
        /// The selected object, so that a client can give a device it just created a variable
        /// of its own, and the Devices folder when nothing useful is selected. The server
        /// refuses any other parent, which the status bar then says.
        /// </remarks>
        private NodeId ParentForNewNodes()
        {
            NodeEntry selected = Selected(NodesLV);

            return selected != null && selected.NodeClass == NodeClass.Object
                ? selected.NodeId
                : m_model.DevicesId;
        }

        /// <summary>
        /// Reads both lists again, once at a time.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (!m_model.IsConnected)
            {
                return;
            }

            if (m_refreshing)
            {
                m_refreshPending = true;
                return;
            }

            m_refreshing = true;

            try
            {
                do
                {
                    m_refreshPending = false;

                    AddressSpace addressSpace = await m_model.ReadAddressSpaceAsync();

                    if (IsDisposed)
                    {
                        return;
                    }

                    Fill(NodesLV, addressSpace.Devices);
                    Fill(GroupLV, addressSpace.Commissioned);
                }
                while (m_refreshPending && m_model.IsConnected);
            }
            finally
            {
                m_refreshing = false;
            }
        }

        /// <summary>
        /// Shows the entries of one folder, indented by their depth.
        /// </summary>
        private static void Fill(ListView list, IReadOnlyList<NodeEntry> entries)
        {
            NodeId selected = Selected(list)?.NodeId ?? NodeId.Null;

            list.BeginUpdate();

            try
            {
                list.Items.Clear();

                foreach (NodeEntry entry in entries)
                {
                    var item = new ListViewItem(new string(' ', entry.Depth * 4) + entry.Name) { Tag = entry };

                    item.SubItems.Add(entry.NodeClass.ToString());
                    item.SubItems.Add(entry.NodeId.ToString());
                    item.SubItems.Add(entry.Value);

                    list.Items.Add(item);

                    // keep the selection across the refresh, so that adding a variable to a
                    // device does not lose the device
                    if (entry.NodeId == selected)
                    {
                        item.Selected = true;
                    }
                }
            }
            finally
            {
                list.EndUpdate();
            }
        }

        /// <summary>
        /// The entry selected in a list, or null.
        /// </summary>
        private static NodeEntry Selected(ListView list)
        {
            return list.SelectedItems.Count > 0 ? (NodeEntry)list.SelectedItems[0].Tag : null;
        }

        /// <summary>
        /// Reports what the server answered to an operation the user asked for.
        /// </summary>
        /// <remarks>
        /// The status bar rather than a message box: most of what this sample has to show is
        /// which requests are refused and with which status code, and a modal dialog between
        /// every click makes trying them tedious. It also keeps the buttons drivable from a
        /// test, which a modal dialog does not.
        /// </remarks>
        private void Report(OperationResult result)
        {
            ActionStatusLB.Text = result.ToString();
            ActionStatusLB.ForeColor = result.Succeeded ? Color.Empty : Color.Red;
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            AddObjectBTN.Enabled = enabled;
            AddVariableBTN.Enabled = enabled;
            DeleteBTN.Enabled = enabled;
            AddReferenceBTN.Enabled = enabled;
            DeleteReferenceBTN.Enabled = enabled;
            RefusedBTN.Enabled = enabled;
        }
        #endregion
    }
}
