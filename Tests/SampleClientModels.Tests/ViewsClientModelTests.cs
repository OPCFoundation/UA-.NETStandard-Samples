/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Quickstarts.ViewsClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the Views client exists to show, asked of its model without the window: the
    /// views of the server are listed, and a browse through one of them sees a different
    /// address space than a browse without one.
    /// </summary>
    /// <remarks>
    /// The Views client has no generated model of its own, so the namespaces of the two
    /// disciplines are taken from the server assembly, where they exist exactly once.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class ViewsClientModelTests : ClientModelFixtureBase<ViewsClientModel>
    {
        private const string EngineeringNamespace = Quickstarts.Views.Namespaces.Engineering;
        private const string OperationsNamespace = Quickstarts.Views.Namespaces.Operations;

        protected override string SampleName => "Views";

        protected override ViewsClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new ViewsClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachListsTheEngineeringAndOperationsViews(CancellationToken ct)
        {
            Assert.That(Model.Views, Is.Empty, "A detached model already lists views.");

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(
                Model.Views.Select(view => view.BrowseName.Name),
                Does.Contain("Engineering").And.Contain("Operations"),
                "The sample serves an engineering and an operations view. The model found: " +
                string.Join(", ", Model.Views.Select(view => view.BrowseName.Name)));

            Assert.That(Model.CurrentView, Is.Null, "No view is selected until the user picks one.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task BrowsingThroughTheEngineeringViewHidesTheOperationsNodes(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId flow = await PathAsync(ct, "Plant", "Boiler #1", "WaterIn", "Flow").ConfigureAwait(false);

            // the baseline the filtered browse is compared against: without a view the
            // flow meter shows the nodes of both disciplines
            IReadOnlyList<ReferenceDescription> unfiltered = await Model.BrowseAsync(flow, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(In(unfiltered, EngineeringNamespace), Is.Not.Empty, "Browsing without a view has to show the engineering nodes.");
                Assert.That(In(unfiltered, OperationsNamespace), Is.Not.Empty, "Browsing without a view has to show the operations nodes.");
            });

            ReferenceDescription engineering = Model.Views.First(view => view.BrowseName.Name == "Engineering");

            ViewDescription view = Model.SelectView(engineering);

            Assert.That(view, Is.Not.Null);
            Assert.That(Model.CurrentView, Is.SameAs(view));
            Assert.That(view.ViewId, Is.EqualTo(ExpandedNodeId.ToNodeId(engineering.NodeId, Session.NamespaceUris)));

            IReadOnlyList<ReferenceDescription> filtered = await Model.BrowseAsync(flow, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Through the engineering view the flow meter shows: {string.Join(", ", filtered.Select(reference => reference.BrowseName))}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    In(filtered, OperationsNamespace),
                    Is.Empty,
                    "The engineering view suppresses the operations nodes, so a browse which " +
                    "carries the view must not see them any more.");

                Assert.That(
                    In(filtered, EngineeringNamespace),
                    Is.Not.Empty,
                    "The engineering view keeps the engineering nodes, so an empty result means " +
                    "the browse failed rather than filtered.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SelectingNoViewBrowsesUnfiltered(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId flow = await PathAsync(ct, "Plant", "Boiler #1", "WaterIn", "Flow").ConfigureAwait(false);

            Model.SelectView(Model.Views.First(view => view.BrowseName.Name == "Engineering"));

            Assume.That(In(await Model.BrowseAsync(flow, ct).ConfigureAwait(false), OperationsNamespace), Is.Empty);

            // the window offers a "None" entry, which is a reference without a node id
            Assert.That(Model.SelectView(null), Is.Null);
            Assert.That(Model.SelectView(new ReferenceDescription { NodeId = ExpandedNodeId.Null }), Is.Null);
            Assert.That(Model.CurrentView, Is.Null);

            IReadOnlyList<ReferenceDescription> unfiltered = await Model.BrowseAsync(flow, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(In(unfiltered, EngineeringNamespace), Is.Not.Empty);
                Assert.That(In(unfiltered, OperationsNamespace), Is.Not.Empty, "Clearing the view has to show the operations nodes again.");
            });
        }

        private string[] In(IEnumerable<ReferenceDescription> references, string namespaceUri)
        {
            ushort index = NamespaceIndex(namespaceUri);

            return references
                .Where(reference => reference.BrowseName.NamespaceIndex == index)
                .Select(reference => reference.BrowseName.Name)
                .ToArray();
        }
    }
}
