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
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class AggregationNodeManagerTests
    {
        private const int kTimeout = 90_000;

        private const string DownstreamSample = "Reference";
        private const string AggregatingSample = "AggregationConsole";

        private SampleServerHost m_downstream;
        private SampleServerHost m_aggregating;
        private TestClient m_downstreamClient;
        private TestClient m_aggregatingClient;
        private NodeId m_proxyRoot;

        [OneTimeSetUp]
        public async Task StartBothServersAsync()
        {
            m_downstream = await StartAsync(DownstreamSample, null).ConfigureAwait(false);

            m_aggregating = await StartAsync(
                AggregatingSample,
                configuration => PointAtDownstream(configuration, m_downstream.EndpointUrl))
                .ConfigureAwait(false);

            m_downstreamClient = await TestClient
                .ConnectAsync(m_downstream.EndpointUrl, "aggregation downstream")
                .ConfigureAwait(false);

            m_aggregatingClient = await TestClient
                .ConnectAsync(m_aggregating.EndpointUrl, "aggregation upstream")
                .ConfigureAwait(false);

            await m_downstreamClient.Session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            await m_aggregatingClient.Session.FetchNamespaceTablesAsync().ConfigureAwait(false);

            // the node manager waits a few seconds before it connects downstream, and the
            // proxy root only appears once it has
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
                "the aggregation server to connect downstream and publish its proxy root",
                timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The proxy root is {m_proxyRoot}").ConfigureAwait(false);

            DataValue rootName = await SessionOps
                .ReadAttributeAsync(Aggregating, m_proxyRoot, Attributes.BrowseName, default)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The proxy root is called {rootName.WrappedValue}")
                .ConfigureAwait(false);
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
                    nodeClass.WrappedValue.AsBoxedObject(),
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
        /// Everything past the proxy root ought to reach the downstream server, and today
        /// nothing does.
        /// </summary>
        /// <remarks>
        /// The aggregating server publishes its proxy root, which
        /// ProxyRootIsPublishedForTheConfiguredServer checks, and then answers
        /// BadNotConnected to every browse of it. The downstream server is running and
        /// serving - the fixture holds an ordinary session to it and reads from it, which
        /// DownstreamServerIsServingItsOwnAddressSpace asserts - so the session the node
        /// manager is supposed to open to it never comes up.
        ///
        /// The refusal is deliberate rather than an error: the node manager hands out a
        /// downstream session only once its type cache is loaded and its status node reads
        /// Good, and both of those are set by the metadata update it schedules five seconds
        /// after start. That update is what never finishes. The proxy root is still called
        /// "Root" rather than the name of the downstream server, and renaming it is the
        /// first thing the metadata update does, so it does not get far.
        ///
        /// Everything this sample exists for is behind that browse: the address space of
        /// the other server, reading and writing through it, and forwarding subscriptions.
        /// None of it can be covered until the connection is made, so the whole of it is
        /// recorded here as one expectation rather than as tests which would all fail for
        /// the same reason.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task DownstreamAddressSpaceIsReachableThroughTheProxy(CancellationToken ct)
        {
            return KnownIssue.RecordAsync(
                async () => {
                    IReadOnlyList<string> throughTheProxy = await SessionOps
                        .BrowseNamesAsync(Aggregating, m_proxyRoot, ct)
                        .ConfigureAwait(false);

                    IReadOnlyList<string> directly = await SessionOps
                        .BrowseNamesAsync(Downstream, ObjectIds.ObjectsFolder, ct)
                        .ConfigureAwait(false);

                    Assert.That(
                        throughTheProxy,
                        Is.SupersetOf(directly.Where(name => name != "Server")),
                        "Everything the downstream server serves has to be visible through the proxy.");
                },
                "the aggregating server answers BadNotConnected to a browse of its proxy root. " +
                "It refuses a downstream session until its metadata update has loaded the type " +
                "cache, and that update never finishes - the root still carries its placeholder " +
                "name. The downstream server is up: the fixture reads from it directly.");
        }

        /// <summary>
        /// The downstream server the fixture started really is serving.
        /// </summary>
        /// <remarks>
        /// Here so that the recorded issue above cannot be blamed on the downstream server
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
        /// throwaway certificates the fixture creates do not have to trust each other.
        /// </remarks>
        private static void PointAtDownstream(ApplicationConfiguration configuration, string downstreamUrl)
        {
            var endpoints = new ConfiguredEndpointCollection();

            var description = new EndpointDescription {
                EndpointUrl = downstreamUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                Server = new ApplicationDescription {
                    ApplicationUri = downstreamUrl,
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
