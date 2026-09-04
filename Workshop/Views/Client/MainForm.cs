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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.ViewsClient.Model;

namespace Quickstarts.ViewsClient
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and the browse control, and hands the
    /// session the connect control opens to the <see cref="ViewsClientModel"/>, which
    /// finds the views of the server. The window fills its combo box from the model and,
    /// when the user picks a view, gives the view description the model prepares to the
    /// browse control, so that its tree browses through the view.
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
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62561/Quickstarts/ViewsServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new ViewsClientModel(telemetry);
            m_model.Error += Model_Error;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Detached asynchronously by MainForm_FormClosing, which cannot await a DisposeAsync.")]
        private readonly ViewsClientModel m_model;
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
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                ISession session = ConnectServerCTRL.Session;

                ViewCB.Items.Clear();

                if (session == null)
                {
                    await m_model.DetachAsync();
                }
                else
                {
                    // the model finds the views of the server while it attaches.
                    await m_model.AttachAsync(session);

                    // the first entry stands for no view at all, which is what a browse
                    // without a view description sees.
                    ViewCB.Items.Add(new ReferenceDescription() { NodeId = ExpandedNodeId.Null, DisplayName = new LocalizedText("None") });

                    foreach (ReferenceDescription view in m_model.Views)
                    {
                        ViewCB.Items.Add(view);
                    }

                    ViewCB.SelectedIndex = 0;
                }

                // browse the instances in the server. A null session clears the tree.
                await BrowseCTRL.InitializeAsync(session, ObjectIds.ObjectsFolder, m_telemetry, default, ReferenceTypeIds.Organizes, ReferenceTypeIds.Aggregates);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private async void Server_ReconnectStartingAsync(object sender, EventArgs e)
        {
            try
            {
                m_model.NotifyReconnectStarting();
                await BrowseCTRL.ChangeSessionAsync(null);
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
                await m_model.NotifyReconnectCompletedAsync();
                await BrowseCTRL.ChangeSessionAsync(ConnectServerCTRL.Session);
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
        /// Browses through the view the user picked.
        /// </summary>
        private void ChangeViewBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // the model prepares the view description; the browse control puts it on
                // every browse of its tree from now on.
                BrowseCTRL.View = m_model.SelectView(ViewCB.SelectedItem as ReferenceDescription);
                BrowseCTRL.RefreshSelection();
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
    }
}
