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
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Starts one sample server for a fixture, and for every test opens a managed session
    /// on it and creates the client model under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the window of a sample client does when the user clicks Connect -
    /// open a managed session through the shared connect control and hand it to the model
    /// with <see cref="SampleClientModel.AttachAsync"/> - without the window. The model is
    /// constructed with no <see cref="SynchronizationContext"/>, so its events arrive
    /// inline on whichever thread raised them, which is why the tests collect them in a
    /// thread safe <see cref="EventSink{TArgs}"/> and wait on that.
    /// </para>
    /// <para>
    /// The session and the model are per test, the server per fixture: the model tests
    /// attach, detach and reattach, and a fresh session per test keeps one test's
    /// subscriptions from leaking into the next.
    /// </para>
    /// </remarks>
    /// <typeparam name="TModel">The model under test.</typeparam>
    public abstract class ClientModelFixtureBase<TModel>
        where TModel : SampleClientModel
    {
        /// <summary>
        /// How long a single test gets.
        /// </summary>
        protected const int kTimeout = 120_000;

        private SampleServerHost m_host;
        private TestClient m_client;
        private TModel m_model;

        /// <summary>
        /// The name of the sample in the catalog, for instance "Boiler".
        /// </summary>
        protected abstract string SampleName { get; }

        /// <summary>
        /// Creates the model under test, the way the window of the sample does.
        /// </summary>
        protected abstract TModel CreateModel(ITelemetryContext telemetry);

        /// <summary>
        /// The user the session of a test is opened for. Anonymous by default.
        /// </summary>
        protected virtual IUserIdentity Identity => null;

        /// <summary>
        /// Whether the session of a test is opened on a secured endpoint.
        /// </summary>
        protected virtual bool UseSecurity => false;

        /// <summary>
        /// Changes the configuration of the sample server before it is started.
        /// </summary>
        protected virtual Action<ApplicationConfiguration> ConfigureServer => null;

        /// <summary>
        /// The session of the current test.
        /// </summary>
        protected ISession Session => m_client.Session;

        /// <summary>
        /// The model of the current test, not yet attached.
        /// </summary>
        protected TModel Model => m_model;

        /// <summary>
        /// The endpoint the sample server listens on.
        /// </summary>
        protected string EndpointUrl => m_host.EndpointUrl;

        /// <summary>
        /// The sample server, for the tests which stop and restart it.
        /// </summary>
        protected SampleServerHost Host => m_host;

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
                    server.ConfigureServices,
                    configure: ConfigureServer)
                .ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopSampleAsync()
        {
            if (m_host != null)
            {
                await m_host.DisposeAsync().ConfigureAwait(false);
                m_host = null;
            }
        }

        [SetUp]
        public async Task OpenSessionAsync()
        {
            m_client = await ConnectAsync(Identity, UseSecurity, TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);

            m_model = CreateModel(NullTelemetry.Instance);
        }

        [TearDown]
        public async Task CloseSessionAsync()
        {
            // the model first: it deletes its subscriptions, and closing a session which
            // still carries one waits for the publish pipeline to drain
            if (m_model != null)
            {
                await m_model.DisposeAsync().ConfigureAwait(false);
                m_model = null;
            }

            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
                m_client = null;
            }
        }

        /// <summary>
        /// Opens another managed session on the sample server, for a test which needs a
        /// second client or a different user.
        /// </summary>
        /// <remarks>
        /// The caller disposes the client. Managed sessions are what every sample client
        /// runs on, and the only kind whose subscription manager is the V2 engine the
        /// models require.
        /// </remarks>
        protected async Task<TestClient> ConnectAsync(
            IUserIdentity identity,
            bool useSecurity,
            string sessionName,
            CancellationToken ct = default)
        {
            TestClient client = await TestClient
                .ConnectManagedAsync(m_host.EndpointUrl, $"{SampleName} model tests {sessionName}", identity, useSecurity, ct)
                .ConfigureAwait(false);

            Assert.That(
                client.Session,
                Is.InstanceOf<ManagedSession>(),
                "The test opened a plain session, and the models need a managed one on the V2 engine.");

            await client.Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            return client;
        }

        /// <summary>
        /// Attaches the model of the test to its session and returns it.
        /// </summary>
        protected async Task<TModel> AttachAsync(CancellationToken ct = default)
        {
            await m_model.AttachAsync(Session, ct).ConfigureAwait(false);

            Assert.That(m_model.IsConnected, Is.True, "The model did not report itself attached.");

            return m_model;
        }

        /// <summary>
        /// The index the server uses for a namespace.
        /// </summary>
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
        /// Finds a child of a node by the text of its browse name, in any namespace.
        /// </summary>
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
        /// Waits until a condition holds, and reports what it last saw when it does not.
        /// </summary>
        protected static Task<bool> WaitUntilAsync(
            Func<bool> condition,
            string because,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            return Poll.UntilAsync(
                _ => Task.FromResult(condition()),
                holds => holds,
                because,
                timeout,
                ct: ct);
        }
    }
}
