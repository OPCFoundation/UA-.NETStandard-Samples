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
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the aggregation sample does: it is a server to its clients and a client to
    /// another server, and passes everything through.
    /// </summary>
    /// <remarks>
    /// This is the only sample which needs a second server to do anything at all, so the
    /// fixture starts one and points the aggregation server at it. The shipped
    /// configuration names a downstream server which is not running, which is why it is
    /// replaced here rather than used.
    ///
    /// What has to survive a migration is the pass through: a node of the downstream
    /// server appears under a root of the aggregating one, with a node id of its own, and
    /// reading or writing it reaches the real thing. The namespace mapping between the two
    /// servers is what makes that work and is the easiest part to break.
    ///
    /// The proxy root exists from the start, but everything behind it needs the session
    /// the aggregating server opens downstream, and it schedules the first connection
    /// attempt a few seconds after startup. Every test which goes through the proxy
    /// therefore waits for the pass through to come up instead of asserting on the first
    /// answer.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class AggregationNodeManagerTests
    {
        private const int kTimeout = 120_000;

        private const string DownstreamSample = "Reference";
        private const string AggregatingSample = "AggregationConsole";

        private SampleServerHost m_downstream;
        private SampleServerHost m_aggregating;
        private TestClient m_downstreamClient;
        private TestClient m_aggregatingClient;
        private NodeId m_proxyRoot;
        private string m_setupFailure;
        private IReadOnlyList<string> m_passThroughNames;

        [OneTimeSetUp]
        public async Task StartBothServersAsync()
        {
            m_downstream = await StartAsync(DownstreamSample, null).ConfigureAwait(false);

            m_aggregating = await StartAsync(
                AggregatingSample,
                configuration => PointAtDownstream(
                    configuration,
                    m_downstream.EndpointUrl,
                    m_downstream.Configuration.ApplicationUri))
                .ConfigureAwait(false);

            m_downstreamClient = await TestClient
                .ConnectAsync(m_downstream.EndpointUrl, "aggregation downstream")
                .ConfigureAwait(false);

            m_aggregatingClient = await TestClient
                .ConnectAsync(m_aggregating.EndpointUrl, "aggregation upstream")
                .ConfigureAwait(false);

            await m_downstreamClient.Session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            await m_aggregatingClient.Session.FetchNamespaceTablesAsync().ConfigureAwait(false);

            // The node manager waits a few seconds before it publishes its proxy root. If it
            // never appears the fixture does not throw from its setup: that reports every
            // test in it as an error with the same stack and says nothing useful. The tests
            // check for the root themselves and say what is missing.
            try
            {
                m_proxyRoot = await Poll.UntilNoThrowAsync(
                    async token => {
                        IReadOnlyList<ReferenceDescription> children = await SessionOps
                            .BrowseAsync(Aggregating, ObjectIds.ObjectsFolder, token)
                            .ConfigureAwait(false);

                        ReferenceDescription root = children.FirstOrDefault(child =>
                            child.NodeClass == NodeClass.Object
                            && child.BrowseName.NamespaceIndex > 1);

                        return root == null
                            ? NodeId.Null
                            : ExpandedNodeId.ToNodeId(root.NodeId, Aggregating.NamespaceUris);
                    },
                    nodeId => !nodeId.IsNull,
                    "the aggregation server to publish its proxy root",
                    timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(false);

                DataValue rootName = await SessionOps
                    .ReadAttributeAsync(Aggregating, m_proxyRoot, Attributes.BrowseName, default)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"The proxy root is {m_proxyRoot}, called {rootName.WrappedValue}")
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                m_setupFailure = failure.Message;

                await TestContext.Out
                    .WriteLineAsync($"The proxy root never appeared: {failure.Message}")
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fails the test unless the aggregating server got as far as publishing its root.
        /// </summary>
        private void RequireProxyRoot()
        {
            Assert.That(
                m_setupFailure,
                Is.Null,
                $"The aggregation server never published its proxy root: {m_setupFailure}");
        }

        /// <summary>
        /// Waits until the aggregating server has connected downstream and serves the
        /// address space of the other server through its proxy root.
        /// </summary>
        /// <remarks>
        /// The aggregating server refuses a downstream session until its metadata update
        /// has connected and loaded the remote type tree, and it makes its first attempt
        /// five seconds after startup. The wait is done once and the result kept: the
        /// tests run one after the other and the connection stays up once it is made.
        /// </remarks>
        private async Task<IReadOnlyList<string>> RequirePassThroughAsync(CancellationToken ct)
        {
            RequireProxyRoot();

            if (m_passThroughNames != null)
            {
                return m_passThroughNames;
            }

            m_passThroughNames = await Poll.UntilNoThrowAsync(
                async token => await SessionOps
                    .BrowseNamesAsync(Aggregating, m_proxyRoot, token)
                    .ConfigureAwait(false),
                names => names.Count > 0,
                "the aggregation server to connect downstream and serve the proxied address space",
                timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Through the proxy: {string.Join(", ", m_passThroughNames)}")
                .ConfigureAwait(false);

            return m_passThroughNames;
        }

        /// <summary>
        /// Follows a chain of browse names downward from a starting node.
        /// </summary>
        /// <remarks>
        /// Matching on the name alone is deliberate: through the proxy the same node
        /// carries the namespace index the aggregating server mapped it to, which is not
        /// the index it has on the downstream server.
        /// </remarks>
        private static async Task<NodeId> WalkAsync(
            ISession session,
            NodeId start,
            CancellationToken ct,
            params string[] names)
        {
            NodeId current = start;

            foreach (string name in names)
            {
                IReadOnlyList<ReferenceDescription> children = await SessionOps
                    .BrowseAsync(session, current, ct)
                    .ConfigureAwait(false);

                ReferenceDescription child = children.FirstOrDefault(candidate =>
                    candidate.BrowseName.Name == name);

                if (child == null)
                {
                    return NodeId.Null;
                }

                current = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
            }

            return current;
        }

        [OneTimeTearDown]
        public async Task StopBothServersAsync()
        {
            foreach (TestClient client in new[] { m_aggregatingClient, m_downstreamClient })
            {
                if (client != null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
            }

            foreach (SampleServerHost host in new[] { m_aggregating, m_downstream })
            {
                if (host != null)
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
            }

            m_aggregatingClient = null;
            m_downstreamClient = null;
            m_aggregating = null;
            m_downstream = null;
        }

        private ISession Aggregating => m_aggregatingClient.Session;

        private ISession Downstream => m_downstreamClient.Session;

        /// <summary>
        /// The aggregating server publishes a root for the server it is configured against.
        /// </summary>
        /// <remarks>
        /// The root is built before anything is connected and is named after the downstream
        /// server, so this is the half of the sample which works without a session to the
        /// other side.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ProxyRootIsPublishedForTheConfiguredServer(CancellationToken ct)
        {
            RequireProxyRoot();

            DataValue browseName = await SessionOps
                .ReadAttributeAsync(Aggregating, m_proxyRoot, Attributes.BrowseName, ct)
                .ConfigureAwait(false);

            DataValue nodeClass = await SessionOps
                .ReadAttributeAsync(Aggregating, m_proxyRoot, Attributes.NodeClass, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The proxy root {m_proxyRoot} is called {browseName.WrappedValue}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    StatusCode.IsGood(browseName.StatusCode),
                    Is.True,
                    $"Reading the proxy root failed: {browseName.StatusCode}");

                Assert.That(
                    nodeClass.WrappedValue.TryGetValue(out int proxyNodeClass) ? proxyNodeClass : 0,
                    Is.EqualTo((int)NodeClass.Object),
                    "The proxy root is an object.");
            });

            // it lives in a namespace of the aggregating server, not in one of the
            // downstream server, which is what the namespace mapping is for
            string namespaceUri = Aggregating.NamespaceUris.GetString(m_proxyRoot.NamespaceIndex);

            await TestContext.Out
                .WriteLineAsync($"The proxy root lives in {namespaceUri}")
                .ConfigureAwait(false);

            Assert.That(
                namespaceUri,
                Is.Not.Null.And.Not.Empty,
                "The proxy root has to live in a namespace the aggregating server serves.");
        }

        /// <summary>
        /// Everything the downstream server serves is visible through the proxy root.
        /// </summary>
        /// <remarks>
        /// This is the pass through the sample exists for. The browse of the proxy root is
        /// forwarded to the downstream server and every result mapped back into the
        /// namespaces of the aggregating one; the Server object is the one node which
        /// stays local on both sides.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DownstreamAddressSpaceIsReachableThroughTheProxy(CancellationToken ct)
        {
            IReadOnlyList<string> throughTheProxy = await RequirePassThroughAsync(ct).ConfigureAwait(false);

            IReadOnlyList<string> directly = await SessionOps
                .BrowseNamesAsync(Downstream, ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);

            Assert.That(
                throughTheProxy,
                Is.SupersetOf(directly.Where(name => name != "Server")),
                "Everything the downstream server serves has to be visible through the proxy.");
        }

        /// <summary>
        /// Reading a node through the proxy returns what the downstream server holds.
        /// </summary>
        /// <remarks>
        /// The static scalar of the reference server keeps whatever value it was given, so
        /// the read through the proxy and the direct read have to agree - which proves the
        /// read was forwarded rather than answered from a copy.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DownstreamValueIsReadableThroughTheProxy(CancellationToken ct)
        {
            await RequirePassThroughAsync(ct).ConfigureAwait(false);

            string[] path = { "CTT", "Scalar", "Scalar_Static", "Scalar_Static_Int32" };

            NodeId proxied = await WalkAsync(Aggregating, m_proxyRoot, ct, path).ConfigureAwait(false);
            NodeId direct = await WalkAsync(Downstream, ObjectIds.ObjectsFolder, ct, path).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(proxied.IsNull, Is.False, "The static scalar has to be reachable through the proxy.");
                Assert.That(direct.IsNull, Is.False, "The static scalar has to exist on the downstream server.");
            });

            DataValue throughTheProxy = await SessionOps
                .ReadValueAsync(Aggregating, proxied, ct)
                .ConfigureAwait(false);

            DataValue directly = await SessionOps
                .ReadValueAsync(Downstream, direct, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Read {throughTheProxy.WrappedValue} through the proxy, {directly.WrappedValue} directly")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    StatusCode.IsGood(throughTheProxy.StatusCode),
                    Is.True,
                    $"The read through the proxy failed: {throughTheProxy.StatusCode}");

                Assert.That(
                    throughTheProxy.WrappedValue,
                    Is.EqualTo(directly.WrappedValue),
                    "The proxy has to hand back the value the downstream server holds.");
            });
        }

        /// <summary>
        /// A subscription made on the aggregating server is forwarded downstream and its
        /// notifications come back through.
        /// </summary>
        /// <remarks>
        /// The dynamic scalar of the reference server changes on its own once a second, so
        /// notifications arriving through the proxy prove the whole chain: the monitored
        /// item was forwarded to the downstream server, its notifications were mapped back
        /// and queued on the item of the aggregating server's own subscription.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DownstreamValueChangesAreForwardedToSubscribers(CancellationToken ct)
        {
            await RequirePassThroughAsync(ct).ConfigureAwait(false);

            NodeId proxied = await WalkAsync(
                Aggregating,
                m_proxyRoot,
                ct,
                "CTT", "Scalar", "Scalar_Simulation", "Scalar_Simulation_Int32").ConfigureAwait(false);

            Assert.That(proxied.IsNull, Is.False, "The simulated scalar has to be reachable through the proxy.");

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Aggregating, proxied, ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> changes = await capture
                .CollectDistinctAsync(2, TimeSpan.FromSeconds(30), ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Received {changes.Count} distinct values through the proxy: " +
                    string.Join(", ", changes.Select(change => change.WrappedValue)))
                .ConfigureAwait(false);

            Assert.That(
                changes.Count,
                Is.GreaterThanOrEqualTo(2),
                "The simulation has to keep producing values through the proxy.");
        }

        /// <summary>
        /// The downstream server the fixture started really is serving.
        /// </summary>
        /// <remarks>
        /// Here so that a pass-through failure cannot be blamed on the downstream server
        /// being absent, which is the first thing anybody reading it will wonder.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DownstreamServerIsServingItsOwnAddressSpace(CancellationToken ct)
        {
            IReadOnlyList<string> children = await SessionOps
                .BrowseNamesAsync(Downstream, ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The downstream server serves: {string.Join(", ", children)}")
                .ConfigureAwait(false);

            Assert.That(
                children,
                Is.Not.Empty,
                "The downstream server has to be serving for this fixture to mean anything.");

            DataValue state = await SessionOps
                .ReadValueAsync(Downstream, VariableIds.Server_ServerStatus_State, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(state.StatusCode),
                Is.True,
                $"The downstream server does not answer for its own state: {state.StatusCode}");
        }

        private static async Task<SampleServerHost> StartAsync(
            string sampleName,
            Action<ApplicationConfiguration> configure)
        {
            SampleServerUnderTest sample = SampleServerFactories.All
                .FirstOrDefault(candidate => candidate.Sample.Name == sampleName)
                ?? throw new InvalidOperationException($"There is no server factory for '{sampleName}'.");

            return await SampleServerHost
                .StartAsync(
                    sample.Sample.Name,
                    sample.Sample.ServerConfig,
                    sample.CreateServer,
                    configure: configure)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Replaces the downstream server the sample ships with the one the test started.
        /// </summary>
        /// <remarks>
        /// The shipped configuration names a server on port 61210 which nothing starts, and
        /// asks for a secured channel. An unsecured one is used here so that the two
        /// throwaway certificates the fixture creates do not have to trust each other. The
        /// application uri has to be the real one of the downstream server: the session the
        /// aggregating server opens names it as the server uri, and the downstream server
        /// rejects a session naming a uri which is not its own (BadServerUriInvalid).
        /// </remarks>
        private static void PointAtDownstream(
            ApplicationConfiguration configuration,
            string downstreamUrl,
            string downstreamApplicationUri)
        {
            var endpoints = new ConfiguredEndpointCollection();

            var description = new EndpointDescription {
                EndpointUrl = downstreamUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                Server = new ApplicationDescription {
                    ApplicationUri = downstreamApplicationUri,
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = new[] { downstreamUrl }.ToArrayOf(),
                },
                // the aggregating server derives the identity for its own metadata session
                // from this list, so an endpoint without one gives it nothing to connect with
                UserIdentityTokens = new[] {
                    new UserTokenPolicy {
                        PolicyId = "Anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None,
                    },
                }.ToArrayOf(),
            };

            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(configuration)) {
                UpdateBeforeConnect = false,
            };

            endpoints.Add(endpoint);

            configuration.UpdateExtension<ConfiguredEndpointCollection>(null, endpoints);
        }
    }
}
