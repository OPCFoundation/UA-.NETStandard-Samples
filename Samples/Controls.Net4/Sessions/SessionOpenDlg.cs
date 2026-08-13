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

using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Client.Controls;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Opc.Ua.Sample.Controls
{
    public partial class SessionOpenDlg : Form
    {
        #region Constructors
        public SessionOpenDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private const string m_BrowseCertificates = "<Browse...>";
        private static uint m_Counter = 0;
        private IList<string> m_preferredLocales;
        private bool m_checkDomain = true;
        private X509Certificate2 m_userCertificate;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowDialog(Session session, IList<string> preferredLocales)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_session = session;
            m_preferredLocales = preferredLocales;

            UserIdentityTypeCB.Items.Clear();

            foreach (UserTokenPolicy policy in session.Endpoint.UserIdentityTokens)
            {
                UserIdentityTypeCB.Items.Add(policy.TokenType);
            }

            if (UserIdentityTypeCB.Items.Count == 0)
            {
                UserIdentityTypeCB.Items.Add(UserTokenType.UserName);
            }

            UserIdentityTypeCB.SelectedIndex = 0;

            SessionNameTB.Text = session.SessionName;

            if (String.IsNullOrEmpty(SessionNameTB.Text))
            {
                SessionNameTB.Text = Utils.Format("MySession {0}", Utils.IncrementIdentifier(ref m_Counter));
            }

            if (session.Identity != null)
            {
                UserIdentityTypeCB.SelectedItem = session.Identity.TokenType;
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion

        private void UserIdentityTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                UserTokenType tokenType = (UserTokenType)UserIdentityTypeCB.SelectedItem;

                UserNameCB.Items.Clear();

                UserNameCB.Enabled = true;
                PasswordTB.Enabled = true;

                // reset any previously selected user certificate.
                m_userCertificate = null;

                // allow use to browse certificate stores.
                if (tokenType == UserTokenType.Certificate)
                {
                    UserNameCB.Items.Add(m_BrowseCertificates);
                    UserNameCB.SelectedIndex = 0;
                }

                // populate list.
                foreach (IUserIdentity identity in m_session.IdentityHistory)
                {
                    if (identity.TokenType == tokenType)
                    {
                        UserNameCB.Items.Add(identity.DisplayName);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Opens a certificate picker when the user selects the &lt;Browse...&gt; entry.
        /// </summary>
        private void UserNameCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if ((UserTokenType)UserIdentityTypeCB.SelectedItem != UserTokenType.Certificate)
                {
                    return;
                }

                if (!Object.Equals(UserNameCB.SelectedItem, m_BrowseCertificates))
                {
                    return;
                }

                X509Certificate2 certificate = BrowseForCertificate();

                if (certificate != null)
                {
                    m_userCertificate = certificate;

                    string displayName = certificate.Subject;

                    int index = UserNameCB.Items.IndexOf(displayName);

                    if (index < 0)
                    {
                        index = UserNameCB.Items.Add(displayName);
                    }

                    UserNameCB.SelectedIndex = index;
                }
                else
                {
                    // restore selection to the browse entry if nothing was picked.
                    UserNameCB.SelectedIndex = UserNameCB.Items.IndexOf(m_BrowseCertificates);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Prompts the user to select a certificate (with private key) from the current user store.
        /// </summary>
        private X509Certificate2 BrowseForCertificate()
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);

                // only offer certificates that have a private key available.
                var candidates = new X509Certificate2Collection();

                foreach (X509Certificate2 certificate in store.Certificates)
                {
                    if (certificate.HasPrivateKey)
                    {
                        candidates.Add(certificate);
                    }
                }

                if (candidates.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "No certificates with a private key were found in the CurrentUser\\My store.",
                        this.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return null;
                }

                X509Certificate2Collection selection = X509Certificate2UI.SelectFromCollection(
                    candidates,
                    "Select User Certificate",
                    "Select the certificate to use for user authentication.",
                    X509SelectionFlag.SingleSelection,
                    this.Handle);

                if (selection.Count > 0)
                {
                    return selection[0];
                }

                return null;
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                // construct the user identity.
                IUserIdentity identity = null;

                UserTokenType tokenType = (UserTokenType)UserIdentityTypeCB.SelectedItem;

                if (tokenType == UserTokenType.UserName)
                {
                    string username = (string)UserNameCB.SelectedItem;

                    if (String.IsNullOrEmpty(username))
                    {
                        username = UserNameCB.Text;
                    }

                    if (!String.IsNullOrEmpty(username) || !String.IsNullOrEmpty(PasswordTB.Text))
                    {
                        #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                        identity = new UserIdentity(username, Encoding.UTF8.GetBytes(PasswordTB.Text));
                        #pragma warning restore CA2000
                    }
                }
                else if (tokenType == UserTokenType.Certificate)
                {
                    if (m_userCertificate == null)
                    {
                        MessageBox.Show(
                            this,
                            "Select a user certificate using the <Browse...> entry before opening the session.",
                            this.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    #pragma warning disable CA2000 // Justification: UserIdentity ownership is transferred to the active session.
                    identity = new UserIdentity(new X509IdentityToken { CertificateData = m_userCertificate.RawData.ToByteString() });
                    #pragma warning restore CA2000
                }

                Cursor = Cursors.WaitCursor;

                Task.Run(() => OpenAsync(m_session, SessionNameTB.Text, identity, m_preferredLocales, m_checkDomain));

                CancelBTN.Enabled = false;
                OkBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Reports the results of the open session operation.
        /// </summary>
        private void OpenComplete(object e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new WaitCallback(OpenComplete), e);
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            try
            {
                Cursor = Cursors.Default;

                ServiceResultException sre = e as ServiceResultException;
                if (sre != null)
                {
                    if (m_checkDomain && sre.StatusCode == StatusCodes.BadCertificateHostNameInvalid)
                    {
                        StringBuilder buffer = new StringBuilder();

                        buffer.AppendFormat(sre.Message);
                        buffer.AppendFormat("\r\n\r\nRetry without certificate hostname validation?");

                        DialogResult result = MessageBox.Show(
                            buffer.ToString(),
                            "Exception: BadCertificateHostNameInvalid",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                        if (result == DialogResult.Yes)
                        {
                            m_checkDomain = false;
                            OkBTN.Enabled = true;
                            OkBTN.PerformClick();
                            return;
                        }

                        DialogResult = DialogResult.OK;
                        return;
                    }
                }

                if (e != null)
                {
                    GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), (Exception)e);
                }

                if (m_session.Connected && m_session.SessionTimeout < 1000)
                {
                    DialogResult result = MessageBox.Show(
                        "Warning: the session time out might be too small: " + m_session.SessionTimeout,
                        "Session revised timeout",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                DialogResult = DialogResult.OK;
            }
            finally
            {
                CancelBTN.Enabled = true;
                OkBTN.Enabled = true;
            }
        }

        /// <summary>
        /// Asynchronously open the session.
        /// </summary>
        private async Task OpenAsync(Session session, string sessionName, IUserIdentity identity, IList<string> preferredLocales, bool? checkDomain, CancellationToken ct = default)
        {
            try
            {
                // open the session.
                await session.OpenAsync(sessionName, (uint)session.SessionTimeout, identity, preferredLocales.ToArray(), checkDomain ?? true, false, ct);

                var typeSystemLoader = new ComplexTypeSystemFactory(session.MessageContext.Telemetry).Create(session);
                _ = await typeSystemLoader.LoadAsync(ct: ct);

                OpenComplete(null);
            }
            catch (Exception exception)
            {
                OpenComplete(exception);
            }
        }
    }
}
