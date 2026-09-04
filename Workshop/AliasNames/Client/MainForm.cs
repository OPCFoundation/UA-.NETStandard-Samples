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
using Quickstarts.AliasNames.Client.Model;

namespace Quickstarts.AliasNames.Client
{
    /// <summary>
    /// The main form of the OPC UA Part 17 alias names Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="AliasNamesClientModel"/>, which browses the plant, names its signals from
    /// the alias inventory, and searches the categories. The window only renders what the
    /// model found into its two lists, translates each button into one model call, and
    /// puts the outcome into the status bar.
    /// </para>
    /// <para>
    /// The upper list is the plant as an ordinary client sees it, with the tag name of each
    /// node in the last column; the lower half is the search that runs the other way. The
    /// two mutation buttons are left enabled for every account on purpose, because seeing
    /// the server refuse an anonymous session is as much a part of the sample as seeing it
    /// succeed for an administrator.
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
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62577/Quickstarts/AliasNamesServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new AliasNamesClientModel(telemetry);
            m_model.Error += Model_Error;

            // the two accounts of the sample server. Which one is signed in decides only
            // whether the mutation Methods are allowed - searching is open to everyone.
            foreach (string account in AliasNamesClientModel.Accounts)
            {
                IdentityCB.Items.Add(account);
            }

            IdentityCB.SelectedIndex = 0;
            IdentityCB.SelectedIndexChanged += IdentityCB_SelectedIndexChanged;

            // the categories this client can search. The standard one needs no prior
            // knowledge of the server; the other three are this server's own.
            foreach (AliasCategoryChoice category in AliasNamesClientModel.Categories)
            {
                CategoryCB.Items.Add(category);
            }

            CategoryCB.SelectedIndex = 0;

            UpdateIdentityHint();
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Detached asynchronously by MainForm_FormClosing, which cannot await a DisposeAsync.")]
        private readonly AliasNamesClientModel m_model;
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
        /// The model is detached first: it releases the resolver before the control closes
        /// the session.
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
        private void IdentityCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.UserIdentity = AliasNamesClientModel.IdentityFor(IdentityCB.SelectedItem as string);

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
                    PlantLV.Items.Clear();
                    AliasLV.Items.Clear();
                    LastChangeLB.Text = string.Empty;
                    SetButtonsEnabled(false);
                    return;
                }

                // the model opens its resolver over the standard category while it attaches
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
        /// Reads the plant and searches the selected category again.
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
        /// Searches the selected category for the pattern in the text box.
        /// </summary>
        private async void FindBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await SearchAsync(verbose: false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Searches the selected category with the optional verbose Method.
        /// </summary>
        private async void FindVerboseBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await SearchAsync(verbose: true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Searches again whenever another category is picked.
        /// </summary>
        private async void CategoryCB_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_model.IsConnected)
                {
                    await SearchAsync(verbose: false);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Gives the node selected in the plant list another tag name.
        /// </summary>
        private async void AddAliasBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                PlantEntry node = PlantLV.SelectedItems.Count == 0
                    ? null
                    : (PlantEntry)PlantLV.SelectedItems[0].Tag;

                Outcome outcome = await m_model.AddAliasAsync(node, SelectedCategory(), NewAliasTB.Text);

                Report(outcome);

                if (!outcome.Failed)
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
        /// Removes the alias selected in the search results.
        /// </summary>
        private async void DeleteAliasBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                AliasEntry alias = AliasLV.SelectedItems.Count == 0
                    ? null
                    : (AliasEntry)AliasLV.SelectedItems[0].Tag;

                Outcome outcome = await m_model.DeleteAliasAsync(alias, SelectedCategory());

                Report(outcome);

                if (!outcome.Failed)
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
        /// Reads the plant and searches the selected category.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (!m_model.IsConnected)
            {
                return;
            }

            // the inventory may have changed, so the cached reverse mapping is dropped
            m_model.Invalidate();

            PlantLV.Items.Clear();

            foreach (PlantEntry node in await m_model.LoadPlantAsync())
            {
                var item = new ListViewItem(node.Path) { Tag = node };

                item.SubItems.Add(node.NodeId.ToString());
                item.SubItems.Add(node.Value);
                item.SubItems.Add(node.TagName);

                PlantLV.Items.Add(item);
            }

            await SearchAsync(verbose: false);
        }

        /// <summary>
        /// Searches the selected category and fills the lower list.
        /// </summary>
        private async Task SearchAsync(bool verbose)
        {
            if (!m_model.IsConnected)
            {
                return;
            }

            AliasLV.Items.Clear();

            AliasSearchResult result = await m_model.SearchAsync(SelectedCategory(), PatternTB.Text, verbose);

            LastChangeLB.Text = result.LastChange;

            foreach (AliasEntry alias in result.Aliases)
            {
                var item = new ListViewItem(alias.Name) { Tag = alias };

                item.SubItems.Add(alias.ResolvesTo);
                item.SubItems.Add(alias.Value);
                item.SubItems.Add(alias.Category);
                item.SubItems.Add(alias.ServerUris);

                AliasLV.Items.Add(item);
            }

            Report(result.Outcome);
        }

        /// <summary>
        /// The category picked in the drop down.
        /// </summary>
        private AliasCategoryChoice SelectedCategory()
        {
            return (AliasCategoryChoice)CategoryCB.SelectedItem;
        }

        /// <summary>
        /// Puts the outcome of an operation into the status bar.
        /// </summary>
        /// <remarks>
        /// The status bar rather than a message box: half the point of this sample is
        /// trying an operation as one account after another and comparing the answers,
        /// and a modal dialog between every click makes that tedious. It also keeps the
        /// buttons drivable from a test.
        /// </remarks>
        private void Report(Outcome outcome)
        {
            ActionStatusLB.Text = outcome.Text;
            ActionStatusLB.ForeColor = outcome.Failed ? Color.Red : Color.Empty;
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            FindBTN.Enabled = enabled;
            FindVerboseBTN.Enabled = enabled;
            AddAliasBTN.Enabled = enabled;
            DeleteAliasBTN.Enabled = enabled;
        }

        /// <summary>
        /// Explains what the selected account may do.
        /// </summary>
        private void UpdateIdentityHint()
        {
            IdentityHintLB.Text = AliasNamesClientModel.HintFor(IdentityCB.SelectedItem as string);
        }
        #endregion
    }
}
