/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 1: the certificate panel of the GDS client reads
    /// <c>EndpointDescription.ServerCertificate</c>, and an Endpoint that carries no
    /// certificate is not the same thing as a null one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server only fills <c>ServerCertificate</c> for an Endpoint that requires encryption
    /// (<c>ServerBase.SetServerCertificateInEndpointDescription</c>), so a
    /// <c>SecurityPolicy=None</c> Endpoint leaves it at its default. That default is an
    /// <em>empty</em> <see cref="ByteString"/>, not a null one - so a guard written as
    /// <c>!ServerCertificate.IsNull</c> lets the empty array through to
    /// <see cref="X509CertificateLoader"/>, which reports it as
    /// <c>CryptographicException: ASN1 corrupted data</c>. That is the shape of the bug this
    /// fixture pins down; the panel now tests <c>Length</c> instead.
    /// </para>
    /// <para>
    /// These assertions are about the platform and the stack rather than about sample code,
    /// which is the point: they fail if either stops behaving the way the guard assumes.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    public class GdsEndpointCertificateTests
    {
        /// <summary>
        /// The default an Endpoint without a certificate carries: empty, and <b>not</b> null.
        /// </summary>
        [Test]
        public void AnEndpointWithoutACertificateCarriesAnEmptyNotNullByteString()
        {
            var description = new EndpointDescription();

            Assert.Multiple(() => {
                Assert.That(description.ServerCertificate.Length, Is.Zero,
                    "A fresh EndpointDescription carries no certificate.");
                Assert.That(description.ServerCertificate.IsNull, Is.False,
                    "IsNull is false for it, which is why a null check is not enough to " +
                    "decide whether there is a certificate to load.");
            });
        }

        /// <summary>
        /// Loading that empty array is what produced the reported "ASN1 corrupted data".
        /// </summary>
        [Test]
        public void LoadingAnEmptyCertificateBlobThrows()
        {
            CryptographicException exception = Assert.Throws<CryptographicException>(
                () => X509CertificateLoader.LoadCertificate(Array.Empty<byte>()));

            Assert.That(exception, Is.Not.Null);
        }

        /// <summary>
        /// The loader is happy with a real certificate, so <c>Length &gt; 0</c> is a
        /// sufficient guard rather than merely a necessary one.
        /// </summary>
        [Test]
        public void LoadingARealCertificateSucceeds()
        {
            using var key = RSA.Create(2048);

            var request = new CertificateRequest(
                "CN=Endpoint",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(30));

            using X509Certificate2 loaded = X509CertificateLoader.LoadCertificate(certificate.RawData);

            Assert.That(loaded.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        }
    }
}
