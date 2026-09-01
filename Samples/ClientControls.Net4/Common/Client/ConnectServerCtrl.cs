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
using Opc.Ua.Client.ComplexTypes;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A tool bar used to connect to a server.
    /// </summary>
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
            m_CertificateValidation = CertificateValidator_CertificateValidation;
            m_endpoints = new Dictionary<Uri, EndpointDescription>();
        }
        #endregion

        #region Private Fields
        private ITelemetryContext m_telemetry;
        private ILogger m_logger;
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        private Func<Opc.Ua.Security.Certificates.Certificate, ServiceResult, bool> m_CertificateValidation;
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
        private Dictionary<Uri, EndpointDescription> m_endpoints;
        #endregion

        #region Public Members
        /// <summary>
        /// Default session values.
        /// </summary>
        public static readonly uint DefaultSessionTimeout = 60000;
        public static readonly int DefaultDiscoverTimeout = 15000;
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
        /// The name of the session to create.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string SessionName { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating that the domain checks should be ignored when connecting.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DisableDomainCheck { get; set; }

        /// <summary>
        /// Gets the cached EndpointDescription for a Url.
        /// </summary>
        public EndpointDescription GetEndpointDescription(Uri url)
        {
            EndpointDescription endpointDescription;
            if (m_endpoints.TryGetValue(url, out endpointDescription))
            {
                return endpointDescription;
            }
            return null;
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
        public string[] PreferredLocales { get; set; }

        /// <summary>
        /// The user identity to use when creating the session.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IUserIdentity UserIdentity { get; set; }

        /// <summary>
        /// The client application configuration.
        /// </summary>
        #pragma warning disable WFO1000 // Justification: sample public API shape is preserved by design.
        public ApplicationConfiguration Configuration
        #pragma warning restore WFO1000
        {
            get => m_configuration;

            set
            {
                if (!Object.ReferenceEquals(m_configuration, value))
                {
                    if (m_configuration != null)
                    {
                        m_configuration.CertificateManager.AcceptError = null;
                    }

                    m_configuration = value;

                    if (m_configuration != null)
                    {
                        m_configuration.CertificateManager.AcceptError = m_CertificateValidation;
                    }
                }
            }
        }

        /// <summary>
        /// The currently active session.
        /// </summary>
        public ISession Session => m_session;

        /// <summary>
        /// The number of seconds between reconnect attempts (0 means reconnect is disabled).
        /// </summary>
        /// <remarks>
        /// Kept for source compatibility. The reconnect is now driven by the reconnect policy
        /// of the <see cref="ManagedSession"/> the control creates, which this value no longer
        /// feeds into.
        /// </remarks>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ReconnectPeriod { get; set; } = DefaultReconnectPeriod;

        /// <summary>
        /// The discover timeout in ms.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int DiscoverTimeout { get; set; } = DefaultDiscoverTimeout;

        /// <summary>
        /// The session timeout in ms.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public uint SessionTimeout { get; set; } = DefaultSessionTimeout;

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
        /// <returns>The new session object.</returns>
        private async Task<ISession> ConnectInternalAsync(
            ITransportWaitingConnection connection,
            EndpointDescription endpointDescription,
            bool useSecurity,
            ITelemetryContext telemetry,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ConnectServerCtrl>();

            // disconnect from existing session.
            await InternalDisconnectAsync(ct);

            // select the best endpoint.
            if (endpointDescription == null)
            {
                endpointDescription = await CoreClientUtils.SelectEndpointAsync(m_configuration, connection, useSecurity, DiscoverTimeout, telemetry, ct);
            }

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            // the managed session brings its own connection state machine and reconnect policy,
            // so no SessionReconnectHandler is wired up here.
            m_session = await new ManagedSessionFactory(telemetry).CreateAsync(m_configuration, connection, endpoint, false, !DisableDomainCheck, (String.IsNullOrEmpty(SessionName)) ? m_configuration.ApplicationName : SessionName, sessionTimeout, UserIdentity, PreferredLocales, ct);

            // set up keep alive and connection state callbacks.
            AttachSession();

            // raise an event.
            DoConnectComplete(null);

            try
            {
                UpdateStatus(false, DateTime.Now, "Connected, loading complex type system.");
                var typeSystemLoader = ComplexTypeSystemClientExtensions.Create(m_session, m_telemetry);
                await typeSystemLoader.LoadAsync(ct: ct);
            }
            catch (Exception e)
            {
                UpdateStatus(true, DateTime.Now, "Connected, failed to load complex type system.");
                m_logger.LogWarning(e, "Failed to load complex type system.");
            }

            // return the new session.
            return m_session;
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <returns>The new session object.</returns>
        private async Task<ISession> ConnectInternalAsync(
            string serverUrl,
            bool useSecurity,
            ITelemetryContext telemetry,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ConnectServerCtrl>();

            // disconnect from existing session.
            await InternalDisconnectAsync(ct);

            // select the best endpoint.
            var endpointDescription = await CoreClientUtils.SelectEndpointAsync(m_configuration, serverUrl, useSecurity, DiscoverTimeout, telemetry, ct);
            var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            // the managed session brings its own connection state machine and reconnect policy,
            // so no SessionReconnectHandler is wired up here.
            m_session = await new ManagedSessionFactory(telemetry).CreateAsync(m_configuration, endpoint, false, !DisableDomainCheck, (String.IsNullOrEmpty(SessionName)) ? m_configuration.ApplicationName : SessionName, sessionTimeout == 0 ? DefaultSessionTimeout : sessionTimeout, UserIdentity, PreferredLocales, ct);

            // set up keep alive and connection state callbacks.
            AttachSession();

            // raise an event.
            DoConnectComplete(null);

            try
            {
                UpdateStatus(false, DateTime.Now, "Connected, loading complex type system.");
                var typeSystemLoader = ComplexTypeSystemClientExtensions.Create(m_session, m_telemetry);
                await typeSystemLoader.LoadAsync(ct: ct);
            }
            catch (Exception e)
            {
                UpdateStatus(true, DateTime.Now, "Connected, failed to load complex type system.");
                m_logger.LogError(e, "Failed to load complex type system.");
            }

            // return the new session.
            return m_session;
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <param name="serverUrl">The URL of a server endpoint.</param>
        /// <param name="useSecurity">Whether to use security.</param>
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
                serverUrl = UrlCB.Text;

                if (UrlCB.SelectedIndex >= 0)
                {
                    serverUrl = (string)UrlCB.SelectedItem;
                }

                useSecurity = UseSecurityCK.Checked;
            }
            else
            {
                UrlCB.Text = serverUrl;
                UseSecurityCK.Checked = useSecurity;
            }

            UpdateStatus(false, DateTime.Now, "Connecting [{0}]", serverUrl);

            return ConnectInternalAsync(serverUrl, useSecurity, telemetry, sessionTimeout, ct);
        }

        /// <summary>
        /// Create a new reverse connection.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="useSecurity"></param>
        public async Task<ISession> ConnectAsync(
            ITransportWaitingConnection connection,
            bool useSecurity,
            ITelemetryContext telemetry,
            int discoverTimeout = -1,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        {
            if (connection.EndpointUrl == null)
            {
                throw new ArgumentException("Endpoint URL is not valid.");
            }

            UrlCB.Text = connection.EndpointUrl.ToString();
            UseSecurityCK.Checked = useSecurity;

            EndpointDescription endpointDescription = null;

            if (!m_endpoints.TryGetValue(connection.EndpointUrl, out endpointDescription))
            {
                // Discovery uses the reverse connection and closes it
                // return and wait for next reverse hello
                endpointDescription = await CoreClientUtils.SelectEndpointAsync(m_configuration, connection, useSecurity, discoverTimeout, telemetry, ct);
                m_endpoints[connection.EndpointUrl] = endpointDescription;
                return null;
            }

            return await ConnectInternalAsync(connection, endpointDescription, UseSecurityCK.Checked, telemetry, sessionTimeout, ct);
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        public Task DisconnectAsync(CancellationToken ct = default)
        {
            UpdateStatus(false, DateTime.UtcNow, "Disconnected");
            return Task.Run(() => InternalDisconnectAsync(), ct);
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        private async Task InternalDisconnectAsync(CancellationToken ct = default)
        {
            // disconnect any existing session. Closing the managed session also stops its
            // connection state machine, so there is no separate reconnect handler to cancel.
            if (m_session != null)
            {
                DetachSession();
                await m_session.CloseAsync(10000, ct);
                m_session = null;
            }

            // raise an event.
            DoConnectComplete(null);
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        public void Disconnect()
        {
            UpdateStatus(false, DateTime.UtcNow, "Disconnected");

            // stop any reconnect operation.
            InternalDisconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        public void Discover(string hostName)
        {
            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            string endpointUrl = new DiscoverServerDlg().ShowDialog(m_configuration, hostName, m_telemetry);
            #pragma warning restore CA2000

            if (endpointUrl != null)
            {
                ServerUrl = endpointUrl;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Subscribes to the session events the control reports on.
        /// </summary>
        private void AttachSession()
        {
            m_session.KeepAlive += Session_KeepAlive;

            if (m_session is ManagedSession managedSession)
            {
                managedSession.ConnectionStateChanged += Session_ConnectionStateChanged;
            }
        }

        /// <summary>
        /// Unsubscribes from the session events the control reports on.
        /// </summary>
        private void DetachSession()
        {
            m_session.KeepAlive -= Session_KeepAlive;

            if (m_session is ManagedSession managedSession)
            {
                managedSession.ConnectionStateChanged -= Session_ConnectionStateChanged;
            }
        }

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

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        private async Task<EndpointDescription> SelectEndpointAsync(ITelemetryContext telemetry, CancellationToken ct = default)
        {
            try
            {
                Cursor = Cursors.WaitCursor;

                // determine the URL that was selected.
                string discoveryUrl = UrlCB.Text;

                if (UrlCB.SelectedIndex >= 0)
                {
                    discoveryUrl = (string)UrlCB.SelectedItem;
                }

                // return the selected endpoint.
                return await CoreClientUtils.SelectEndpointAsync(m_configuration, discoveryUrl, UseSecurityCK.Checked, DiscoverTimeout, telemetry, ct);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
        #endregion

        #region Event Handlers
        private delegate void UpdateStatusCallback(bool error, DateTime time, string status, params object[] arg);
        /// <summary>
        /// Updates the status control.
        /// </summary>
        /// <param name="error">Whether the status represents an error.</param>
        /// <param name="time">The time associated with the status.</param>
        /// <param name="status">The status message.</param>
        /// <param name="args">Arguments used to format the status message.</param>
        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
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
        /// Handles a keep alive event from a session.
        /// </summary>
        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new KeepAliveEventHandler(Session_KeepAlive), session, e);
                return;
            }

            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(session, m_session))
                {
                    return;
                }

                // the managed session starts the reconnect sequence itself, this only reports it.
                if (ServiceResult.IsBad(e.Status))
                {
                    UpdateStatus(true, e.CurrentTime, "Communication Error ({0})", e.Status);
                    return;
                }

                // update status.
                UpdateStatus(false, e.CurrentTime, "Connected [{0}]", session.Endpoint.EndpointUrl);

                // raise any additional notifications.
                m_KeepAliveComplete?.Invoke(this, e);
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
            string serverUrl = UrlCB.Text;

            if (UrlCB.SelectedIndex >= 0)
            {
                serverUrl = (string)UrlCB.SelectedItem;
            }

            bool useSecurity = UseSecurityCK.Checked;

            UpdateStatus(false, DateTime.Now, "Connecting [{0}]", serverUrl);

            try
            {
                // Await directly so that continuations resume on the UI thread's
                // SynchronizationContext, keeping all control access thread-safe.
                await this.ConnectAsync(m_telemetry, serverUrl, useSecurity);
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
        /// Handles the connection state changes reported by the managed session.
        /// </summary>
        /// <remarks>
        /// The managed session keeps the same <see cref="ISession"/> instance across a
        /// reconnect, so the control only has to report the transitions - there is no
        /// session to swap out as there was with the SessionReconnectHandler.
        /// </remarks>
        private void Session_ConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler<ConnectionStateChangedEventArgs>(Session_ConnectionStateChanged), sender, e);
                return;
            }

            try
            {
                // ignore callbacks from discarded sessions. The event may be raised by the
                // session or by the connection state machine behind it, so only a sender which
                // is a session is worth comparing.
                if (m_session == null || (sender is ISession sessionOfEvent && !Object.ReferenceEquals(sessionOfEvent, m_session)))
                {
                    return;
                }

                switch (e.NewState)
                {
                    case ConnectionState.Reconnecting:
                    case ConnectionState.Failover:
                    {
                        UpdateStatus(true, DateTime.UtcNow, "Reconnecting (attempt {0})", e.ReconnectAttempt);
                        m_ReconnectStarting?.Invoke(this, e);
                        break;
                    }

                    case ConnectionState.Connected:
                    {
                        UpdateStatus(false, DateTime.UtcNow, "Connected [{0}]", m_session.Endpoint.EndpointUrl);

                        if (e.PreviousState is ConnectionState.Reconnecting or ConnectionState.Failover)
                        {
                            m_ReconnectComplete?.Invoke(this, e);
                        }

                        break;
                    }

                    case ConnectionState.Disconnected:
                    {
                        UpdateStatus(true, DateTime.UtcNow, "Disconnected ({0})", e.Error);
                        break;
                    }
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
                if (!m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    return GuiUtils.HandleCertificateValidationError(this.FindForm(), certificate, error);
                }

                return true;
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
