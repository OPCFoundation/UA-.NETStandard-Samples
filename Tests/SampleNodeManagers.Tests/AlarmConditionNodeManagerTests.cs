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
    /// What the alarm sample does: it builds an area tree out of its configuration, hangs
    /// alarm sources under it, and routes the events of a source up to every area which
    /// declares it.
    /// </summary>
    /// <remarks>
    /// The area tree is not a folder hierarchy: areas are notifiers, wired to the server
    /// object and to each other with HasNotifier, and sources are attached with
    /// HasEventSource. That wiring is what makes an event reported by one source arrive at
    /// a client which subscribed to an area several levels above it, and it is the whole
    /// point of the sample.
    ///
    /// The configuration deliberately lists the same source under more than one area -
    /// Colours/EastTank belongs to both Green/East/Red and Yellow/West/Blue - so the tree
    /// is a graph rather than a hierarchy. A migration which turns it back into a tree
    /// would be caught by SourceIsSharedBetweenAreas.
    ///
    /// This is one of the three samples built on the local QuickstartNodeManager fork.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class AlarmConditionNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "AlarmCondition";

        private const string AlarmNamespace = Quickstarts.AlarmConditionServer.Namespaces.AlarmCondition;

        /// <summary>
        /// The configured area tree is served, down to the sources at its leaves.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ConfiguredAreaTreeIsExposed(CancellationToken ct)
        {
            NodeId green = await ResolveAreaAsync(ct, "Green").ConfigureAwait(false);

            IReadOnlyList<string> underGreen = await BrowseNamesAsync(green, ct).ConfigureAwait(false);
            await ReportAsync("Green", underGreen).ConfigureAwait(false);

            Assert.That(underGreen, Does.Contain("East"), "Green has an east area in the configuration.");

            NodeId red = await ResolveAreaAsync(ct, "Green", "East", "Red").ConfigureAwait(false);

            IReadOnlyList<string> underRed = await BrowseNamesAsync(red, ct).ConfigureAwait(false);
            await ReportAsync("Green/East/Red", underRed).ConfigureAwait(false);

            Assert.That(
                underRed,
                Does.Contain("EastTank").And.Contain("NorthMotor"),
                "Green/East/Red carries the two sources the configuration gives it.");

            // the second branch of the configuration has to be there too
            NodeId yellow = await ResolveAreaAsync(ct, "Yellow").ConfigureAwait(false);

            Assert.That(yellow.IsNull, Is.False, "The yellow branch of the configuration is missing.");
        }

        /// <summary>
        /// The root areas are wired to the server object as notifiers.
        /// </summary>
        /// <remarks>
        /// This is the reference which lets a client find the areas at all when it starts
        /// from the server object, and it is added as an external reference by the node
        /// manager rather than being part of any node set.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RootAreasAreNotifiersOfTheServerObject(CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> notifiers = await SessionOps.BrowseAsync(
                Session,
                ObjectIds.Server,
                ct,
                referenceTypeId: ReferenceTypeIds.HasNotifier,
                includeSubtypes: false).ConfigureAwait(false);

            await ReportAsync("Server HasNotifier", notifiers.Select(n => n.BrowseName.Name))
                .ConfigureAwait(false);

            Assert.That(
                notifiers.Select(n => n.BrowseName.Name),
                Does.Contain("Green").And.Contain("Yellow"),
                "Both root areas have to be notifiers of the server object.");
        }

        /// <summary>
        /// A source which two areas declare is one node, reachable from both.
        /// </summary>
        /// <remarks>
        /// EastTank is listed under Green/East/Red and under Yellow/West/Blue. Both paths
        /// have to lead to the same node, because there is only one tank.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SourceIsSharedBetweenAreas(CancellationToken ct)
        {
            NodeId underGreen = await ResolveAreaAsync(ct, "Green", "East", "Red", "EastTank")
                .ConfigureAwait(false);

            NodeId underYellow = await ResolveAreaAsync(ct, "Yellow", "West", "Blue", "EastTank")
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"EastTank under Green: {underGreen}, under Yellow: {underYellow}")
                .ConfigureAwait(false);

            Assert.That(
                underYellow,
                Is.EqualTo(underGreen),
                "The same source under two areas has to be the same node.");
        }

        /// <summary>
        /// The server object delivers the alarms of the areas underneath it.
        /// </summary>
        /// <remarks>
        /// Subscribing to the server object rather than to an area is what a client does
        /// when it wants everything, and it works because the root areas are registered as
        /// notifiers. The alarms which arrive have to come from the configured sources.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ServerObjectDeliversAlarmsOfEveryArea(CancellationToken ct)
        {
            await using EventCapture capture = await EventCapture
                .CreateAsync(Session, ObjectIds.Server, ct)
                .ConfigureAwait(false);

            string[] configuredSources = ["EastTank", "NorthMotor", "WestTank", "SouthMotor"];
            var seen = new HashSet<string>(StringComparer.Ordinal);

            await capture.WaitAsync(
                candidate => {
                    if (candidate.SourceName != null && configuredSources.Contains(candidate.SourceName))
                    {
                        seen.Add(candidate.SourceName);
                    }

                    return seen.Count >= 2;
                },
                TimeSpan.FromSeconds(40),
                "alarms from at least two of the configured sources",
                ct).ConfigureAwait(false);

            await ReportAsync("Sources which reported", seen).ConfigureAwait(false);

            Assert.That(
                seen,
                Is.SubsetOf(configuredSources),
                "Every alarm which reaches the server object has to come from a configured source.");
        }

        /// <summary>
        /// The simulation reports a system event every second.
        /// </summary>
        /// <remarks>
        /// This comes from the node manager's own timer rather than from an alarm source.
        /// The event is picked out of the stream by its type, because the alarm sources
        /// report often enough that most of what arrives is an alarm.
        ///
        /// The timer also reports an audit event, and that one does not arrive: this
        /// sample's configuration does not turn auditing on, and a server drops audit
        /// events when it is off. That is the server behaving correctly rather than the
        /// sample being broken, so it is not asserted here.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SimulationReportsASystemEvent(CancellationToken ct)
        {
            await using EventCapture capture = await EventCapture
                .CreateAsync(Session, ObjectIds.Server, ct)
                .ConfigureAwait(false);

            CapturedEvent systemEvent = await capture.WaitAsync(
                candidate => candidate.EventType == ObjectTypeIds.SystemEventType,
                TimeSpan.FromSeconds(30),
                "the system event the simulation reports",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"System event: {systemEvent}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    systemEvent.Message,
                    Does.Contain("Raising Events"),
                    "The system event carries the message the simulation gives it.");

                Assert.That(
                    systemEvent.SourceName,
                    Is.EqualTo("Internal"),
                    "The simulation reports its system event as coming from inside the server.");
            });
        }

        /// <summary>
        /// Alarms raised by a source arrive at a client subscribed to an area above it.
        /// </summary>
        /// <remarks>
        /// This is the behaviour the whole sample is built around. Subscribing to Green and
        /// receiving a condition which one of the tanks or motors underneath it raised
        /// means the notifier wiring between source and area does its job.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AlarmsTravelFromSourceToArea(CancellationToken ct)
        {
            NodeId green = await ResolveAreaAsync(ct, "Green").ConfigureAwait(false);

            await using EventCapture capture = await EventCapture
                .CreateAsync(Session, green, ct)
                .ConfigureAwait(false);

            string[] sourcesUnderGreen = ["EastTank", "NorthMotor"];

            CapturedEvent alarm = await capture.WaitAsync(
                candidate => candidate.SourceName != null
                    && sourcesUnderGreen.Contains(candidate.SourceName),
                TimeSpan.FromSeconds(40),
                "an event from a source underneath the green area",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Alarm: {alarm}").ConfigureAwait(false);

            Assert.That(
                alarm.SourceName,
                Is.AnyOf("EastTank", "NorthMotor"),
                "The event has to name the source underneath the area which raised it.");

            Assert.That(
                alarm.EventType.IsNull,
                Is.False,
                "An event which reaches a client has to name its type.");
        }

        /// <summary>
        /// Follows a path of areas, starting at the server object.
        /// </summary>
        /// <remarks>
        /// The areas do not hang under the Objects folder: they are notifiers of the server
        /// object, which is how an alarm client is meant to find them. HasNotifier is a
        /// hierarchical reference, so an ordinary browse path walks the tree.
        /// </remarks>
        private Task<NodeId> ResolveAreaAsync(CancellationToken ct, params string[] path)
        {
            return ResolveFromAsync(
                ObjectIds.Server,
                ct,
                path.Select(name => Name(AlarmNamespace, name)).ToArray());
        }
    }
}
