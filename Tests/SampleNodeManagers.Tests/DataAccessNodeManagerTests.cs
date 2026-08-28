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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the data access sample does: it turns a plant hierarchy which lives in an
    /// underlying system into an address space, without keeping a node for any of it.
    /// </summary>
    /// <remarks>
    /// Segments and blocks exist only as paths in the underlying system. The node manager
    /// parses a string node id, asks the system what is at that path and builds a node for
    /// the duration of the operation. The same block appears under several paths, which is
    /// the part of the design a rewrite is most likely to lose, so it is checked here.
    ///
    /// The node manager is built directly on the AsyncCustomNodeManager of the SDK, and
    /// the tag variables route their writes to the underlying system through the
    /// asynchronous write handler.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class DataAccessNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "DataAccess";

        private const string DataAccessNamespace = Quickstarts.DataAccessServer.Namespaces.DataAccess;

        /// <summary>
        /// The segments of the underlying system are browsable from the Objects folder.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SegmentHierarchyIsExposed(CancellationToken ct)
        {
            NodeId factoryId = await ResolveAsync(ct, Segment("Factory")).ConfigureAwait(false);

            IReadOnlyList<string> areas = await BrowseNamesAsync(factoryId, ct).ConfigureAwait(false);

            await ReportAsync("Factory", areas).ConfigureAwait(false);

            Assert.That(
                areas,
                Does.Contain("East").And.Contain("West"),
                "The factory has an east and a west side in the underlying system.");

            NodeId boilerId = await ResolveAsync(ct, Segment("Factory"), Segment("East"), Segment("Boiler1"))
                .ConfigureAwait(false);

            IReadOnlyList<string> blocks = await BrowseNamesAsync(boilerId, ct).ConfigureAwait(false);

            await ReportAsync("Factory/East/Boiler1", blocks).ConfigureAwait(false);

            Assert.That(
                blocks,
                Does.Contain("Pipe1001")
                    .And.Contain("Drum1002")
                    .And.Contain("Pipe1002")
                    .And.Contain("FC1001")
                    .And.Contain("LC1001")
                    .And.Contain("CC1001"),
                "The boiler does not carry the six blocks the underlying system puts under it.");
        }

        /// <summary>
        /// The tags of a flow sensor block are readable.
        /// </summary>
        /// <remarks>
        /// Reading a tag is what forces the node manager to build a block for the duration
        /// of the operation and to walk the component path to the tag inside it, so a good
        /// status code here means the on demand construction works. The engineering unit
        /// comes from the underlying system rather than from the address space, which is
        /// what makes it worth asserting.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task FlowSensorTagsAreReadable(CancellationToken ct)
        {
            NodeId measurementId = Tag("Pipe1001", "Measurement");
            NodeId onlineId = Tag("Pipe1001", "Online");

            DataValue measurement = await SessionOps
                .ReadValueAsync(Session, measurementId, ct)
                .ConfigureAwait(false);

            DataValue online = await SessionOps
                .ReadValueAsync(Session, onlineId, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    StatusCode.IsGood(measurement.StatusCode),
                    Is.True,
                    $"Reading the measurement failed: {measurement.StatusCode}");

                Assert.That(
                    StatusCode.IsGood(online.StatusCode),
                    Is.True,
                    $"Reading the online tag failed: {online.StatusCode}");
            });

            // a tag which the block type does not declare must not appear out of nowhere
            DataValue notATag = await SessionOps
                .ReadValueAsync(Session, Tag("Pipe1001", "SetPoint"), ct)
                .ConfigureAwait(false);

            Assert.That(
                notATag.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                "A flow sensor has no set point, that belongs to a controller.");
        }

        /// <summary>
        /// The analog tag of a flow sensor reports the unit the underlying system gives it.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnalogTagReportsItsEngineeringUnits(CancellationToken ct)
        {
            NodeId unitsId = Tag("Pipe1001", $"Measurement/{BrowseNames.EngineeringUnits}");

            DataValue units = await SessionOps
                .ReadValueAsync(Session, unitsId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(units.StatusCode),
                Is.True,
                $"Reading the engineering units failed: {units.StatusCode}");

            Assert.That(
                units.WrappedValue.AsBoxedObject(),
                Is.InstanceOf<ExtensionObject>(),
                "The engineering units of an analog item are a structure.");

            ((ExtensionObject)units.WrappedValue.AsBoxedObject())
                .TryGetValue(out EUInformation information, Session.MessageContext);

            Assert.That(
                information?.DisplayName.Text,
                Is.EqualTo("liters/sec"),
                "The flow sensor measures in liters per second.");
        }

        /// <summary>
        /// The tags of a block can be discovered by browsing it.
        /// </summary>
        /// <remarks>
        /// A block is built for the duration of an operation and never lives in the address
        /// space, so it has to offer its own children to a browser. Without that a client
        /// which does not already know the tag names cannot find them, which is most of
        /// what this sample exists to demonstrate.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task BlockTagsAreBrowsable(CancellationToken ct)
        {
            NodeId pipeId = await ResolvePipe1001Async(ct).ConfigureAwait(false);

            IReadOnlyList<string> tags = await BrowseNamesAsync(pipeId, ct).ConfigureAwait(false);

            await ReportAsync("Pipe1001", tags).ConfigureAwait(false);

            Assert.That(
                tags,
                Is.EquivalentTo(new[] { "Measurement", "Online" }),
                "A flow sensor has an analog measurement and a digital online tag, and nothing else.");

            // a controller is a different block type, so it has to browse differently
            NodeId controllerId = await ResolveAsync(
                ct,
                Segment("Factory"),
                Segment("East"),
                Segment("Boiler1"),
                Segment("FC1001")).ConfigureAwait(false);

            IReadOnlyList<string> controllerTags = await BrowseNamesAsync(controllerId, ct).ConfigureAwait(false);

            await ReportAsync("FC1001", controllerTags).ConfigureAwait(false);

            Assert.That(
                controllerTags,
                Does.Contain("SetPoint").And.Contain("Measurement").And.Contain("Output").And.Contain("Status"),
                "A controller carries the four tags its block type declares.");
        }

        /// <summary>
        /// A browsed tag is the same node as the one addressed by node id.
        /// </summary>
        /// <remarks>
        /// Discovering a tag is only useful if what comes back can then be read, so this
        /// follows the browse through to a value rather than stopping at the name.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task BrowsedTagIsTheTagWhichCanBeRead(CancellationToken ct)
        {
            NodeId pipeId = await ResolvePipe1001Async(ct).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> tags = await SessionOps
                .BrowseAsync(Session, pipeId, ct)
                .ConfigureAwait(false);

            ReferenceDescription measurement = tags
                .First(tag => tag.BrowseName.Name == "Measurement");

            NodeId browsed = ExpandedNodeId.ToNodeId(measurement.NodeId, Session.NamespaceUris);

            await TestContext.Out
                .WriteLineAsync($"Browsing to the measurement gives {browsed}")
                .ConfigureAwait(false);

            Assert.That(
                browsed,
                Is.EqualTo(Tag("Pipe1001", "Measurement")),
                "The tag a browse returns has to be the tag its node id names.");

            DataValue value = await SessionOps
                .ReadValueAsync(Session, browsed, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading the tag a browse led to failed: {value.StatusCode}");
        }

        /// <summary>
        /// The same block is reachable through the plant path and through the asset path.
        /// </summary>
        /// <remarks>
        /// The underlying system deliberately lists Pipe1001 twice, once under the boiler
        /// it belongs to and once under the sensors it is one of. Both paths have to lead
        /// to the same node, because they are the same block.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task BlockIsReachableThroughBothOfItsPaths(CancellationToken ct)
        {
            NodeId throughTheFactory = await ResolvePipe1001Async(ct).ConfigureAwait(false);

            NodeId throughTheAssets = await ResolveAsync(
                ct,
                Segment("Assets"),
                Segment("Sensors"),
                Segment("Flow"),
                Segment("Pipe1001")).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Factory path: {throughTheFactory}, asset path: {throughTheAssets}")
                .ConfigureAwait(false);

            Assert.That(
                throughTheAssets,
                Is.EqualTo(throughTheFactory),
                "Both paths name the same block, so they have to resolve to the same node.");
        }

        /// <summary>
        /// Subscribing to a tag starts the block and its values begin to move.
        /// </summary>
        /// <remarks>
        /// The node manager keeps one block instance for as long as anybody monitors it,
        /// and tells the underlying system to start and stop it around that. Values which
        /// change while a subscription is open are the observable half of that bookkeeping.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SubscribingToATagStartsTheBlockSimulation(CancellationToken ct)
        {
            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, Tag("Pipe1001", "Measurement"), ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await capture
                .CollectDistinctAsync(2, TimeSpan.FromSeconds(20), ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Measurement",
                values.Select(value => string.Format(CultureInfo.InvariantCulture, "{0}", value.WrappedValue)))
                .ConfigureAwait(false);

            Assert.That(
                values.Select(value => value.StatusCode),
                Is.All.Matches<StatusCode>(StatusCode.IsGood),
                "The block simulation has to report good values.");
        }

        /// <summary>
        /// Writing the set point of a controller reaches the underlying system.
        /// </summary>
        /// <remarks>
        /// The write goes through the asynchronous write handler of the tag variable into
        /// the underlying system, and the simulation never touches writable tags, so a
        /// fresh read - which builds a new block from the system - has to return the
        /// written value. The original value is restored so the fixture stays order
        /// independent.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ControllerSetPointIsWritable(CancellationToken ct)
        {
            NodeId setPointId = Tag("FC1001", "SetPoint");

            DataValue original = await SessionOps
                .ReadValueAsync(Session, setPointId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(original.StatusCode),
                Is.True,
                $"Reading the set point failed: {original.StatusCode}");

            float written = (original.WrappedValue.AsBoxedObject() as float? ?? 0f) + 10f;

            try
            {
                StatusCode result = await SessionOps
                    .WriteValueAsync(Session, setPointId, Variant.From(written), ct)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(result),
                    Is.True,
                    $"Writing the set point failed: {result}");

                DataValue readBack = await SessionOps
                    .ReadValueAsync(Session, setPointId, ct)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"Set point: was {original.WrappedValue}, wrote {written}, read {readBack.WrappedValue}")
                    .ConfigureAwait(false);

                Assert.That(
                    readBack.WrappedValue.AsBoxedObject(),
                    Is.EqualTo(written),
                    "The set point has to come back from the underlying system with the written value.");
            }
            finally
            {
                await SessionOps
                    .WriteValueAsync(Session, setPointId, original.WrappedValue, ct)
                    .ConfigureAwait(false);
            }
        }

        private QualifiedName Segment(string name)
        {
            return Name(DataAccessNamespace, name);
        }

        /// <summary>
        /// The node id of a tag inside a block, built the way the sample builds it.
        /// </summary>
        /// <remarks>
        /// A block node id names the block, and the tag is a component path within it.
        /// Nothing of this exists as a node until somebody asks for it.
        /// </remarks>
        private NodeId Tag(string blockId, string componentPath)
        {
            var parsed = new Opc.Ua.Server.ParsedNodeId {
                NamespaceIndex = NamespaceIndex(DataAccessNamespace),
                RootType = Quickstarts.DataAccessServer.ModelUtils.Block,
                RootId = blockId,
                ComponentPath = componentPath,
            };

            return parsed.Construct();
        }

        private Task<NodeId> ResolvePipe1001Async(CancellationToken ct)
        {
            return ResolveAsync(
                ct,
                Segment("Factory"),
                Segment("East"),
                Segment("Boiler1"),
                Segment("Pipe1001"));
        }
    }
}
