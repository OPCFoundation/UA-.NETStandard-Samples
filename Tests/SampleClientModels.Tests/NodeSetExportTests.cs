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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Samples.Client;
using Quickstarts.Boiler.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The client side NodeSet2 export the sample clients offer under
    /// <c>Export NodeSet2...</c>, driven against the Boiler sample server, whose address
    /// space carries a model of its own next to the standard nodes.
    /// </summary>
    /// <remarks>
    /// The model of the sample plays no part here - the export needs nothing but a session
    /// - but the fixture is what starts a sample server and opens one, so the export rides
    /// along on the Boiler fixture.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class NodeSetExportTests : ClientModelFixtureBase<BoilerClientModel>
    {
        private string m_filePath;

        protected override string SampleName => "Boiler";

        protected override BoilerClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new BoilerClientModel(telemetry);
        }

        [TearDown]
        public void DeleteExportedFile()
        {
            if (m_filePath != null && File.Exists(m_filePath))
            {
                File.Delete(m_filePath);
            }

            m_filePath = null;
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ExportWritesTheModelOfTheServerToANodeSet2File(CancellationToken ct)
        {
            m_filePath = NewFilePath();

            NodeSetExportResult result = await NodeSetExport
                .ExportToFileAsync(Session, m_filePath, ct: ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Exported {result.NodeCount} nodes of [{string.Join(", ", result.NamespaceUris)}].")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(result.FilePath, Is.EqualTo(m_filePath));
                Assert.That(result.NodeCount, Is.GreaterThan(0), "The browse below the Objects folder reached no node of the server.");
                Assert.That(File.Exists(m_filePath), Is.True, "The export wrote no file.");
                Assert.That(
                    result.NamespaceUris,
                    Has.Some.Contains("Boiler"),
                    "The export did not reach the model namespace of the sample server.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheExportedFileReadsBackWithTheNodeSet2Reader(CancellationToken ct)
        {
            m_filePath = NewFilePath();

            NodeSetExportResult result = await NodeSetExport
                .ExportToFileAsync(Session, m_filePath, ct: ct)
                .ConfigureAwait(false);

            // the point of writing NodeSet2 rather than some private format: the same
            // reader a server loads its model with reads the file back.
            UANodeSet nodeSet;

            using (var stream = File.OpenRead(m_filePath))
            {
                nodeSet = UANodeSet.Read(stream);
            }

            Assert.Multiple(() => {
                Assert.That(nodeSet, Is.Not.Null, "The exported file is not a NodeSet2 document.");
                Assert.That(nodeSet.Items, Has.Length.EqualTo(result.NodeCount), "The file holds a different number of nodes than the export reported.");
                Assert.That(
                    nodeSet.NamespaceUris,
                    Is.Not.Null.And.Not.Empty,
                    "A NodeSet of server nodes has to declare the namespaces those nodes belong to.");
            });

            foreach (string uri in result.NamespaceUris)
            {
                Assert.That(nodeSet.NamespaceUris, Has.Member(uri), "A namespace of the exported nodes is not declared in the file.");
            }
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheStandardNamespaceIsLeftOutUnlessItIsAskedFor(CancellationToken ct)
        {
            // the default: a NodeSet2 of server nodes, because every consumer already has
            // the standard address space.
            IList<INode> serverNodes = await NodeSetExport
                .CollectNodesAsync(Session, new NodeSetExportSettings(), ct)
                .ConfigureAwait(false);

            IList<INode> allNodes = await NodeSetExport
                .CollectNodesAsync(
                    Session,
                    new NodeSetExportSettings { ExcludeStandardNamespace = false },
                    ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    serverNodes.Select(node => node.NodeId.NamespaceIndex),
                    Has.All.Not.Zero,
                    "A default export carries nodes of the OPC UA namespace.");
                Assert.That(
                    allNodes.Count,
                    Is.GreaterThan(serverNodes.Count),
                    "Including the standard namespace reached no additional node, so the filter does nothing.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheBrowseStopsAtTheChildrenWhenTheTreeIsNotFetched(CancellationToken ct)
        {
            var shallow = new NodeSetExportSettings {
                ExcludeStandardNamespace = false,
                FetchTree = false,
            };

            IList<INode> children = await NodeSetExport.CollectNodesAsync(Session, shallow, ct).ConfigureAwait(false);

            IList<INode> tree = await NodeSetExport
                .CollectNodesAsync(Session, shallow with { FetchTree = true }, ct)
                .ConfigureAwait(false);

            Assert.That(
                tree.Count,
                Is.GreaterThan(children.Count),
                "Walking the hierarchy reached no more nodes than its first level.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheNodeCountIsBounded(CancellationToken ct)
        {
            IList<INode> bounded = await NodeSetExport
                .CollectNodesAsync(
                    Session,
                    new NodeSetExportSettings { ExcludeStandardNamespace = false, MaxNodeCount = 10 },
                    ct)
                .ConfigureAwait(false);

            Assert.That(bounded, Has.Count.LessThanOrEqualTo(10), "The bound on the collected nodes was not honoured.");
        }

        /// <summary>
        /// A file of this test run, in the temporary directory of the machine.
        /// </summary>
        private static string NewFilePath()
        {
            return Path.Combine(Path.GetTempPath(), $"SampleExport.{Guid.NewGuid():N}.NodeSet2.xml");
        }
    }
}
