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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The client half of reverse connect, which the Reference Client offers under
    /// <c>Server &gt; Reverse Connect</c>: the client listens, the server dials it, and the
    /// session is opened on the connection the server offered.
    /// </summary>
    /// <remarks>
    /// The Reference Server is the sample server which can dial out, so it is the one this
    /// fixture hosts. Both ends are pointed at a port picked for the test rather than the
    /// 65301 the sample configurations ship with, so a test run does not collide with a
    /// client of the developer, or with a second test run.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class ReverseConnectListenerTests
    {
        private const int kTimeout = 120_000;

        /// <summary>
        /// Short enough that the second <c>ReverseHello</c> - the one the session is opened
        /// on, after discovery consumed the first - does not dominate the test.
        /// </summary>
        private const int kConnectInterval = 2_000;

        private SampleServerHost m_host;
        private Uri m_clientUrl;
        private ApplicationInstance m_application;
        private ApplicationConfiguration m_configuration;
        private TemporaryPki m_pki;

        [SetUp]
        public async Task StartServerAsync()
        {
            m_clientUrl = new Uri($"opc.tcp://localhost:{FreeTcpPort()}");

            SampleServerUnderTest server = SampleServerFactories.All
                .First(candidate => candidate.Sample.Name == "Reference");

            m_host = await SampleServerHost
                .StartAsync(
                    server.Sample.Name,
                    server.Sample.ServerConfig,
                    server.ConfigureServices,
                    configure: DialThisClient)
                .ConfigureAwait(false);

            m_pki = new TemporaryPki("reverse-connect-client");

            (m_application, m_configuration) = await TestClient.CreateApplicationAsync(m_pki).ConfigureAwait(false);

            // what the ClientConfiguration/ReverseConnect block of the sample client
            // configuration says, with the port of this test.
            m_configuration.ClientConfiguration.ReverseConnect = new ReverseConnectClientConfiguration {
                ClientEndpoints = [new ReverseConnectClientEndpoint { EndpointUrl = m_clientUrl.ToString() }],
                HoldTime = 15_000,
                WaitTimeout = 30_000,
            };
        }

        [TearDown]
        public async Task StopServerAsync()
        {
            if (m_application != null)
            {
                await m_application.DisposeAsync().ConfigureAwait(false);
                m_application = null;
            }

            m_pki?.Dispose();
            m_pki = null;

            if (m_host != null)
            {
                await m_host.DisposeAsync().ConfigureAwait(false);
                m_host = null;
            }
        }

        /// <summary>
        /// Points the reverse connect of the sample server at the port of this test.
        /// </summary>
        private void DialThisClient(ApplicationConfiguration configuration)
        {
            configuration.ServerConfiguration.ReverseConnect = new ReverseConnectServerConfiguration {
                Clients = [
                    new ReverseConnectClient {
                        EndpointUrl = m_clientUrl.ToString(),
                        MaxSessionCount = 0,
                        Enabled = true,
                    }
                ],
                ConnectInterval = kConnectInterval,
                ConnectTimeout = 30_000,
                RejectTimeout = 10_000,
            };
        }

        [Test]
        public void AConfigurationWithoutTheBlockIsReported()
        {
            var bare = new ApplicationConfiguration(NullTelemetry.Instance) {
                ClientConfiguration = new ClientConfiguration(),
            };

            Assert.Multiple(() => {
                Assert.That(ReverseConnectListener.IsConfigured(bare), Is.False);
                Assert.That(ReverseConnectListener.GetClientEndpointUrls(bare), Is.Empty);
                Assert.That(ReverseConnectListener.IsConfigured(null), Is.False, "A null configuration must not throw, the menu item asks before a connect.");
            });
        }

        [Test]
        public void TheConfiguredEndpointsAreReported()
        {
            Assert.Multiple(() => {
                Assert.That(ReverseConnectListener.IsConfigured(m_configuration), Is.True);
                Assert.That(
                    ReverseConnectListener.GetClientEndpointUrls(m_configuration),
                    Has.Member(m_clientUrl.ToString()),
                    "The listener reports endpoints the configuration does not name.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartingWithoutAnEndpointFails(CancellationToken ct)
        {
            var bare = new ApplicationConfiguration(NullTelemetry.Instance) {
                ClientConfiguration = new ClientConfiguration(),
            };

            await using var listener = new ReverseConnectListener(bare, NullTelemetry.Instance);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await listener.StartAsync(ct).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AWaitNamesTheServerItIsFor(CancellationToken ct)
        {
            await using var listener = new ReverseConnectListener(m_configuration, NullTelemetry.Instance);

            // the manager matches the ReverseHello against the endpoint url, so a wait
            // without one is a programming error rather than "accept anybody".
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await listener.WaitForConnectionAsync(null, null, ct).ConfigureAwait(false));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheServerConnectsOutToTheWaitingClient(CancellationToken ct)
        {
            await using var listener = new ReverseConnectListener(m_configuration, NullTelemetry.Instance);

            await listener.StartAsync(ct).ConfigureAwait(false);

            ITransportWaitingConnection connection = await listener
                .WaitForConnectionAsync(new Uri(m_host.EndpointUrl), null, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The server offered a connection for [{connection.EndpointUrl}].")
                .ConfigureAwait(false);

            var expected = new Uri(m_host.EndpointUrl);

            Assert.That(connection, Is.Not.Null, "No server dialled the client.");

            // the ReverseHello carries the endpoint url the server knows itself by, which
            // names the host rather than "localhost" - and the manager matched the wait
            // against it anyway, which is what lets the sample configurations say
            // localhost on both ends.
            Assert.Multiple(() => {
                Assert.That(connection.EndpointUrl, Is.Not.Null);
                Assert.That(connection.EndpointUrl.Port, Is.EqualTo(expected.Port), "The connection came from another port than the sample server listens on.");
                Assert.That(connection.EndpointUrl.AbsolutePath, Is.EqualTo(expected.AbsolutePath), "The connection came from another endpoint of the sample server.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ASessionIsOpenedOnTheConnectionTheServerOffered(CancellationToken ct)
        {
            await using var listener = new ReverseConnectListener(m_configuration, NullTelemetry.Instance);

            var serverUrl = new Uri(m_host.EndpointUrl);
            EndpointDescription endpoint = null;
            ISession session = null;

            try
            {
                // exactly what the Reference Client does: the first connection is spent on
                // GetEndpoints, which closes the channel, so the connect reports null and
                // the wait runs again for the connection the server opens next.
                session = await listener.ConnectAsync(
                    serverUrl,
                    null,
                    async (connection, token) => {
                        if (endpoint == null)
                        {
                            endpoint = await CoreClientUtils
                                .SelectEndpointAsync(m_configuration, connection, false, 15_000, NullTelemetry.Instance, token)
                                .ConfigureAwait(false);
                            return null;
                        }

                        var configured = new ConfiguredEndpoint(
                            null,
                            endpoint,
                            EndpointConfiguration.Create(m_configuration));

                        return await new ManagedSessionFactory(NullTelemetry.Instance)
                            .CreateAsync(
                                m_configuration,
                                connection,
                                configured,
                                false,
                                false,
                                "Reverse Connect Test",
                                60_000,
                                null,
                                default,
                                token)
                            .ConfigureAwait(false);
                    },
                    ct: ct).ConfigureAwait(false);

                Assert.That(session, Is.Not.Null, "No session was opened on the connections the server offered.");
                Assert.That(session.Connected, Is.True);

                // and the session really talks to the sample server over that socket
                DataValue value = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, "The reverse connected session could not read the server state.");
                Assert.That(value.WrappedValue.TryGetValue(out int state), Is.True, "The server state did not come back as an Int32.");
                Assert.That((ServerState)state, Is.EqualTo(ServerState.Running));
            }
            finally
            {
                if (session != null)
                {
                    await SampleSession.CloseAndDisposeAsync(session, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A port nothing listens on right now, which is the best a test can do: the
        /// reverse connect listener binds it a moment later.
        /// </summary>
        private static int FreeTcpPort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);

            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
    }
}
