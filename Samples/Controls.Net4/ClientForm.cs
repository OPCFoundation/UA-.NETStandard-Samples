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
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using static System.Net.Mime.MediaTypeNames;

namespace Opc.Ua.Sample.Controls
{
    public partial class ClientForm : Form
    {
        #region Private Fields
        private Session m_session;
        private SessionReconnectHandler m_reconnectHandler;
        private int m_reconnectPeriod = 10;
        private ApplicationInstance m_application;
        private Opc.Ua.Server.StandardServer m_server;
        private ConfiguredEndpointCollection m_endpoints;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_context;
        private ClientForm m_masterForm;
        protected readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private List<ClientForm> m_forms;
        #endregion

        public ClientForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        public ClientForm(
            ServiceMessageContext context,
            ApplicationInstance application,
            ClientForm masterForm,
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_masterForm = masterForm;
            m_context = context;
            m_application = application;
            m_server = application.Server as Opc.Ua.Server.StandardServer;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ClientForm>();

            if (m_masterForm == null)
            {
                m_forms = new List<ClientForm>();
            }

            SessionsCTRL.Configuration = m_configuration = configuration;
            SessionsCTRL.MessageContext = context;

            // get list of cached endpoints.
            m_endpoints = m_configuration.LoadCachedEndpoints(true);
            m_endpoints.DiscoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            EndpointSelectorCTRL.Initialize(m_endpoints, m_configuration, telemetry);

            // initialize control state.
            DisconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Opens a new form.
        /// </summary>
        public void OpenForm()
        {
            if (m_masterForm == null)
            {
                ClientForm form = new ClientForm(m_context, m_application, this, m_configuration, m_telemetry);
                m_forms.Add(form);
                form.FormClosing += new FormClosingEventHandler(Window_FormClosing);
                form.Show();
            }
            else
            {
                m_masterForm.OpenForm();
            }
        }

        /// <summary>
        /// Handles a close event fo
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Window_FormClosing(object sender, FormClosingEventArgs e)
        {
            for (int ii = 0; ii < m_forms.Count; ii++)
            {
                if (Object.ReferenceEquals(m_forms[ii], sender))
                {
                    m_forms.RemoveAt(ii);
                    break;
                }
            }
        }

        /// <summary>
        /// Disconnect from the server if ths form is closing.
        /// </summary>
        protected override async void OnClosing(CancelEventArgs e)
        {
            if (m_masterForm == null && m_forms.Count > 0)
            {
                if (MessageBox.Show("Close all sessions?", "Close Window", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                List<ClientForm> forms = new List<ClientForm>(m_forms);

                foreach (ClientForm form in forms)
                {
                    form.Close();
                }
            }
            try
            {
                await DisconnectAsync();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Disconnects from a server.
        /// </summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            if (m_session != null)
            {
                // stop any reconnect operation.
                if (m_reconnectHandler != null)
                {
                    m_reconnectHandler.Dispose();
                    m_reconnectHandler = null;
                }

                m_session.KeepAlive -= StandardClient_KeepAlive;

                await m_session.CloseAsync(ct);
                m_session = null;
            }

            ServerUrlLB.Text = "";
        }

        /// <summary>
        /// Provides a user defined method.
        /// </summary>
        protected virtual void DoTest(Session session)
        {
            MessageBox.Show("A handy place to put test code.");
        }

        private async void EndpointSelectorCTRL_ConnectEndpointAsync(object sender, ConnectEndpointEventArgs e)
        {
            try
            {
                await ConnectAsync(e.Endpoint, m_telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
                e.UpdateControl = false;
            }
        }

        private void EndpointSelectorCTRL_OnChange(object sender, EventArgs e)
        {
            try
            {
                m_endpoints.Save();
            }
            catch
            {
                // GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Connects to a server.
        /// </summary>
        public async Task ConnectAsync(ConfiguredEndpoint endpoint, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (endpoint == null)
            {
                return;
            }

            Session session = await SessionsCTRL.ConnectAsync(endpoint, telemetry, ct);

            if (session != null)
            {
                // stop any reconnect operation.
                m_reconnectHandler?.CancelReconnect();
                Utils.SilentDispose(m_reconnectHandler);

                m_reconnectHandler = new SessionReconnectHandler(telemetry, true);
                session.TransferSubscriptionsOnReconnect = true;

                m_session = session;
                m_session.KeepAlive += new KeepAliveEventHandler(StandardClient_KeepAlive);
                await BrowseCTRL.SetViewAsync(m_session, BrowseViewType.Objects, null, m_telemetry, ct);
                StandardClient_KeepAlive(m_session, null);
            }
        }

        /// <summary>
        /// Updates the status control when a keep alive event occurs.
        /// </summary>
        void StandardClient_KeepAlive(ISession sender, KeepAliveEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new KeepAliveEventHandler(StandardClient_KeepAlive), sender, e);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            if (sender != null && sender.Endpoint != null)
            {
                ServerUrlLB.Text = Utils.Format(
                    "{0} ({1}) {2}",
                    sender.Endpoint.EndpointUrl,
                    sender.Endpoint.SecurityMode,
                    (sender.EndpointConfiguration.UseBinaryEncoding) ? "UABinary" : "XML");
            }
            else
            {
                ServerUrlLB.Text = "None";
            }

            if (m_session != null)
            {
                if (e == null || ServiceResult.IsGood(e.Status))
                {
                    ServerState serverState = e?.CurrentState ?? ServerState.Running;
                    DateTime currentTime = e?.CurrentTime ?? DateTime.UtcNow;
                    ServerStatusLB.Text = Utils.Format(
                        "Server Status: {0} {1:yyyy-MM-dd HH:mm:ss} {2}/{3}",
                        serverState,
                        currentTime.ToLocalTime(),
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount);

                    ServerStatusLB.ForeColor = Color.Empty;
                    ServerStatusLB.Font = new Font(ServerStatusLB.Font, FontStyle.Regular);
                }
                else
                {
                    var state = SessionReconnectHandler.ReconnectState.Ready;
                    if (m_reconnectPeriod > 0)
                    {
                        state = m_reconnectHandler.BeginReconnect(m_session, m_reconnectPeriod * 1000, StandardClient_Server_ReconnectComplete);
                    }

                    ServerStatusLB.Text = String.Format(
                        "{0} {1}/{2}/{3}", e.Status,
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount,
                        state);

                    ServerStatusLB.ForeColor = Color.Red;
                    ServerStatusLB.Font = new Font(ServerStatusLB.Font, FontStyle.Bold);
                }
            }
        }

        private void StandardClient_Server_ReconnectComplete(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(StandardClient_Server_ReconnectComplete), sender, e);
                return;
            }

            try
            {
                // ignore callbacks from discarded objects.
                if (!Object.ReferenceEquals(sender, m_reconnectHandler))
                {
                    return;
                }

                if (m_reconnectHandler.Session != null)
                {
                    if (!ReferenceEquals(m_session, m_reconnectHandler.Session))
                    {
                        var session = m_session;
                        session.KeepAlive -= StandardClient_KeepAlive;
                        m_session = m_reconnectHandler.Session as Session;
                        m_session.KeepAlive += StandardClient_KeepAlive;
                        Utils.SilentDispose(session);
                    }
                }

                BrowseCTRL.SetViewAsync(m_session, BrowseViewType.Objects, null, m_telemetry);

                SessionsCTRL.Reload(m_session);

                StandardClient_KeepAlive(m_session, null);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void MainForm_FormClosingAsync(object sender, FormClosingEventArgs e)
        {
            try
            {
                await SessionsCTRL.CloseAsync();

                if (m_masterForm == null)
                {
                    await m_application.StopAsync();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void FileExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private async void PerformanceTestMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                _ = new PerformanceTestDlg().ShowDialog(
                    m_configuration,
                    m_endpoints,
                    await m_configuration.SecurityConfiguration.ApplicationCertificate.FindAsync(true),
                    m_telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DiscoverServersMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = new ConfiguredServerListDlg().ShowDialog(m_configuration, true, m_telemetry);

                if (endpoint != null)
                {
                    this.EndpointSelectorCTRL.SelectedEndpoint = endpoint;
                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DiscoveryServersOnNetworkMI_Click(object sender, EventArgs e)
        {
            try
            {
                ServerOnNetwork serverOnNetwork = new DiscoveredServerOnNetworkListDlg().ShowDialog(null, m_configuration, m_telemetry);

                if (serverOnNetwork != null)
                {
                    ApplicationDescription server = new ApplicationDescription();
                    server.ApplicationName = serverOnNetwork.ServerName;
                    server.DiscoveryUrls.Add(serverOnNetwork.DiscoveryUrl);

                    ConfiguredEndpoint endpoint = new ConfiguredEndpoint(server, EndpointConfiguration.Create(m_configuration));

                    this.EndpointSelectorCTRL.SelectedEndpoint = endpoint;

                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }

        }

        private void NewWindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                this.OpenForm();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void Discovery_RegisterMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_server != null)
                {
                    _ = OnRegisterAsync();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async Task OnRegisterAsync()
        {
            try
            {
                Opc.Ua.Server.StandardServer server = m_server;

                if (server != null)
                {
                    await server.RegisterWithDiscoveryServerAsync();
                }
            }
            catch (Exception exception)
            {
                m_logger.LogTrace(exception, "Could not register with the LDS");
            }
        }

        private void Task_TestMI_Click(object sender, EventArgs e)
        {
            try
            {
                DoTest(m_session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void contentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + Path.DirectorySeparatorChar + "WebHelp" + Path.DirectorySeparatorChar + "index.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }

    }
}
