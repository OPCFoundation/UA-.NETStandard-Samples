/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A tool bar used to connect to a server.
    /// </summary>
    /// <remarks>
    /// The control is the window half of a connection: it holds the url and the security
    /// flag a person edits, shows what the connection reports in a status strip, and asks
    /// about a certificate which did not validate. Everything else - discovery, the
    /// session, the reconnect, the bounded close - belongs to the
    /// <see cref="SampleConnection"/> behind it, which has no window and which the
    /// headless tests use directly.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "WinForms designer/owner lifetime manages this sample field.")]
    public partial class ConnectServerCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public ConnectServerCtrl()
        {
            InitializeComponent();

            m_connection = new SampleConnection {
                CertificateValidation = CertificateValidator_CertificateValidation,
            };

            m_connection.StatusChanged += Connection_StatusChanged;
            m_connection.ConnectComplete += Connection_ConnectComplete;
            m_connection.KeepAlive += Connection_KeepAlive;
            m_connection.ReconnectStarting += Connection_ReconnectStarting;
            m_connection.ReconnectComplete += Connection_ReconnectComplete;
        }
        #endregion

        #region Private Fields
        // the connection is released with the session it holds; there is nothing else in
        // it to dispose, and the control has no Dispose of its own to release it from.
        private readonly SampleConnection m_connection;
        private ITelemetryContext m_telemetry;
        private ILogger m_logger;
        private EventHandler m_ReconnectComplete;
        private EventHandler m_ReconnectStarting;
        private EventHandler m_KeepAliveComplete;
        private EventHandler m_ConnectComplete;
        private StatusStrip m_StatusStrip;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "WinForms designer/owner lifetime manages this sample field.")]
        private ToolStripItem m_ServerStatusLB;
#pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private ToolStripItem m_StatusUpateTimeLB;
#pragma warning restore CA2213
        #endregion

        #region Public Members
        /// <summary>
        /// Default session values.
        /// </summary>
        public static readonly uint DefaultSessionTimeout = SampleConnection.DefaultSessionTimeout;
        public static readonly int DefaultDiscoverTimeout = SampleConnection.DefaultDiscoverTimeout;
        public static readonly int DefaultReconnectPeriod = 1;
        public static readonly int DefaultReconnectPeriodExponentialBackOff = 10;

        /// <summary>
        /// A strip used to display session status information.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public StatusStrip StatusStrip
        {
            get => m_StatusStrip;

            set
            {
                if (!Object.ReferenceEquals(m_StatusStrip, value))
                {
                    m_StatusStrip = value;

                    if (value != null)
                    {
                        m_ServerStatusLB = new ToolStripStatusLabel();
                        m_StatusUpateTimeLB = new ToolStripStatusLabel();
                        m_StatusStrip.Items.Add(m_ServerStatusLB);
                        m_StatusStrip.Items.Add(m_StatusUpateTimeLB);
                    }
                }
            }
        }

        /// <summary>
        /// A control that contains the last time a keep alive was returned from the server.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ToolStripItem ServerStatusControl { get => m_ServerStatusLB; set => m_ServerStatusLB = value; }

        /// <summary>
        /// A control that contains the last time a keep alive was returned from the server.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ToolStripItem StatusUpateTimeControl { get => m_StatusUpateTimeLB; set => m_StatusUpateTimeLB = value; }

        /// <summary>
        /// The connection behind the control, for a caller which wants the session
        /// without the tool bar.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public SampleConnection Connection => m_connection;

        /// <summary>
        /// The name of the session to create.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string SessionName { get => m_connection.SessionName; set => m_connection.SessionName = value; }

        /// <summary>
        /// Gets or sets a flag indicating that the domain checks should be ignored when connecting.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DisableDomainCheck { get => m_connection.DisableDomainCheck; set => m_connection.DisableDomainCheck = value; }

        /// <summary>
        /// Gets the cached EndpointDescription for a Url.
        /// </summary>
        public EndpointDescription GetEndpointDescription(Uri url)
        {
            return m_connection.GetEndpointDescription(url);
        }

        /// <summary>
        /// The URL displayed in the control.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        #pragma warning disable CA1056 // Justification: sample public API shape is preserved by design.
        public string ServerUrl
        #pragma warning restore CA1056
        {
            get
            {
                if (UrlCB.SelectedIndex >= 0)
                {
                    return (string)UrlCB.SelectedItem;
                }

                return UrlCB.Text;
            }

            set
            {
                UrlCB.SelectedIndex = -1;
                UrlCB.Text = value;
            }
        }

        /// <summary>
        /// Whether to use security when connecting.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool UseSecurity
        {
            get => UseSecurityCK.Checked;
            set => UseSecurityCK.Checked = value;
        }

        /// <summary>
        /// The locales to use when creating the session.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        #pragma warning disable CA1819 // Justification: sample public API shape is preserved by design.
        public string[] PreferredLocales { get => m_connection.PreferredLocales; set => m_connection.PreferredLocales = value; }
        #pragma warning restore CA1819

        /// <summary>
        /// The user identity to use when creating the session.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IUserIdentity UserIdentity { get => m_connection.UserIdentity; set => m_connection.UserIdentity = value; }

        /// <summary>
        /// The client application configuration.
        /// </summary>
        #pragma warning disable WFO1000 // Justification: sample public API shape is preserved by design.
        public ApplicationConfiguration Configuration
        #pragma warning restore WFO1000
        {
            get => m_connection.Configuration;
            set => m_connection.Configuration = value;
        }

        /// <summary>
        /// The currently active session.
        /// </summary>
        public ISession Session => m_connection.Session;

        /// <summary>
        /// The number of seconds between reconnect attempts (0 means reconnect is disabled).
        /// </summary>
        /// <remarks>
        /// Kept for source compatibility. The reconnect is now driven by the reconnect policy
        /// of the <see cref="ManagedSession"/> the connection creates, which this value no
        /// longer feeds into.
        /// </remarks>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ReconnectPeriod { get; set; } = DefaultReconnectPeriod;

        /// <summary>
        /// The discover timeout in ms.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int DiscoverTimeout { get => m_connection.DiscoverTimeout; set => m_connection.DiscoverTimeout = value; }

        /// <summary>
        /// The session timeout in ms.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public uint SessionTimeout { get => m_connection.SessionTimeout; set => m_connection.SessionTimeout = value; }

        /// <summary>
        /// Raised when a good keep alive from the server arrives.
        /// </summary>
        public event EventHandler KeepAliveComplete
        {
            add { m_KeepAliveComplete += value; }
            remove { m_KeepAliveComplete -= value; }
        }

        /// <summary>
        /// Raised when a reconnect operation starts.
        /// </summary>
        public event EventHandler ReconnectStarting
        {
            add { m_ReconnectStarting += value; }
            remove { m_ReconnectStarting -= value; }
        }

        /// <summary>
        /// Raised when a reconnect operation completes.
        /// </summary>
        public event EventHandler ReconnectComplete
        {
            add { m_ReconnectComplete += value; }
            remove { m_ReconnectComplete -= value; }
        }

        /// <summary>
        /// Raised after successfully connecting to or disconnecing from a server.
        /// </summary>
        public event EventHandler ConnectComplete
        {
            add { m_ConnectComplete += value; }
            remove { m_ConnectComplete -= value; }
        }

        /// <summary>
        /// Sets the URLs shown in the control.
        /// </summary>
        public void SetAvailableUrls(IList<string> urls)
        {
            UrlCB.Items.Clear();

            if (urls != null)
            {
                foreach (string url in urls)
                {
                    int index = url.LastIndexOf("/discovery", StringComparison.InvariantCultureIgnoreCase);

                    if (index != -1)
                    {
                        UrlCB.Items.Add(url.Substring(0, index));
                        continue;
                    }

                    UrlCB.Items.Add(url);
                }

                if (UrlCB.Items.Count > 0)
                {
                    UrlCB.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="serverUrl">The URL of a server endpoint, or null for the one shown.</param>
        /// <param name="useSecurity">Whether to use security. Ignored when the url is null.</param>
        /// <param name="sessionTimeout">The session timeout in ms, or zero for the default.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        public Task<ISession> ConnectAsync(
            ITelemetryContext telemetry,
            #pragma warning disable CA1054 // Justification: sample public API shape is preserved by design.
            string serverUrl = null,
            #pragma warning restore CA1054
            bool useSecurity = false,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        {
            if (serverUrl == null)
            {
                serverUrl = ServerUrl;
                useSecurity = UseSecurityCK.Checked;
            }
            else
            {
                UrlCB.Text = serverUrl;
                UseSecurityCK.Checked = useSecurity;
            }

            m_telemetry = telemetry;
            m_logger = telemetry?.CreateLogger<ConnectServerCtrl>();

            return m_connection.ConnectAsync(serverUrl, useSecurity, telemetry, sessionTimeout, ct);
        }

        /// <summary>
        /// Create a new reverse connection.
        /// </summary>
        /// <param name="connection">The connection the server opened.</param>
        /// <param name="useSecurity">Whether to use security.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="discoverTimeout">The discovery timeout in ms, or -1 for the default.</param>
        /// <param name="sessionTimeout">The session timeout in ms, or zero for the default.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<ISession> ConnectAsync(
            ITransportWaitingConnection connection,
            bool useSecurity,
            ITelemetryContext telemetry,
            int discoverTimeout = -1,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(connection);

            if (connection.EndpointUrl == null)
            {
                throw new ArgumentException("Endpoint URL is not valid.", nameof(connection));
            }

            UrlCB.Text = connection.EndpointUrl.ToString();
            UseSecurityCK.Checked = useSecurity;

            m_telemetry = telemetry;
            m_logger = telemetry?.CreateLogger<ConnectServerCtrl>();

            // the security of the session follows the check box, the way it always has:
            // the discovery of the first reverse hello is what the argument steers.
            return m_connection.ConnectAsync(
                connection,
                UseSecurityCK.Checked,
                telemetry,
                discoverTimeout,
                sessionTimeout,
                ct);
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public Task DisconnectAsync(CancellationToken ct = default)
        {
            UpdateStatus(false, DateTime.UtcNow, "Disconnected");
            return m_connection.DisconnectAsync(ct);
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        /// <remarks>
        /// The synchronous entry point exists for the callers which cannot await: the
        /// Disconnect menu item of the samples, and their FormClosing handler, which the
        /// event signature keeps synchronous. Both run on the UI thread, and the
        /// connection reports the completion on that same thread rather than marshalling
        /// it - a form which is already on its way out would never see a marshalled one.
        /// </remarks>
        public void Disconnect()
        {
            m_connection.Disconnect();
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        public void Discover(string hostName)
        {
            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            string endpointUrl = new DiscoverServerDlg().ShowDialog(Configuration, hostName, m_telemetry);
            #pragma warning restore CA2000

            if (endpointUrl != null)
            {
                ServerUrl = endpointUrl;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Raises the connect complete event on the main GUI thread.
        /// </summary>
        private void DoConnectComplete(object state)
        {
            if (m_ConnectComplete != null)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new System.Threading.WaitCallback(DoConnectComplete), state);
                    return;
                }

                m_ConnectComplete(this, null);
            }
        }
        #endregion

        #region Event Handlers
        private delegate void UpdateStatusCallback(bool error, DateTime time, string status, params object[] arg);
        /// <summary>
        /// Updates the status control.
        /// </summary>
        /// <remarks>
        /// Public so that a form which drives a connection the control does not start
        /// itself - a client waiting for a reverse connection, for one - reports it on the
        /// same status line as the connects the control does start.
        /// </remarks>
        /// <param name="error">Whether the status represents an error.</param>
        /// <param name="time">The time associated with the status.</param>
        /// <param name="status">The status message.</param>
        /// <param name="args">Arguments used to format the status message.</param>
        public void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new UpdateStatusCallback(UpdateStatus), error, time, status, args);
                return;
            }

            if (m_ServerStatusLB != null)
            {
                m_ServerStatusLB.Text = String.Format(status, args);
                m_ServerStatusLB.ForeColor = (error) ? Color.Red : Color.Empty;
            }

            if (m_StatusUpateTimeLB != null)
            {
                m_StatusUpateTimeLB.Text = time.ToLocalTime().ToString("T");
                m_StatusUpateTimeLB.ForeColor = (error) ? Color.Red : Color.Empty;
            }
        }

        /// <summary>
        /// Shows what the connection reports in the status strip.
        /// </summary>
        private void Connection_StatusChanged(object sender, SampleConnectionStatusEventArgs e)
        {
            // the message is already formatted, so it is passed as a literal: a status
            // which happens to contain a brace must not be formatted a second time.
            UpdateStatus(e.IsError, e.Time, "{0}", e.Message);
        }

        /// <summary>
        /// Reports a connect or a disconnect to the window.
        /// </summary>
        private void Connection_ConnectComplete(object sender, EventArgs e)
        {
            DoConnectComplete(null);
        }

        /// <summary>
        /// Reports a keep alive to the window.
        /// </summary>
        private void Connection_KeepAlive(object sender, KeepAliveEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler<KeepAliveEventArgs>(Connection_KeepAlive), sender, e);
                return;
            }

            try
            {
                m_KeepAliveComplete?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_logger, this.Text, exception);
            }
        }

        /// <summary>
        /// Reports the start of a reconnect to the window.
        /// </summary>
        private void Connection_ReconnectStarting(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler(Connection_ReconnectStarting), sender, e);
                return;
            }

            try
            {
                m_ReconnectStarting?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_logger, this.Text, exception);
            }
        }

        /// <summary>
        /// Reports the completion of a reconnect to the window.
        /// </summary>
        private void Connection_ReconnectComplete(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler(Connection_ReconnectComplete), sender, e);
                return;
            }

            try
            {
                m_ReconnectComplete?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_logger, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles a click on the connect button.
        /// </summary>
        private async void Server_ConnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                // Await directly so that continuations resume on the UI thread's
                // SynchronizationContext, keeping all control access thread-safe.
                await this.ConnectAsync(m_telemetry, ServerUrl, UseSecurityCK.Checked);
            }
            catch (ServiceResultException sre)
            {
                if (sre.StatusCode == StatusCodes.BadCertificateHostNameInvalid)
                {
                    if (GuiUtils.HandleDomainCheckError(FindForm().Text, sre.Result))
                    {
                        DisableDomainCheck = true;
                    }
                }
                else
                {
                    // update status.
                    UpdateStatus(true, DateTime.Now, "Connection failed! [{0}]", sre.Message);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_logger, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        private bool CertificateValidator_CertificateValidation(Opc.Ua.Security.Certificates.Certificate certificate, ServiceResult error)
        {
            if (this.InvokeRequired)
            {
                return (bool)this.Invoke(new Func<Opc.Ua.Security.Certificates.Certificate, ServiceResult, bool>(CertificateValidator_CertificateValidation), certificate, error);
            }

            try
            {
                return GuiUtils.HandleCertificateValidationError(this.FindForm(), certificate, error);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_logger, this.Text, exception);
                return false;
            }
        }
        #endregion
    }
}
