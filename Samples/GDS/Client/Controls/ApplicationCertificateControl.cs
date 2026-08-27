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
using System.Collections.Generic;
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

        private ITelemetryContext m_telemetry;
        private GlobalDiscoveryClientConfiguration m_configuration;
        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;
        #pragma warning disable CA2213 // Justification: Designer-generated Dispose owns the WinForms disposal pattern for this sample.
        private X509Certificate2 m_certificate;
        #pragma warning restore CA2213
        private bool m_temporaryCertificateCreated;
        private string m_certificatePassword;

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
            m_certificate = null;
            m_temporaryCertificateCreated = false;
            m_certificatePassword = null;
            PrivateKeyPasswordTextBox.Text = string.Empty;

            CertificateRequestTimer.Enabled = false;
            RequestProgressLabel.Visible = false;
            ApplyChangesButton.Enabled = false;

            CertificateControl.ShowNothing();

            X509Certificate2 certificate = null;

            if (!isHttps)
            {
                // Only ServerPush applications may seed the certificate from the connected
                // server's endpoint. For pull applications (ClientPull/ServerPull) the connected
                // server is the GDS (or the target server) and its certificate must not be used
                // as the application certificate, otherwise the GDS/server host name leaks into
                // the requested certificate's domain list (SANs). See issue #741.
                if (application?.RegistrationType == RegistrationType.ServerPush
                    && server.Endpoint != null
                    && !server.Endpoint.Description.ServerCertificate.IsNull)
                {
                    certificate = GdsCertificateLoader.LoadCertificate(server.Endpoint.Description.ServerCertificate.ToArray());
                }
                else if (application != null)
                {
                    if (!String.IsNullOrEmpty(application.CertificatePublicKeyPath))
                    {
                        string file = Utils.GetAbsoluteFilePath(application.CertificatePublicKeyPath, true, false, false);

                        if (file != null)
                        {
                            certificate = GdsCertificateLoader.LoadCertificateFromFile(file);
                        }
                    }
                    else if (!String.IsNullOrEmpty(application.CertificateStorePath))
                    {
                        CertificateIdentifier id = new CertificateIdentifier {
                            StorePath = application.CertificateStorePath
                        };
                        id.StoreType = CertificateStoreIdentifier.DetermineStoreType(id.StorePath);
                        id.SubjectName = application.CertificateSubjectName.Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);

                        certificate = await FindCertificateAsync(id, ct);
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
                            certificate = GdsCertificateLoader.LoadCertificateFromFile(file);
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

                                certificate = await FindCertificateAsync(id, ct);
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
                var wrapper = new CertificateWrapper() { Certificate = Certificate.From(certificate) };
                CertificateControl.ShowValue(TypeInfo.Construct(wrapper), "Application Certificate", wrapper, true);
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

        private async void NewKeyPairFromDerButton_Click(object sender, EventArgs e)
        {
            await CreatePfxFromCertificateInfoAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Creates a new .pfx (with a fresh public/private key pair) from the information
        /// contained in the currently loaded certificate.
        /// </summary>
        /// <remarks>
        /// The subject name, application URI(s) and domain names (Subject Alternative Name) of the
        /// loaded certificate are reused, while a brand new RSA key pair is generated. This makes it
        /// possible to turn a public-only certificate (e.g. loaded from a <c>.der</c> file) into a
        /// usable <c>.pfx</c> that carries a private key.
        /// </remarks>
        private async Task CreatePfxFromCertificateInfoAsync()
        {
            try
            {
                if (m_certificate == null)
                {
                    MessageBox.Show(
                        Parent,
                        "No certificate is loaded. Load a public certificate (e.g. a .der file) first.",
                        Parent?.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                // Clone the subject and the domain/application URI information from the loaded certificate.
                string subjectName = m_certificate.Subject;
                Certificate certificateInfo = Certificate.From(m_certificate);
                IList<string> domainNames = X509Utils.GetDomainsFromCertificate(certificateInfo).ToList();
                IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(certificateInfo);
                ushort keySize = (ushort)(m_certificate.GetRSAPublicKey()?.KeySize ?? X509Defaults.RSAKeySize);

                ICertificateBuilder builder = DefaultCertificateFactory.Instance.CreateCertificate(subjectName)
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddYears(1));

                // Reuse the Subject Alternative Name (application URI + domains) of the original certificate.
                if (domainNames.Count > 0 || applicationUris.Count > 0)
                {
                    builder = builder.AddExtension(
                        new X509SubjectAltNameExtension(
                            applicationUris.Count > 0 ? applicationUris[0] : string.Empty,
                            domainNames));
                }

                // Generate a new key pair for the cloned certificate information.
                X509Certificate2 newCertificate = builder
                    .SetRSAKeySize(keySize)
                    .CreateForRSA()
                    .AsX509Certificate2();

                string savePath;
                #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                using (SaveFileDialog dialog = new SaveFileDialog {
                    Title = "Save new PFX certificate",
                    Filter = "PKCS#12 files (*.pfx)|*.pfx|All files (*.*)|*.*",
                    DefaultExt = "pfx",
                    FileName = GetDefaultPfxFileName(subjectName),
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

                string password = string.IsNullOrEmpty(m_certificatePassword) ? null : m_certificatePassword;
                byte[] pfx = newCertificate.Export(X509ContentType.Pfx, password);
                File.WriteAllBytes(savePath, pfx);
                #pragma warning restore CA1849

                if (m_temporaryCertificateCreated)
                {
                    m_certificate.Dispose();
                    m_temporaryCertificateCreated = false;
                }
                m_certificate = newCertificate;

                var wrapper = new CertificateWrapper() { Certificate = Certificate.From(newCertificate) };
                CertificateControl.ShowValue(TypeInfo.Construct(wrapper), "Application Certificate", wrapper, true);
                WarningLabel.Visible = false;

                MessageBox.Show(
                    Parent,
                    "A new .pfx with a fresh key pair was created from the certificate information and saved to:\n" + savePath,
                    Parent?.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                await Task.CompletedTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
                #pragma warning restore CA1849
            }
        }

        /// <summary>
        /// Builds a sensible default file name for the exported .pfx based on the certificate subject.
        /// </summary>
        private static string GetDefaultPfxFileName(string subjectName)
        {
            string commonName = null;
            if (!String.IsNullOrEmpty(subjectName))
            {
                foreach (string part in subjectName.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    {
                        commonName = trimmed.Substring(3).Trim();
                        break;
                    }
                }
            }

            if (String.IsNullOrEmpty(commonName))
            {
                commonName = "certificate";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                commonName = commonName.Replace(invalid, '_');
            }

            return commonName + ".pfx";
        }

        private async Task RequestNewCertificatePushModeAsync(object sender, EventArgs e)
        {
            try
            {
                NodeId trustListId = await m_gds.GetTrustListAsync(NodeId.Parse(m_application.ApplicationId), NodeId.Null);
                var trustList = await m_gds.ReadTrustListAsync(trustListId);
                bool applyChanges = await m_server.UpdateTrustListAsync(trustList);

                byte[] unusedNonce = Array.Empty<byte>();
                ByteString certificateRequest = await m_server.CreateSigningRequestAsync(
                    NodeId.Null,
                    m_server.ApplicationCertificateType,
                    string.Empty,
                    false,
unusedNonce.ToByteString());
                var domainNames = m_application.GetDomainNames(Certificate.From(m_certificate));
                NodeId requestId = await m_gds.StartSigningRequestAsync(
                    NodeId.Parse(m_application.ApplicationId),
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
                #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
                #pragma warning restore CA1849
            }
        }

        private async Task<X509Certificate2> FindCertificateAsync(CertificateIdentifier id, CancellationToken ct = default)
        {
            var storeIdentifier = new CertificateStoreIdentifier(id.StorePath, false);
            using (ICertificateStore store = storeIdentifier.OpenStore(m_telemetry))
            {
                CertificateCollection certificates = await store.EnumerateAsync(ct);
                foreach (Certificate certificate in certificates)
                {
                    X509Certificate2 x509 = certificate.AsX509Certificate2();
                    if (!String.IsNullOrEmpty(id.Thumbprint) &&
                        String.Equals(x509.Thumbprint, id.Thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return x509;
                    }

                    if (!String.IsNullOrEmpty(id.SubjectName) &&
                        x509.Subject.Contains(id.SubjectName, StringComparison.OrdinalIgnoreCase))
                    {
                        return x509;
                    }
                }
            }

            return null;
        }

        private async Task<X509Certificate2> LoadPrivateKeyAsync(CertificateIdentifier id, X509Certificate2 certificate, char[] password)
        {
            if (certificate == null)
            {
                return null;
            }

            // Certificate already carries an exportable private key, nothing to load.
            if (certificate.HasPrivateKey)
            {
                return certificate;
            }

            if (id == null || String.IsNullOrEmpty(id.StorePath))
            {
                return certificate;
            }

            var storeIdentifier = new CertificateStoreIdentifier(id.StorePath, false);
            using (ICertificateStore store = storeIdentifier.OpenStore(m_telemetry))
            {
                if (store == null || !store.SupportsLoadPrivateKey)
                {
                    return certificate;
                }

                Certificate withPrivateKey = await store.LoadPrivateKeyAsync(
                    certificate.Thumbprint,
                    certificate.Subject,
                    null,
                    id.CertificateType,
                    (password != null && password.Length > 0) ? password : null);

                if (withPrivateKey != null)
                {
                    return withPrivateKey.AsX509Certificate2();
                }
            }

            return certificate;
        }
        private async Task RequestNewCertificatePullModeAsync(object sender, EventArgs e)
        {
            try
            {
                // check if we already have a private key
                NodeId requestId = NodeId.Null;
                if (!string.IsNullOrEmpty(m_application.CertificateStorePath))
                {
                    CertificateIdentifier id = new CertificateIdentifier {
                        StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath),
                        StorePath = m_application.CertificateStorePath,
                        SubjectName = Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName)
                    };
                    m_certificate = await FindCertificateAsync(id);
                    //test if private key is available & exportable, else create new temporary certificate for csr
                    if (m_certificate != null &&
                        m_certificate.HasPrivateKey)
                    {
                        try
                        {
                            //this line fails with a CryptographicException if export of private key is not allowed
                            _ = m_certificate.GetRSAPrivateKey().ExportParameters(true);
                            //proceed with a CSR using the exportable private key
                            m_certificate = await LoadPrivateKeyAsync(id, m_certificate, m_certificatePassword?.ToCharArray());
                        }
                        catch
                        {
                            //create temporary cert to generate csr from
                            m_certificate = DefaultCertificateFactory.Instance.CreateCertificate(
                                Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName))
                                .SetNotBefore(DateTime.Today.AddDays(-1))
                                .SetNotAfter(DateTime.Today.AddDays(14))
                                .SetRSAKeySize((ushort)(m_certificate.GetRSAPublicKey()?.KeySize ?? 0))
                                .CreateForRSA()
                                .AsX509Certificate2();
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

                var domainNames = m_application.GetDomainNames(m_certificate != null ? Certificate.From(m_certificate) : null);
                if (m_certificate == null)
                {
                    // no private key
                    requestId = await m_gds.StartNewKeyPairRequestAsync(
                        NodeId.Parse(m_application.ApplicationId),
                        NodeId.Null,
                        NodeId.Null,
                        Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName),
                        domainNames,
                        "PFX",
                        m_certificatePassword?.ToCharArray() ?? Array.Empty<char>());
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
                        #pragma warning disable CA1849 // Justification: Synchronous WinForms sample handler preserves existing behavior.
                        byte[] pkcsData = File.ReadAllBytes(absoluteCertificatePrivateKeyPath);
                        #pragma warning restore CA1849
                        if (m_application.GetPrivateKeyFormat((m_server != null ? await m_server.GetSupportedKeyFormatsAsync() : ArrayOf<string>.Empty).ToArray()) == "PFX")
                        {
                            #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                            csrCertificate = X509PfxUtils.CreateCertificateFromPKCS12(pkcsData, m_certificatePassword.AsSpan()).AsX509Certificate2();
                            #pragma warning restore CA2000
                        }
                        else
                        {
                            #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                            csrCertificate = DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(Certificate.From(m_certificate), pkcsData, m_certificatePassword.AsSpan()).AsX509Certificate2();
                            #pragma warning restore CA2000
                        }
                    }
                    byte[] certificateRequest = DefaultCertificateFactory.Instance.CreateSigningRequest(Certificate.From(csrCertificate), domainNames);
                    requestId = await m_gds.StartSigningRequestAsync(NodeId.Parse(m_application.ApplicationId), NodeId.Null, NodeId.Null, certificateRequest.ToByteString());
                }

                m_application.CertificateRequestId = requestId.ToString();
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

        private async void CertificateRequestTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                NodeId requestId = NodeId.Parse(m_application.CertificateRequestId);

                (ByteString certificate, ByteString privateKeyPFX, ArrayOf<ByteString> issuerCertificates) = await m_gds.FinishRequestAsync(
                    NodeId.Parse(m_application.ApplicationId),
                    requestId);

                if (certificate.IsNull)
                {
                    // request not done yet, try again in a few seconds
                    return;
                }

                CertificateRequestTimer.Enabled = false;
                RequestProgressLabel.Visible = false;

                if (m_application.RegistrationType != RegistrationType.ServerPush)
                {

                    X509Certificate2 newCert = GdsCertificateLoader.LoadCertificate(certificate.ToArray());

                    if (!String.IsNullOrEmpty(m_application.CertificateStorePath) && !String.IsNullOrEmpty(m_application.CertificateSubjectName))
                    {
                        CertificateIdentifier cid = new CertificateIdentifier() {
                            StorePath = m_application.CertificateStorePath,
                            StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath),
                            SubjectName = Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName)
                        };

                        // update store
                        var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.CertificateStorePath, false);
                        using (ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry))
                        {
                            // if we used a CSR, we already have a private key and therefore didn't request one from the GDS
                            // in this case, privateKey is null
                            if (privateKeyPFX.IsNull)
                            {
                                X509Certificate2 oldCertificate = await FindCertificateAsync(cid);
                                if (oldCertificate != null && oldCertificate.HasPrivateKey)
                                {
                                    oldCertificate = await LoadPrivateKeyAsync(cid, oldCertificate, m_certificatePassword?.ToCharArray());
                                    newCert = DefaultCertificateFactory.Instance.CreateWithPrivateKey(Certificate.From(newCert), Certificate.From(m_temporaryCertificateCreated ? m_certificate : oldCertificate)).AsX509Certificate2();
                                    await store.DeleteAsync(oldCertificate.Thumbprint);
                                }
                                else
                                {
                                    throw new ServiceResultException("Failed to merge signed certificate with the private key.");
                                }
                            }
                            else
                            {
                                newCert = GdsCertificateLoader.LoadPkcs12(privateKeyPFX.ToArray(), m_certificatePassword ?? string.Empty, X509KeyStorageFlags.Exportable);
                            }
                            await store.AddAsync(Certificate.From(newCert));
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
                        string absoluteCertificatePublicKeyPath = GetSaveFilePath(m_application.CertificatePublicKeyPath);
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
                            if (string.Equals(file.Extension, ".PEM", StringComparison.OrdinalIgnoreCase))
                            {
                                exportedCert = PEMWriter.ExportCertificateAsPEM(Certificate.From(newCert));
                            }
                            else
                            {
                                exportedCert = newCert.Export(X509ContentType.Cert);
                            }
                            File.WriteAllBytes(absoluteCertificatePublicKeyPath, exportedCert);
                        }

                        // if we provided a PFX or P12 with the private key, we need to merge the new cert with the private key
                        if (m_application.GetPrivateKeyFormat((m_server != null ? await m_server.GetSupportedKeyFormatsAsync() : ArrayOf<string>.Empty).ToArray()) == "PFX")
                        {
                            string absoluteCertificatePrivateKeyPath = GetSaveFilePath(m_application.CertificatePrivateKeyPath);
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
                                    #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                                    X509Certificate2 oldCertificate = X509PfxUtils.CreateCertificateFromPKCS12(pkcsData, m_certificatePassword.AsSpan()).AsX509Certificate2();
                                    #pragma warning restore CA2000
                                    #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                                    newCert = DefaultCertificateFactory.Instance.CreateWithPrivateKey(Certificate.From(newCert), Certificate.From(oldCertificate)).AsX509Certificate2();
                                    #pragma warning restore CA2000
                                    pkcsData = newCert.Export(X509ContentType.Pfx, m_certificatePassword);
                                    File.WriteAllBytes(absoluteCertificatePrivateKeyPath, pkcsData);

                                    if (!privateKeyPFX.IsNull)
                                    {
                                        throw new ServiceResultException("Did not expect a private key for this operation.");
                                    }
                                }
                                else
                                {
                                    File.WriteAllBytes(absoluteCertificatePrivateKeyPath, privateKeyPFX.ToArray());
                                }
                            }
                        }
                    }

                    // update trust list.
                    if (!String.IsNullOrEmpty(m_application.TrustListStorePath))
                    {
                        var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.TrustListStorePath);
                        using (ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry))
                        {
                            foreach (ByteString issuerCertificate in issuerCertificates.ToArray())
                            {
                                X509Certificate2 x509 = GdsCertificateLoader.LoadCertificate(issuerCertificate.ToArray());
                                CertificateCollection certs = await store.FindByThumbprintAsync(x509.Thumbprint);
                                if (certs.Count == 0)
                                {
                                    await store.AddAsync(Certificate.From(GdsCertificateLoader.LoadCertificate(issuerCertificate.ToArray())));
                                }
                            }
                        }
                    }

                    m_certificate = newCert;

                }
                else
                {
                    if (!privateKeyPFX.IsNull && privateKeyPFX.Length > 0)
                    {
                        var x509 = GdsCertificateLoader.LoadPkcs12(privateKeyPFX.ToArray(), m_certificatePassword, X509KeyStorageFlags.Exportable);
                        privateKeyPFX = x509.Export(X509ContentType.Pfx).ToByteString();
                    }

                    ByteString unusedPrivateKey = Array.Empty<byte>().ToByteString();
                    bool applyChanges = await m_server.UpdateCertificateAsync(
                        NodeId.Null,
                        m_server.ApplicationCertificateType,
certificate,
                        (!privateKeyPFX.IsNull) ? "pfx" : String.Empty,
(!privateKeyPFX.IsNull) ? privateKeyPFX : unusedPrivateKey,
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

                var updatedWrapper = new CertificateWrapper() { Certificate = Certificate.From(m_certificate) };
                CertificateControl.ShowValue(TypeInfo.Construct(updatedWrapper), "Application Certificate", updatedWrapper, true);
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
        /// Resolves a certificate file path for saving.
        /// </summary>
        /// <remarks>
        /// <see cref="Utils.GetAbsoluteFilePath(string, bool, bool, bool)"/> only resolves paths to files that
        /// already exist. When saving a new certificate the target file does not exist yet, so that call fails
        /// and the raw path (which may still contain an unexpanded placeholder such as
        /// <c>%CommonApplicationData%</c>) would be used verbatim. This helper falls back to
        /// <see cref="Utils.ReplaceSpecialFolderNames(string)"/> so environment/special-folder placeholders are
        /// consistently expanded and the certificate is written to the intended location.
        /// </remarks>
        private static string GetSaveFilePath(string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            try
            {
                // Prefer an already existing file (also handles current-directory lookup).
                string resolved = Utils.GetAbsoluteFilePath(filePath, true, false, false);
                if (!String.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }
            }
            catch (ServiceResultException)
            {
                // File does not exist yet (new certificate): fall back to placeholder expansion below.
            }

            // Expand special-folder/environment placeholders so a new certificate is saved to the intended path.
            return Utils.ReplaceSpecialFolderNames(filePath) ?? filePath;
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
            m_certificatePassword = PrivateKeyPasswordTextBox.Text;
        }

    }
}
