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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.RuntimeNodeSets.Client.Model;

namespace Quickstarts.RuntimeNodeSets.Client
{
    /// <summary>
    /// The window of the RuntimeNodeSets client: it drives the control model of the
    /// server and shows what happens to the vendor model while it does.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="RuntimeNodeSetsClientModel"/>. Every operation reports the status code
    /// the server answered into the status bar rather than into a dialog, because the
    /// refusals are half of what there is to see.
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates the form.
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
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry, RuntimeNodeSetsClientModel model)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62579/Quickstarts/RuntimeNodeSetsServer";
            this.Text = configuration.ApplicationName;

            ModeCB.Items.AddRange(new object[] {
                ReloadMode.Reload,
                ReloadMode.ShadowReload,
                ReloadMode.ImmediateReload,
            });

            ModeCB.SelectedIndex = 0;

            // created by the container while this constructor runs, so on the thread of
            // the window: that is the context the model captures for its events, and it is
            // why the handlers below can touch the controls directly
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_model.WatchedValueChanged += Model_WatchedValueChanged;
            m_model.Error += Model_Error;

            SetOperationsEnabled(false);
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Detached asynchronously by MainForm_FormClosing, which cannot await a DisposeAsync.")]
        private readonly RuntimeNodeSetsClientModel m_model;
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
        /// Updates the window after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                ISession session = ConnectServerCTRL.Session;

                if (session == null)
                {
                    await m_model.DetachAsync();

                    RevisionCB.Items.Clear();
                    NodesLV.Items.Clear();
                    WatchLV.Items.Clear();
                    StateLB.Text = string.Empty;
                    SetOperationsEnabled(false);
                    return;
                }

                await m_model.AttachAsync(session);

                RevisionCB.Items.Clear();

                foreach (string revision in m_model.AvailableRevisions)
                {
                    RevisionCB.Items.Add(revision);
                }

                if (RevisionCB.Items.Count > 0)
                {
                    RevisionCB.SelectedIndex = RevisionCB.Items.Count - 1;
                }

                SetOperationsEnabled(m_model.IsControlModelAvailable);

                if (!m_model.IsControlModelAvailable)
                {
                    ActionStatusLB.Text = "This server does not serve the control model of the sample.";
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
        /// Updates the window after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            m_model.NotifyReconnectStarting();
        }

        /// <summary>
        /// Updates the window after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.NotifyReconnectCompletedAsync();
                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Publishes the selected revision on the running server.
        /// </summary>
        private async void LoadBTN_ClickAsync(object sender, EventArgs e)
        {
            await RunAsync(ct => m_model.LoadAsync(SelectedRevision, ct));
        }

        /// <summary>
        /// Replaces the published model with the selected revision.
        /// </summary>
        private async void ReloadBTN_ClickAsync(object sender, EventArgs e)
        {
            await RunAsync(ct => m_model.ReloadAsync(SelectedRevision, SelectedMode, ct));
        }

        /// <summary>
        /// Takes the vendor model off the running server.
        /// </summary>
        private async void RemoveBTN_ClickAsync(object sender, EventArgs e)
        {
            await RunAsync(ct => m_model.RemoveAsync(ct));
        }

        /// <summary>
        /// Browses the vendor model again.
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
        /// Creates the MonitoredItem on the conveyor speed.
        /// </summary>
        private async void WatchBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                WatchLV.Items.Clear();

                OperationResult result = await m_model.WatchSpeedAsync();

                ActionStatusLB.Text = result.ToString();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        private async void StopWatchingBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.StopWatchingAsync();

                ActionStatusLB.Text = "Stopped watching the conveyor speed.";
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Appends one notification of the watched variable.
        /// </summary>
        /// <remarks>
        /// A plain reload deletes the MonitoredItem, a shadow reload keeps it delivering
        /// off the retired generation, and an immediate reload sends BadNodeIdUnknown -
        /// which is the whole client-visible difference between the three modes and the
        /// reason this list is here.
        /// </remarks>
        private void Model_WatchedValueChanged(object sender, WatchedValueEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));

            item.SubItems.Add(e.Value.WrappedValue.ToString());
            item.SubItems.Add(e.Value.StatusCode.ToString());

            WatchLV.Items.Insert(0, item);
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
        /// Cleans up when the window closes.
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
        /// The revision the user picked, or the first one the server offers.
        /// </summary>
        private string SelectedRevision => RevisionCB.SelectedItem as string ?? string.Empty;

        /// <summary>
        /// The reload the user picked.
        /// </summary>
        private ReloadMode SelectedMode
            => ModeCB.SelectedItem is ReloadMode mode ? mode : ReloadMode.Reload;

        /// <summary>
        /// Runs one operation of the control model, reports what the server answered and
        /// shows the address space it left behind.
        /// </summary>
        private async Task RunAsync(Func<CancellationToken, Task<OperationResult>> operation)
        {
            try
            {
                OperationResult result = await operation(default);

                ActionStatusLB.Text = result.ToString();

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Reads the state of the control model and browses the vendor model again.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (!m_model.IsConnected)
            {
                return;
            }

            ModelState state = await m_model.ReadStateAsync();

            StateLB.Text = string.IsNullOrEmpty(state.LoadedRevision)
                ? "No revision is published. Load one."
                : $"Revision {state.LoadedRevision} is published as generation {state.Generation}.";

            IReadOnlyList<VendorNode> nodes = await m_model.BrowseVendorModelAsync();

            NodesLV.BeginUpdate();

            try
            {
                NodesLV.Items.Clear();

                foreach (VendorNode node in nodes)
                {
                    var item = new ListViewItem(new string(' ', node.Depth * 4) + node.Name);

                    item.SubItems.Add(node.NodeId.ToString());
                    item.SubItems.Add(node.Value);

                    NodesLV.Items.Add(item);
                }
            }
            finally
            {
                NodesLV.EndUpdate();
            }
        }

        /// <summary>
        /// Enables the operations only while a session is attached to a server which
        /// serves the control model.
        /// </summary>
        private void SetOperationsEnabled(bool enabled)
        {
            LoadBTN.Enabled = enabled;
            ReloadBTN.Enabled = enabled;
            RemoveBTN.Enabled = enabled;
            RefreshBTN.Enabled = enabled;
            WatchBTN.Enabled = enabled;
            StopWatchingBTN.Enabled = enabled;
            RevisionCB.Enabled = enabled;
            ModeCB.Enabled = enabled;
        }
        #endregion
    }
}
