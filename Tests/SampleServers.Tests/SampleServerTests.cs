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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 1: every sample server has to start and serve its address space.
    /// </summary>
    /// <remarks>
    /// The servers bind fixed ports, so the fixture runs one sample at a time.
    /// </remarks>
    [TestFixture]
    [Category("ServerSmoke")]
    [NonParallelizable]
    public class SampleServerTests
    {
        /// <summary>
        /// How long a sample gets to start, answer and shut down again.
        /// </summary>
        private const int kTimeout = 60_000;

        /// <summary>
        /// Samples which are known not to work with the OPC UA 2.0 preview stack.
        /// </summary>
        /// <remarks>
        /// A sample listed here is reported as ignored instead of failed, so the suite stays
        /// usable, but the moment the sample works again the test fails and asks for the
        /// entry to be removed. Never add an entry without the reason it is here.
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, string> s_knownIssues =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                // empty on purpose: every sample server this tier covers works. The four
                // samples which did not survive the 2.0 preview stack were fixed rather
                // than parked here.
            };

        public static IEnumerable<SampleServerUnderTest> Servers => SampleServerFactories.All;

        /// <summary>
        /// Starts the sample server, connects to it and asks it the three questions every
        /// working OPC UA server has to answer: are you running, what is in your address
        /// space, and which namespaces do you serve.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Servers))]
        [CancelAfter(kTimeout)]
        public async Task ServerStartsAndServesItsAddressSpace(SampleServerUnderTest server, CancellationToken ct)
        {
            Exception failure = null;

            try
            {
                await SmokeTestAsync(server, ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (s_knownIssues.TryGetValue(server.Sample.Name, out string issue))
            {
                Assert.That(
                    failure,
                    Is.Not.Null,
                    $"{server.Sample.Name} is listed as a known issue, but the smoke test passed. " +
                    "Remove the entry from s_knownIssues and from docs/TESTING.md.");

                Assert.Ignore($"{server.Sample.Name}: known issue - {issue}. The test reported: {failure.Message}");
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>
        /// Every sample the catalog knows a server for should have a factory, otherwise it
        /// silently drops out of this tier.
        /// </summary>
        [Test]
        public void UncoveredServerSamplesAreDeclared()
        {
            // the console GDS and the LDS build their host in Main, so they are started as
            // processes by ConsoleSampleTests instead of through a factory. The WinForms
            // aggregation server is not started at all: it shares its sources with the
            // console variant and only differs in a configuration which binds the port of
            // the reference server sample. The WinForms GDS brings its own certificate
            // authority and user database.
            string[] expectedGaps = ["Aggregation", "Gds", "GdsConsole", "Lds"];

            IEnumerable<string> uncovered = SampleCatalog.Servers
                .Select(sample => sample.Name)
                .Except(SampleServerFactories.CoveredSamples);

            Assert.That(
                uncovered,
                Is.EquivalentTo(expectedGaps),
                "A sample server without a factory is not started by any test. Add it to SampleServerFactories, " +
                "or to the list of known gaps here.");
        }

        /// <summary>
        /// A known issue has to belong to a sample which is actually started here.
        /// </summary>
        [Test]
        public void KnownIssuesReferenceCoveredSamples()
        {
            Assert.That(
                s_knownIssues.Keys.Except(SampleServerFactories.CoveredSamples),
                Is.Empty,
                "A known issue for a sample which no test starts is dead weight.");
        }

        private static async Task SmokeTestAsync(SampleServerUnderTest server, CancellationToken ct)
        {
            await using SampleServerHost host = await SampleServerHost
                .StartAsync(server.Sample.Name, server.Sample.ServerConfig, server.ConfigureServices, ct)
                .ConfigureAwait(false);

            Assert.That(
                host.EndpointUrl,
                Is.EqualTo(server.Sample.ServerUrl).IgnoreCase,
                "The server did not come up on the endpoint the catalog expects.");

            await using TestClient client = await TestClient
                .ConnectAsync(host.EndpointUrl, $"{server.Sample.Name} smoke test", ct)
                .ConfigureAwait(false);

            ISession session = client.Session;

            Assert.That(session.Connected, Is.True, "The session is not connected.");

            // 1. the server reports itself as running
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

            // 2. the address space of the sample is reachable
            ArrayOf<ReferenceDescription> children = await BrowseAsync(session, ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync(
                $"{server.Sample.Name}: Objects -> {string.Join(", ", children.ConvertAll(child => child.BrowseName.Name))}")
                .ConfigureAwait(false);

            Assert.That(children, Is.Not.Empty, "The Objects folder of the server is empty.");

            // 3. the sample contributed its own namespace, which means its node manager loaded
            await session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            string[] namespaces = session.NamespaceUris.ToArray();

            await TestContext.Out.WriteLineAsync(
                $"{server.Sample.Name}: namespaces -> {string.Join(", ", namespaces)}")
                .ConfigureAwait(false);

            Assert.That(
                namespaces.Length,
                Is.GreaterThan(2),
                "The server serves no namespace of its own, so its node manager did not load.");
        }

        private static async Task<ArrayOf<ReferenceDescription>> BrowseAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            var browser = new Browser(
                session,
                new BrowserOptions {
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                });

            return await browser.BrowseAsync(nodeId, ct).ConfigureAwait(false);
        }
    }
}
