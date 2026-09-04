/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client.Model
{
    /// <summary>
    /// Where a trust list read from a push server came from: the list itself and the
    /// certificates the server rejected.
    /// </summary>
    /// <param name="TrustList">The trust list the server holds.</param>
    /// <param name="RejectedCertificates">What the server put in its rejected list.</param>
    public sealed record ServerTrustList(
        TrustListDataType TrustList,
        X509Certificate2Collection RejectedCertificates);

    /// <summary>
    /// What a pull from the Global Discovery Server ended in.
    /// </summary>
    public enum TrustListPullOutcome
    {
        /// <summary>The GDS holds no trust list for this application.</summary>
        NoTrustList,

        /// <summary>The list was downloaded and still has to be pushed to the server.</summary>
        AwaitingPushToServer,

        /// <summary>The list was downloaded and written into the local stores.</summary>
        SavedLocally,
    }

    /// <summary>
    /// The result of a pull from the Global Discovery Server.
    /// </summary>
    /// <param name="Outcome">What happened.</param>
    /// <param name="TrustList">The list which was downloaded, or null when there was none.</param>
    public sealed record TrustListPullResult(TrustListPullOutcome Outcome, TrustListDataType TrustList);

    /// <summary>
    /// The trust list of a registered application: reading it off a push server, pulling it
    /// from the Global Discovery Server into the local stores, and pushing it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which of those happen depends on how the application is registered. A
    /// <see cref="RegistrationType.ServerPush"/> application keeps its trust list on the
    /// server and is asked for it over the session; every other kind keeps it in a
    /// certificate store on disk, which this reads and writes directly.
    /// </para>
    /// <para>
    /// The window shows the lists, asks the questions and reports the failures; nothing
    /// here knows about a window.
    /// </para>
    /// </remarks>
    public sealed class TrustListModel
    {
        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;
        private ITelemetryContext m_telemetry;

        /// <summary>
        /// The store the trusted certificates of the application live in, or null when it
        /// keeps them on a server instead.
        /// </summary>
        public string TrustListStorePath { get; private set; }

        /// <summary>
        /// The store the issuer certificates of the application live in.
        /// </summary>
        public string IssuerListStorePath { get; private set; }

        /// <summary>
        /// True while the model has an application to work on.
        /// </summary>
        public bool HasApplication => m_application != null;

        /// <summary>
        /// True when the application keeps its trust list on a server, which is what
        /// decides between the two halves of every operation below.
        /// </summary>
        public bool IsServerPush => m_application?.RegistrationType == RegistrationType.ServerPush;

        /// <summary>
        /// True when there is somewhere to pull a trust list to.
        /// </summary>
        public bool CanPullFromGds => !String.IsNullOrEmpty(TrustListStorePath) || IsServerPush;

        /// <summary>
        /// Points the model at an application and the two clients which serve it.
        /// </summary>
        /// <param name="gds">The Global Discovery Server client.</param>
        /// <param name="server">The push configuration client, for a server push application.</param>
        /// <param name="application">The application, or null when none is selected.</param>
        /// <param name="isHttps">Whether the https stores of the application are the ones to use.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public void Initialize(
            GlobalDiscoveryServerClient gds,
            ServerPushConfigurationClient server,
            RegisteredApplication application,
            bool isHttps,
            ITelemetryContext telemetry)
        {
            m_gds = gds;
            m_server = server;
            m_application = application;
            m_telemetry = telemetry;

            TrustListStorePath = null;
            IssuerListStorePath = null;

            if (application != null)
            {
                TrustListStorePath = isHttps ? application.HttpsTrustListStorePath : application.TrustListStorePath;
                IssuerListStorePath = isHttps ? application.HttpsIssuerListStorePath : application.IssuerListStorePath;
            }
        }

        /// <summary>
        /// Reads the trust list and the rejected list off a push server.
        /// </summary>
        /// <param name="masks">Which parts of the list to read.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<ServerTrustList> ReadFromServerAsync(TrustListMasks masks, CancellationToken ct = default)
        {
            TrustListDataType trustList = await m_server.ReadTrustListAsync(masks: masks, ct: ct).ConfigureAwait(false);

            var rejectedList = new X509Certificate2Collection();

            foreach (Certificate certificate in await m_server.GetRejectedListAsync(ct).ConfigureAwait(false))
            {
                rejectedList.Add(certificate.AsX509Certificate2());
            }

            return new ServerTrustList(trustList, rejectedList);
        }

        /// <summary>
        /// Downloads the trust list the Global Discovery Server holds for the application.
        /// </summary>
        /// <remarks>
        /// For a server push application the list is only handed back - it still has to be
        /// pushed to the server. For every other kind it is written into the local stores,
        /// where the application will read it on its next start.
        /// </remarks>
        /// <param name="deleteBeforeAdd">Whether to empty the local stores first, so that the
        /// list becomes exactly what the GDS holds rather than the union of the two.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<TrustListPullResult> PullFromGdsAsync(bool deleteBeforeAdd, CancellationToken ct = default)
        {
            NodeId trustListId = await m_gds
                .GetTrustListAsync(NodeId.Parse(m_application.ApplicationId), NodeId.Null, ct)
                .ConfigureAwait(false);

            if (trustListId.IsNull)
            {
                return new TrustListPullResult(TrustListPullOutcome.NoTrustList, null);
            }

            TrustListDataType trustList = await m_gds.ReadTrustListAsync(trustListId, 0, ct).ConfigureAwait(false);

            if (IsServerPush)
            {
                return new TrustListPullResult(TrustListPullOutcome.AwaitingPushToServer, trustList);
            }

            if (deleteBeforeAdd && !String.IsNullOrEmpty(TrustListStorePath))
            {
                await DeleteExistingFromStoreAsync(TrustListStorePath, ct).ConfigureAwait(false);
                await DeleteExistingFromStoreAsync(IssuerListStorePath, ct).ConfigureAwait(false);
            }

            await SaveToStoreAsync(
                TrustListStorePath,
                trustList,
                Opc.Ua.TrustListMasks.TrustedCertificates,
                trustList.TrustedCertificates,
                Opc.Ua.TrustListMasks.TrustedCrls,
                trustList.TrustedCrls,
                ct).ConfigureAwait(false);

            // the issuer list of the application rather than the one selected above: the
            // https stores share the issuers of the application.
            await SaveToStoreAsync(
                m_application.IssuerListStorePath,
                trustList,
                Opc.Ua.TrustListMasks.IssuerCertificates,
                trustList.IssuerCertificates,
                Opc.Ua.TrustListMasks.IssuerCrls,
                trustList.IssuerCrls,
                ct).ConfigureAwait(false);

            return new TrustListPullResult(TrustListPullOutcome.SavedLocally, trustList);
        }

        /// <summary>
        /// Pushes a trust list to a server.
        /// </summary>
        /// <param name="trustList">The list to push.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>True when the server needs an apply changes before it uses the new list.</returns>
        public Task<bool> PushToServerAsync(TrustListDataType trustList, CancellationToken ct = default)
        {
            return m_server.UpdateTrustListAsync(trustList, ct).AsTask();
        }

        /// <summary>
        /// Tells the server to start using what was pushed to it, and lets go of the session.
        /// </summary>
        /// <remarks>
        /// Applying the changes usually restarts the server, which answers the call with
        /// <c>BadServerHalted</c> and then drops the connection. That status is the expected
        /// outcome rather than a failure, and it is reported as such so that a caller does not
        /// show it; anything else is thrown. The session is let go either way.
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
        /// Empties a certificate store of everything which is not the application's own
        /// certificate or the local discovery server.
        /// </summary>
        /// <remarks>
        /// Deleting the certificate the application itself runs on would leave it unable to
        /// prove who it is, and deleting the local discovery server would cut it off from the
        /// discovery it registers with; a certificate whose private key is in the store is
        /// somebody's own for the same reason. Everything else is trust which the pull is
        /// about to replace.
        /// </remarks>
        /// <param name="storePath">The store to empty. A null or empty path does nothing.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task DeleteExistingFromStoreAsync(string storePath, CancellationToken ct = default)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return;
            }

            var certificateStoreIdentifier = new CertificateStoreIdentifier(storePath);

            using ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry);

            CertificateCollection certificates = await store.EnumerateAsync(ct).ConfigureAwait(false);

            foreach (Certificate certificateWrapper in certificates)
            {
                X509Certificate2 certificate = certificateWrapper.AsX509Certificate2();
                List<string> fields = X509Utils.ParseDistinguishedName(certificate.Subject);

                if (fields.Contains("CN=UA Local Discovery Server"))
                {
                    continue;
                }

                if (store is DirectoryCertificateStore ds && KeepsItsOwnKey(ds, certificate))
                {
                    continue;
                }

                await store.DeleteAsync(certificate.Thumbprint, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// True when a certificate in a directory store is one the application itself runs on.
        /// </summary>
        private bool KeepsItsOwnKey(DirectoryCertificateStore store, X509Certificate2 certificate)
        {
            if (store.GetPrivateKeyFilePath(certificate.Thumbprint) != null)
            {
                return true;
            }

            string path = Utils.GetAbsoluteFilePath(m_application.CertificatePublicKeyPath, true, false, false);

            if (path != null &&
                String.Equals(path, store.GetPublicKeyFilePath(certificate.Thumbprint), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);

            return path != null &&
                String.Equals(path, store.GetPrivateKeyFilePath(certificate.Thumbprint), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes one half of a trust list - its certificates and its revocation lists - into
        /// a certificate store, skipping the certificates which are already there.
        /// </summary>
        private async Task SaveToStoreAsync(
            string storePath,
            TrustListDataType trustList,
            Opc.Ua.TrustListMasks certificateMask,
            ArrayOf<ByteString> certificates,
            Opc.Ua.TrustListMasks crlMask,
            ArrayOf<ByteString> crls,
            CancellationToken ct)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return;
            }

            var certificateStoreIdentifier = new CertificateStoreIdentifier(storePath);

            using ICertificateStore store = certificateStoreIdentifier.OpenStore(m_telemetry);

            if ((trustList.SpecifiedLists & (uint)certificateMask) != 0)
            {
                foreach (ByteString certificate in certificates.ToArray())
                {
                    X509Certificate2 x509 = GdsCertificateLoader.LoadCertificate(certificate.ToArray());

                    CertificateCollection found = await store.FindByThumbprintAsync(x509.Thumbprint, ct).ConfigureAwait(false);

                    if (found.Count == 0)
                    {
                        await store.AddAsync(Certificate.From(x509), ct: ct).ConfigureAwait(false);
                    }
                }
            }

            if ((trustList.SpecifiedLists & (uint)crlMask) != 0)
            {
                foreach (ByteString crl in crls.ToArray())
                {
                    await store.AddCRLAsync(new X509CRL(crl.ToArray()), ct).ConfigureAwait(false);
                }
            }
        }
    }
}
