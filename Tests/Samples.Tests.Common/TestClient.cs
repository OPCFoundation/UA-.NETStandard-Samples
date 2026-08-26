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
using System.Linq;
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
        /// The endpoint the session was opened on, for test output.
        /// </summary>
        public EndpointDescription Endpoint { get; private set; }

        /// <summary>
        /// Builds the configuration of a plain client, certificate included.
        /// </summary>
        /// <remarks>
        /// Discovery needs a configuration but no session, so it is available on its own.
        /// The caller owns the returned application instance.
        /// </remarks>
        public static async Task<(ApplicationInstance Application, ApplicationConfiguration Configuration)>
            CreateApplicationAsync(TemporaryPki pki, CancellationToken ct = default)
        {
            var application = new ApplicationInstance(NullTelemetry.Instance) {
                ApplicationName = "Sample Test Client",
                ApplicationType = ApplicationType.Client,
            };

            try
            {
                ApplicationConfiguration configuration =
                    await CreateConfigurationAsync(application, pki, ct).ConfigureAwait(false);

                return (application, configuration);
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

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

                EndpointDescription[] candidates = await SelectEndpointsAsync(configuration, endpointUrl, ct)
                    .ConfigureAwait(false);

                var factory = new DefaultSessionFactory(NullTelemetry.Instance);
                var refusals = new List<string>();

                foreach (EndpointDescription description in candidates)
                {
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        description,
                        EndpointConfiguration.Create(configuration));

                    try
                    {
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

                        return new TestClient(session, application, pki) { Endpoint = description };
                    }
                    catch (ServiceResultException refused)
                    {
                        // a sample server may offer endpoints its own certificate cannot
                        // actually serve - a freshly created certificate and a policy which
                        // demands a different signature, for instance - so try the next one
                        // rather than deciding the sample is broken
                        refusals.Add($"{Describe(description)}: {refused.Message}");
                    }
                }

                throw new InvalidOperationException(
                    $"None of the {candidates.Length} endpoints of {endpointUrl} accepted a session." +
                    $"{Environment.NewLine}{string.Join(Environment.NewLine, refusals)}");
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
        /// Asks the server for its endpoints, least secure first.
        /// </summary>
        /// <remarks>
        /// Tier 1 is about the address space, not about security, and an unsecured endpoint
        /// keeps a sample server which runs out of process from having to trust the throw
        /// away certificate of this client. The endpoints are read directly rather than
        /// through CoreClientUtils.SelectEndpoint, because that helper ranks by security
        /// level and would pick a secured endpoint even when None is on offer.
        /// </remarks>
        private static async Task<EndpointDescription[]> SelectEndpointsAsync(
            ApplicationConfiguration configuration,
            string endpointUrl,
            CancellationToken ct)
        {
            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            using DiscoveryClient discovery = await DiscoveryClient
                .CreateAsync(configuration, new Uri(endpointUrl), endpointConfiguration, DiagnosticsMasks.None, ct)
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await discovery
                .GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);

            EndpointDescription[] usable = endpoints
                .ToArray()
                .Where(endpoint => endpoint.EndpointUrl != null
                    && endpoint.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.OrdinalIgnoreCase))
                // an endpoint counts as unsecured only when mode and policy agree: some
                // sample configurations declare a security policy with an empty uri, which
                // produces endpoints whose mode says None while their policy does not, and
                // opening a channel on one of those is refused by the server
                .OrderBy(endpoint => endpoint.SecurityMode == MessageSecurityMode.None
                    && endpoint.SecurityPolicyUri == SecurityPolicies.None ? 0 : 1)
                // an RsaPss policy demands a certificate signed the same way, which a sample
                // server which just created its own certificate may well not have, so leave
                // those for last
                .ThenBy(endpoint => endpoint.SecurityPolicyUri?.Contains("RsaPss", StringComparison.Ordinal) == true ? 1 : 0)
                .ThenBy(endpoint => endpoint.SecurityLevel)
                .ToArray();

            if (usable.Length == 0)
            {
                throw new InvalidOperationException($"{endpointUrl} offers no opc.tcp endpoint.");
            }

            return usable;
        }

        private static string Describe(EndpointDescription endpoint)
        {
            return $"{endpoint.SecurityMode} {endpoint.SecurityPolicyUri}";
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
