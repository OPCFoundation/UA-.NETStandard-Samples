/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.RoleManagement.Client.Model;

namespace Quickstarts.RoleManagement.Client
{
    /// <summary>
    /// The main form of the OPC UA Part 18 role management Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="RoleManagementClientModel"/>, which reads the machine and the RoleSet as
    /// the Session sees them and offers the Part 18 operations. The window only renders
    /// what the model found into its two lists, translates each button into one model call,
    /// and puts what the server answered into the status bar.
    /// </para>
    /// <para>
    /// The buttons are deliberately left enabled for every account, because seeing the
    /// server answer BadUserAccessDenied or BadSecurityModeInsufficient is the point.
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
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62573/Quickstarts/RoleManagementServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new RoleManagementClientModel(telemetry);
            m_model.Error += Model_Error;

            // the accounts the sample server knows, and the Role each of them earns. The
            // client has no idea what any of that means - it only picks the identity token
            // and lets the server decide what the Session is worth.
            foreach (string account in RoleManagementClientModel.Accounts)
            {
                IdentityCB.Items.Add(account);
            }

            IdentityCB.SelectedIndex = 0;
            IdentityCB.SelectedIndexChanged += IdentityCB_SelectedIndexChanged;

            UpdateIdentityHint();
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly RoleManagementClientModel m_model;
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
        /// The model is detached first: it releases what it holds of the session before
        /// the control closes it.
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
        /// Picks the identity token the next connect uses.
        /// </summary>
        /// <remarks>
        /// The whole of the identity handling of this window is these few lines. Everything
        /// the rest of the form shows follows from which token was sent, because the server
        /// resolves the Roles of the Session from it.
        /// </remarks>
        private void IdentityCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.UserIdentity = RoleManagementClientModel.IdentityFor(IdentityCB.SelectedItem as string);

                UpdateIdentityHint();
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
                    RolesLV.Items.Clear();
                    SetButtonsEnabled(false);
                    return;
                }

                // the model resolves the machine of the server while it attaches
                await m_model.AttachAsync(session);

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
                await m_model.NotifyReconnectCompletedAsync();

                SetButtonsEnabled(m_model.IsConnected);

                if (m_model.IsConnected)
                {
                    await RefreshAsync();
                }
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
        /// Reads the machine and the RoleSet again.
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
        /// Writes the value in the text box to the selected node.
        /// </summary>
        private async void WriteBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected || NodesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                var node = (MachineNodeEntry)NodesLV.SelectedItems[0].Tag;

                Report(await m_model.WriteAsync(node, WriteValueTB.Text));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Calls the Reset method of the machine.
        /// </summary>
        private async void ResetBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // the answer is reported before the lists are re-read, so that a refusal is
                // on the status bar even when the refresh takes a moment
                Report(await m_model.ResetAsync());

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Grants the selected Role to the user in the text box.
        /// </summary>
        private async void AddIdentityBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected || RolesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                var role = (RoleEntry)RolesLV.SelectedItems[0].Tag;

                Report(await m_model.AddIdentityAsync(role, RoleUserTB.Text));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Revokes the selected Role from the user in the text box.
        /// </summary>
        private async void RemoveIdentityBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected || RolesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                var role = (RoleEntry)RolesLV.SelectedItems[0].Tag;

                Report(await m_model.RemoveIdentityAsync(role, RoleUserTB.Text));

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Adds a Role of the server's own to the RoleSet.
        /// </summary>
        private async void AddRoleBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                Report(await m_model.AddRoleAsync(NewRoleTB.Text));

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
        /// Reads the machine and the RoleSet and fills both lists.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (!m_model.IsConnected)
            {
                return;
            }

            RoleManagementSnapshot snapshot = await m_model.RefreshAsync();

            NodesLV.Items.Clear();

            foreach (MachineNodeEntry node in snapshot.Nodes)
            {
                var item = new ListViewItem(node.Name) { Tag = node };

                item.SubItems.Add(node.Value);
                item.SubItems.Add(node.Status);
                item.SubItems.Add(node.Permissions);

                NodesLV.Items.Add(item);
            }

            RolesLV.Items.Clear();

            foreach (RoleEntry role in snapshot.Roles)
            {
                var item = new ListViewItem(role.Name) { Tag = role };

                item.SubItems.Add(role.Granted ? "yes" : string.Empty);
                item.SubItems.Add(role.Identities);

                RolesLV.Items.Add(item);
            }
        }

        /// <summary>
        /// Reports what the server answered to an operation the user asked for.
        /// </summary>
        /// <remarks>
        /// The status bar rather than a message box: half the point of this sample is to try
        /// an operation as one account after another and compare the refusals, and a modal
        /// dialog between every click makes that tedious. It also keeps the buttons drivable
        /// from a test, which a modal dialog does not.
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
            WriteBTN.Enabled = enabled;
            ResetBTN.Enabled = enabled;
            AddIdentityBTN.Enabled = enabled;
            RemoveIdentityBTN.Enabled = enabled;
            AddRoleBTN.Enabled = enabled;
        }

        /// <summary>
        /// Explains what the selected account is expected to be able to do.
        /// </summary>
        private void UpdateIdentityHint()
        {
            IdentityHintLB.Text = RoleManagementClientModel.HintFor(IdentityCB.SelectedItem as string);
        }
        #endregion
    }
}
