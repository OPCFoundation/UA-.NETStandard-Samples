/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// A plain OPC UA client used to prove that a sample server actually serves.
    /// </summary>
    /// <remarks>
    /// This is deliberately not one of the sample clients: tier 1 asks whether the server
    /// works, so the client side has to be boring and known good. The sample clients are
    /// covered by tier 2.
    /// </remarks>
    public sealed class TestClient : IAsyncDisposable
    {
        private readonly TemporaryPki m_pki;
        private readonly ApplicationInstance m_application;

        private TestClient(ISession session, ApplicationInstance application, TemporaryPki pki)
        {
            Session = session;
            m_application = application;
            m_pki = pki;
        }

        /// <summary>
        /// The open session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Connects to the endpoint and opens a session with an anonymous user.
        /// </summary>
        /// <param name="endpointUrl">The endpoint to connect to.</param>
        /// <param name="sessionName">The name of the session, for readable server logs.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<TestClient> ConnectAsync(
            string endpointUrl,
            string sessionName,
            CancellationToken ct = default)
        {
            var pki = new TemporaryPki($"client-{sessionName}");

            var application = new ApplicationInstance(NullTelemetry.Instance) {
                ApplicationName = "Sample Test Client",
                ApplicationType = ApplicationType.Client,
            };

            try
            {
                ApplicationConfiguration configuration =
                    await CreateConfigurationAsync(application, pki, ct).ConfigureAwait(false);

                ConfiguredEndpoint endpoint = await SelectEndpointAsync(configuration, endpointUrl, ct)
                    .ConfigureAwait(false);

                var factory = new DefaultSessionFactory(NullTelemetry.Instance);

                ISession session = await factory.CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    false,
                    sessionName,
                    30_000,
                    new UserIdentity(),
                    default,
                    ct).ConfigureAwait(false);

                return new TestClient(session, application, pki);
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                pki.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                try
                {
                    await Session.CloseAsync(default).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // closing a session on a server which is already gone must not fail a test
                }

                await Session.DisposeAsync().ConfigureAwait(false);
            }

            if (m_application != null)
            {
                await m_application.DisposeAsync().ConfigureAwait(false);
            }
            m_pki.Dispose();
        }

        /// <summary>
        /// Prefers an unsecured endpoint, because tier 1 is about the address space and
        /// not about security, but falls back to a secured one for samples which do not
        /// offer SecurityPolicy None.
        /// </summary>
        private static async Task<ConfiguredEndpoint> SelectEndpointAsync(
            ApplicationConfiguration configuration,
            string endpointUrl,
            CancellationToken ct)
        {
            EndpointDescription description;

            try
            {
                description = await CoreClientUtils
                    .SelectEndpointAsync(configuration, endpointUrl, false, NullTelemetry.Instance, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                description = await CoreClientUtils
                    .SelectEndpointAsync(configuration, endpointUrl, true, NullTelemetry.Instance, ct)
                    .ConfigureAwait(false);
            }

            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            return new ConfiguredEndpoint(null, description, endpointConfiguration);
        }

        private static async Task<ApplicationConfiguration> CreateConfigurationAsync(
            ApplicationInstance application,
            TemporaryPki pki,
            CancellationToken ct)
        {
            ApplicationConfiguration configuration = await application
                .Build(
                    "urn:localhost:OPCFoundation:SampleTestClient",
                    "http://opcfoundation.org/UA/SampleTestClient")
                .AsClient()
                // the newer AddSecurityConfiguration(ArrayOf<CertificateIdentifier>, ...) overload
                // leaves SecurityConfiguration.ApplicationCertificate unset in 2.0.158-preview,
                // and the configuration then fails validation with "ApplicationCertificate must
                // be specified", so the subject name overload is used here on purpose
#pragma warning disable CS0618
                .AddSecurityConfiguration(
                    "CN=Sample Test Client, C=US, S=Arizona, O=OPC Foundation, DC=localhost",
                    pki.RootPath)
#pragma warning restore CS0618
                .SetAutoAcceptUntrustedCertificates(true)
                .SetAddAppCertToTrustedStore(true)
                .CreateAsync(ct)
                .ConfigureAwait(false);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(false);

            if (!certificateOk)
            {
                throw new InvalidOperationException("The test client certificate could not be created.");
            }

            return configuration;
        }
    }
}
