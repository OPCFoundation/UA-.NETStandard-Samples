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
    /// Starts one sample server for a fixture and connects a client to it.
    /// </summary>
    /// <remarks>
    /// The node managers of the samples are about to be migrated, so these tests may only
    /// look at them the way a real client does: through the services of a running server.
    /// Nothing here reaches into a node manager object, because that is precisely the part
    /// which is going to be replaced. What has to survive the migration is the address
    /// space and the behaviour a client can observe, and that is what the fixtures assert.
    ///
    /// Starting the server once per fixture rather than once per test keeps a tier which
    /// covers sixteen samples at a few minutes instead of a quarter of an hour.
    /// </remarks>
    public abstract class NodeManagerFixtureBase
    {
        /// <summary>
        /// How long a single test gets.
        /// </summary>
        protected const int kTimeout = 60_000;

        private SampleServerHost m_host;
        private TestClient m_client;

        /// <summary>
        /// The name of the sample in the catalog, for instance "Methods".
        /// </summary>
        protected abstract string SampleName { get; }

        /// <summary>
        /// Changes the configuration of the sample before its server is started.
        /// </summary>
        /// <remarks>
        /// Only the aggregation sample needs this, to point it at a downstream server the
        /// test controls instead of at the one its shipped configuration names.
        /// </remarks>
        protected virtual Action<ApplicationConfiguration> ConfigureServer => null;

        /// <summary>
        /// The session of the fixture.
        /// </summary>
        protected ISession Session => m_client.Session;

        /// <summary>
        /// The endpoint the sample server listens on.
        /// </summary>
        protected string EndpointUrl => m_host.EndpointUrl;

        [OneTimeSetUp]
        public async Task StartSampleAsync()
        {
            SampleServerUnderTest server = SampleServerFactories.All
                .FirstOrDefault(candidate => candidate.Sample.Name == SampleName)
                ?? throw new InvalidOperationException(
                    $"There is no server factory for the sample '{SampleName}'. " +
                    "Add one to SampleServerFactories.");

            m_host = await SampleServerHost
                .StartAsync(
                    server.Sample.Name,
                    server.Sample.ServerConfig,
                    server.CreateServer,
                    configure: ConfigureServer)
                .ConfigureAwait(false);

            m_client = await TestClient
                .ConnectAsync(m_host.EndpointUrl, $"{SampleName} node manager tests")
                .ConfigureAwait(false);

            await Session.FetchNamespaceTablesAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopSampleAsync()
        {
            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
                m_client = null;
            }

            if (m_host != null)
            {
                await m_host.DisposeAsync().ConfigureAwait(false);
                m_host = null;
            }
        }

        /// <summary>
        /// The index the server uses for a namespace.
        /// </summary>
        /// <remarks>
        /// Indexes are never written down in these tests. A node manager which is rewritten
        /// may well end up registering its namespaces in a different order, and that is a
        /// change in an implementation detail rather than in behaviour, so the tests look
        /// the index up by uri every time.
        /// </remarks>
        protected ushort NamespaceIndex(string namespaceUri)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);

            Assert.That(
                index,
                Is.GreaterThanOrEqualTo(0),
                $"The server does not serve the namespace '{namespaceUri}'. It serves: " +
                string.Join(", ", Session.NamespaceUris.ToArray()));

            return (ushort)index;
        }

        /// <summary>
        /// A browse name in one of the namespaces of the sample.
        /// </summary>
        protected QualifiedName Name(string namespaceUri, string name)
        {
            return new QualifiedName(name, NamespaceIndex(namespaceUri));
        }

        /// <summary>
        /// Resolves a browse path which starts at the Objects folder.
        /// </summary>
        protected async Task<NodeId> ResolveAsync(CancellationToken ct, params QualifiedName[] path)
        {
            NodeId nodeId = await SessionOps.ResolveAsync(Session, ct, path).ConfigureAwait(false);

            Assert.That(
                nodeId.IsNull,
                Is.False,
                $"The path Objects/{string.Join("/", path.Select(name => name.Name))} " +
                "does not resolve to a node.");

            return nodeId;
        }

        /// <summary>
        /// Resolves a browse path which starts at the given node.
        /// </summary>
        protected async Task<NodeId> ResolveFromAsync(
            NodeId startingNode,
            CancellationToken ct,
            params QualifiedName[] path)
        {
            NodeId nodeId = await SessionOps
                .ResolveFromAsync(Session, startingNode, ct, path)
                .ConfigureAwait(false);

            Assert.That(
                nodeId.IsNull,
                Is.False,
                $"The path {startingNode}/{string.Join("/", path.Select(name => name.Name))} " +
                "does not resolve to a node.");

            return nodeId;
        }

        /// <summary>
        /// Finds a child of a node by the text of its browse name, in any namespace.
        /// </summary>
        /// <remarks>
        /// Useful where a sample builds a path out of nodes from several namespaces, and
        /// where the namespace a node ends up in is an implementation detail a migration is
        /// allowed to change. What a client sees, and what these tests hold the samples to,
        /// is the name.
        /// </remarks>
        protected async Task<NodeId> ChildAsync(NodeId parent, string name, CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, parent, ct)
                .ConfigureAwait(false);

            ReferenceDescription child = children
                .FirstOrDefault(candidate => candidate.BrowseName.Name == name);

            Assert.That(
                child,
                Is.Not.Null,
                $"{parent} has no child called '{name}'. It has: " +
                string.Join(", ", children.Select(candidate => candidate.BrowseName.Name)));

            return ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Follows a path of browse name texts, starting at the Objects folder.
        /// </summary>
        protected async Task<NodeId> PathAsync(CancellationToken ct, params string[] path)
        {
            NodeId current = ObjectIds.ObjectsFolder;

            foreach (string name in path)
            {
                current = await ChildAsync(current, name, ct).ConfigureAwait(false);
            }

            return current;
        }

        /// <summary>
        /// Browses a node and returns the browse names of its children.
        /// </summary>
        protected Task<IReadOnlyList<string>> BrowseNamesAsync(
            NodeId nodeId,
            CancellationToken ct,
            ViewDescription view = null)
        {
            return SessionOps.BrowseNamesAsync(Session, nodeId, ct, view);
        }

        /// <summary>
        /// Records behaviour which is broken today and has to be fixed by the migration.
        /// </summary>
        /// <seealso cref="KnownIssue.RecordAsync"/>
        protected static Task KnownIssueAsync(Func<Task> check, string issue)
        {
            return KnownIssue.RecordAsync(check, issue);
        }

        /// <summary>
        /// Writes what a browse returned to the test output.
        /// </summary>
        /// <remarks>
        /// A regression suite whose job is to record current behaviour is much easier to
        /// use when a failing run shows what the address space actually looked like.
        /// </remarks>
        protected static Task ReportAsync(string what, IEnumerable<string> names)
        {
            return TestContext.Out.WriteLineAsync($"{what}: {string.Join(", ", names)}");
        }
    }
}
