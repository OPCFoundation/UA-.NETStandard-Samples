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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Sample implementation of the OPC 10000-12 §7.10.13
    /// <c>ResetToServerDefaults</c> provider: it restores the trusted-peer store to the
    /// snapshot taken when the server started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ResetToServerDefaults</c> is Optional; <c>ConfigurationNodeManager</c> only exposes
    /// the Method when a provider is configured. The node manager owns everything around it -
    /// the <c>SecurityAdmin</c> Role check, the authenticated-channel check, rejecting the
    /// call while a PushManagement transaction is open, auditing, and advertising the pending
    /// shutdown through <c>ServerState</c> / <c>SecondsTillShutdown</c> - and calls this
    /// provider once the Method response has already reached the caller and the grace period
    /// has elapsed. The server shuts down afterwards.
    /// </para>
    /// <para>
    /// The specification deliberately leaves "default settings" vendor-defined: it is not
    /// necessarily the factory state, and a machine builder may keep enough configuration for
    /// the application to keep talking inside its machine. The sample reads that as "undo the
    /// trust decisions made while this server was running": certificates trusted since
    /// start-up are removed, certificates removed since start-up are put back. The
    /// application instance certificate and the CA material are deliberately left alone -
    /// a GDS that deleted its own CA on a remote Method call would be unusable.
    /// </para>
    /// </remarks>
    public sealed class SampleServerConfigurationResetProvider : IServerConfigurationResetProvider
    {
        private readonly CertificateStoreIdentifier m_trustedStore;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;

        /// <summary>
        /// The snapshot, held as DER blobs rather than as <see cref="Certificate"/> handles:
        /// the handles enumerated out of a store belong to the <see cref="CertificateCollection"/>
        /// that produced them and are released with it.
        /// </summary>
        private IList<(string Thumbprint, string Subject, byte[] RawData)> m_defaults;

        /// <summary>
        /// Creates the provider for the trusted-peer store of the supplied configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public SampleServerConfigurationResetProvider(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<SampleServerConfigurationResetProvider>();
            m_trustedStore = configuration.SecurityConfiguration.TrustedPeerCertificates;
        }

        /// <summary>
        /// Records the current content of the trusted-peer store as the "server defaults"
        /// that <see cref="ResetToServerDefaultsAsync"/> restores. Call once, after the
        /// configuration has been loaded and before the first client connects.
        /// </summary>
        public async Task CaptureDefaultsAsync(CancellationToken ct = default)
        {
            try
            {
                using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);

                if (store == null)
                {
                    return;
                }

                using CertificateCollection certificates = await store.EnumerateAsync(ct).ConfigureAwait(false);
                var snapshot = new List<(string, string, byte[])>();

                foreach (Certificate certificate in certificates)
                {
                    snapshot.Add((certificate.Thumbprint, certificate.Subject, certificate.RawData));
                }

                m_defaults = snapshot;

                #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                m_logger.LogInformation(
                    "ResetToServerDefaults: captured {Count} trusted certificate(s) as the server defaults.",
                    snapshot.Count);
                #pragma warning restore CA1873
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    "ResetToServerDefaults: failed to capture the trusted store. Error=({ExceptionType}){Message}",
                    ex.GetType().Name, ex.Message);
            }
        }

        /// <inheritdoc/>
        public async ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
        {
            IList<(string Thumbprint, string Subject, byte[] RawData)> defaults = m_defaults;

            if (defaults == null)
            {
                #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                m_logger.LogWarning(
                    "ResetToServerDefaults: no defaults were captured, the trusted store is left unchanged.");
                #pragma warning restore CA1873
                return;
            }

            using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);

            if (store == null)
            {
                return;
            }

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string thumbprint, _, _) in defaults)
            {
                wanted.Add(thumbprint);
            }

            // drop everything that was trusted after the snapshot was taken.
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stale = new List<(string Thumbprint, string Subject)>();

            using (CertificateCollection current =
                await store.EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (Certificate certificate in current)
                {
                    if (wanted.Contains(certificate.Thumbprint))
                    {
                        present.Add(certificate.Thumbprint);
                    }
                    else
                    {
                        stale.Add((certificate.Thumbprint, certificate.Subject));
                    }
                }
            }

            foreach ((string thumbprint, string subject) in stale)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await store.DeleteAsync(thumbprint, cancellationToken).ConfigureAwait(false);

                #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                m_logger.LogInformation(
                    "ResetToServerDefaults: removed trusted certificate {Subject}.", subject);
                #pragma warning restore CA1873
            }

            // and restore everything that was removed after it.
            foreach ((string thumbprint, string subject, byte[] rawData) in defaults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (present.Contains(thumbprint))
                {
                    continue;
                }

                using Certificate certificate = Certificate.FromRawData(rawData);
                await store.AddAsync(certificate, ct: cancellationToken).ConfigureAwait(false);

                #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                m_logger.LogInformation(
                    "ResetToServerDefaults: restored trusted certificate {Subject}.", subject);
                #pragma warning restore CA1873
            }
        }
    }
}
