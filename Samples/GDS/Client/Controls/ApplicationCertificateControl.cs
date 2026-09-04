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
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Gds.Client.Model;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Shows the certificate of the selected application and offers to have a new one
    /// issued by the Global Discovery Server.
    /// </summary>
    /// <remarks>
    /// The certificates, the stores, the files and the service calls belong to the
    /// <see cref="CertificateModel"/>. What is left here is the display, the timer which
    /// polls the request, and the two questions only a person can answer: which file to
    /// export a new key pair to, and whether an existing file may be overwritten.
    /// </remarks>
    public partial class ApplicationCertificateControl : UserControl
    {
        public ApplicationCertificateControl()
        {
            InitializeComponent();
        }

        private readonly CertificateModel m_model = new CertificateModel();
        private ITelemetryContext m_telemetry;
        private GlobalDiscoveryClientConfiguration m_configuration;
        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;

        public async Task InitializeAsync(
            GlobalDiscoveryClientConfiguration configuration,
            GlobalDiscoveryServerClient gds,
            ServerPushConfigurationClient server,
            RegisteredApplication application,
            bool isHttps,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            m_telemetry = telemetry;
            m_configuration = configuration;
            m_gds = gds;
            m_server = server;
            m_application = application;

            PrivateKeyPasswordTextBox.Text = string.Empty;

            CertificateRequestTimer.Enabled = false;
            RequestProgressLabel.Visible = false;
            ApplyChangesButton.Enabled = false;

            CertificateControl.ShowNothing();

            X509Certificate2 certificate = await m_model
                .InitializeAsync(gds, server, application, isHttps, telemetry, ct)
                .ConfigureAwait(true);

            if (certificate != null)
            {
                try
                {
                    CertificateControl.Tag = certificate.Thumbprint;
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        Parent,
                        "The certificate does not appear to be valid. Please check configuration settings.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    m_model.ForgetCertificate();
                    certificate = null;
                }
            }

            WarningLabel.Visible = certificate == null;

            if (certificate != null)
            {
                CertificateControl.ShowCertificate(certificate);
            }
        }

        /// <summary>
        /// Asks the Global Discovery Server for a new certificate and starts polling for it.
        /// </summary>
        private async void RequestNewButton_Click(object sender, EventArgs e)
        {
            try
            {
                CertificateRequestStart start = await m_model.StartRequestAsync();

                if (start.TrustListApplyChangesRequired)
                {
                    MessageBox.Show(
                        Parent,
                        "The updated Trust List was loaded however, the apply changes command must be sent before the server will update its Trust List.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    ApplyChangesButton.Enabled = true;
                }

                CertificateRequestTimer.Enabled = true;
                RequestProgressLabel.Visible = true;
                WarningLabel.Visible = false;
            }
            catch (Exception ex)
            {
                #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
                #pragma warning restore CA1849
            }
        }

        private async void NewKeyPairFromDerButton_Click(object sender, EventArgs e)
        {
            await CreatePfxFromCertificateInfoAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Opens the certificate list of the GDS (pull) or of the managed server (push).
        /// </summary>
        /// <remarks>
        /// OPC 10000-12 v1.05.07 added a <c>GetCertificates</c> Method to both models
        /// (§7.9.8 and §7.10.8), so a client no longer has to infer what a server holds from
        /// the certificate its endpoint happens to present. The dialog also drives the
        /// per-slot Methods that came with it - see
        /// <see cref="Controls.CertificateManagementDialog"/>. A push Method only stages its
        /// change, so a staged change leaves <c>Apply Changes</c> enabled here.
        /// </remarks>
        private void CertificatesButton_Click(object sender, EventArgs e)
        {
            try
            {
                #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                bool staged = new Controls.CertificateManagementDialog().ShowDialog(
                    Parent,
                    m_gds,
                    m_server,
                    m_application,
                    m_telemetry);
                #pragma warning restore CA2000

                if (staged)
                {
                    ApplyChangesButton.Enabled = true;
                }
            }
            catch (Exception exception)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, exception);
            }
        }

        /// <summary>
        /// Creates a new .pfx (with a fresh public/private key pair) from the information
        /// contained in the currently loaded certificate.
        /// </summary>
        /// <remarks>
        /// The subject name, application URI(s) and domain names (Subject Alternative Name)
        /// of the loaded certificate are reused, while a brand new RSA key pair is generated.
        /// This makes it possible to turn a public-only certificate (e.g. loaded from a
        /// <c>.der</c> file) into a usable <c>.pfx</c> that carries a private key.
        /// </remarks>
        private async Task CreatePfxFromCertificateInfoAsync()
        {
            try
            {
                if (m_model.ApplicationCertificate == null)
                {
                    MessageBox.Show(
                        Parent,
                        "No certificate is loaded. Load a public certificate (e.g. a .der file) first.",
                        Parent?.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string subjectName = m_model.ApplicationCertificate.Subject;
                X509Certificate2 newCertificate = m_model.CreateKeyPairFromCertificateInfo();

                string savePath;

                #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                using (SaveFileDialog dialog = new SaveFileDialog {
                    Title = "Save new PFX certificate",
                    Filter = "PKCS#12 files (*.pfx)|*.pfx|All files (*.*)|*.*",
                    DefaultExt = "pfx",
                    FileName = CertificateModel.GetDefaultPfxFileName(subjectName),
                    OverwritePrompt = true
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        newCertificate.Dispose();
                        return;
                    }

                    savePath = dialog.FileName;
                }
                #pragma warning restore CA1849

                await m_model.ExportPfxAsync(newCertificate, savePath).ConfigureAwait(true);

                m_model.AdoptCertificate(newCertificate);

                CertificateControl.ShowCertificate(newCertificate);
                WarningLabel.Visible = false;

                MessageBox.Show(
                    Parent,
                    "A new .pfx with a fresh key pair was created from the certificate information and saved to:\n" + savePath,
                    Parent?.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
                #pragma warning restore CA1849
            }
        }

        /// <summary>
        /// Asks whether the certificate request has finished, once a second.
        /// </summary>
        /// <remarks>
        /// A request which is still running answers Pending and the timer keeps going.
        /// Overwriting an existing certificate file is the one decision the model hands
        /// back here, because only a person can make it.
        /// </remarks>
        private async void CertificateRequestTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                CertificateRequestResult result = await m_model
                    .FinishRequestAsync(ShouldReplaceFile)
                    .ConfigureAwait(true);

                if (result.State == CertificateRequestState.Pending)
                {
                    return;
                }

                CertificateRequestTimer.Enabled = false;
                RequestProgressLabel.Visible = false;

                if (result.ApplyChangesRequired)
                {
                    MessageBox.Show(
                        Parent,
                        "The certificate was updated, however, the apply changes command must be sent before the server will use the new certificate.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    ApplyChangesButton.Enabled = true;
                }

                CertificateControl.ShowCertificate(m_model.ApplicationCertificate);
            }
            catch (Exception exception)
            {
                if (exception is ServiceResultException sre && sre.StatusCode == StatusCodes.BadNothingToDo)
                {
                    return;
                }

                RequestProgressLabel.Visible = false;
                CertificateRequestTimer.Enabled = false;
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, exception);
            }
        }

        /// <summary>
        /// Asks whether an existing certificate file may be replaced.
        /// </summary>
        private bool ShouldReplaceFile(string path)
        {
            return MessageBox.Show(
                Parent,
                "Replace certificate " + path + "?",
                Parent.Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Exclamation) == DialogResult.Yes;
        }

        /// <summary>
        /// Tells the server to start using the certificate which was pushed to it.
        /// </summary>
        /// <remarks>
        /// A server which restarts itself to apply the changes is the expected outcome, not
        /// a failure, and the model reports it rather than throwing it.
        /// </remarks>
        private async void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            ApplyChangesButton.Enabled = false;

            try
            {
                await m_model.ApplyChangesAsync();
            }
            catch (Exception exception)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Parent.Text, exception);
            }
        }

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.CornflowerBlue;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.MidnightBlue;
        }

        private void PrivateKeyPasswordTextBox_TextChanged(object sender, EventArgs e)
        {
            m_model.CertificatePassword = PrivateKeyPasswordTextBox.Text;
        }
    }
}
