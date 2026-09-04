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

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the RuntimeNodeSets sample does: it hosts a vendor NodeSet2 document with no
    /// generated code, and adds, reloads and removes that model while the server runs.
    /// </summary>
    /// <remarks>
    /// The tests run in a fixed order, because the state under test is the server's own:
    /// which revision of the vendor model is published. Each test leaves revision 1
    /// loaded again, so the fixture is the same before and after every one of them.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class RuntimeNodeSetsNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "RuntimeNodeSets";

        private const string LineNamespace =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Line/";

        private const string ControlNamespace =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Control/";

        private QualifiedName ConveyorLine => Name(LineNamespace, "ConveyorLine");
        private QualifiedName ModelControl => Name(ControlNamespace, "ModelControl");

        [SetUp]
        public async Task LoadRevision1Async()
        {
            // every test starts from the state the server starts in
            if (await ReadLoadedRevisionAsync(default).ConfigureAwait(false) != "Rev1")
            {
                await ReloadAsync("Rev1", ReloadMode.Reload, default).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Both NodeSet2 documents reached the address space: the one the composition root
        /// registered with <c>AddRuntimeNodeSet</c> and the one the controller published
        /// at run time.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task BothNodeSetsAreServed(CancellationToken ct)
        {
            NodeId lineId = await ResolveAsync(ct, ConveyorLine).ConfigureAwait(false);
            NodeId controlId = await ResolveAsync(ct, ModelControl).ConfigureAwait(false);

            IReadOnlyList<string> lineChildren = await BrowseNamesAsync(lineId, ct).ConfigureAwait(false);
            IReadOnlyList<string> controlChildren = await BrowseNamesAsync(controlId, ct).ConfigureAwait(false);

            await ReportAsync("ConveyorLine", lineChildren).ConfigureAwait(false);
            await ReportAsync("ModelControl", controlChildren).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    lineChildren,
                    Does.Contain("Conveyor1").And.Contain("Revision"),
                    "The vendor NodeSet did not reach the address space.");

                Assert.That(
                    controlChildren,
                    Does.Contain("Load").And.Contain("Reload").And.Contain("Remove"),
                    "The control NodeSet did not reach the address space.");
            });
        }

        /// <summary>
        /// The nodes of the vendor model carry the node ids and the type definition its
        /// document declares - the SDK materialized the document rather than a copy of it.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task VendorNodesKeepTheirDeclaredIdentity(CancellationToken ct)
        {
            ushort ns = NamespaceIndex(LineNamespace);

            NodeId conveyorId = await ResolveAsync(ct, ConveyorLine, Name(LineNamespace, "Conveyor1"))
                .ConfigureAwait(false);
            NodeId speedId = await ResolveFromAsync(conveyorId, ct, Name(LineNamespace, "Speed"))
                .ConfigureAwait(false);

            NodeId typeDefinition = await SessionOps
                .GetTypeDefinitionAsync(Session, conveyorId, ct)
                .ConfigureAwait(false);

            DataValue speed = await SessionOps.ReadValueAsync(Session, speedId, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(conveyorId, Is.EqualTo(new NodeId(1101u, ns)), "Conveyor1 moved.");
                Assert.That(speedId, Is.EqualTo(new NodeId(1102u, ns)), "Speed moved.");
                Assert.That(
                    typeDefinition,
                    Is.EqualTo(new NodeId(1010u, ns)),
                    "Conveyor1 is no longer an instance of the ConveyorType the document declares.");
                Assert.That(
                    speed.WrappedValue.TryGetValue(out double value) ? value : 0,
                    Is.EqualTo(1.4),
                    "The value the document declares was not applied.");
            });
        }

        /// <summary>
        /// The enumeration the vendor document declares reaches the client as a data type
        /// with its definition, which is what lets a client which never compiled the model
        /// make sense of the State variables.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task VendorDataTypeCarriesItsDefinition(CancellationToken ct)
        {
            ushort ns = NamespaceIndex(LineNamespace);
            var conveyorState = new NodeId(1000u, ns);

            DataValue definition = await SessionOps
                .ReadAttributeAsync(Session, conveyorState, Attributes.DataTypeDefinition, ct)
                .ConfigureAwait(false);

            Assert.That(
                definition.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good),
                "ConveyorState has no DataTypeDefinition.");

            Assert.That(
                definition.WrappedValue.TryGetValue(out ExtensionObject encoded),
                Is.True,
                "The DataTypeDefinition of ConveyorState is not an ExtensionObject.");

            var enumeration = ExtensionObject
                .ToEncodeable(encoded) as EnumDefinition;

            Assert.That(enumeration, Is.Not.Null, "The definition of ConveyorState is not an enumeration.");

            var fields = new List<string>();

            foreach (EnumField field in enumeration.Fields)
            {
                fields.Add(field.Name);
            }

            Assert.That(
                fields,
                Is.EqualTo(new[] { "Stopped", "Running", "Faulted" }),
                "The fields of ConveyorState changed.");
        }

        /// <summary>
        /// A reload publishes the next revision: the new nodes appear, the generation
        /// counter moves on, and the Revision property reports the document that is live.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReloadPublishesTheNextRevision(CancellationToken ct)
        {
            long before = await ReadGenerationAsync(ct).ConfigureAwait(false);

            NodeId lineId = await ResolveAsync(ct, ConveyorLine).ConfigureAwait(false);
            Assert.That(
                await BrowseNamesAsync(lineId, ct).ConfigureAwait(false),
                Does.Not.Contain("Conveyor2"),
                "Revision 1 already has the conveyor revision 2 adds.");

            CallMethodResult result = await ReloadAsync("Rev2", ReloadMode.Reload, ct).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good),
                "Reload was refused.");

            lineId = await ResolveAsync(ct, ConveyorLine).ConfigureAwait(false);
            IReadOnlyList<string> children = await BrowseNamesAsync(lineId, ct).ConfigureAwait(false);
            await ReportAsync("ConveyorLine after the reload", children).ConfigureAwait(false);

            await Assert.MultipleAsync(async () => {
                Assert.That(children, Does.Contain("Conveyor2"), "Revision 2 did not reach the address space.");
                Assert.That(
                    await ReadRevisionAsync(ct).ConfigureAwait(false),
                    Is.EqualTo("Rev2"),
                    "The Revision property still reports the old document.");
                Assert.That(
                    await ReadLoadedRevisionAsync(ct).ConfigureAwait(false),
                    Is.EqualTo("Rev2"),
                    "The control model still reports the old revision.");
                Assert.That(
                    await ReadGenerationAsync(ct).ConfigureAwait(false),
                    Is.GreaterThan(before),
                    "The generation of the registration did not move on.");
            });
        }

        /// <summary>
        /// The two reload modes which do not drain the requests in flight publish the same
        /// address space as the plain one.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        [TestCase(ReloadMode.ShadowReload)]
        [TestCase(ReloadMode.ImmediateReload)]
        public async Task EveryReloadModePublishesTheReplacement(ReloadMode mode, CancellationToken ct)
        {
            CallMethodResult result = await ReloadAsync("Rev2", mode, ct).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good),
                $"A {mode} was refused.");

            NodeId lineId = await ResolveAsync(ct, ConveyorLine).ConfigureAwait(false);

            Assert.That(
                await BrowseNamesAsync(lineId, ct).ConfigureAwait(false),
                Does.Contain("Conveyor2"),
                $"A {mode} did not publish revision 2.");
        }

        /// <summary>
        /// Remove takes the model off the server, and Load puts it back. The namespace
        /// stays in the namespace table either way - only the nodes go.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RemoveAndLoadTakeTheModelOffAndPutItBack(CancellationToken ct)
        {
            CallMethodResult removed = await CallControlAsync("Remove", ct).ConfigureAwait(false);

            Assert.That(
                removed.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good),
                "Remove was refused.");

            NodeId gone = await SessionOps
                .ResolveAsync(Session, ct, ConveyorLine)
                .ConfigureAwait(false);

            await Assert.MultipleAsync(async () => {
                Assert.That(gone.IsNull, Is.True, "ConveyorLine is still browsable after Remove.");
                Assert.That(
                    Session.NamespaceUris.GetIndex(LineNamespace),
                    Is.GreaterThanOrEqualTo(0),
                    "Removing the model took its namespace out of the namespace table.");
                Assert.That(
                    await ReadLoadedRevisionAsync(ct).ConfigureAwait(false),
                    Is.Empty,
                    "The control model still reports a loaded revision.");
            });

            CallMethodResult loaded = await CallControlAsync("Load", ct, Variant.From("Rev1"))
                .ConfigureAwait(false);

            Assert.That(
                loaded.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good),
                "Load was refused after a Remove.");

            NodeId back = await ResolveAsync(ct, ConveyorLine).ConfigureAwait(false);

            Assert.That(
                await BrowseNamesAsync(back, ct).ConfigureAwait(false),
                Does.Contain("Conveyor1"),
                "Load did not put the model back.");
        }

        /// <summary>
        /// The two refusals a caller can provoke: a revision the server has no document
        /// for, and a Load while a model is already published.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheControlMethodsRefuseWhatTheyCannotDo(CancellationToken ct)
        {
            CallMethodResult unknown = await CallControlAsync("Load", ct, Variant.From("Rev9"))
                .ConfigureAwait(false);

            CallMethodResult duplicate = await CallControlAsync("Load", ct, Variant.From("Rev2"))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    unknown.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument),
                    "A revision the sample does not ship was accepted.");

                Assert.That(
                    duplicate.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                    "A second Load over a published model was accepted.");
            });
        }

        /// <summary>
        /// The revisions the server has a document for are the ones the control model
        /// advertises.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheControlModelAdvertisesTheRevisionsItHas(CancellationToken ct)
        {
            NodeId revisionsId = await ResolveAsync(ct, ModelControl, Name(ControlNamespace, "AvailableRevisions"))
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, revisionsId, ct).ConfigureAwait(false);

            Assert.That(
                value.WrappedValue.TryGetValue(out ArrayOf<string> revisions)
                    ? revisions.ToArray()
                    : null,
                Is.EqualTo(new[] { "Rev1", "Rev2" }),
                "The advertised revisions changed.");
        }

        /// <summary>
        /// Which reload the control model was asked for. Mirrors the ReloadMode
        /// enumeration of the control NodeSet, which is not compiled anywhere.
        /// </summary>
        public enum ReloadMode
        {
            /// <summary>Drain, then swap.</summary>
            Reload = 0,

            /// <summary>Swap, and let the old generation keep its monitored items.</summary>
            ShadowReload = 1,

            /// <summary>Swap, and invalidate the monitored items of the old generation.</summary>
            ImmediateReload = 2,
        }

        private Task<CallMethodResult> ReloadAsync(string revision, ReloadMode mode, CancellationToken ct)
        {
            return CallControlAsync("Reload", ct, Variant.From(revision), Variant.From((int)mode));
        }

        private async Task<CallMethodResult> CallControlAsync(
            string method,
            CancellationToken ct,
            params Variant[] arguments)
        {
            NodeId controlId = await ResolveAsync(ct, ModelControl).ConfigureAwait(false);
            NodeId methodId = await ResolveFromAsync(controlId, ct, Name(ControlNamespace, method))
                .ConfigureAwait(false);

            return await SessionOps
                .CallAsync(Session, controlId, methodId, ct, arguments)
                .ConfigureAwait(false);
        }

        private async Task<string> ReadLoadedRevisionAsync(CancellationToken ct)
        {
            NodeId nodeId = await ResolveAsync(ct, ModelControl, Name(ControlNamespace, "LoadedRevision"))
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, nodeId, ct).ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out string text) ? text : string.Empty;
        }

        private async Task<long> ReadGenerationAsync(CancellationToken ct)
        {
            NodeId nodeId = await ResolveAsync(ct, ModelControl, Name(ControlNamespace, "Generation"))
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, nodeId, ct).ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out long generation) ? generation : 0;
        }

        private async Task<string> ReadRevisionAsync(CancellationToken ct)
        {
            NodeId nodeId = await ResolveAsync(ct, ConveyorLine, Name(LineNamespace, "Revision"))
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, nodeId, ct).ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out string text) ? text : string.Empty;
        }
    }
}
