/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// The user identities a sample client builds from its own configuration.
    /// </summary>
    public static class SampleIdentities
    {
        /// <summary>
        /// An X.509 user identity made from the application instance certificate of the
        /// client itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC UA Part 18 4.4.3 matches the Thumbprint and X509Subject identity criteria
        /// against the certificate of the <b>user</b>, which only reaches the server in an
        /// X509IdentityToken. A Session opened with an anonymous or a user name token has no
        /// user certificate at all, however good the certificate of its secure channel is.
        /// So a client which is to earn a Role for the machine it runs on - a maintenance
        /// workstation, say - signs in with the certificate that machine already holds.
        /// </para>
        /// <para>
        /// The server still has to trust the certificate as a user certificate: that is a
        /// trust list of its own, separate from the one it trusts application
        /// certificates in.
        /// </para>
        /// <para>
        /// The RSA certificate is used because a user token policy which names no security
        /// policy of its own is signed with the one of the endpoint, and every sample server
        /// offers an RSA endpoint.
        /// </para>
        /// </remarks>
        /// <param name="configuration">The configuration of the client, which holds its certificate.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">The client has no application certificate
        /// with a private key.</exception>
        public static async Task<IUserIdentity> FromApplicationCertificateAsync(
            ApplicationConfiguration configuration,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            SecurityConfiguration security = configuration.SecurityConfiguration;

            CertificateIdentifier certificateId = security.ApplicationCertificates.ToArray()?
                .FirstOrDefault(id =>
                    id.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                    id.CertificateType.IsNull);

            if (certificateId == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The client has no RSA application certificate to sign in with.");
            }

            // the identity loads the private key once, synchronously, while it is built; off
            // the calling thread, because that is the UI thread of a window
            return await Task.Run(
                () => UserIdentity.CreateAsync(
                    certificateId,
                    security.CertificatePasswordProvider ?? new CertificatePasswordProvider(),
                    configuration.CertificateManager.CertificateProvider,
                    ct),
                ct).ConfigureAwait(false);
        }
    }
}
