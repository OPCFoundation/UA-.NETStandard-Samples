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

using ReportBrowseNames = Quickstarts.HistoricalEvents.BrowseNames;
using ReportObjectTypes = Quickstarts.HistoricalEvents.ObjectTypes;

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
    /// This node manager is source generated from the sample's model design and overrides
    /// the async history interface of AsyncCustomNodeManager.
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

        private static int s_backdatedReports;

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

            notifier.WrappedValue.TryGetValue(out byte flags);

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
        /// The generated reports also arrive as live events on the well hierarchy.
        /// </summary>
        /// <remarks>
        /// The wells report through the notifier chain the node manager builds - well to
        /// area to platforms - and the platforms folder reaches the server object because
        /// the model declares it as a notifier below it. The simulation only reports on
        /// monitored wells and ticks every ten seconds, so the wait spans two ticks.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GeneratedReportsAreReportedLive(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            ushort ns = NamespaceIndex(Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);
            NodeId reportType = new(ReportObjectTypes.WellTestReportType, ns);

            await using EventCapture capture = await EventCapture.CreateAsync(
                Session,
                platforms,
                ct,
                reportType,
                [new QualifiedName(ReportBrowseNames.UidWell, ns)]).ConfigureAwait(false);

            CapturedEvent reported = await capture.WaitAsync(
                candidate => !candidate.Field(ReportBrowseNames.UidWell).IsNull,
                TimeSpan.FromSeconds(25),
                "a well test report carrying the well it belongs to",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Event: {reported}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    reported.SourceName,
                    Is.Not.Null.And.Not.Empty,
                    "The report names the well it came from as its source.");

                Assert.That(
                    reported.Field(ReportBrowseNames.UidWell).TryGetValue(out string uidWell) ? uidWell : null,
                    Does.StartWith("Well_"),
                    "The report carries the sample's own well identifier field.");
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
        /// A report written into the event history comes back out of it.
        /// </summary>
        /// <remarks>
        /// The historian provider of the sample implements the write half of Part 11
        /// event history, so a client can backfill reports which were raised while it
        /// was not connected. The filter of the write and of the read that checks it
        /// are the same: the fields of an event travel in the order its select clauses
        /// name them.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnInsertedReportIsReadBackFromHistory(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            EventFilter filter = ReportFilter();
            ByteString eventId = NewEventId();
            DateTime raised = BackdatedTime();

            (StatusCode result, IReadOnlyList<StatusCode> perEvent) = await HistoryOps
                .UpdateEventsAsync(
                    Session,
                    platforms,
                    PerformUpdateType.Insert,
                    filter,
                    [FluidLevelReport(eventId, raised, "Well_24412", 42.0)],
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Inserting a report: {result} / {string.Join(", ", perEvent)}")
                .ConfigureAwait(false);

            Assert.That(
                perEvent[0],
                Is.EqualTo((StatusCode)StatusCodes.GoodEntryInserted),
                "The server has to accept a report which is not in the history yet.");

            HistoryReadOutcome outcome = await HistoryOps
                .ReadEventsAsync(Session, platforms, raised.AddSeconds(-1), raised.AddSeconds(1), 0, filter, ct)
                .ConfigureAwait(false);

            Assert.That(
                Find(outcome, eventId),
                Is.Not.Null,
                "The report which was inserted has to be in the history it was inserted into.");
        }

        /// <summary>
        /// Inserting the same report twice is refused the second time.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task InsertingTheSameReportTwiceIsRefused(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            EventFilter filter = ReportFilter();
            HistoryEventFieldList report = FluidLevelReport(NewEventId(), BackdatedTime(), "Well_48306", 7.0);

            await HistoryOps
                .UpdateEventsAsync(Session, platforms, PerformUpdateType.Insert, filter, [report], ct)
                .ConfigureAwait(false);

            (_, IReadOnlyList<StatusCode> perEvent) = await HistoryOps
                .UpdateEventsAsync(Session, platforms, PerformUpdateType.Insert, filter, [report], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Inserting the same report again: {string.Join(", ", perEvent)}")
                .ConfigureAwait(false);

            Assert.That(
                perEvent[0],
                Is.EqualTo((StatusCode)StatusCodes.BadEntryExists),
                "An event id which is already in the history cannot be inserted again.");
        }

        /// <summary>
        /// A report can be deleted out of the event history again.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnInsertedReportCanBeDeletedAgain(CancellationToken ct)
        {
            NodeId platforms = await ResolvePlatformsAsync(ct).ConfigureAwait(false);

            EventFilter filter = ReportFilter();
            ByteString eventId = NewEventId();
            DateTime raised = BackdatedTime();

            await HistoryOps
                .UpdateEventsAsync(
                    Session,
                    platforms,
                    PerformUpdateType.Insert,
                    filter,
                    [FluidLevelReport(eventId, raised, "Well_86234", 13.0)],
                    ct)
                .ConfigureAwait(false);

            (StatusCode result, IReadOnlyList<StatusCode> perEvent) = await HistoryOps
                .DeleteEventsAsync(Session, platforms, [eventId], ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Deleting the report again: {result} / {string.Join(", ", perEvent)}")
                .ConfigureAwait(false);

            Assert.That(
                perEvent[0],
                Is.EqualTo((StatusCode)StatusCodes.Good),
                "A report which is in the history has to be deletable.");

            HistoryReadOutcome outcome = await HistoryOps
                .ReadEventsAsync(Session, platforms, raised.AddSeconds(-1), raised.AddSeconds(1), 0, filter, ct)
                .ConfigureAwait(false);

            Assert.That(
                Find(outcome, eventId),
                Is.Null,
                "The report which was deleted must be gone from the history.");
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
        /// The server advertises that its event history can be read and written.
        /// </summary>
        /// <remarks>
        /// The roll-up of the registered historian providers only covers the data half
        /// of the HistoryServerCapabilities node, so the sample server sets the event
        /// flags of it once the address space is up. A client reads them to find out
        /// what it may attempt at all.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ServerAdvertisesItsEventHistoryCapabilities(CancellationToken ct)
        {
            foreach (NodeId capability in new[] {
                VariableIds.HistoryServerCapabilities_AccessHistoryEventsCapability,
                VariableIds.HistoryServerCapabilities_InsertEventCapability,
                VariableIds.HistoryServerCapabilities_ReplaceEventCapability,
                VariableIds.HistoryServerCapabilities_UpdateEventCapability,
                VariableIds.HistoryServerCapabilities_DeleteEventCapability,
            })
            {
                DataValue value = await SessionOps
                    .ReadAttributeAsync(Session, capability, Attributes.Value, ct)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"{capability}: {value.WrappedValue}")
                    .ConfigureAwait(false);

                Assert.That(
                    value.WrappedValue.TryGetValue(out bool supported) && supported,
                    Is.True,
                    $"The server has to advertise {capability} for its event history.");
            }
        }

        /// <summary>
        /// The event id of a report from an answer, or null when it is not in it.
        /// </summary>
        private static HistoryEventFieldList Find(HistoryReadOutcome outcome, ByteString eventId)
        {
            return outcome.Events.FirstOrDefault(
                candidate => candidate.EventFields.Count > 0 &&
                    candidate.EventFields[0].TryGetValue(out ByteString candidateId) &&
                    candidateId == eventId);
        }

        /// <summary>
        /// An event id in the sixteen byte form the sample archive keys its reports by.
        /// </summary>
        private static ByteString NewEventId()
        {
            return Guid.NewGuid().ToByteArray().ToByteString();
        }

        /// <summary>
        /// A moment far enough in the past that the reports the simulation generates
        /// while a test runs cannot land on it, and distinct for every report a run
        /// writes so that two of them never collide.
        /// </summary>
        private static DateTime BackdatedTime()
        {
            return new DateTime(2020, 6, 1, 12, 0, 0, DateTimeKind.Utc)
                .AddSeconds(Interlocked.Increment(ref s_backdatedReports));
        }

        /// <summary>
        /// A fluid level test report, with its fields in the order
        /// <see cref="ReportFilter"/> names them.
        /// </summary>
        private HistoryEventFieldList FluidLevelReport(
            ByteString eventId,
            DateTime raised,
            string uidWell,
            double fluidLevel)
        {
            ushort ns = NamespaceIndex(Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

            return new HistoryEventFieldList {
                EventFields = new[] {
                    Variant.From(eventId),
                    Variant.From(new NodeId(ReportObjectTypes.FluidLevelTestReportType, ns)),
                    Variant.From(uidWell),
                    Variant.From((DateTimeUtc)raised),
                    Variant.From(uidWell),
                    Variant.From(uidWell),
                    Variant.From(fluidLevel),
                }.ToArrayOf(),
            };
        }

        /// <summary>
        /// A filter which selects the fields a well test report is written with.
        /// </summary>
        /// <remarks>
        /// The event type has to be among them: it is what tells the archive which of
        /// its report tables an incoming event belongs in.
        /// </remarks>
        private EventFilter ReportFilter()
        {
            ushort ns = NamespaceIndex(Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

            SimpleAttributeOperand[] operands = new[] {
                new QualifiedName(Opc.Ua.BrowseNames.EventId),
                new QualifiedName(Opc.Ua.BrowseNames.EventType),
                new QualifiedName(Opc.Ua.BrowseNames.SourceName),
                new QualifiedName(Opc.Ua.BrowseNames.Time),
                new QualifiedName(ReportBrowseNames.NameWell, ns),
                new QualifiedName(ReportBrowseNames.UidWell, ns),
                new QualifiedName(ReportBrowseNames.FluidLevel, ns),
            }.Select(name => new SimpleAttributeOperand {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                AttributeId = Attributes.Value,
                BrowsePath = new[] { name }.ToArrayOf(),
            }).ToArray();

            return new EventFilter { SelectClauses = operands.ToArrayOf() };
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
