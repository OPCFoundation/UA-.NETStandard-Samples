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

        public void Initialize(GlobalDiscoveryServerClient gds, ServerPushConfigurationClient server, RegisteredApplication application, bool isHttps)
        {
            m_gds = gds;
            m_server = server;
            m_application = application;

            // display local trust list.
            if (application != null)
            {
                m_trustListStorePath = (isHttps) ? m_application.HttpsTrustListStorePath : m_application.TrustListStorePath;
                m_issuerListStorePath = (isHttps) ? m_application.HttpsIssuerListStorePath : m_application.IssuerListStorePath;
                CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);
                MergeWithGdsButton.Enabled = !String.IsNullOrEmpty(m_trustListStorePath) || m_application.RegistrationType == RegistrationType.ServerPush;
            }

            ApplyChangesButton.Enabled = false;
        }

        private void ReloadTrustListButton_Click(object sender, EventArgs e)
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
                        var trustList = m_server.ReadTrustListAsync(masks).GetAwaiter().GetResult();
                        var rejectedList = m_server.GetRejectedListAsync().GetAwaiter().GetResult();
                        CertificateStoreControl.Initialize(trustList, rejectedList, true);
                    }
                    else
                    {
                        CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);
                    }
                }
                else
                {
                    CertificateStoreControl.Initialize(null, null, null);
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void MergeWithGdsButton_Click(object sender, EventArgs e)
        {
            PullFromGds(false);
        }

        private void PullFromGdsButton_Click(object sender, EventArgs e)
        {
            PullFromGds(true);
        }

        private async Task DeleteExistingFromStore(string storePath)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return;
            }

            var certificateStoreIdentifier = new CertificateStoreIdentifier(storePath);
            using (var store = certificateStoreIdentifier.OpenStore())
            {
                X509Certificate2Collection certificates = await store.EnumerateAsync();
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

                    await store.DeleteAsync(certificate.Thumbprint);
                }
            }
        }

        private void PullFromGds(bool deleteBeforeAdd)
        {
            try
            {
                NodeId trustListId = m_gds.GetTrustListAsync(m_application.ApplicationId, NodeId.Null).GetAwaiter().GetResult();

                if (trustListId == null)
                {
                    CertificateStoreControl.Initialize(null, null, null);
                    return;
                }

                var trustList = m_gds.ReadTrustListAsync(trustListId).GetAwaiter().GetResult();

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
                        DeleteExistingFromStore(m_trustListStorePath).Wait();
                        DeleteExistingFromStore(m_issuerListStorePath).Wait(); ;
                    }
                }

                if (!String.IsNullOrEmpty(m_trustListStorePath))
                {
                    var certificateStoreIdentifier = new CertificateStoreIdentifier(m_trustListStorePath);
                    using (ICertificateStore store = certificateStoreIdentifier.OpenStore())
                    {
                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.TrustedCertificates) != 0)
                        {
                            foreach (var certificate in trustList.TrustedCertificates)
                            {
                                var x509 = new X509Certificate2(certificate);

                                X509Certificate2Collection certs = store.FindByThumbprintAsync(x509.Thumbprint).Result;
                                if (certs.Count == 0)
                                {
                                    store.AddAsync(x509).Wait();
                                }
                            }
                        }

                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.TrustedCrls) != 0)
                        {
                            foreach (var crl in trustList.TrustedCrls)
                            {
                                store.AddCRLAsync(new X509CRL(crl)).GetAwaiter().GetResult();
                            }
                        }
                    }
                }

                if (!String.IsNullOrEmpty(m_application.IssuerListStorePath))
                {
                    var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.IssuerListStorePath);
                    using (ICertificateStore store = certificateStoreIdentifier.OpenStore())
                    {
                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.IssuerCertificates) != 0)
                        {
                            foreach (var certificate in trustList.IssuerCertificates)
                            {
                                var x509 = new X509Certificate2(certificate);

                                X509Certificate2Collection certs = store.FindByThumbprintAsync(x509.Thumbprint).Result;
                                if (certs.Count == 0)
                                {
                                    store.AddAsync(x509).Wait();
                                }
                            }
                        }

                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.IssuerCrls) != 0)
                        {
                            foreach (var crl in trustList.IssuerCrls)
                            {
                                store.AddCRLAsync(new X509CRL(crl)).GetAwaiter().GetResult();
                            }
                        }
                    }
                }

                CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);

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

        private void PushToServerButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_application != null)
                {
                    if (m_application.RegistrationType == RegistrationType.ServerPush)
                    {
                        var trustList = CertificateStoreControl.GetTrustLists();

                        bool applyChanges = m_server.UpdateTrustListAsync(trustList).GetAwaiter().GetResult();

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
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Parent.Text, exception);
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

        private void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            try
            {
                m_server.ApplyChangesAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                var se = exception as ServiceResultException;

                if (se == null || se.StatusCode != StatusCodes.BadServerHalted)
                {
                    Opc.Ua.Client.Controls.ExceptionDlg.Show(Parent.Text, exception);
                }
            }

            try
            {
                m_server.DisconnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // ignore.
            }
        }
    }
}
