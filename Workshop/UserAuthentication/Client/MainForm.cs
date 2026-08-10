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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Quickstarts.UserAuthenticationClient
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
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

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62565/Quickstarts/UserAuthenticationServer";
            this.Text = m_configuration.ApplicationName;
            m_telemetry = telemetry;

            UserNameTB.Text = "Operator";
            PreferredLocalesTB.Text = "de,es,en";
            SetAvailableUserTokens(null);

            KerberosUserNameTB.Text = "Operator";
            KerberosPasswordTB.Text = "operator";
            KerberosDomainTB.Text = "GEMS";

            UserNameTokenLB.Text =
            "UserName/Password tokens can be used with any password based system including Windows.\r\n" +
            "The main disadvantage is client must trust the server with its password.\r\n" +
            "Password must be encrypted when sent to the server.";

            AnonymousTokenLB.Text =
            "Anonymous tokens mean no user is associated with the session.\r\n" +
            "It is used by servers that do not require user authentication.\r\n" +
            "It can also be used to logout while keeping a session active.";

            CertificateTokenLB.Text =
            "Certificate tokens use a X509 certicate associated with a user.\r\n" +
            "These could come from a smart card and identify a user account.\r\n" +
            "Tokens must be signed when sent to the server.";

            KereberosTokenLB.Text =
            "Kereberos tokens allow use of Windows domain credentials without\r\n" +
            "requiring the client to explictly enter a password.\r\n" +
            "The token must be encrypted when sent to the server.";
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private bool m_connectedOnce;

        // hard code for convience only valid when connecting to UserAuthenticationServer.
        private NodeId m_logFileNodeId = new NodeId(2, 2);
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
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
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
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    return;
                }

                // set a suitable initial state.
                if (!m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                m_session.RenewUserIdentity += new RenewUserIdentityEventHandler(Session_RenewUserIdentity);

                // set the available tokens.
                SetAvailableUserTokens(m_session.ConfiguredEndpoint.Description);
                await ReadLogFilePathAsync();
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
                m_session = ConnectServerCTRL.Session;

                foreach (Subscription subscription in m_session.Subscriptions)
                {
                    m_subscription = subscription;
                    break;
                }

                foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                {
                    m_monitoredItem = monitoredItem;
                    break;
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
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a SAML token for the specified email address.
        /// </summary>
        /// <remarks>
        /// SAML token creation relied on System.IdentityModel (WIF), which is not available on
        /// modern .NET. This path is stubbed out under the .NET 10 upgrade (Option C).
        /// </remarks>
        public static Task<UserIdentity> CreateSAMLTokenAsync(string emailAddress, CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "SAML tokens (System.IdentityModel / WIF) are not supported on this platform.");
        }

        private IUserIdentity GetKerberosToken()
        {
            // Kerberos WS-Security token providers (System.IdentityModel.Selectors) are not
            // available on modern .NET. This path is stubbed out under the .NET 10 upgrade (Option C).
            throw new NotSupportedException(
                "Kerberos issued tokens (System.IdentityModel) are not supported on this platform.");
        }

        /// <summary>
        /// Called when a Kerberos token needs to be renewed before reconnect.
        /// </summary>
        IUserIdentity Session_RenewUserIdentity(ISession session, IUserIdentity identity)
        {
            if (identity == null || identity.TokenType != UserTokenType.IssuedToken)
            {
                return identity;
            }

            return GetKerberosToken();
        }

        /// <summary>
        /// Sets the available user tokens.
        /// </summary>
        /// <param name="endpointDescription">The endpoint description.</param>
        private void SetAvailableUserTokens(EndpointDescription endpointDescription)
        {
            AnonymousTAB.Enabled = false;
            UserNameTAB.Enabled = false;
            CertificateTAB.Enabled = false;
            KerberosTAB.Enabled = false;

            if (endpointDescription == null)
            {
                return;
            }

            foreach (UserTokenPolicy policy in endpointDescription.UserIdentityTokens)
            {
                if (policy.TokenType == UserTokenType.Anonymous)
                {
                    if (!AnonymousTAB.Enabled)
                    {
                        AnonymousTAB.Tag = policy;
                        AnonymousTAB.Enabled = true;
                    }
                }

                if (policy.TokenType == UserTokenType.UserName)
                {
                    if (!UserNameTAB.Enabled)
                    {
                        UserNameTAB.Tag = policy;
                        UserNameTAB.Enabled = true;
                    }
                }

                if (policy.TokenType == UserTokenType.Certificate)
                {
                    if (!CertificateTAB.Enabled)
                    {
                        CertificateTAB.Tag = policy;
                        CertificateTAB.Enabled = true;
                    }
                }

                if (policy.TokenType == UserTokenType.IssuedToken)
                {
                    if (!KerberosTAB.Enabled)
                    {
                        if (policy.IssuedTokenType == "http://docs.oasis-open.org/wss/oasis-wss-kerberos-token-profile-1.1")
                        {
                            KerberosTAB.Tag = policy;
                            KerberosTAB.Enabled = true;
                        }
                    }
                }
            }
        }
        #endregion

        #region Event Handlers
        private void UserNameImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

#pragma warning disable CA2000 // Justification: UserIdentity ownership is transferred to the active session.
                UserIdentity identity = new UserIdentity(UserNameTB.Text, Encoding.UTF8.GetBytes(PasswordTB.Text));
#pragma warning restore CA2000
                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void CertificateImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // load the certficate.
#pragma warning disable CA2000, SYSLIB0057 // Justification: Certificate ownership is transferred to UserIdentity; sample targets frameworks without a common loader API.
                X509Certificate2 certificate = new X509Certificate2(
                    CertificateTB.Text,
                    CertificatePasswordTB.Text,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
#pragma warning restore CA2000, SYSLIB0057

                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

#pragma warning disable CA2000 // Justification: UserIdentity ownership is transferred to the active session.
                UserIdentity identity = new UserIdentity(new X509IdentityToken { CertificateData = certificate.RawData.ToByteString() });
#pragma warning restore CA2000
                string[] preferredLocales = PreferredLocalesTB.Text.Split([','], StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void AnonymousImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
#pragma warning disable CA2000 // Justification: UserIdentity and token ownership is transferred to the active session.
                m_session.UpdateSession(new UserIdentity(new AnonymousIdentityToken()), preferredLocales);
#pragma warning restore CA2000

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private void KerberosImpersonateBTN_Click(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // request the token.
                IUserIdentity identity = GetKerberosToken();

                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                string[] preferredLocales = PreferredLocalesTB.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_session.UpdateSession(identity, preferredLocales);

                MessageBox.Show("User identity changed.", "Impersonate User", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// Reads the log file path.
        /// </summary>
        private async Task ReadLogFilePathAsync(CancellationToken ct = default)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                ReadValueId value = new ReadValueId();
                value.NodeId = m_logFileNodeId;
                value.AttributeId = Attributes.Value;

                List<ReadValueId> valuesToRead = new List<ReadValueId>();
                valuesToRead.Add(value);

                ReadResponse response = await m_session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    valuesToRead,
                    ct);

                ResponseHeader responseHeader = response.ResponseHeader;
                List<DataValue> results = response.Results.ToList();
                List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
                }

                LogFilePathTB.Text = results[0].GetValue<string>("");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        private async void ChangeLogFileBTN_ClickAsync(object sender, EventArgs e)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                // want to get error text for this call.
                m_session.ReturnDiagnostics = DiagnosticsMasks.All;

                WriteValue value = new WriteValue();
                value.NodeId = m_logFileNodeId;
                value.AttributeId = Attributes.Value;
                value.Value = new DataValue(new Variant(LogFilePathTB.Text));

                List<WriteValue> valuesToWrite = new List<WriteValue>();
                valuesToWrite.Add(value);

                WriteResponse response = await m_session.WriteAsync(
                    null,
                    valuesToWrite,
                    default);

                ResponseHeader responseHeader = response.ResponseHeader;
                List<StatusCode> results = response.Results.ToList();
                List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, valuesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                m_session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }
        #endregion
    }
}
