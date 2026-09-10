/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The overlay half of the RuntimeNodeSets sample: a source-generated node manager
    /// which imports two NodeSet2 documents into the model it was generated from.
    /// </summary>
    /// <remarks>
    /// These tests never touch the node manager object. Everything the overlay does is
    /// visible to a Client which browses the site model, and that is the level the whole
    /// tier works at: the placeholder is gone, the replacement is in its slot, and the
    /// nodes of the second document hang off a parent from the first one.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class RuntimeNodeSetsOverlayNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "RuntimeNodeSets";

        private const string SiteNamespace =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Site/";

        /// <summary>The generated <c>Site</c> object.</summary>
        private const uint SiteIdentifier = 100;

        /// <summary>The generated <c>Site/Station1</c> placeholder the overlay displaces.</summary>
        private const uint GeneratedStation1Identifier = 101;

        /// <summary>The generated <c>Site/Station2</c> slot no document claims.</summary>
        private const uint GeneratedStation2Identifier = 110;

        /// <summary>The generated <c>StationType</c>.</summary>
        private const uint StationTypeIdentifier = 10;

        /// <summary>The <c>Station1</c> the overlay puts in the first slot.</summary>
        private const uint ImportedStation1Identifier = 5001;

        /// <summary>The <c>Station3</c> the overlay adds below <c>Site</c>.</summary>
        private const uint ImportedStation3Identifier = 5010;

        /// <summary>The <c>Reset</c> the second document hangs off <c>Station3</c>.</summary>
        private const uint ImportedStation3ResetIdentifier = 5020;

        /// <summary>The <c>Temperature</c> the second document adds to <c>Station3</c>.</summary>
        private const uint ImportedStation3TemperatureIdentifier = 5030;

        private QualifiedName Site => Name(SiteNamespace, "Site");

        /// <summary>
        /// The compiled model reached the address space, and so did the stations the
        /// overlay documents put into it.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheOverlayAndTheModelShareOneAddressSpace(CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);

            IReadOnlyList<string> stations = await BrowseNamesAsync(siteId, ct).ConfigureAwait(false);

            await ReportAsync("Site", stations).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    siteId,
                    Is.EqualTo(SiteId(SiteIdentifier)),
                    "Site is not the node the model design declares.");
                Assert.That(
                    stations,
                    Is.EquivalentTo(new[] { "Station1", "Station2", "Station3" }),
                    "The site does not carry the model's two slots plus the station the overlay added.");
            });
        }

        /// <summary>
        /// The child of the overlay took the slot of the generated placeholder: same
        /// BrowseName, a different node, and the node which was there is gone.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheImportedChildReplacedTheGeneratedPlaceholder(CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);
            NodeId station1 = await ChildAsync(siteId, "Station1", ct).ConfigureAwait(false);

            DataValue displaced = await SessionOps
                .ReadAttributeAsync(Session, SiteId(GeneratedStation1Identifier), Attributes.NodeId, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    station1,
                    Is.EqualTo(SiteId(ImportedStation1Identifier)),
                    "Site/Station1 is not the node the overlay document declares.");
                Assert.That(
                    displaced.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "The generated placeholder is still in the address space next to its replacement.");
            });
        }

        /// <summary>
        /// A replacement carries exactly the children its document declares. The slot no
        /// document claimed still carries everything the model gives it, which is what
        /// makes the difference readable.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnImportedInstanceCarriesOnlyWhatItsDocumentDeclares(CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);

            NodeId replaced = await ChildAsync(siteId, "Station1", ct).ConfigureAwait(false);
            NodeId generated = await ChildAsync(siteId, "Station2", ct).ConfigureAwait(false);
            NodeId added = await ChildAsync(siteId, "Station3", ct).ConfigureAwait(false);

            IReadOnlyList<string> replacedChildren = await BrowseNamesAsync(replaced, ct).ConfigureAwait(false);
            IReadOnlyList<string> generatedChildren = await BrowseNamesAsync(generated, ct).ConfigureAwait(false);
            IReadOnlyList<string> addedChildren = await BrowseNamesAsync(added, ct).ConfigureAwait(false);

            await ReportAsync("Station1 (replaced by the overlay)", replacedChildren).ConfigureAwait(false);
            await ReportAsync("Station2 (left to the model)", generatedChildren).ConfigureAwait(false);
            await ReportAsync("Station3 (added by the overlay)", addedChildren).ConfigureAwait(false);

            Assert.Multiple(() => {
                // the document gives Station1 no Reset, so the replacement has none - the
                // mandatory children of StationType are not materialized behind its back.
                Assert.That(
                    replacedChildren,
                    Is.EquivalentTo(new[] { "Throughput", "Status" }),
                    "The replacement does not carry exactly the children its document declares.");

                Assert.That(
                    generatedChildren,
                    Is.EquivalentTo(new[] { "Throughput", "Status", "Reset" }),
                    "The slot no document claimed lost children of the type it is an instance of.");

                // Throughput comes from the first document, Reset and Temperature from
                // the second one.
                Assert.That(
                    addedChildren,
                    Is.EquivalentTo(new[] { "Throughput", "Reset", "Temperature" }),
                    "The station the overlay added did not get the nodes of both documents.");
            });
        }

        /// <summary>
        /// The second document declares nodes whose parent lives in the first one, and
        /// the batch links them anyway.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheTwoDocumentsOfOneBatchLinkAcrossEachOther(CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);
            NodeId station3 = await ChildAsync(siteId, "Station3", ct).ConfigureAwait(false);

            NodeId temperature = await ChildAsync(station3, "Temperature", ct).ConfigureAwait(false);
            NodeId reset = await ChildAsync(station3, "Reset", ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    station3,
                    Is.EqualTo(SiteId(ImportedStation3Identifier)),
                    "Station3 is not the node the layout document declares.");
                Assert.That(
                    temperature,
                    Is.EqualTo(SiteId(ImportedStation3TemperatureIdentifier)),
                    "Temperature did not reach the parent the other document declares.");
                Assert.That(
                    reset,
                    Is.EqualTo(SiteId(ImportedStation3ResetIdentifier)),
                    "Reset did not reach the parent the other document declares.");
            });
        }

        /// <summary>
        /// Every station is an instance of the compiled <c>StationType</c>, whether the
        /// model or a document put it there. That is what the generated import factories
        /// buy: the imported nodes are the model's own states, not generic ones.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        [TestCase("Station1")]
        [TestCase("Station2")]
        [TestCase("Station3")]
        public async Task EveryStationIsAnInstanceOfTheCompiledType(string station, CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);
            NodeId stationId = await ChildAsync(siteId, station, ct).ConfigureAwait(false);

            NodeId typeDefinition = await SessionOps
                .GetTypeDefinitionAsync(Session, stationId, ct)
                .ConfigureAwait(false);

            Assert.That(
                typeDefinition,
                Is.EqualTo(SiteId(StationTypeIdentifier)),
                $"{station} is not an instance of the StationType the model design declares.");
        }

        /// <summary>
        /// Behaviour wired onto an imported node in the same <c>Configure</c> pass works
        /// like behaviour wired onto a generated one.
        /// </summary>
        /// <remarks>
        /// The imported Method also has to have kept the arguments its document declares:
        /// an imported Method carries no children of its declaration, so a document which
        /// leaves the InputArguments Property out gets a Method nobody can call.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        [TestCase("Station3", Description = "wired onto a node the overlay imported")]
        [TestCase("Station2", Description = "wired onto a node the model generated")]
        public async Task ResetAnswersOnAnImportedAndOnAGeneratedStation(
            string station,
            CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);
            NodeId stationId = await ChildAsync(siteId, station, ct).ConfigureAwait(false);
            NodeId resetId = await ChildAsync(stationId, "Reset", ct).ConfigureAwait(false);

            CallMethodResult result = await SessionOps
                .CallAsync(Session, stationId, resetId, ct, Variant.From(true))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good),
                    $"{station}/Reset was refused.");
                Assert.That(
                    result.OutputArguments.Count,
                    Is.EqualTo(1),
                    $"{station}/Reset did not answer with the output argument it declares.");
            });
        }

        /// <summary>
        /// The values of an imported variable are served like any other: the manager
        /// owns the imported nodes once the batch is linked.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ImportedVariablesAreReadable(CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);
            NodeId station1 = await ChildAsync(siteId, "Station1", ct).ConfigureAwait(false);
            NodeId throughput = await ChildAsync(station1, "Throughput", ct).ConfigureAwait(false);
            NodeId status = await ChildAsync(station1, "Status", ct).ConfigureAwait(false);

            // the simulation of the node manager runs once a second, and the document
            // starts the variable at zero, so the first tick may still be ahead of us.
            DataValue throughputValue = await Poll.UntilAsync(
                token => SessionOps.ReadValueAsync(Session, throughput, token),
                value => value.WrappedValue.TryGetValue(out double parts) && parts > 0,
                "the simulation of the node manager to reach the imported variable",
                ct: ct).ConfigureAwait(false);

            DataValue statusValue = await SessionOps
                .ReadValueAsync(Session, status, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    throughputValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good),
                    "Station1/Throughput does not read.");
                Assert.That(
                    throughputValue.WrappedValue.TryGetValue(out double parts) ? parts : 0,
                    Is.GreaterThan(0),
                    "The simulation of the node manager does not reach the imported variable.");
                Assert.That(
                    statusValue.WrappedValue.TryGetValue(out string text) ? text : null,
                    Is.Not.Null.And.Not.Empty,
                    "Station1/Status does not carry the value its document declares.");
            });
        }

        /// <summary>
        /// The generated placeholder is not only unbrowsable from its parent - nothing
        /// points at it any more. References to a displaced node are retargeted at its
        /// replacement.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task NoReferenceOfTheSiteStillPointsAtTheDisplacedNode(CancellationToken ct)
        {
            NodeId siteId = await ResolveAsync(ct, Site).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> references = await SessionOps
                .BrowseAsync(Session, siteId, ct)
                .ConfigureAwait(false);

            var targets = new List<NodeId>();

            foreach (ReferenceDescription reference in references)
            {
                targets.Add(ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris));
            }

            Assert.Multiple(() => {
                Assert.That(
                    targets,
                    Does.Not.Contain(SiteId(GeneratedStation1Identifier)),
                    "Site still references the placeholder the overlay displaced.");
                Assert.That(
                    targets,
                    Does.Contain(SiteId(ImportedStation1Identifier)),
                    "Site does not reference the replacement.");
                Assert.That(
                    targets,
                    Does.Contain(SiteId(GeneratedStation2Identifier)),
                    "The slot no document claimed lost its reference from Site.");
            });
        }

        private NodeId SiteId(uint identifier)
        {
            return new NodeId(identifier, NamespaceIndex(SiteNamespace));
        }
    }
}
