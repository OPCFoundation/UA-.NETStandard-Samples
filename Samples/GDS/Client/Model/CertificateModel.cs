/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client.Model
{
    /// <summary>
    /// What starting a certificate request left behind.
    /// </summary>
    /// <param name="RequestId">The request the Global Discovery Server opened.</param>
    /// <param name="TrustListApplyChangesRequired">True when a trust list was pushed to the
    /// server on the way and the server needs an apply changes before it uses it.</param>
    public sealed record CertificateRequestStart(NodeId RequestId, bool TrustListApplyChangesRequired);

    /// <summary>
    /// How far a certificate request has got.
    /// </summary>
    public enum CertificateRequestState
    {
        /// <summary>The Global Discovery Server has not finished the request yet.</summary>
        Pending,

        /// <summary>The certificate was issued and stored where the application will find it.</summary>
        Issued,

        /// <summary>The certificate was pushed to the server, which may need an apply changes.</summary>
        PushedToServer,
    }

    /// <summary>
    /// The outcome of asking whether a certificate request has finished.
    /// </summary>
    /// <param name="State">How far the request has got.</param>
    /// <param name="Certificate">The certificate which was issued, or null while pending.</param>
    /// <param name="ApplyChangesRequired">True when a push server needs an apply changes.</param>
    public sealed record CertificateRequestResult(
        CertificateRequestState State,
        X509Certificate2 Certificate,
        bool ApplyChangesRequired);

    /// <summary>
    /// The certificate of a registered application: finding the one it runs on, asking the
    /// Global Discovery Server for a new one, and putting the answer where the application
    /// will find it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are two shapes of this, and which one applies is the registration type of the
    /// application. A <b>push</b> application is a running server which the client is
    /// connected to: the certificate is signed by the GDS and then written to the server
    /// over that session. A <b>pull</b> application keeps its certificate in a store or in
    /// files on disk, and the client is the one which reads and writes them.
    /// </para>
    /// <para>
    /// Overwriting a file which is already there is a question for a person, so the model
    /// does not decide it: <see cref="FinishRequestAsync"/> takes the answer as a delegate.
    /// Everything else here is certificates and service calls, and knows nothing about a
    /// window.
    /// </para>
    /// </remarks>
    public sealed class CertificateModel
    {
        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;
        private ITelemetryContext m_telemetry;
        private X509Certificate2 m_certificate;
        private bool m_temporaryCertificateCreated;

        /// <summary>
        /// The password of the private key of the application, as a person typed it.
        /// </summary>
        public string CertificatePassword { get; set; }

        /// <summary>
        /// The certificate the application currently runs on, or null.
        /// </summary>
        /// <remarks>
        /// Not called <c>Certificate</c>: a member of that name would shadow the
        /// <see cref="Opc.Ua.Certificate"/> type inside this class, and every
        /// <c>Certificate.From(...)</c> below would bind to the property instead.
        /// </remarks>
        public X509Certificate2 ApplicationCertificate => m_certificate;

        /// <summary>
        /// True while the model has an application to work on.
        /// </summary>
        public bool HasApplication => m_application != null;

        /// <summary>
        /// True when the application is a running server the certificate is pushed to,
        /// rather than one which keeps it on disk.
        /// </summary>
        public bool IsServerPush => m_application?.RegistrationType == RegistrationType.ServerPush;

        /// <summary>
        /// Points the model at an application and the two clients which serve it, and finds
        /// the certificate the application runs on today.
        /// </summary>
        /// <remarks>
        /// Where that certificate comes from depends on the application. A push server hands
        /// it out on its own endpoint. Everything else names either a public key file or a
        /// certificate store, and the https half of an application names a second pair of
        /// those for the certificate its https endpoint uses.
        /// </remarks>
        /// <param name="gds">The Global Discovery Server client.</param>
        /// <param name="server">The push configuration client, for a server push application.</param>
        /// <param name="application">The application, or null when none is selected.</param>
        /// <param name="isHttps">Whether the https certificate of the application is the one wanted.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The certificate which was found, or null.</returns>
        public async Task<X509Certificate2> InitializeAsync(
            GlobalDiscoveryServerClient gds,
            ServerPushConfigurationClient server,
            RegisteredApplication application,
            bool isHttps,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            m_gds = gds;
            m_server = server;
            m_application = application;
            m_telemetry = telemetry;
            m_certificate = null;
            m_temporaryCertificateCreated = false;
            CertificatePassword = null;

            if (application == null)
            {
                return null;
            }

            m_certificate = isHttps
                ? await FindHttpsCertificateAsync(application, ct).ConfigureAwait(false)
                : await FindApplicationCertificateAsync(application, server, ct).ConfigureAwait(false);

            return m_certificate;
        }

        /// <summary>
        /// Forgets the certificate the model found, for a caller which decided it cannot be
        /// used.
        /// </summary>
        public void ForgetCertificate()
        {
            m_certificate = null;
        }

        /// <summary>
        /// Builds a new certificate from the information in the one which is loaded, with a
        /// key pair of its own.
        /// </summary>
        /// <remarks>
        /// This is what turns a public-only certificate - one read out of a <c>.der</c> file,
        /// for instance - into something an application can actually run on. The subject name,
        /// the application uri and the domain names are copied over, so the new certificate
        /// still identifies the same application; only the key pair is new.
        /// </remarks>
        /// <exception cref="InvalidOperationException">No certificate is loaded.</exception>
        public X509Certificate2 CreateKeyPairFromCertificateInfo()
        {
            if (m_certificate == null)
            {
                throw new InvalidOperationException("No certificate is loaded.");
            }

            Certificate certificateInfo = Certificate.From(m_certificate);

            IList<string> domainNames = X509Utils.GetDomainsFromCertificate(certificateInfo).ToList();
            IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(certificateInfo);
            ushort keySize = (ushort)(m_certificate.GetRSAPublicKey()?.KeySize ?? X509Defaults.RSAKeySize);

            ICertificateBuilder builder = DefaultCertificateFactory.Instance
                .CreateCertificate(m_certificate.Subject)
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1));

            if (domainNames.Count > 0 || applicationUris.Count > 0)
            {
                builder = builder.AddExtension(
                    new X509SubjectAltNameExtension(
                        applicationUris.Count > 0 ? applicationUris[0] : String.Empty,
                        domainNames));
            }

            return builder.SetRSAKeySize(keySize).CreateForRSA().AsX509Certificate2();
        }

        /// <summary>
        /// Writes a certificate and its private key to a PKCS#12 file, under
        /// <see cref="CertificatePassword"/>.
        /// </summary>
        /// <param name="certificate">The certificate to export.</param>
        /// <param name="path">The file to write.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task ExportPfxAsync(X509Certificate2 certificate, string path, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(certificate);

            string password = String.IsNullOrEmpty(CertificatePassword) ? null : CertificatePassword;

            return File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pfx, password), ct);
        }

        /// <summary>
        /// Takes a certificate as the one the application runs on, releasing the short lived
        /// stand-in if there was one.
        /// </summary>
        /// <param name="certificate">The certificate to take.</param>
        public void AdoptCertificate(X509Certificate2 certificate)
        {
            if (m_temporaryCertificateCreated)
            {
                m_certificate?.Dispose();
                m_temporaryCertificateCreated = false;
            }

            m_certificate = certificate;
        }

        /// <summary>
        /// A file name for an exported PKCS#12, taken from the common name of a subject.
        /// </summary>
        /// <param name="subjectName">The subject of the certificate.</param>
        public static string GetDefaultPfxFileName(string subjectName)
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

        /// <summary>
        /// Asks the Global Discovery Server for a new certificate.
        /// </summary>
        /// <remarks>
        /// The request runs on the server and is not finished when this returns;
        /// <see cref="FinishRequestAsync"/> is what asks whether it is done. The id of the
        /// request is remembered on the application record, so that a client which is
        /// restarted in the meantime can pick the answer up.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<CertificateRequestStart> StartRequestAsync(CancellationToken ct = default)
        {
            CertificateRequestStart start = IsServerPush
                ? await StartPushRequestAsync(ct).ConfigureAwait(false)
                : await StartPullRequestAsync(ct).ConfigureAwait(false);

            m_application.CertificateRequestId = start.RequestId.ToString();

            return start;
        }

        /// <summary>
        /// Asks whether the certificate request has finished, and puts the answer where the
        /// application will find it.
        /// </summary>
        /// <remarks>
        /// A request which is still running answers with <see cref="CertificateRequestState.Pending"/>
        /// and nothing else happens, so a caller can poll this on a timer.
        /// </remarks>
        /// <param name="shouldReplaceFile">Asked before an existing file is overwritten, with
        /// the path of that file. Null overwrites without asking.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<CertificateRequestResult> FinishRequestAsync(
            Func<string, bool> shouldReplaceFile = null,
            CancellationToken ct = default)
        {
            var requestId = NodeId.Parse(m_application.CertificateRequestId);

            (ByteString certificate, ByteString privateKeyPFX, ArrayOf<ByteString> issuerCertificates) = await m_gds
                .FinishRequestAsync(NodeId.Parse(m_application.ApplicationId), requestId, ct)
                .ConfigureAwait(false);

            // request not done yet. Length rather than IsNull: a GDS that answers a pending
            // request with an empty ByteString instead of a null one would otherwise fall
            // through to the loader below.
            if (certificate.Length == 0)
            {
                return new CertificateRequestResult(CertificateRequestState.Pending, null, false);
            }

            if (IsServerPush)
            {
                bool applyChanges = await PushCertificateToServerAsync(certificate, privateKeyPFX, issuerCertificates, ct)
                    .ConfigureAwait(false);

                return new CertificateRequestResult(CertificateRequestState.PushedToServer, m_certificate, applyChanges);
            }

            X509Certificate2 issued = await StoreIssuedCertificateAsync(
                certificate,
                privateKeyPFX,
                issuerCertificates,
                shouldReplaceFile,
                ct).ConfigureAwait(false);

            m_certificate = issued;

            return new CertificateRequestResult(CertificateRequestState.Issued, issued, false);
        }

        /// <summary>
        /// Tells the server to start using what was pushed to it, and lets go of the session.
        /// </summary>
        /// <remarks>
        /// A server which restarts itself to apply the changes answers with
        /// <c>BadServerHalted</c> and then drops the connection; that is the expected outcome
        /// rather than a failure, and it is reported instead of thrown.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>True when the server halted itself to apply the changes.</returns>
        public async Task<bool> ApplyChangesAsync(CancellationToken ct = default)
        {
            bool halted = false;

            try
            {
                await m_server.ApplyChangesAsync(ct).ConfigureAwait(false);
            }
            catch (ServiceResultException e) when (e.StatusCode == StatusCodes.BadServerHalted)
            {
                halted = true;
            }
            finally
            {
                try
                {
                    await m_server.DisconnectAsync(ct).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // the session is gone with the server which just restarted itself
                }
            }

            return halted;
        }

        /// <summary>
        /// The certificate a push server is running on, or the one its application record
        /// points at.
        /// </summary>
        /// <remarks>
        /// Only a push application may seed the certificate from the endpoint of the
        /// connected server. For a pull application that server is the GDS, or the target
        /// server, and its certificate must not be used as the application certificate -
        /// otherwise the host name of the GDS leaks into the domain list of the certificate
        /// which is then requested. See issue #741.
        /// </remarks>
        private async Task<X509Certificate2> FindApplicationCertificateAsync(
            RegisteredApplication application,
            ServerPushConfigurationClient server,
            CancellationToken ct)
        {
            // Length, not IsNull: an EndpointDescription which carries no certificate holds
            // an empty ByteString rather than a null one, and a server only fills the field
            // for an endpoint which requires encryption. A SecurityPolicy=None endpoint
            // would otherwise hand an empty array to the loader, which reports it as
            // "ASN1 corrupted data".
            if (application.RegistrationType == RegistrationType.ServerPush &&
                server.Endpoint != null &&
                server.Endpoint.Description.ServerCertificate.Length > 0)
            {
                return GdsCertificateLoader.LoadCertificate(server.Endpoint.Description.ServerCertificate.ToArray());
            }

            if (!String.IsNullOrEmpty(application.CertificatePublicKeyPath))
            {
                return LoadFromFile(application.CertificatePublicKeyPath);
            }

            if (!String.IsNullOrEmpty(application.CertificateStorePath))
            {
                var id = new CertificateIdentifier {
                    StorePath = application.CertificateStorePath,
                };

                id.StoreType = CertificateStoreIdentifier.DetermineStoreType(id.StorePath);
                id.SubjectName = application.CertificateSubjectName.Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);

                return await FindCertificateAsync(id, ct).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// The certificate the https endpoint of an application uses.
        /// </summary>
        /// <remarks>
        /// With no public key file named, the certificate is looked for under the host name
        /// of each discovery url the application advertises: an https certificate is issued
        /// to a host rather than to an application.
        /// </remarks>
        private async Task<X509Certificate2> FindHttpsCertificateAsync(
            RegisteredApplication application,
            CancellationToken ct)
        {
            if (!String.IsNullOrEmpty(application.HttpsCertificatePublicKeyPath))
            {
                return LoadFromFile(application.HttpsCertificatePublicKeyPath);
            }

            X509Certificate2 certificate = null;

            foreach (string discoveryUrl in application.DiscoveryUrl)
            {
                if (!Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    continue;
                }

                var url = new Uri(discoveryUrl);

                var id = new CertificateIdentifier {
                    StoreType = CertificateStoreType.X509Store,
                    StorePath = "CurrentUser\\UA_MachineDefault",
                    SubjectName = "CN=" + url.DnsSafeHost,
                };

                certificate = await FindCertificateAsync(id, ct).ConfigureAwait(false);
            }

            return certificate;
        }

        /// <summary>
        /// Loads a certificate from a public key file, or null when the file is not there.
        /// </summary>
        private static X509Certificate2 LoadFromFile(string publicKeyPath)
        {
            string file = Utils.GetAbsoluteFilePath(publicKeyPath, true, false, false);

            return file != null ? GdsCertificateLoader.LoadCertificateFromFile(file) : null;
        }

        /// <summary>
        /// Pushes the trust list of the application to the server, then has the server build
        /// a signing request and hands that to the Global Discovery Server.
        /// </summary>
        private async Task<CertificateRequestStart> StartPushRequestAsync(CancellationToken ct)
        {
            NodeId trustListId = await m_gds
                .GetTrustListAsync(NodeId.Parse(m_application.ApplicationId), NodeId.Null, ct)
                .ConfigureAwait(false);

            TrustListDataType trustList = await m_gds.ReadTrustListAsync(trustListId, 0, ct).ConfigureAwait(false);
            bool applyChanges = await m_server.UpdateTrustListAsync(trustList, ct).ConfigureAwait(false);

            ByteString certificateRequest = await m_server.CreateSigningRequestAsync(
                NodeId.Null,
                m_server.ApplicationCertificateType,
                String.Empty,
                false,
                Array.Empty<byte>().ToByteString(),
                ct).ConfigureAwait(false);

            NodeId requestId = await m_gds.StartSigningRequestAsync(
                NodeId.Parse(m_application.ApplicationId),
                NodeId.Null,
                NodeId.Null,
                certificateRequest,
                ct).ConfigureAwait(false);

            return new CertificateRequestStart(requestId, applyChanges);
        }

        /// <summary>
        /// Asks the Global Discovery Server for a certificate for an application which keeps
        /// its key itself.
        /// </summary>
        /// <remarks>
        /// With no private key of its own the application asks the GDS to generate the key
        /// pair as well. With one, it signs a certificate request instead, so that the
        /// private key never leaves the machine - and when that key cannot be exported, a
        /// short lived certificate is created only to carry the request.
        /// </remarks>
        private async Task<CertificateRequestStart> StartPullRequestAsync(CancellationToken ct)
        {
            await PrepareCertificateForRequestAsync(ct).ConfigureAwait(false);

            List<string> domainNames = m_application.GetDomainNames(
                m_certificate != null ? Certificate.From(m_certificate) : null);

            if (m_certificate == null)
            {
                // no private key: the GDS generates the key pair as well.
                NodeId newKeyPair = await m_gds.StartNewKeyPairRequestAsync(
                    NodeId.Parse(m_application.ApplicationId),
                    NodeId.Null,
                    NodeId.Null,
                    Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName),
                    domainNames,
                    "PFX",
                    CertificatePassword?.ToCharArray() ?? Array.Empty<char>(),
                    ct).ConfigureAwait(false);

                return new CertificateRequestStart(newKeyPair, false);
            }

            // a certificate built out of the private key file is only there to sign the
            // request, so it is released again; the one the model holds is not.
            X509Certificate2 csrCertificate = m_certificate.HasPrivateKey
                ? m_certificate
                : await LoadCsrCertificateFromPrivateKeyFileAsync(ct).ConfigureAwait(false);

            byte[] certificateRequest;

            try
            {
                certificateRequest = DefaultCertificateFactory.Instance
                    .CreateSigningRequest(Certificate.From(csrCertificate), domainNames);
            }
            finally
            {
                if (!ReferenceEquals(csrCertificate, m_certificate))
                {
                    csrCertificate.Dispose();
                }
            }

            NodeId requestId = await m_gds.StartSigningRequestAsync(
                NodeId.Parse(m_application.ApplicationId),
                NodeId.Null,
                NodeId.Null,
                certificateRequest.ToByteString(),
                ct).ConfigureAwait(false);

            return new CertificateRequestStart(requestId, false);
        }

        /// <summary>
        /// Finds the certificate the request will be built from, and makes sure its private
        /// key can be used for one.
        /// </summary>
        /// <remarks>
        /// A key which the platform will not export cannot sign a certificate request, and
        /// the store does not say so up front - the export is what fails. So it is tried,
        /// and a short lived certificate of the same key size takes over when it does not
        /// work; that stand-in is only there to carry the request and is thrown away once
        /// the signed certificate arrives.
        /// </remarks>
        private async Task PrepareCertificateForRequestAsync(CancellationToken ct)
        {
            m_certificate = null;
            m_temporaryCertificateCreated = false;

            if (String.IsNullOrEmpty(m_application.CertificateStorePath))
            {
                return;
            }

            var id = new CertificateIdentifier {
                StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath),
                StorePath = m_application.CertificateStorePath,
                SubjectName = Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName),
            };

            m_certificate = await FindCertificateAsync(id, ct).ConfigureAwait(false);

            if (m_certificate == null || !m_certificate.HasPrivateKey)
            {
                return;
            }

            try
            {
                // this throws a CryptographicException when the private key may not be exported
                _ = m_certificate.GetRSAPrivateKey().ExportParameters(true);

                m_certificate = await LoadPrivateKeyAsync(id, m_certificate, CertificatePassword?.ToCharArray())
                    .ConfigureAwait(false);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                m_certificate = DefaultCertificateFactory.Instance
                    .CreateCertificate(Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName))
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddDays(14))
                    .SetRSAKeySize((ushort)(m_certificate.GetRSAPublicKey()?.KeySize ?? 0))
                    .CreateForRSA()
                    .AsX509Certificate2();

                m_temporaryCertificateCreated = true;
            }
        }

        /// <summary>
        /// Builds the certificate which carries the signing request out of the private key
        /// file the application names.
        /// </summary>
        private async Task<X509Certificate2> LoadCsrCertificateFromPrivateKeyFileAsync(CancellationToken ct)
        {
            string path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);
            byte[] pkcsData = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);

            ArrayOf<string> keyFormats = m_server != null
                ? await m_server.GetSupportedKeyFormatsAsync(ct).ConfigureAwait(false)
                : ArrayOf<string>.Empty;

            if (m_application.GetPrivateKeyFormat(keyFormats.ToArray()) == "PFX")
            {
                #pragma warning disable CA2000 // Justification: AsX509Certificate2 takes over the handle of the wrapper, which the caller disposes.
                return X509PfxUtils.CreateCertificateFromPKCS12(pkcsData, CertificatePassword.AsSpan()).AsX509Certificate2();
                #pragma warning restore CA2000
            }

            return DefaultCertificateFactory.Instance
                .CreateWithPEMPrivateKey(Certificate.From(m_certificate), pkcsData, CertificatePassword.AsSpan())
                .AsX509Certificate2();
        }

        /// <summary>
        /// Writes the certificate the Global Discovery Server issued to the server it belongs
        /// to.
        /// </summary>
        private async Task<bool> PushCertificateToServerAsync(
            ByteString certificate,
            ByteString privateKeyPFX,
            ArrayOf<ByteString> issuerCertificates,
            CancellationToken ct)
        {
            if (!privateKeyPFX.IsNull && privateKeyPFX.Length > 0)
            {
                X509Certificate2 x509 = GdsCertificateLoader.LoadPkcs12(
                    privateKeyPFX.ToArray(),
                    CertificatePassword,
                    X509KeyStorageFlags.Exportable);

                privateKeyPFX = x509.Export(X509ContentType.Pfx).ToByteString();
            }

            return await m_server.UpdateCertificateAsync(
                NodeId.Null,
                m_server.ApplicationCertificateType,
                certificate,
                (!privateKeyPFX.IsNull) ? "pfx" : String.Empty,
                (!privateKeyPFX.IsNull) ? privateKeyPFX : Array.Empty<byte>().ToByteString(),
                issuerCertificates,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Puts an issued certificate where the application will find it - its certificate
        /// store, or the files it names - and adds the issuers to its trust list.
        /// </summary>
        private async Task<X509Certificate2> StoreIssuedCertificateAsync(
            ByteString certificate,
            ByteString privateKeyPFX,
            ArrayOf<ByteString> issuerCertificates,
            Func<string, bool> shouldReplaceFile,
            CancellationToken ct)
        {
            X509Certificate2 newCert = GdsCertificateLoader.LoadCertificate(certificate.ToArray());

            if (!String.IsNullOrEmpty(m_application.CertificateStorePath) &&
                !String.IsNullOrEmpty(m_application.CertificateSubjectName))
            {
                newCert = await StoreInCertificateStoreAsync(newCert, privateKeyPFX, ct).ConfigureAwait(false);
            }
            else
            {
                newCert = await StoreInFilesAsync(newCert, privateKeyPFX, shouldReplaceFile, ct).ConfigureAwait(false);
            }

            await AddIssuersToTrustListAsync(issuerCertificates, ct).ConfigureAwait(false);

            return newCert;
        }

        /// <summary>
        /// Adds the issued certificate to the certificate store of the application, married
        /// to the private key it belongs with.
        /// </summary>
        /// <remarks>
        /// A certificate which was requested with a signing request has no private key in the
        /// answer, because the key never left this machine: the new certificate is joined to
        /// the key of the old one, and the old certificate is deleted. A certificate the GDS
        /// generated the key pair for arrives as a PFX and carries its own.
        /// </remarks>
        private async Task<X509Certificate2> StoreInCertificateStoreAsync(
            X509Certificate2 newCert,
            ByteString privateKeyPFX,
            CancellationToken ct)
        {
            var cid = new CertificateIdentifier {
                StorePath = m_application.CertificateStorePath,
                StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath),
                SubjectName = Utils.ReplaceDCLocalhost(m_application.CertificateSubjectName),
            };

            var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.CertificateStorePath, false);

            using ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry);

            if (privateKeyPFX.IsNull)
            {
                X509Certificate2 oldCertificate = await FindCertificateAsync(cid, ct).ConfigureAwait(false);

                if (oldCertificate == null || !oldCertificate.HasPrivateKey)
                {
                    throw new ServiceResultException("Failed to merge signed certificate with the private key.");
                }

                oldCertificate = await LoadPrivateKeyAsync(cid, oldCertificate, CertificatePassword?.ToCharArray())
                    .ConfigureAwait(false);

                newCert = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                    Certificate.From(newCert),
                    Certificate.From(m_temporaryCertificateCreated ? m_certificate : oldCertificate)).AsX509Certificate2();

                await store.DeleteAsync(oldCertificate.Thumbprint, ct).ConfigureAwait(false);
            }
            else
            {
                newCert = GdsCertificateLoader.LoadPkcs12(
                    privateKeyPFX.ToArray(),
                    CertificatePassword ?? String.Empty,
                    X509KeyStorageFlags.Exportable);
            }

            await store.AddAsync(Certificate.From(newCert), ct: ct).ConfigureAwait(false);

            if (m_temporaryCertificateCreated)
            {
                m_certificate?.Dispose();
                m_certificate = null;
                m_temporaryCertificateCreated = false;
            }

            return newCert;
        }

        /// <summary>
        /// Writes the issued certificate, and the private key when there is one, to the files
        /// the application names.
        /// </summary>
        private async Task<X509Certificate2> StoreInFilesAsync(
            X509Certificate2 newCert,
            ByteString privateKeyPFX,
            Func<string, bool> shouldReplaceFile,
            CancellationToken ct)
        {
            string publicKeyPath = GetSaveFilePath(m_application.CertificatePublicKeyPath);
            var publicKeyFile = new FileInfo(publicKeyPath);

            if (!publicKeyFile.Exists || shouldReplaceFile == null || shouldReplaceFile(publicKeyPath))
            {
                byte[] exportedCert = String.Equals(publicKeyFile.Extension, ".PEM", StringComparison.OrdinalIgnoreCase)
                    ? PEMWriter.ExportCertificateAsPEM(Certificate.From(newCert))
                    : newCert.Export(X509ContentType.Cert);

                await File.WriteAllBytesAsync(publicKeyPath, exportedCert, ct).ConfigureAwait(false);
            }

            ArrayOf<string> keyFormats = m_server != null
                ? await m_server.GetSupportedKeyFormatsAsync(ct).ConfigureAwait(false)
                : ArrayOf<string>.Empty;

            // only a PFX private key file has to be rewritten: the new certificate is married
            // to the key which is already in it.
            if (m_application.GetPrivateKeyFormat(keyFormats.ToArray()) != "PFX")
            {
                return newCert;
            }

            string privateKeyPath = GetSaveFilePath(m_application.CertificatePrivateKeyPath);
            var privateKeyFile = new FileInfo(privateKeyPath);

            if (privateKeyFile.Exists && shouldReplaceFile != null && !shouldReplaceFile(privateKeyPath))
            {
                return newCert;
            }

            if (privateKeyFile.Exists)
            {
                if (!privateKeyPFX.IsNull)
                {
                    throw new ServiceResultException("Did not expect a private key for this operation.");
                }

                byte[] pkcsData = await File.ReadAllBytesAsync(privateKeyPath, ct).ConfigureAwait(false);

                // the key in the file is the one to keep; the certificate around it is only
                // read to get at it and goes no further.
                #pragma warning disable CA2000 // Justification: AsX509Certificate2 takes over the handle of the wrapper, and the using below disposes it.
                using (X509Certificate2 oldCertificate = X509PfxUtils
                    .CreateCertificateFromPKCS12(pkcsData, CertificatePassword.AsSpan())
                    .AsX509Certificate2())
                #pragma warning restore CA2000
                {
                    newCert = DefaultCertificateFactory.Instance
                        .CreateWithPrivateKey(Certificate.From(newCert), Certificate.From(oldCertificate))
                        .AsX509Certificate2();
                }

                await File.WriteAllBytesAsync(
                    privateKeyPath,
                    newCert.Export(X509ContentType.Pfx, CertificatePassword),
                    ct).ConfigureAwait(false);

                return newCert;
            }

            await File.WriteAllBytesAsync(privateKeyPath, privateKeyPFX.ToArray(), ct).ConfigureAwait(false);

            return newCert;
        }

        /// <summary>
        /// Adds the issuers of the new certificate to the trust list of the application, so
        /// that it can build the chain of its own certificate.
        /// </summary>
        private async Task AddIssuersToTrustListAsync(ArrayOf<ByteString> issuerCertificates, CancellationToken ct)
        {
            if (String.IsNullOrEmpty(m_application.TrustListStorePath))
            {
                return;
            }

            var certificateStoreIdentifier = new CertificateStoreIdentifier(m_application.TrustListStorePath);

            using ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry);

            foreach (ByteString issuerCertificate in issuerCertificates.ToArray())
            {
                X509Certificate2 x509 = GdsCertificateLoader.LoadCertificate(issuerCertificate.ToArray());

                CertificateCollection found = await store.FindByThumbprintAsync(x509.Thumbprint, ct).ConfigureAwait(false);

                if (found.Count == 0)
                {
                    await store.AddAsync(Certificate.From(x509), ct: ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Finds a certificate in a store by its thumbprint or its subject name.
        /// </summary>
        private async Task<X509Certificate2> FindCertificateAsync(CertificateIdentifier id, CancellationToken ct = default)
        {
            var storeIdentifier = new CertificateStoreIdentifier(id.StorePath, false);

            using ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);

            CertificateCollection certificates = await store.EnumerateAsync(ct).ConfigureAwait(false);

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

            return null;
        }

        /// <summary>
        /// Loads the private key of a certificate out of the store it lives in.
        /// </summary>
        private async Task<X509Certificate2> LoadPrivateKeyAsync(
            CertificateIdentifier id,
            X509Certificate2 certificate,
            char[] password)
        {
            if (certificate == null)
            {
                return null;
            }

            // the certificate already carries an exportable private key, nothing to load.
            if (certificate.HasPrivateKey)
            {
                return certificate;
            }

            if (id == null || String.IsNullOrEmpty(id.StorePath))
            {
                return certificate;
            }

            var storeIdentifier = new CertificateStoreIdentifier(id.StorePath, false);

            using ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);

            if (store == null || !store.SupportsLoadPrivateKey)
            {
                return certificate;
            }

            Certificate withPrivateKey = await store.LoadPrivateKeyAsync(
                certificate.Thumbprint,
                certificate.Subject,
                null,
                id.CertificateType,
                (password != null && password.Length > 0) ? password : null).ConfigureAwait(false);

            return withPrivateKey != null ? withPrivateKey.AsX509Certificate2() : certificate;
        }

        /// <summary>
        /// Resolves a certificate file path for saving.
        /// </summary>
        /// <remarks>
        /// <see cref="Utils.GetAbsoluteFilePath(string, bool, bool, bool)"/> only resolves paths
        /// to files that already exist. When saving a new certificate the target file does not
        /// exist yet, so that call fails and the raw path - which may still contain an unexpanded
        /// placeholder such as <c>%CommonApplicationData%</c> - would be used verbatim. This
        /// falls back to <see cref="Utils.ReplaceSpecialFolderNames(string)"/> so that the
        /// placeholders are expanded and the certificate is written where it was meant to go.
        /// </remarks>
        private static string GetSaveFilePath(string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            try
            {
                // prefer an already existing file (this also handles the current directory).
                string resolved = Utils.GetAbsoluteFilePath(filePath, true, false, false);

                if (!String.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }
            }
            catch (ServiceResultException)
            {
                // the file does not exist yet (a new certificate): fall through to the
                // placeholder expansion below.
            }

            return Utils.ReplaceSpecialFolderNames(filePath) ?? filePath;
        }
    }
}
