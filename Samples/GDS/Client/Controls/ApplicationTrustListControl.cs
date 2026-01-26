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
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Gds;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using System.Threading;

namespace Opc.Ua.Gds.Client
{
    public partial class ApplicationTrustListControl : UserControl
    {
        public ApplicationTrustListControl()
        {
            InitializeComponent();
            TrustListMasksComboBox.DataSource = Enum.GetValues(typeof(TrustListMasks));
            TrustListMasksComboBox.SelectedItem = TrustListMasks.All;
        }

        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;
        private string m_trustListStorePath;
        private string m_issuerListStorePath;
        private ITelemetryContext m_telemetry;

        public async Task Initialize(GlobalDiscoveryServerClient gds, ServerPushConfigurationClient server, RegisteredApplication application, bool isHttps, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            m_gds = gds;
            m_server = server;
            m_application = application;
            m_telemetry = telemetry;

            // display local trust list.
            if (application != null)
            {
                m_trustListStorePath = (isHttps) ? m_application.HttpsTrustListStorePath : m_application.TrustListStorePath;
                m_issuerListStorePath = (isHttps) ? m_application.HttpsIssuerListStorePath : m_application.IssuerListStorePath;
                await CertificateStoreControl.Initialize(telemetry, m_trustListStorePath, m_issuerListStorePath, null, ct);
                MergeWithGdsButton.Enabled = !String.IsNullOrEmpty(m_trustListStorePath) || m_application.RegistrationType == RegistrationType.ServerPush;
            }

            ApplyChangesButton.Enabled = false;
        }

        private async void ReloadTrustListButton_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_application != null)
                {
                    if (m_application.RegistrationType == RegistrationType.ServerPush)
                    {
                        TrustListMasks masks;

                        if (!Enum.TryParse(TrustListMasksComboBox.SelectedItem.ToString(), out masks))
                            masks = TrustListMasks.All;
                        var trustList = await m_server.ReadTrustListAsync(masks);
                        var rejectedList = await m_server.GetRejectedListAsync();
                        CertificateStoreControl.Initialize(trustList, rejectedList, true);
                    }
                    else
                    {
                        await CertificateStoreControl.Initialize(m_telemetry, m_trustListStorePath, m_issuerListStorePath, null);
                    }
                }
                else
                {
                    await CertificateStoreControl.Initialize(m_telemetry, null, null, null);
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
            }
        }

        private async void MergeWithGdsButton_Click(object sender, EventArgs e)
        {
            try
            {
                await PullFromGdsAsync(false);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
            }
        }

        private async void PullFromGdsButton_Click(object sender, EventArgs e)
        {
            try
            {
                await PullFromGdsAsync(true);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
            }
        }

        private async Task DeleteExistingFromStoreAsync(string storePath, CancellationToken ct = default)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return;
            }

            var certificateStoreIdentifier = new CertificateStoreIdentifier(storePath);
            using (var store = certificateStoreIdentifier.OpenStore(m_telemetry))
            {
                X509Certificate2Collection certificates = await store.EnumerateAsync(ct);
                foreach (var certificate in certificates)
                {
                    List<string> fields = X509Utils.ParseDistinguishedName(certificate.Subject);

                    if (fields.Contains("CN=UA Local Discovery Server"))
                    {
                        continue;
                    }


                    if (store is DirectoryCertificateStore ds)
                    {
                        if (ds.GetPrivateKeyFilePath(certificate.Thumbprint) != null)
                        {
                            continue;
                        }

                        string path = Utils.GetAbsoluteFilePath(m_application.CertificatePublicKeyPath, true, false, false);

                        if (path != null)
                        {
                            if (String.Compare(path, ds.GetPublicKeyFilePath(certificate.Thumbprint), StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                continue;
                            }
                        }

                        path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);

                        if (path != null)
                        {
                            if (String.Compare(path, ds.GetPrivateKeyFilePath(certificate.Thumbprint), StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                continue;
                            }
                        }
                    }

                    await store.DeleteAsync(certificate.Thumbprint, ct);
                }
            }
        }

        private async Task PullFromGdsAsync(bool deleteBeforeAdd, CancellationToken ct = default)
        {
            try
            {
                NodeId trustListId = await m_gds.GetTrustListAsync(m_application.ApplicationId, NodeId.Null, ct);

                if (trustListId == null)
                {
                    await CertificateStoreControl.Initialize(m_telemetry, null, null, null, ct);
                    return;
                }

                var trustList = await m_gds.ReadTrustListAsync(trustListId, 0, ct);

                if (m_application.RegistrationType == RegistrationType.ServerPush)
                {
                    CertificateStoreControl.Initialize(trustList, null, deleteBeforeAdd);

                    MessageBox.Show(
                        Parent,
                        "The trust list (include CRLs) was downloaded from the GDS. It now has to be pushed to the Server.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }

                if (!String.IsNullOrEmpty(m_trustListStorePath))
                {
                    if (deleteBeforeAdd)
                    {
                        await DeleteExistingFromStoreAsync(m_trustListStorePath, ct);
                        await DeleteExistingFromStoreAsync(m_issuerListStorePath, ct);
                    }
                }

                if (!String.IsNullOrEmpty(m_trustListStorePath))
                {
                    var certificateStoreIdentifier = new CertificateStoreIdentifier(m_trustListStorePath);
                    using (ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry))
                    {
                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.TrustedCertificates) != 0)
                        {
                            foreach (var certificate in trustList.TrustedCertificates)
                            {
                                var x509 = new X509Certificate2(certificate);

                                X509Certificate2Collection certs = await store.FindByThumbprintAsync(x509.Thumbprint, ct);
                                if (certs.Count == 0)
                                {
                                    await store.AddAsync(x509, ct: ct);
                                }
                            }
                        }

                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.TrustedCrls) != 0)
                        {
                            foreach (var crl in trustList.TrustedCrls)
                            {
                                await store.AddCRLAsync(new X509CRL(crl), ct);
                            }
                        }
                    }
                }

                if (!String.IsNullOrEmpty(m_application.IssuerListStorePath))
                {
                    var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.IssuerListStorePath);
                    using (ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry))
                    {
                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.IssuerCertificates) != 0)
                        {
                            foreach (var certificate in trustList.IssuerCertificates)
                            {
                                var x509 = new X509Certificate2(certificate);

                                X509Certificate2Collection certs = await store.FindByThumbprintAsync(x509.Thumbprint, ct);
                                if (certs.Count == 0)
                                {
                                    await store.AddAsync(x509, ct: ct);
                                }
                            }
                        }

                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.IssuerCrls) != 0)
                        {
                            foreach (var crl in trustList.IssuerCrls)
                            {
                                await store.AddCRLAsync(new X509CRL(crl), ct);
                            }
                        }
                    }
                }

                await CertificateStoreControl.Initialize(m_telemetry, m_trustListStorePath, m_issuerListStorePath, null, ct);

                MessageBox.Show(
                    Parent,
                    "The trust list (include CRLs) was downloaded from the GDS and saved locally.",
                    Parent.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private async void PushToServerButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_application != null)
                {
                    if (m_application.RegistrationType == RegistrationType.ServerPush)
                    {
                        var trustList = CertificateStoreControl.GetTrustLists();

                        bool applyChanges = await m_server.UpdateTrustListAsync(trustList);

                        if (applyChanges)
                        {
                            MessageBox.Show(
                                Parent,
                                "The trust list was updated, however, the apply changes command must be sent before the server will use the new trust list.",
                                Parent.Text,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);

                            ApplyChangesButton.Enabled = true;
                        }
                    }
                }
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

        private async void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            try
            {
                await m_server.ApplyChangesAsync();
            }
            catch (Exception exception)
            {
                var se = exception as ServiceResultException;

                if (se == null || se.StatusCode != StatusCodes.BadServerHalted)
                {
                    Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Parent.Text, exception);
                }
            }

            try
            {
                await m_server.DisconnectAsync();
            }
            catch
            {
                // ignore.
            }
        }
    }
}
