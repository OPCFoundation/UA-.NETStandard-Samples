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
    /// What the historical events sample does: it keeps a history of events rather than of
    /// values, and serves it through the history services.
    /// </summary>
    /// <remarks>
    /// Reading events from history is a different service path from reading values, with
    /// its own continuation points and its own filter, and this is the only sample which
    /// implements it. The sample also declares which parts of the path it does not
    /// implement, and answering an unimplemented request properly is behaviour worth
    /// keeping: a client which is told "not implemented" can fall back, one which gets an
    /// unexpected error cannot.
    ///
    /// This node manager is built on the local QuickstartNodeManager fork.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class HistoricalEventsNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "HistoricalEvents";

        private static readonly DateTime HistoryStart = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime HistoryEnd = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The areas and the wells underneath them are served.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AreaAndWellTreeIsExposed(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            IReadOnlyList<string> areas = await BrowseNamesAsync(platforms, ct).ConfigureAwait(false);

            await ReportAsync("Platforms", areas).ConfigureAwait(false);

            Assert.That(areas, Is.Not.Empty, "The sample serves no areas at all.");

            NodeId area = await ChildAsync(platforms, areas[0], ct).ConfigureAwait(false);

            IReadOnlyList<string> wells = await BrowseNamesAsync(area, ct).ConfigureAwait(false);

            await ReportAsync($"Platforms/{areas[0]}", wells).ConfigureAwait(false);

            Assert.That(wells, Is.Not.Empty, $"The area {areas[0]} has no wells.");
        }

        /// <summary>
        /// The platforms object announces that its events can be read from history.
        /// </summary>
        /// <remarks>
        /// The event notifier attribute is how a client learns that history is on offer at
        /// all, and the sample sets the history bits on it by hand.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task PlatformsAnnounceThatTheirEventsAreHistorized(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            DataValue notifier = await SessionOps
                .ReadAttributeAsync(Session, platforms, Attributes.EventNotifier, ct)
                .ConfigureAwait(false);

            byte flags = Convert.ToByte(
                notifier.WrappedValue.AsBoxedObject(),
                System.Globalization.CultureInfo.InvariantCulture);

            await TestContext.Out
                .WriteLineAsync($"EventNotifier of the platforms object: {flags}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    flags & EventNotifiers.SubscribeToEvents,
                    Is.Not.Zero,
                    "The platforms object has to accept event subscriptions.");

                Assert.That(
                    flags & EventNotifiers.HistoryRead,
                    Is.Not.Zero,
                    "The platforms object has to offer its event history.");
            });
        }

        /// <summary>
        /// The generated reports can be read out of the event history.
        /// </summary>
        /// <remarks>
        /// The simulation reports one every ten seconds, so the first read may well come
        /// too early and is retried until the archive has something in it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EventHistoryReturnsTheGeneratedReports(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            HistoryReadOutcome outcome = await Poll.UntilNoThrowAsync(
                token => HistoryOps.ReadEventsAsync(
                    Session, platforms, HistoryStart, HistoryEnd, 0, StandardFilter(), token),
                result => result.Events.Count > 0,
                "the event history to hold a generated report",
                timeout: TimeSpan.FromSeconds(40),
                ct: ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The event history holds {outcome.Events.Count} events.")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(outcome.StatusCode),
                Is.True,
                $"Reading the event history failed: {outcome.StatusCode}");

            Assert.That(
                outcome.Events[0].EventFields.ToArray(),
                Is.Not.Empty,
                "An event from history has to carry the fields which were selected.");
        }

        /// <summary>
        /// A read which asks for fewer events than there are is continued.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EventReadIsContinuedInSteps(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            // wait until there is more than one event to page through
            await Poll.UntilNoThrowAsync(
                token => HistoryOps.ReadEventsAsync(
                    Session, platforms, HistoryStart, HistoryEnd, 0, StandardFilter(), token),
                result => result.Events.Count > 1,
                "the event history to hold more than one report",
                timeout: TimeSpan.FromSeconds(40),
                ct: ct).ConfigureAwait(false);

            HistoryReadOutcome first = await HistoryOps
                .ReadEventsAsync(Session, platforms, HistoryStart, HistoryEnd, 1, StandardFilter(), ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Asking for one event returned {first.Events.Count}, continued: {first.HasMore}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    first.Events,
                    Has.Count.EqualTo(1),
                    "The server has to honour the requested number of events.");

                Assert.That(
                    first.HasMore,
                    Is.True,
                    "There are more events, so the server has to continue.");
            });

            HistoryReadOutcome next = await HistoryOps
                .ReadEventsAsync(
                    Session, platforms, HistoryStart, HistoryEnd, 1, StandardFilter(), ct,
                    continuationPoint: first.ContinuationPoint)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(next.StatusCode),
                Is.True,
                $"Continuing the event read failed: {next.StatusCode}");
        }

        /// <summary>
        /// Writing to the event history is refused as not implemented.
        /// </summary>
        /// <remarks>
        /// The sample validates the request and then says plainly that it does not do this,
        /// which is what lets a client tell "not supported" apart from "went wrong".
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task UpdatingTheEventHistoryIsNotImplemented(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            var details = new UpdateEventDetails {
                NodeId = platforms,
                PerformInsertReplace = PerformUpdateType.Insert,
                Filter = StandardFilter(),
                EventData = ArrayOf<HistoryEventFieldList>.Empty,
            };

            HistoryUpdateResponse response = await Session
                .HistoryUpdateAsync(null, new List<ExtensionObject> { new(details) }, ct)
                .ConfigureAwait(false);

            StatusCode result = response.Results.ToList()[0].StatusCode;

            await TestContext.Out
                .WriteLineAsync($"Updating the event history: {result}")
                .ConfigureAwait(false);

            Assert.That(
                result,
                Is.EqualTo((StatusCode)StatusCodes.BadNotImplemented),
                "The sample says plainly that it does not write event history.");
        }

        /// <summary>
        /// Deleting an event which is not in the history is refused.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DeletingAnUnknownEventIsRefused(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            var madeUp = new ByteString(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            (StatusCode result, IReadOnlyList<StatusCode> perEvent) = await HistoryOps
                .DeleteEventsAsync(Session, platforms, [madeUp], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Deleting an unknown event: {result} / {string.Join(", ", perEvent)}")
                .ConfigureAwait(false);

            Assert.That(
                perEvent,
                Is.Not.Empty,
                "The server has to report per event what became of the request.");

            Assert.That(
                perEvent[0],
                Is.EqualTo((StatusCode)StatusCodes.BadEventIdUnknown),
                "An event id which is not in the history cannot be deleted.");
        }

        /// <summary>
        /// The root of the well hierarchy, whatever the model spells it.
        /// </summary>
        /// <remarks>
        /// Found by browsing rather than by name, because the model spells it "Plaforms",
        /// and a migration which corrects that typo should not break these tests.
        /// </remarks>
        private async Task<NodeId> ResolvePlatformsAsync(CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);

            ushort ns = NamespaceIndex(Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

            ReferenceDescription root = children
                .FirstOrDefault(child => child.BrowseName.NamespaceIndex == ns);

            Assert.That(
                root,
                Is.Not.Null,
                "The sample serves nothing of its own under the Objects folder. It serves: " +
                string.Join(", ", children.Select(child => child.BrowseName.Name)));

            return ExpandedNodeId.ToNodeId(root.NodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// A filter which selects the fields every event has.
        /// </summary>
        private static EventFilter StandardFilter()
        {
            SimpleAttributeOperand[] operands = new[] {
                Opc.Ua.BrowseNames.EventId,
                Opc.Ua.BrowseNames.EventType,
                Opc.Ua.BrowseNames.SourceName,
                Opc.Ua.BrowseNames.Time,
            }.Select(name => new SimpleAttributeOperand {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                AttributeId = Attributes.Value,
                BrowsePath = new[] { new QualifiedName(name) }.ToArrayOf(),
            }).ToArray();

            return new EventFilter { SelectClauses = operands.ToArrayOf() };
        }
    }
}
