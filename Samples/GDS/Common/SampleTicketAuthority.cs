/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// The OPC 10000-21 ticket authority of the sample: a stand-in device manufacturer whose
    /// certificate the registrar trusts, and which signs one sample ticket for the user to load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Since SDK 2.0.2 an onboarding ticket is an RFC 7515 JWS: the registrar only accepts a
    /// ticket whose signature verifies against a certificate in its <c>TicketAuthorities</c>
    /// trust list (<see cref="OnboardingTicketTrustProvider.TicketAuthorities"/>). A real
    /// registrar trusts the certificates its device manufacturers publish; the sample has no
    /// manufacturer, so it plays one itself. At start-up it makes sure that
    /// </para>
    /// <list type="bullet">
    /// <item>a self-signed "manufacturer" certificate with its private key exists in
    /// <c>GDS/PKI/manufacturer</c>,</item>
    /// <item>the public half of it is trusted in <c>GDS/PKI/ticketAuthorities</c>, the store the
    /// registrar's trust list is registered on, and</item>
    /// <item>a ticket for one sample device, signed by that certificate with the SDK's
    /// <see cref="OnboardingTicketWriter"/>, is in <c>GDS/Onboarding/sample-device.ticket</c>.</item>
    /// </list>
    /// <para>
    /// The manufacturer's private key never belongs on a registrar - it is only here so that the
    /// sample can issue a ticket of its own. Trusting another manufacturer is a matter of copying
    /// its certificate into the <c>ticketAuthorities/certs</c> folder.
    /// </para>
    /// </remarks>
    public sealed class SampleTicketAuthority
    {
        /// <summary>
        /// Subject of the sample manufacturer's ticket signing certificate.
        /// </summary>
        public const string ManufacturerSubjectName = "CN=Sample Device Manufacturer, O=OPC Foundation";

        /// <summary>
        /// The product instance the sample ticket entitles to onboard.
        /// </summary>
        public const string SampleProductInstanceUri = "urn:opcfoundation.org:UA-NETStandard-Samples:device:SN-0001";

        /// <summary>
        /// File name of the sample ticket.
        /// </summary>
        public const string SampleTicketFileName = "sample-device.ticket";

        private const string kOnboardingNamespaceUri = "http://opcfoundation.org/UA/Onboarding/";

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Creates the authority over the PKI of the supplied GDS configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public SampleTicketAuthority(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

            string root = GetStateDirectory(configuration);

            TrustedStorePath = Path.Combine(root, "PKI", "ticketAuthorities");
            ManufacturerStorePath = Path.Combine(root, "PKI", "manufacturer");
            SampleTicketPath = Path.Combine(root, "Onboarding", SampleTicketFileName);
        }

        /// <summary>
        /// The directory store holding the ticket authorities the registrar trusts.
        /// </summary>
        public string TrustedStorePath { get; }

        /// <summary>
        /// The directory store holding the sample manufacturer's certificate and private key.
        /// </summary>
        public string ManufacturerStorePath { get; }

        /// <summary>
        /// The file the signed sample ticket is written to.
        /// </summary>
        public string SampleTicketPath { get; }

        /// <summary>
        /// Creates the manufacturer certificate and the sample ticket when they are missing,
        /// registers the <c>TicketAuthorities</c> trust list with the certificate manager of the
        /// configuration, and returns the validator the registrar checks tickets with.
        /// </summary>
        public async Task<IOnboardingTicketValidator> InitializeAsync(CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = CreateMessageContext(m_telemetry);

            (CertificateIdentifier signer, bool created) = await EnsureManufacturerCertificateAsync(ct)
                .ConfigureAwait(false);

            // the trust list is registered once per certificate manager; a server restarted by
            // the WinForms host reuses the configuration and with it the registration.
            ICertificateManager certificateManager = m_configuration.CertificateManager;
            var trustLists = (ICertificateTrustListManager)certificateManager;

            if (!trustLists.TrustLists.Contains(OnboardingTicketTrustProvider.TicketAuthorities))
            {
                trustLists.RegisterTrustList(OnboardingTicketTrustProvider.TicketAuthorities, TrustedStorePath);
            }

            if (created || !File.Exists(SampleTicketPath))
            {
                await WriteSampleTicketAsync(signer, messageContext, ct).ConfigureAwait(false);
            }

            return new OnboardingTicketValidator(
                new OnboardingTicketTrustProvider(certificateManager),
                messageContext);
        }

        /// <summary>
        /// Signs a ticket for <see cref="SampleProductInstanceUri"/> and writes it to
        /// <see cref="SampleTicketPath"/>.
        /// </summary>
        private async Task WriteSampleTicketAsync(
            CertificateIdentifier signer,
            ServiceMessageContext messageContext,
            CancellationToken ct)
        {
            // the identity authority is the CA which issues the device's own (IDevID)
            // certificate. The sample manufacturer plays that role as well.
            var ticket = new DeviceIdentityTicketType {
                ProductInstanceUri = SampleProductInstanceUri,
                ManufacturerName = "Sample Device Manufacturer",
                ModelName = "Sample Device",
                ModelVersion = "1.0",
                HardwareRevision = "1.0",
                SoftwareRevision = "1.0",
                SerialNumber = "SN-0001",
                ManufactureDate = DateTime.UtcNow.Date,
                Authorities = new[] {
                    new CertificateAuthorityType {
                        AuthorityCertificate = ByteString.From(signer.RawData)
                    }
                }.ToArrayOf()
            };

            var writer = new OnboardingTicketWriter(m_configuration.CertificateManager.CertificateProvider, messageContext);
            string encoded = await writer.WriteAsync(ticket, signer, ct: ct).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(SampleTicketPath));
            await File.WriteAllTextAsync(SampleTicketPath, encoded, new UTF8Encoding(false), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds the manufacturer certificate, or creates it when there is none that is still
        /// valid, and makes sure the registrar trusts it.
        /// </summary>
        private async Task<(CertificateIdentifier Signer, bool Created)> EnsureManufacturerCertificateAsync(
            CancellationToken ct)
        {
            Directory.CreateDirectory(ManufacturerStorePath);
            Directory.CreateDirectory(TrustedStorePath);

            var manufacturerStore = new CertificateStoreIdentifier(ManufacturerStorePath, false);
            var trustedStore = new CertificateStoreIdentifier(TrustedStorePath, false);

            Certificate current = null;
            bool created = false;

            using (ICertificateStore store = manufacturerStore.OpenStore(m_telemetry))
            {
                CertificateCollection certificates = await store.EnumerateAsync(ct).ConfigureAwait(false);

                foreach (Certificate certificate in certificates)
                {
                    if (String.Equals(certificate.Subject, ManufacturerSubjectName, StringComparison.Ordinal) &&
                        certificate.NotAfter > DateTime.UtcNow.AddDays(1) &&
                        (current == null || certificate.NotAfter > current.NotAfter))
                    {
                        current = certificate;
                    }
                }

                if (current == null)
                {
                    // RSA 2048 with SHA-256 is what the registrar's trust provider asks of a
                    // ticket signer at least; the certificate is self-signed, so it is its own
                    // trust anchor and needs no revocation list.
                    using Certificate signer = DefaultCertificateFactory.Instance
                        .CreateCertificate(ManufacturerSubjectName)
                        .SetNotBefore(DateTime.UtcNow.Date.AddDays(-1))
                        .SetLifeTime(120)
                        .SetRSAKeySize(2048)
                        .CreateForRSA();

                    await store.AddAsync(signer, null, ct).ConfigureAwait(false);

                    current = Certificate.FromRawData(signer.RawData);
                    created = true;
                }
            }

            using (ICertificateStore store = trustedStore.OpenStore(m_telemetry))
            {
                CertificateCollection trusted = await store.FindByThumbprintAsync(current.Thumbprint, ct)
                    .ConfigureAwait(false);

                if (trusted.Count == 0)
                {
                    using Certificate publicOnly = Certificate.FromRawData(current.RawData);
                    await store.AddAsync(publicOnly, null, ct).ConfigureAwait(false);
                }
            }

            var identifier = new CertificateIdentifier {
                StoreType = manufacturerStore.StoreType,
                StorePath = ManufacturerStorePath,
                SubjectName = ManufacturerSubjectName,
                Thumbprint = current.Thumbprint,
                RawData = current.RawData
            };

            return (identifier, created);
        }

        /// <summary>
        /// Creates a message context which knows the Onboarding companion model's types - the
        /// ticket payload is one of them.
        /// </summary>
        private static ServiceMessageContext CreateMessageContext(ITelemetryContext telemetry)
        {
            ServiceMessageContext context = ServiceMessageContextExtensions.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(kOnboardingNamespaceUri);
            context.Factory.Builder.AddOpcUaOnboarding().Commit();
            return context;
        }

        /// <summary>
        /// Returns the directory the GDS keeps its state in - one level above its PKI.
        /// </summary>
        private static string GetStateDirectory(ApplicationConfiguration configuration)
        {
            GlobalDiscoveryServerConfiguration gdsConfiguration =
                configuration.ParseExtension<GlobalDiscoveryServerConfiguration>();

            return GetStateDirectory(
                gdsConfiguration?.AuthoritiesStorePath,
                Path.GetDirectoryName(configuration.SourceFilePath) ?? ".");
        }

        private static string GetStateDirectory(string authoritiesStorePath, string fallback)
        {
            string authorities = Utils.ReplaceSpecialFolderNames(authoritiesStorePath);

            // .../GDS/PKI/authorities -> .../GDS
            string pki = String.IsNullOrEmpty(authorities) ? null : Path.GetDirectoryName(authorities);
            string root = String.IsNullOrEmpty(pki) ? null : Path.GetDirectoryName(pki);

            return String.IsNullOrEmpty(root) ? fallback : root;
        }
    }
}
