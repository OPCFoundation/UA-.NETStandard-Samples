/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.UserAuthenticationClient.Model;

namespace Quickstarts.UserAuthenticationClient
{
    /// <summary>
    /// The main form of the user authentication Quickstart client.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="UserAuthenticationClientModel"/>, which reads and writes the log file
    /// path and changes the identity of the session. The window only enables the tab of
    /// each kind of token the server accepts, translates each button into one model call,
    /// and puts what the server answered into the status bar.
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
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62565/Quickstarts/UserAuthenticationServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new UserAuthenticationClientModel(telemetry);
            m_model.Error += Model_Error;

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
        private readonly ITelemetryContext m_telemetry;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Detached asynchronously by MainForm_FormClosing, which cannot await a DisposeAsync.")]
        private readonly UserAuthenticationClientModel m_model;
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

                if (session == null)
                {
                    await m_model.DetachAsync();
                    SetAvailableUserTokens(null);
                    return;
                }

                await m_model.AttachAsync(session);

                // set the available tokens.
                SetAvailableUserTokens(m_model.UserTokenPolicies);

                LogFilePathTB.Text = await m_model.ReadLogFilePathAsync();
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
                // this sample does not subscribe to anything: it demonstrates the user
                // identity tokens, and the managed session reconnects on its own.
                await m_model.NotifyReconnectCompletedAsync();
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
        /// Changes the identity of the session to the user name and password in the boxes.
        /// </summary>
        private async void UserNameImpersonateBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                Report(await m_model.ImpersonateUserNameAsync(
                    UserNameTB.Text,
                    PasswordTB.Text,
                    UserAuthenticationClientModel.ParseLocales(PreferredLocalesTB.Text)));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Changes the identity of the session to the certificate in the boxes.
        /// </summary>
        private async void CertificateImpersonateBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                Report(await m_model.ImpersonateWithCertificateAsync(
                    CertificateTB.Text,
                    CertificatePasswordTB.Text,
                    UserAuthenticationClientModel.ParseLocales(PreferredLocalesTB.Text)));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Drops the identity of the session.
        /// </summary>
        private async void AnonymousImpersonateBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                Report(await m_model.ImpersonateAnonymouslyAsync(
                    UserAuthenticationClientModel.ParseLocales(PreferredLocalesTB.Text)));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Changes the identity of the session to a Kerberos token.
        /// </summary>
        private async void KerberosImpersonateBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                Report(await m_model.ImpersonateWithKerberosAsync(
                    UserAuthenticationClientModel.ParseLocales(PreferredLocalesTB.Text)));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Writes the log file path in the box to the server.
        /// </summary>
        private async void ChangeLogFileBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // the refusal is the interesting outcome here, not an error: the node manager
                // answers the user access level per session, and an identity which may not
                // write is told so by the write handler as well
                Report(await m_model.WriteLogFilePathAsync(LogFilePathTB.Text));
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
        /// Enables the tab of each kind of token the server accepts.
        /// </summary>
        /// <param name="policies">The user token policies of the endpoint, or null when disconnected.</param>
        private void SetAvailableUserTokens(IReadOnlyList<UserTokenPolicy> policies)
        {
            AnonymousTAB.Enabled = false;
            UserNameTAB.Enabled = false;
            CertificateTAB.Enabled = false;
            KerberosTAB.Enabled = false;

            if (policies == null)
            {
                return;
            }

            foreach (UserTokenPolicy policy in policies)
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

        /// <summary>
        /// Reports what the server answered to an operation the user asked for.
        /// </summary>
        /// <remarks>
        /// The status bar rather than a message box: the whole point of this sample is to try
        /// the same operation as one identity after another and compare what the server says,
        /// and a modal dialog between every click makes that tedious. It also keeps the
        /// buttons drivable from a test, which a modal dialog does not.
        /// </remarks>
        private void Report(OperationResult result)
        {
            ActionStatusLB.Text = result.ToString();
            ActionStatusLB.ForeColor = result.Succeeded ? Color.Empty : Color.Red;
        }
        #endregion
    }
}
