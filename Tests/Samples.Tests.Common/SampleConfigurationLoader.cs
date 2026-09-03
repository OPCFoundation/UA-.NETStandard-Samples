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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// A telemetry context which drops everything, so that test output stays readable.
    /// </summary>
    public sealed class NullTelemetry : TelemetryContextBase
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static NullTelemetry Instance { get; } = new NullTelemetry();

        private NullTelemetry()
            : base(NullLoggerFactory.Instance)
        {
        }
    }

    /// <summary>
    /// Loads the application configuration of a sample the way the sample itself would,
    /// but from an explicit path and without touching the certificate stores of the machine.
    /// </summary>
    /// <remarks>
    /// The samples name their configuration file relative to the executable, which under a
    /// test host is the test runner, so tests always load by an explicit repository path
    /// instead.
    /// </remarks>
    public static class SampleConfigurationLoader
    {
        /// <summary>
        /// Loads and validates the configuration file, with the certificate stores
        /// redirected into the temporary PKI when one is supplied.
        /// </summary>
        /// <param name="relativePath">The configuration file, relative to the repository root.</param>
        /// <param name="pki">The temporary PKI to use, or null to keep the configured stores.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<ApplicationConfiguration> LoadAsync(
            string relativePath,
            TemporaryPki pki = null,
            CancellationToken ct = default)
        {
            var file = new FileInfo(RepositoryLayout.PathOf(relativePath));

            // read first without validation: the file itself declares which application
            // type it is, and the certificate stores have to be redirected before the
            // configuration is validated.
            ApplicationConfiguration configuration = ApplicationConfiguration.LoadWithNoValidation(
                file,
                typeof(ApplicationConfiguration),
                NullTelemetry.Instance);

            pki?.Redirect(configuration);

            await configuration.ValidateAsync(configuration.ApplicationType, ct).ConfigureAwait(false);

            return configuration;
        }

        /// <summary>
        /// Returns the base addresses configured for the server, or an empty list for
        /// a configuration which does not describe a server.
        /// </summary>
        public static IReadOnlyList<string> GetBaseAddresses(ApplicationConfiguration configuration)
        {
            if (configuration?.ServerConfiguration?.BaseAddresses == null)
            {
                return Array.Empty<string>();
            }

            return configuration.ServerConfiguration.BaseAddresses.ToList();
        }
    }

    /// <summary>
    /// A throw away certificate store layout for one test or one test run.
    /// </summary>
    /// <remarks>
    /// Sample configurations keep their certificates under %CommonApplicationData%. Tests
    /// redirect every store into a temporary directory so that a test run neither depends
    /// on, nor pollutes, the state of the machine it runs on.
    /// </remarks>
    public sealed class TemporaryPki : IDisposable
    {
        private bool m_disposed;

        /// <summary>
        /// Creates a temporary PKI directory.
        /// </summary>
        /// <param name="name">A name which makes the directory recognizable while debugging.</param>
        public TemporaryPki(string name)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "opc.ua.samples.tests",
                $"{SanitizeName(name)}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(RootPath);
        }

        /// <summary>
        /// The root directory of the temporary PKI.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Points every certificate store of the configuration into the temporary PKI and
        /// accepts untrusted peers, so that a sample client and a sample server can talk to
        /// each other without a shared trust list.
        /// </summary>
        public void Redirect(ApplicationConfiguration configuration)
        {
            if (configuration?.SecurityConfiguration == null)
            {
                return;
            }

            SecurityConfiguration security = configuration.SecurityConfiguration;

            if (security.ApplicationCertificates != null)
            {
                foreach (CertificateIdentifier certificate in security.ApplicationCertificates)
                {
                    certificate.StoreType = CertificateStoreType.Directory;
                    certificate.StorePath = Path.Combine(RootPath, "own");
                }
            }

            Redirect(security.TrustedIssuerCertificates, "issuer");
            Redirect(security.TrustedPeerCertificates, "trusted");
            Redirect(security.TrustedUserCertificates, "userTrusted");
            Redirect(security.UserIssuerCertificates, "userIssuer");
            Redirect(security.TrustedHttpsCertificates, "httpsTrusted");
            Redirect(security.HttpsIssuerCertificates, "httpsIssuer");
            Redirect(security.RejectedCertificateStore, "rejected");

            security.AutoAcceptUntrustedCertificates = true;
            security.AddAppCertToTrustedStore = true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;

            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, true);
                }
            }
            catch (IOException)
            {
                // a locked certificate file must not fail a test
            }
            catch (UnauthorizedAccessException)
            {
                // as above
            }
        }

        private void Redirect(CertificateStoreIdentifier store, string folder)
        {
            if (store == null)
            {
                return;
            }

            store.StoreType = CertificateStoreType.Directory;
            store.StorePath = Path.Combine(RootPath, folder);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "sample";
            }

            return string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-'));
        }
    }
}
