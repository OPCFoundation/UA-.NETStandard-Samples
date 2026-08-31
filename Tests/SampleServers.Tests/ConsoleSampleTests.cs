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
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 1, the console half: samples which build their host in Main and block there.
    /// </summary>
    /// <remarks>
    /// These are started as the processes they really are, which also covers the entry point,
    /// the command line and the configuration lookup that the in process tests bypass.
    /// </remarks>
    [TestFixture]
    [Category("ServerSmoke")]
    [NonParallelizable]
    public class ConsoleSampleTests
    {
        private const int kTimeout = 120_000;

        private static readonly TimeSpan s_startupTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// The local discovery server answers FindServers, and knows about itself.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task LocalDiscoveryServerAnswersFindServers(CancellationToken ct)
        {
            await using ConsoleSampleProcess lds = await ConsoleSampleProcess.StartAsync(
                "Samples/LDS/ConsoleServer/ConsoleLds.csproj",
                "ConsoleLds",
                "LDS listening",
                s_startupTimeout,
                ct: ct).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await FindServersAsync(
                "opc.tcp://localhost:4840",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync(
                $"Lds: FindServers -> {string.Join(", ", servers.ConvertAll(server => server.ApplicationUri))}")
                .ConfigureAwait(false);

            Assert.That(servers.ToArray(), Is.Not.Empty, "The discovery server returned no applications at all.");

            Assert.That(
                servers.ConvertAll(server => server.ApplicationUri).ToArray(),
                Has.Some.Contains("LocalDiscoveryServer"),
                "The discovery server does not report itself.");
        }

        /// <summary>
        /// The global discovery server starts, serves its address space and shuts down on
        /// the command it documents.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GlobalDiscoveryServerStartsAndServes(CancellationToken ct)
        {
            SampleDefinition sample = SampleCatalog.All.Single(entry => entry.Name == "GdsConsole");

            // create the certificate of the sample before it starts. On a machine which has
            // never run it - a build agent - the server would otherwise create it while
            // starting up, and the first run would differ from every later one.
            string certificate = await SampleCertificates
                .EnsureApplicationCertificateAsync(sample.ServerConfig, ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"GdsConsole: certificate {certificate}").ConfigureAwait(false);

            await using ConsoleSampleProcess gds = await ConsoleSampleProcess.StartAsync(
                sample.ServerProject,
                "NetCoreGlobalDiscoveryServer",
                "Server started",
                s_startupTimeout,
                quitCommand: "quit",
                ct: ct).ConfigureAwait(false);

            await using TestClient client = await TestClient
                .ConnectAsync(sample.ServerUrl, "gds smoke test", ct)
                .ConfigureAwait(false);

            await AssertServerIsRunningAsync(client.Session, ct).ConfigureAwait(false);

            await client.Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            string[] namespaces = client.Session.NamespaceUris.ToArray();

            await TestContext.Out.WriteLineAsync($"GdsConsole: namespaces -> {string.Join(", ", namespaces)}")
                .ConfigureAwait(false);

            Assert.That(
                namespaces,
                Has.Some.Contains("GDS"),
                "The global discovery server does not serve the GDS namespace.");
        }

        private static async Task AssertServerIsRunningAsync(ISession session, CancellationToken ct)
        {
            Assert.That(session.Connected, Is.True, "The session is not connected.");

            DataValue state = await session
                .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(state.StatusCode),
                Is.True,
                $"Reading the server state failed: {state.StatusCode}");

            state.WrappedValue.TryGetValue(out int stateCode);
            var serverState = (ServerState)stateCode;

            Assert.That(serverState, Is.EqualTo(ServerState.Running), "The server does not report itself as running.");
        }

        /// <summary>
        /// Asks a discovery endpoint which servers it knows about.
        /// </summary>
        /// <remarks>
        /// Discovery needs no session and no certificate, so this deliberately does not go
        /// through <see cref="TestClient"/>.
        /// </remarks>
        private static async Task<ArrayOf<ApplicationDescription>> FindServersAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            using var pki = new TemporaryPki("discovery");

            (Configuration.ApplicationInstance application, ApplicationConfiguration configuration) =
                await TestClient.CreateApplicationAsync(pki, ct).ConfigureAwait(false);

            await using (application.ConfigureAwait(false))
            {
                using DiscoveryClient discovery = await DiscoveryClient
                    .CreateAsync(configuration, new Uri(discoveryUrl), DiagnosticsMasks.None, ct)
                    .ConfigureAwait(false);

                return await discovery.FindServersAsync(default, ct).ConfigureAwait(false);
            }
        }
    }
}
