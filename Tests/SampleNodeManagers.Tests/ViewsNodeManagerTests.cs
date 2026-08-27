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

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the views sample does: the same address space looks different depending on
    /// which view a client browses it through.
    /// </summary>
    /// <remarks>
    /// This is the only sample which overrides IsNodeInView and IsReferenceInView, and the
    /// filtering they implement is invisible unless a browse actually carries a view. The
    /// rule is by namespace: the engineering view hides the operations nodes and the other
    /// way round, so a flow meter shows its serial number to an engineer and its
    /// measurement to an operator.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class ViewsNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Views";

        private const string ViewsNamespace = Quickstarts.Views.Namespaces.Views;
        private const string EngineeringNamespace = Quickstarts.Views.Namespaces.Engineering;
        private const string OperationsNamespace = Quickstarts.Views.Namespaces.Operations;

        /// <summary>
        /// The two views the sample declares are where the model says they are.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EngineeringAndOperationsViewsAreExposed(CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> views = await SessionOps
                .BrowseAsync(Session, ObjectIds.ViewsFolder, ct)
                .ConfigureAwait(false);

            await ReportAsync("Views", views.Select(view => view.BrowseName.Name)).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    views.Select(view => view.BrowseName.Name),
                    Does.Contain("Engineering").And.Contain("Operations"),
                    "The sample serves an engineering and an operations view.");

                Assert.That(
                    views.Select(view => ExpandedNodeId.ToNodeId(view.NodeId, Session.NamespaceUris)),
                    Does.Contain(EngineeringView).And.Contain(OperationsView),
                    "The views kept their node ids from the model.");
            });
        }

        /// <summary>
        /// Without a view a client sees the nodes of both disciplines.
        /// </summary>
        /// <remarks>
        /// This is the baseline the two filtered browses below are compared against: if
        /// this one ever stops showing both, the filtering tests would pass for the wrong
        /// reason.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task UnrestrictedBrowseShowsBothDisciplines(CancellationToken ct)
        {
            NodeId flowId = await ResolveFlowAsync(ct).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, flowId, ct)
                .ConfigureAwait(false);

            await ReportAsync("Flow without a view", Describe(children)).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    NamesIn(children, EngineeringNamespace),
                    Is.Not.Empty,
                    "Browsing without a view has to show the engineering nodes.");

                Assert.That(
                    NamesIn(children, OperationsNamespace),
                    Is.Not.Empty,
                    "Browsing without a view has to show the operations nodes.");
            });
        }

        /// <summary>
        /// The engineering view hides the operations nodes and keeps the engineering ones.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EngineeringViewSuppressesOperationsNodes(CancellationToken ct)
        {
            NodeId flowId = await ResolveFlowAsync(ct).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, flowId, ct, view: new ViewDescription { ViewId = EngineeringView })
                .ConfigureAwait(false);

            await ReportAsync("Flow through the engineering view", Describe(children)).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    NamesIn(children, OperationsNamespace),
                    Is.Empty,
                    "The engineering view has to suppress the operations nodes.");

                Assert.That(
                    NamesIn(children, EngineeringNamespace),
                    Is.Not.Empty,
                    "The engineering view has to keep the engineering nodes.");
            });
        }

        /// <summary>
        /// The operations view hides the engineering nodes and keeps the operations ones.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperationsViewSuppressesEngineeringNodes(CancellationToken ct)
        {
            NodeId flowId = await ResolveFlowAsync(ct).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, flowId, ct, view: new ViewDescription { ViewId = OperationsView })
                .ConfigureAwait(false);

            await ReportAsync("Flow through the operations view", Describe(children)).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    NamesIn(children, EngineeringNamespace),
                    Is.Empty,
                    "The operations view has to suppress the engineering nodes.");

                Assert.That(
                    NamesIn(children, OperationsNamespace),
                    Is.Not.Empty,
                    "The operations view has to keep the operations nodes.");
            });
        }

        /// <summary>
        /// Both boilers the sample creates in code are under the plant folder.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task PlantContainsBothBoilers(CancellationToken ct)
        {
            NodeId plantId = await ResolveAsync(ct, Name(ViewsNamespace, "Plant")).ConfigureAwait(false);

            IReadOnlyList<string> boilers = await BrowseNamesAsync(plantId, ct).ConfigureAwait(false);

            await ReportAsync("Plant", boilers).ConfigureAwait(false);

            Assert.That(
                boilers,
                Does.Contain("Boiler #1").And.Contain("Boiler #2"),
                "The sample creates two boilers under the plant folder.");
        }

        private NodeId EngineeringView
            => new(Quickstarts.Views.Views.Engineering, NamespaceIndex(ViewsNamespace));

        private NodeId OperationsView
            => new(Quickstarts.Views.Views.Operations, NamespaceIndex(ViewsNamespace));

        /// <summary>
        /// The flow meter of the first boiler, which carries nodes of both disciplines.
        /// </summary>
        private async Task<NodeId> ResolveFlowAsync(CancellationToken ct)
        {
            return await ResolveAsync(
                ct,
                Name(ViewsNamespace, "Plant"),
                Name(ViewsNamespace, "Boiler #1"),
                Name(ViewsNamespace, "WaterIn"),
                Name(ViewsNamespace, "Flow")).ConfigureAwait(false);
        }

        private IEnumerable<string> NamesIn(IEnumerable<ReferenceDescription> references, string namespaceUri)
        {
            ushort ns = NamespaceIndex(namespaceUri);

            return references
                .Where(reference => reference.BrowseName.NamespaceIndex == ns)
                .Select(reference => reference.BrowseName.Name)
                .ToArray();
        }

        private IEnumerable<string> Describe(IEnumerable<ReferenceDescription> references)
        {
            return references.Select(reference =>
                $"{reference.BrowseName.Name} ({Session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex)})");
        }
    }
}
