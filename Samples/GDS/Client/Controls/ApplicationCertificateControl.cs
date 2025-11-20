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

using Opc.Ua.Security.Certificates;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Opc.Ua.Gds.Client
{
    public partial class ApplicationCertificateControl : UserControl
    {
        public ApplicationCertificateControl()
        {
            InitializeComponent();
        }

        private GlobalDiscoveryClientConfiguration m_configuration;
        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;
        private X509Certificate2 m_certificate;
        private bool m_temporaryCertificateCreated;
        private string m_certificatePassword;

        public async Task InitializeAsync(
            GlobalDiscoveryClientConfiguration configuration,
            GlobalDiscoveryServerClient gds,
            ServerPushConfigurationClient server,
            RegisteredApplication application,
            bool isHttps,
            CancellationToken ct = default)
        {
            m_configuration = configuration;
            m_gds = gds;
            m_server = server;
            m_application = application;
            m_certificate = null;
            m_temporaryCertificateCreated = false;
            m_certificatePassword = null;

            CertificateRequestTimer.Enabled = false;
            RequestProgressLabel.Visible = false;
            ApplyChangesButton.Enabled = false;

            CertificateControl.ShowNothing();

            X509Certificate2 certificate = null;

            if (!isHttps)
            {
                if (server.Endpoint != null && server.Endpoint.Description.ServerCertificate != null)
                {
                    certificate = new X509Certificate2(server.Endpoint.Description.ServerCertificate);
                }
                else if (application != null)
                {
                    if (!String.IsNullOrEmpty(application.CertificatePublicKeyPath))
                    {
                        string file = Utils.GetAbsoluteFilePath(application.CertificatePublicKeyPath, true, false, false);

                        if (file != null)
                        {
                            certificate = new X509Certificate2(file);
                        }
                    }
                    else if (!String.IsNullOrEmpty(application.CertificateStorePath))
                    {
                        CertificateIdentifier id = new CertificateIdentifier {
                            StorePath = application.CertificateStorePath
                        };
                        id.StoreType = CertificateStoreIdentifier.DetermineStoreType(id.StorePath);
                        id.SubjectName = application.CertificateSubjectName.Replace("localhost", Utils.GetHostName());

                        certificate = await id.FindAsync(true, ct: ct);
                    }
                }
            }
            else
            {
                if (application != null)
                {
                    if (!String.IsNullOrEmpty(application.HttpsCertificatePublicKeyPath))
                    {
                        string file = Utils.GetAbsoluteFilePath(application.HttpsCertificatePublicKeyPath, true, false, false);

                        if (file != null)
                        {
                            certificate = new X509Certificate2(file);
                        }
                    }
                    else
                    {
                        foreach (string disoveryUrl in application.DiscoveryUrl)
                        {
                            if (Uri.IsWellFormedUriString(disoveryUrl, UriKind.Absolute))
                            {
                                Uri url = new Uri(disoveryUrl);

                                CertificateIdentifier id = new CertificateIdentifier() {
                                    StoreType = CertificateStoreType.X509Store,
                                    StorePath = "CurrentUser\\UA_MachineDefault",
                                    SubjectName = "CN=" + url.DnsSafeHost
                                };

                                certificate = await id.FindAsync(ct: ct);
                            }
                        }
                    }
                }
            }

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

                    certificate = null;
                }
            }

            WarningLabel.Visible = certificate == null;

            if (certificate != null)
            {
                m_certificate = certificate;
                CertificateControl.ShowValue(null, "Application Certificate", new CertificateWrapper() { Certificate = certificate }, true);
            }
        }

        private async void RequestNewButton_Click(object sender, EventArgs e)
        {
            if (m_application.RegistrationType == RegistrationType.ServerPush)
            {
                await RequestNewCertificatePushModeAsync(sender, e);
            }
            else
            {
                await RequestNewCertificatePullModeAsync(sender, e);
            }
        }

        private async Task RequestNewCertificatePushModeAsync(object sender, EventArgs e)
        {
            try
            {
                NodeId trustListId = await m_gds.GetTrustListAsync(m_application.ApplicationId, NodeId.Null);
                var trustList = await m_gds.ReadTrustListAsync(trustListId);
                bool applyChanges = await m_server.UpdateTrustListAsync(trustList);

                byte[] unusedNonce = Array.Empty<byte>();
                byte[] certificateRequest = await m_server.CreateSigningRequestAsync(
                    NodeId.Null,
                    m_server.ApplicationCertificateType,
                    string.Empty,
                    false,
                    unusedNonce);
                var domainNames = m_application.GetDomainNames(m_certificate);
                NodeId requestId = await m_gds.StartSigningRequestAsync(
                    m_application.ApplicationId,
                    NodeId.Null,
                    NodeId.Null,
                    certificateRequest);

                if (applyChanges)
                {
                    MessageBox.Show(
                        Parent,
                        "The updated Trust List was loaded however, the apply changes command must be sent before the server will update its Trust List.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    ApplyChangesButton.Enabled = true;
                }

                m_application.CertificateRequestId = requestId.ToString();
                CertificateRequestTimer.Enabled = true;
                RequestProgressLabel.Visible = true;
                WarningLabel.Visible = false;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }

        }
        private async Task RequestNewCertificatePullModeAsync(object sender, EventArgs e)
        {
            try
            {
                // check if we already have a private key
                NodeId requestId = null;
                if (!string.IsNullOrEmpty(m_application.CertificateStorePath))
                {
                    CertificateIdentifier id = new CertificateIdentifier {
                        StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath),
                        StorePath = m_application.CertificateStorePath,
                        SubjectName = Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName)
                    };
                    m_certificate = await id.FindAsync(true);
                    //test if private key is available & exportable, else create new temporary certificate for csr
                    if (m_certificate != null &&
                        m_certificate.HasPrivateKey)
                    {
                        try
                        {
                            //this line fails with a CryptographicException if export of private key is not allowed
                            _ = m_certificate.GetRSAPrivateKey().ExportParameters(true);
                            //proceed with a CSR using the exportable private key
                            m_certificate = await id.LoadPrivateKeyAsync(m_certificatePassword.ToCharArray());
                        }
                        catch
                        {
                            //create temporary cert to generate csr from
                            m_certificate = CertificateFactory.CreateCertificate(
                                X509Utils.GetApplicationUrisFromCertificate(m_certificate)[0],
                                m_application.ApplicationName,
                                Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName),
                                m_application.GetDomainNames(m_certificate))
                                .SetNotBefore(DateTime.Today.AddDays(-1))
                                .SetNotAfter(DateTime.Today.AddDays(14))
                                .SetRSAKeySize((ushort)(m_certificate.GetRSAPublicKey()?.KeySize ?? 0))
                                .CreateForRSA();
                            m_temporaryCertificateCreated = true;
                        }
                    }
                }

                bool hasPrivateKeyFile = false;
                if (!string.IsNullOrEmpty(m_application.CertificatePrivateKeyPath))
                {
                    FileInfo file = new FileInfo(m_application.CertificatePrivateKeyPath);
                    hasPrivateKeyFile = file.Exists;
                }

                var domainNames = m_application.GetDomainNames(m_certificate);
                if (m_certificate == null)
                {
                    // no private key
                    requestId = await m_gds.StartNewKeyPairRequestAsync(
                        m_application.ApplicationId,
                        NodeId.Null,
                        NodeId.Null,
                        Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName),
                        domainNames,
                        "PFX",
                        m_certificatePassword?.ToCharArray());
                }
                else
                {
                    X509Certificate2 csrCertificate = null;
                    if (m_certificate.HasPrivateKey)
                    {
                        csrCertificate = m_certificate;
                    }
                    else
                    {
                        string absoluteCertificatePrivateKeyPath = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);
                        byte[] pkcsData = File.ReadAllBytes(absoluteCertificatePrivateKeyPath);
                        if (m_application.GetPrivateKeyFormat(await m_server?.GetSupportedKeyFormatsAsync()) == "PFX")
                        {
                            csrCertificate = X509PfxUtils.CreateCertificateFromPKCS12(pkcsData, m_certificatePassword.AsSpan());
                        }
                        else
                        {
                            csrCertificate = CertificateFactory.CreateCertificateWithPEMPrivateKey(m_certificate, pkcsData, m_certificatePassword.AsSpan());
                        }
                    }
                    byte[] certificateRequest = CertificateFactory.CreateSigningRequest(csrCertificate, domainNames);
                    requestId = await m_gds.StartSigningRequestAsync(m_application.ApplicationId, NodeId.Null, NodeId.Null, certificateRequest);
                }

                m_application.CertificateRequestId = requestId.ToString();
                CertificateRequestTimer.Enabled = true;
                RequestProgressLabel.Visible = true;
                WarningLabel.Visible = false;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private async void CertificateRequestTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                NodeId requestId = NodeId.Parse(m_application.CertificateRequestId);

                (byte[] certificate, byte[] privateKeyPFX, byte[][] issuerCertificates) = await m_gds.FinishRequestAsync(
                    m_application.ApplicationId,
                    requestId);

                if (certificate == null)
                {
                    // request not done yet, try again in a few seconds
                    return;
                }

                CertificateRequestTimer.Enabled = false;
                RequestProgressLabel.Visible = false;

                if (m_application.RegistrationType != RegistrationType.ServerPush)
                {

                    X509Certificate2 newCert = new X509Certificate2(certificate);

                    if (!String.IsNullOrEmpty(m_application.CertificateStorePath) && !String.IsNullOrEmpty(m_application.CertificateSubjectName))
                    {
                        CertificateIdentifier cid = new CertificateIdentifier() {
                            StorePath = m_application.CertificateStorePath,
                            StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath),
                            SubjectName = Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName)
                        };

                        // update store
                        var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.CertificateStorePath, false);
                        using (ICertificateStore store = certificateStoreIdentifier.OpenStore())
                        {
                            // if we used a CSR, we already have a private key and therefore didn't request one from the GDS
                            // in this case, privateKey is null
                            if (privateKeyPFX == null)
                            {
                                X509Certificate2 oldCertificate = await cid.FindAsync(true);
                                if (oldCertificate != null && oldCertificate.HasPrivateKey)
                                {
                                    oldCertificate = await cid.LoadPrivateKeyAsync([]);
                                    newCert = CertificateFactory.CreateCertificateWithPrivateKey(newCert, m_temporaryCertificateCreated ? m_certificate : oldCertificate);
                                    await store.DeleteAsync(oldCertificate.Thumbprint);
                                }
                                else
                                {
                                    throw new ServiceResultException("Failed to merge signed certificate with the private key.");
                                }
                            }
                            else
                            {
                                newCert = new X509Certificate2(privateKeyPFX, string.Empty, X509KeyStorageFlags.Exportable);
                                newCert = CertificateFactory.Load(newCert, true);
                            }
                            await store.AddAsync(newCert);
                            if (m_temporaryCertificateCreated)
                            {
                                m_certificate.Dispose();
                                m_certificate = null;
                                m_temporaryCertificateCreated = false;
                            }
                        }
                    }
                    else
                    {
                        DialogResult result = DialogResult.Yes;
                        string absoluteCertificatePublicKeyPath = Utils.GetAbsoluteFilePath(m_application.CertificatePublicKeyPath, true, false, false) ?? m_application.CertificatePublicKeyPath;
                        FileInfo file = new FileInfo(absoluteCertificatePublicKeyPath);
                        if (file.Exists)
                        {
                            result = MessageBox.Show(
                                Parent,
                                "Replace certificate " +
                                absoluteCertificatePublicKeyPath +
                                "?",
                                Parent.Text,
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Exclamation);
                        }

                        if (result == DialogResult.Yes)
                        {
                            byte[] exportedCert;
                            if (string.Compare(file.Extension, ".PEM", true) == 0)
                            {
                                exportedCert = PEMWriter.ExportCertificateAsPEM(newCert);
                            }
                            else
                            {
                                exportedCert = newCert.Export(X509ContentType.Cert);
                            }
                            File.WriteAllBytes(absoluteCertificatePublicKeyPath, exportedCert);
                        }

                        // if we provided a PFX or P12 with the private key, we need to merge the new cert with the private key
                        if (m_application.GetPrivateKeyFormat(await m_server?.GetSupportedKeyFormatsAsync()) == "PFX")
                        {
                            string absoluteCertificatePrivateKeyPath = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false) ?? m_application.CertificatePrivateKeyPath;
                            file = new FileInfo(absoluteCertificatePrivateKeyPath);
                            if (file.Exists)
                            {
                                result = MessageBox.Show(
                                    Parent,
                                    "Replace private key " +
                                    absoluteCertificatePrivateKeyPath +
                                    "?",
                                    Parent.Text,
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Exclamation);
                            }

                            if (result == DialogResult.Yes)
                            {
                                if (file.Exists)
                                {
                                    byte[] pkcsData = File.ReadAllBytes(absoluteCertificatePrivateKeyPath);
                                    X509Certificate2 oldCertificate = X509PfxUtils.CreateCertificateFromPKCS12(pkcsData, m_certificatePassword.AsSpan());
                                    newCert = CertificateFactory.CreateCertificateWithPrivateKey(newCert, oldCertificate);
                                    pkcsData = newCert.Export(X509ContentType.Pfx, m_certificatePassword);
                                    File.WriteAllBytes(absoluteCertificatePrivateKeyPath, pkcsData);

                                    if (privateKeyPFX != null)
                                    {
                                        throw new ServiceResultException("Did not expect a private key for this operation.");
                                    }
                                }
                                else
                                {
                                    File.WriteAllBytes(absoluteCertificatePrivateKeyPath, privateKeyPFX);
                                }
                            }
                        }
                    }

                    // update trust list.
                    if (!String.IsNullOrEmpty(m_application.TrustListStorePath))
                    {
                        var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.TrustListStorePath);
                        using (ICertificateStore store = certificateStoreIdentifier.OpenStore())
                        {
                            foreach (byte[] issuerCertificate in issuerCertificates)
                            {
                                X509Certificate2 x509 = new X509Certificate2(issuerCertificate);
                                X509Certificate2Collection certs = await store.FindByThumbprintAsync(x509.Thumbprint);
                                if (certs.Count == 0)
                                {
                                    await store.AddAsync(new X509Certificate2(issuerCertificate));
                                }
                            }
                        }
                    }

                    m_certificate = newCert;

                }
                else
                {
                    if (privateKeyPFX != null && privateKeyPFX.Length > 0)
                    {
                        var x509 = new X509Certificate2(privateKeyPFX, m_certificatePassword, X509KeyStorageFlags.Exportable);
                        privateKeyPFX = x509.Export(X509ContentType.Pfx);
                    }

                    byte[] unusedPrivateKey = Array.Empty<byte>();
                    bool applyChanges = await m_server.UpdateCertificateAsync(
                        NodeId.Null,
                        m_server.ApplicationCertificateType,
                        certificate,
                        (privateKeyPFX != null) ? "pfx" : String.Empty,
                        (privateKeyPFX != null) ? privateKeyPFX : unusedPrivateKey,
                        issuerCertificates);
                    if (applyChanges)
                    {
                        MessageBox.Show(
                            Parent,
                            "The certificate was updated, however, the apply changes command must be sent before the server will use the new certificate.",
                            Parent.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        ApplyChangesButton.Enabled = true;
                    }
                }

                CertificateControl.ShowValue(null, "Application Certificate", new CertificateWrapper() { Certificate = m_certificate }, true);
            }
            catch (Exception exception)
            {

                if (exception is ServiceResultException sre && sre.StatusCode == StatusCodes.BadNothingToDo)
                {
                    return;
                }

                RequestProgressLabel.Visible = false;
                CertificateRequestTimer.Enabled = false;
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, exception);
            }
        }

        private async void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            ApplyChangesButton.Enabled = false;
            try
            {
                await m_server.ApplyChangesAsync();
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
                await m_server.DisconnectAsync();
            }
            catch
            {
                // ignore.
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

    }
}
