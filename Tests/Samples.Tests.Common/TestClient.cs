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
        /// <summary>
        /// How long a teardown waits for the session to close before it disposes it instead.
        /// </summary>
        private static readonly TimeSpan kCloseTimeout = TimeSpan.FromSeconds(5);

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
        /// Connects to the endpoint and opens a session for the given user.
        /// </summary>
        /// <remarks>
        /// The session is opened on the first endpoint which accepts one, so a server which
        /// offers an endpoint its own certificate cannot serve does not fail the test. A
        /// test which needs to see why a server refused a user has to ask for one endpoint
        /// only, which is what ConnectWithIdentityAsync is for.
        /// </remarks>
        /// <param name="endpointUrl">The endpoint to connect to.</param>
        /// <param name="sessionName">The name of the session, for readable server logs.</param>
        /// <param name="identity">The user to open the session for. Null for anonymous.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<TestClient> ConnectAsync(
            string endpointUrl,
            string sessionName,
            IUserIdentity identity,
            CancellationToken ct = default)
        {
            return await ConnectCoreAsync(endpointUrl, sessionName, identity, EndpointChoice.Any, ct)
                .ConfigureAwait(false);
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
            return await ConnectCoreAsync(endpointUrl, sessionName, null, EndpointChoice.Any, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a session on the unsecured endpoint and lets a refusal through.
        /// </summary>
        /// <remarks>
        /// A test which asserts that a server rejects a user needs the status code the
        /// server answered with. The ordinary connect swallows that: it treats a refusal as
        /// a reason to try the next endpoint and ends up reporting that none of them
        /// worked, which says nothing about why.
        /// </remarks>
        public static async Task<TestClient> ConnectWithIdentityAsync(
            string endpointUrl,
            string sessionName,
            IUserIdentity identity,
            CancellationToken ct = default)
        {
            return await ConnectCoreAsync(endpointUrl, sessionName, identity, EndpointChoice.UnsecuredOnly, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a session on an encrypted endpoint and lets a refusal through.
        /// </summary>
        /// <remarks>
        /// OPC UA Part 18 requires the Methods which change the role configuration of a
        /// server to be called over an encrypted channel, so a test which exercises them has
        /// to say which endpoint it wants rather than take the unsecured one the ordinary
        /// connect prefers.
        /// </remarks>
        public static async Task<TestClient> ConnectEncryptedAsync(
            string endpointUrl,
            string sessionName,
            IUserIdentity identity,
            CancellationToken ct = default)
        {
            return await ConnectCoreAsync(endpointUrl, sessionName, identity, EndpointChoice.EncryptedOnly, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the kind of session the sample clients run on: a managed session on the
        /// V2 subscription engine.
        /// </summary>
        /// <remarks>
        /// The other connects here use the plain session factory, whose sessions have no
        /// subscription manager - fine for asking a server whether it serves, useless for
        /// a client model, which creates its subscriptions on the V2 engine and would be
        /// refused with BadNotSupported. This one goes through the same factory the shared
        /// connect control of the windows uses.
        /// </remarks>
        /// <param name="endpointUrl">The endpoint to connect to.</param>
        /// <param name="sessionName">The name of the session, for readable server logs.</param>
        /// <param name="identity">The user to open the session for. Null for anonymous.</param>
        /// <param name="useSecurity">Whether to prefer a secured endpoint.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<TestClient> ConnectManagedAsync(
            string endpointUrl,
            string sessionName,
            IUserIdentity identity = null,
            bool useSecurity = false,
            CancellationToken ct = default)
        {
            TemporaryPki pki = null;
            ApplicationInstance application = null;

            try
            {
                pki = new TemporaryPki($"client-{sessionName}");

                application = new ApplicationInstance(NullTelemetry.Instance) {
                    ApplicationName = "Sample Test Client",
                    ApplicationType = ApplicationType.Client,
                };

                ApplicationConfiguration configuration =
                    await CreateConfigurationAsync(application, pki, ct).ConfigureAwait(false);

                ISession session = await Opc.Ua.Samples.Client.SampleSessionFactory
                    .ConnectAsync(configuration, endpointUrl, useSecurity, identity, sessionName, NullTelemetry.Instance, ct: ct)
                    .ConfigureAwait(false);

                var client = new TestClient(session, application, pki) {
                    Endpoint = session.ConfiguredEndpoint?.Description,
                };
                application = null;
                pki = null;
                return client;
            }
            finally
            {
                if (application != null)
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                }
                pki?.Dispose();
            }
        }

        /// <summary>
        /// Which endpoint of the server a connect is willing to use.
        /// </summary>
        private enum EndpointChoice
        {
            /// <summary>Any endpoint which accepts a session, least secure first.</summary>
            Any,

            /// <summary>Only the unsecured endpoint, and report what it answered.</summary>
            UnsecuredOnly,

            /// <summary>Only an encrypted endpoint, and report what it answered.</summary>
            EncryptedOnly,
        }

        private static async Task<TestClient> ConnectCoreAsync(
            string endpointUrl,
            string sessionName,
            IUserIdentity identity,
            EndpointChoice choice,
            CancellationToken ct)
        {
            TemporaryPki pki = null;
            ApplicationInstance application = null;

            try
            {
                pki = new TemporaryPki($"client-{sessionName}");

                application = new ApplicationInstance(NullTelemetry.Instance) {
                    ApplicationName = "Sample Test Client",
                    ApplicationType = ApplicationType.Client,
                };

                ApplicationConfiguration configuration =
                    await CreateConfigurationAsync(application, pki, ct).ConfigureAwait(false);

                EndpointDescription[] candidates = await SelectEndpointsAsync(configuration, endpointUrl, ct)
                    .ConfigureAwait(false);

                if (choice == EndpointChoice.UnsecuredOnly)
                {
                    candidates = candidates
                        .Where(endpoint => endpoint.SecurityMode == MessageSecurityMode.None
                            && endpoint.SecurityPolicyUri == SecurityPolicies.None)
                        .Take(1)
                        .ToArray();

                    if (candidates.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{endpointUrl} offers no unsecured endpoint to open a session on.");
                    }
                }
                else if (choice == EndpointChoice.EncryptedOnly)
                {
                    candidates = candidates
                        .Where(endpoint => endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt
                            && !string.IsNullOrEmpty(endpoint.SecurityPolicyUri)
                            && endpoint.SecurityPolicyUri != SecurityPolicies.None)
                        .Take(1)
                        .ToArray();

                    if (candidates.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{endpointUrl} offers no encrypted endpoint to open a session on.");
                    }
                }

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
                            identity ?? new UserIdentity(),
                            default,
                            ct).ConfigureAwait(false);

                        // the client owns the application and the pki from here on, so the
                        // locals are cleared to keep the finally below from disposing them
                        var client = new TestClient(session, application, pki) { Endpoint = description };
                        application = null;
                        pki = null;
                        return client;
                    }
                    catch (ServiceResultException) when (choice != EndpointChoice.Any)
                    {
                        // the caller asked for one endpoint on purpose, because what the
                        // server answered is the thing it wants to look at
                        throw;
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
            finally
            {
                // both locals are cleared once the client takes ownership, so this only
                // runs for the paths which never produced a client
                if (application != null)
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                }
                pki?.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                // the close is bounded: a session which is inside a reconnect attempt against
                // a server that is gone only leaves that attempt when it runs out, and against
                // an endpoint which accepts but never answers that is the OperationTimeout of
                // the sample configuration - ten minutes. Disposing is what actually cancels
                // the attempt, so a teardown must not wait for the close indefinitely.
                using var bounded = new CancellationTokenSource(kCloseTimeout);

                try
                {
                    await Session.CloseAsync(bounded.Token).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // closing a session on a server which is already gone must not fail a test
                }
                catch (OperationCanceledException)
                {
                    // the server did not answer in time, the dispose below tears it down
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
// the diagnostic for the obsolete overload below lands on the start of the chain,
// so the suppression has to cover the whole statement
#pragma warning disable CS0618
            ApplicationConfiguration configuration = await application
                .Build(
                    "urn:localhost:OPCFoundation:SampleTestClient",
                    "http://opcfoundation.org/UA/SampleTestClient")
                .AsClient()
                // the newer AddSecurityConfiguration(ArrayOf<CertificateIdentifier>, ...) overload
                // leaves SecurityConfiguration.ApplicationCertificate unset in 2.0.158-preview,
                // and the configuration then fails validation with "ApplicationCertificate must
                // be specified", so the subject name overload is used here on purpose
                .AddSecurityConfiguration(
                    "CN=Sample Test Client, C=US, S=Arizona, O=OPC Foundation, DC=localhost",
                    pki.RootPath)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetAddAppCertToTrustedStore(true)
                .CreateAsync(ct)
                .ConfigureAwait(false);
#pragma warning restore CS0618

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
