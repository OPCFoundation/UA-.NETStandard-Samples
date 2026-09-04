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
using Opc.Ua.Client.Alarms;

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
    /// The node manager is a hand-written FluentNodeManagerBase of the SDK: the areas and
    /// sources are predefined nodes, and the builder only adds the notifier link to the
    /// server object and the one second cycle of the simulation.
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
        /// A source of type "Colours": three alarms, of which "Green" is the trip alarm.
        /// It does not run the FirstInGroup pattern, so its alarms are only ever suppressed
        /// by something a test did.
        /// </summary>
        private const string kColoursSource = "EastTank";
        private const string kLevelAlarm = "Red";
        private const string kColoursTripAlarm = "Green";

        /// <summary>
        /// A source of type "Metals", whose group is led by its trip alarm "Bronze".
        /// </summary>
        private const string kMetalsSource = "WestTank";
        private const string kMetalsTripAlarm = "Bronze";

        /// <summary>
        /// The dialog condition every source carries, and the responses it offers.
        /// </summary>
        private const string kOnlineDialog = "OnlineState";
        private const int kOnline = 0;
        private const int kOffline = 1;

        private const string kAlarmGroup = "Alarms";
        private const string kAlarmMetrics = "AlarmMetrics";
        private const string kMaintenanceMode = "MaintenanceMode";

        // the two state variables of a condition all carry their boolean in an "Id" child,
        // so a selected field is named after the whole path rather than its last element
        private const string kActive = BrowseNames.ActiveState + "/" + BrowseNames.Id;
        private const string kAcked = BrowseNames.AckedState + "/" + BrowseNames.Id;
        private const string kLatched = BrowseNames.LatchedState + "/" + BrowseNames.Id;
        private const string kSilenced = BrowseNames.SilenceState + "/" + BrowseNames.Id;
        private const string kSuppressed = BrowseNames.SuppressedState + "/" + BrowseNames.Id;
        private const string kOutOfService = BrowseNames.OutOfServiceState + "/" + BrowseNames.Id;

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
        /// A condition refresh replays the retained conditions between its markers.
        /// </summary>
        /// <remarks>
        /// Every source creates a dialog condition which stays retained because nothing in
        /// this fixture ever answers it, so a refresh must always replay one dialog per
        /// configured source between the RefreshStart and RefreshEnd events. This is how an
        /// alarm client synchronizes its condition list after connecting, and the half of
        /// the event path which runs through ConditionRefreshAsync.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ConditionRefreshReplaysRetainedConditions(CancellationToken ct)
        {
            await using EventCapture capture = await EventCapture
                .CreateAsync(Session, ObjectIds.Server, ct)
                .ConfigureAwait(false);

            CallMethodResult result = await SessionOps.CallAsync(
                Session,
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh,
                ct,
                Variant.From(capture.SubscriptionId)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"The ConditionRefresh call failed: {result.StatusCode}");

            await capture.WaitAsync(
                candidate => candidate.EventType == ObjectTypeIds.RefreshStartEventType,
                TimeSpan.FromSeconds(30),
                "the refresh start marker",
                ct).ConfigureAwait(false);

            // everything between the markers is the refresh; live alarms may interleave,
            // so only the dialogs are counted, and they identify their source by name
            var betweenMarkers = new List<CapturedEvent>();
            CapturedEvent next;

            while ((next = await capture.NextAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false)).EventType
                != ObjectTypeIds.RefreshEndEventType)
            {
                betweenMarkers.Add(next);
            }

            await ReportAsync(
                "Events replayed by the refresh",
                betweenMarkers.Select(replayed => replayed.ToString())).ConfigureAwait(false);

            var dialogs = betweenMarkers
                .Where(replayed => replayed.EventType == ObjectTypeIds.DialogConditionType
                    && replayed.SourceName != null)
                .Select(replayed => replayed.SourceName)
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(
                dialogs,
                Is.SupersetOf(new[] { "EastTank", "NorthMotor", "WestTank", "SouthMotor" }),
                "The refresh has to replay the retained dialog condition of every configured source.");
        }

        /// <summary>
        /// The generated ConditionType proxy drives a condition refresh, and reports a
        /// rejected call as an exception.
        /// </summary>
        /// <remarks>
        /// This is the call machinery the alarm sample client now uses for every condition
        /// method it offers - enable, disable, comment, acknowledge, confirm, shelve and
        /// respond all go through the same generated wrapper. What is worth pinning down is
        /// both halves of it: a good call reaches the server object it names, and a bad one
        /// arrives as a ServiceResultException rather than as a status code nobody looks at,
        /// because that is what the client turns into the status column of a condition.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GeneratedConditionProxyRefreshesAndReportsFailures(CancellationToken ct)
        {
            await using EventCapture capture = await EventCapture
                .CreateAsync(Session, ObjectIds.Server, ct)
                .ConfigureAwait(false);

            var conditions = new ConditionTypeClient(
                Session,
                ObjectTypeIds.ConditionType,
                NullTelemetry.Instance);

            await conditions.ConditionRefreshAsync(capture.SubscriptionId, ct).ConfigureAwait(false);

            CapturedEvent marker = await capture.WaitAsync(
                candidate => candidate.EventType == ObjectTypeIds.RefreshStartEventType,
                TimeSpan.FromSeconds(30),
                "the refresh start marker of a refresh called through the generated proxy",
                ct).ConfigureAwait(false);

            Assert.That(marker, Is.Not.Null);

            // no subscription has the id zero, so the server has to reject this one
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(
                async () => await conditions.ConditionRefreshAsync(0, ct).ConfigureAwait(false));

            await TestContext.Out
                .WriteLineAsync($"Rejected refresh: {rejected.StatusCode}")
                .ConfigureAwait(false);

            Assert.That(
                rejected.StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid),
                "A refresh for a subscription which does not exist has to surface as an error.");
        }

        /// <summary>
        /// Every alarm carries the Part 9 states and the Methods which drive them.
        /// </summary>
        /// <remarks>
        /// These are optional children of AlarmConditionType, so a client can only use
        /// Silence, Suppress or RemoveFromService on an alarm which has both the state and
        /// the Method. Latching is deliberately not universal: only the trip alarm of a
        /// source keeps asking for attention after the process condition is gone, so only
        /// it has a LatchedState and a Reset.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AlarmsCarryThePart9StatesAndMethods(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            NodeId levelAlarm = await ChildAsync(tank, kLevelAlarm, ct).ConfigureAwait(false);
            IReadOnlyList<string> parts = await BrowseNamesAsync(levelAlarm, ct).ConfigureAwait(false);
            await ReportAsync($"{kLevelAlarm} of {kColoursSource}", parts).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    parts,
                    Is.SupersetOf(new[] {
                        BrowseNames.SuppressedState,
                        BrowseNames.SilenceState,
                        BrowseNames.OutOfServiceState,
                        BrowseNames.ShelvingState,
                        BrowseNames.AudibleEnabled,
                        BrowseNames.AudibleSound,
                        BrowseNames.ReAlarmTime,
                        BrowseNames.ReAlarmRepeatCount,
                        BrowseNames.FirstInGroupFlag,
                    }),
                    "The alarm has to carry the optional Part 9 states.");

                Assert.That(
                    parts,
                    Is.SupersetOf(new[] {
                        BrowseNames.Silence,
                        BrowseNames.Suppress,
                        BrowseNames.Unsuppress,
                        BrowseNames.RemoveFromService,
                        BrowseNames.PlaceInService,
                        BrowseNames.GetGroupMemberships,
                    }),
                    "A state without its Method cannot be driven by a client.");

                Assert.That(
                    parts,
                    Does.Not.Contain(BrowseNames.LatchedState),
                    "A level alarm follows its process value and does not latch.");
            });

            NodeId tripAlarm = await ChildAsync(tank, kColoursTripAlarm, ct).ConfigureAwait(false);
            IReadOnlyList<string> tripParts = await BrowseNamesAsync(tripAlarm, ct).ConfigureAwait(false);
            await ReportAsync($"{kColoursTripAlarm} of {kColoursSource}", tripParts).ConfigureAwait(false);

            Assert.That(
                tripParts,
                Is.SupersetOf(new[] { BrowseNames.LatchedState, BrowseNames.Reset }),
                "The trip alarm latches, so it has to offer a Reset.");
        }

        /// <summary>
        /// A source exposes its alarm group, its metrics and the flag which suppresses it.
        /// </summary>
        /// <remarks>
        /// The group is what makes the alarms of a source addressable as a set. Its members
        /// are AlarmGroupMember references, and reading them back is what the
        /// GetGroupMemberships Method of an alarm does - so calling it on a member has to
        /// name the group the member was added to.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SourceExposesItsAlarmGroupMetricsAndMaintenanceFlag(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            IReadOnlyList<string> children = await BrowseNamesAsync(tank, ct).ConfigureAwait(false);
            await ReportAsync(kColoursSource, children).ConfigureAwait(false);

            Assert.That(
                children,
                Is.SupersetOf(new[] { kAlarmGroup, kAlarmMetrics, kMaintenanceMode }),
                "The source has to carry the group, the metrics and the maintenance flag.");

            NodeId group = await ChildAsync(tank, kAlarmGroup, ct).ConfigureAwait(false);
            NodeId levelAlarm = await ChildAsync(tank, kLevelAlarm, ct).ConfigureAwait(false);

            ArrayOf<NodeId> memberships = await Session
                .GetAlarmClient(NullTelemetry.Instance)
                .GetGroupMembershipsAsync(levelAlarm, ct)
                .ConfigureAwait(false);

            await ReportAsync(
                $"Groups of {kLevelAlarm}",
                memberships.ToArray().Select(membership => membership.ToString())).ConfigureAwait(false);

            Assert.That(
                memberships.ToArray(),
                Does.Contain(group),
                "An alarm which was added to the group of its source has to report that group.");
        }

        /// <summary>
        /// Writing the maintenance flag of a source suppresses every alarm it owns.
        /// </summary>
        /// <remarks>
        /// This is the AlarmSuppressionGroup pattern: a process value the server watches
        /// decides that the alarms of a group are not worth annunciating. The suppression
        /// engine of the stack owns the decision; what a client sees is SuppressedState
        /// turning true on every member and turning back off when the flag is cleared.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task MaintenanceModeSuppressesTheAlarmsOfItsSource(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            NodeId maintenanceMode = await ChildAsync(tank, kMaintenanceMode, ct).ConfigureAwait(false);

            await using EventCapture capture = await CaptureConditionStatesAsync(ct).ConfigureAwait(false);

            try
            {
                StatusCode wrote = await SessionOps
                    .WriteValueAsync(Session, maintenanceMode, Variant.From(true), ct)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(wrote),
                    Is.True,
                    $"The maintenance flag of a source has to be writable: {wrote}");

                CapturedEvent suppressed = await capture.WaitAsync(
                    candidate => candidate.SourceName == kColoursSource && IsTrue(candidate, kSuppressed),
                    TimeSpan.FromSeconds(30),
                    "a suppressed alarm of the source which was put into maintenance",
                    ct).ConfigureAwait(false);

                await TestContext.Out.WriteLineAsync($"Suppressed: {suppressed}").ConfigureAwait(false);
            }
            finally
            {
                await SessionOps
                    .WriteValueAsync(Session, maintenanceMode, Variant.From(false), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            CapturedEvent released = await capture.WaitAsync(
                candidate => candidate.SourceName == kColoursSource && IsFalse(candidate, kSuppressed),
                TimeSpan.FromSeconds(30),
                "the alarms of the source coming back out of maintenance",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Released: {released}").ConfigureAwait(false);
        }

        /// <summary>
        /// An operator can silence the audible annunciation of an alarm.
        /// </summary>
        /// <remarks>
        /// Silence is the simplest of the Part 9 Methods the stack implements for an alarm:
        /// the sample only creates the state and the Method node and hands the transition
        /// on to its underlying system in the veto delegate. Nothing in the sample writes
        /// SilenceState itself.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SilencingAnAlarmIsReportedToClients(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            NodeId levelAlarm = await ChildAsync(tank, kLevelAlarm, ct).ConfigureAwait(false);

            await using EventCapture capture = await CaptureConditionStatesAsync(ct).ConfigureAwait(false);

            await Session
                .GetAlarmClient(NullTelemetry.Instance)
                .SilenceAsync(levelAlarm, ct)
                .ConfigureAwait(false);

            CapturedEvent silenced = await capture.WaitAsync(
                candidate => candidate.SourceName == kColoursSource && IsTrue(candidate, kSilenced),
                TimeSpan.FromSeconds(30),
                "the alarm reporting that it was silenced",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Silenced: {silenced}").ConfigureAwait(false);
        }

        /// <summary>
        /// An alarm which is removed from service reports itself as suppressed or shelved.
        /// </summary>
        /// <remarks>
        /// Part 9 5.8.2 folds OutOfServiceState into SuppressedOrShelved, which is the
        /// single flag a client filters on. Placing the alarm back in service has to clear
        /// it again, and that only works because the stack recomputes the flag from all
        /// three of suppressed, shelved and out of service rather than just remembering it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RemovingAnAlarmFromServiceMarksItSuppressedOrShelved(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            NodeId levelAlarm = await ChildAsync(tank, kLevelAlarm, ct).ConfigureAwait(false);

            AlarmClient alarms = Session.GetAlarmClient(NullTelemetry.Instance);

            await using EventCapture capture = await CaptureConditionStatesAsync(ct).ConfigureAwait(false);

            try
            {
                await alarms.RemoveFromServiceAsync(levelAlarm, ct: ct).ConfigureAwait(false);

                CapturedEvent outOfService = await capture.WaitAsync(
                    candidate => candidate.SourceName == kColoursSource
                        && IsTrue(candidate, kOutOfService)
                        && IsTrue(candidate, BrowseNames.SuppressedOrShelved),
                    TimeSpan.FromSeconds(30),
                    "an alarm which reports itself out of service and therefore suppressed",
                    ct).ConfigureAwait(false);

                await TestContext.Out.WriteLineAsync($"Out of service: {outOfService}").ConfigureAwait(false);
            }
            finally
            {
                await alarms
                    .PlaceInServiceAsync(levelAlarm, ct: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            CapturedEvent inService = await capture.WaitAsync(
                candidate => candidate.SourceName == kColoursSource
                    && IsFalse(candidate, kOutOfService)
                    && IsFalse(candidate, BrowseNames.SuppressedOrShelved),
                TimeSpan.FromSeconds(30),
                "the alarm coming back into service",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"In service: {inService}").ConfigureAwait(false);
        }

        /// <summary>
        /// An alarm which stays active and unacknowledged asks again.
        /// </summary>
        /// <remarks>
        /// The stack offers ProcessReAlarm but no timer; the schedule belongs to the
        /// application, and this sample drives it from the same cycle which runs its
        /// simulation. What a client sees is the repeat count climbing and the alarm asking
        /// for an acknowledgement it had already been given.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnUnacknowledgedAlarmRepeatsItself(CancellationToken ct)
        {
            await using EventCapture capture = await CaptureConditionStatesAsync(ct).ConfigureAwait(false);

            CapturedEvent repeated = await capture.WaitAsync(
                candidate => candidate.Field(BrowseNames.ReAlarmRepeatCount)
                    .TryGetValue(out short repeats) && repeats > 0,
                TimeSpan.FromSeconds(90),
                "an alarm which repeated because nobody acknowledged it",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Re-alarmed: {repeated}").ConfigureAwait(false);
        }

        /// <summary>
        /// A trip alarm stays retained after it goes inactive, and only a Reset clears it.
        /// </summary>
        /// <remarks>
        /// This is the whole of Part 9 4.8 in one test. The latch is set by the activation,
        /// survives the alarm going away, keeps the alarm retained so a refresh replays it,
        /// and is cleared by Reset - which the stack refuses until the alarm is inactive,
        /// acknowledged and confirmed.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ATripAlarmLatchesUntilItIsReset(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            NodeId tripAlarm = await ChildAsync(tank, kColoursTripAlarm, ct).ConfigureAwait(false);

            AlarmClient alarms = Session.GetAlarmClient(NullTelemetry.Instance);

            await using EventCapture capture = await CaptureConditionStatesAsync(ct).ConfigureAwait(false);

            bool IsTheTrip(CapturedEvent candidate)
                => candidate.SourceName == kColoursSource
                    && candidate.Field(BrowseNames.ConditionName).TryGetValue(out string name)
                    && name == kColoursTripAlarm;

            // the latch is set by the activation, so wait for one rather than for the alarm
            // which happens to be active when the subscription is created
            CapturedEvent active = await capture.WaitAsync(
                candidate => IsTheTrip(candidate)
                    && IsTrue(candidate, kActive)
                    && IsTrue(candidate, kLatched),
                TimeSpan.FromSeconds(60),
                "the trip alarm going active and latching",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Latched: {active}").ConfigureAwait(false);

            // resetting a latch which the process still holds has to be refused
            ServiceResultException tooEarly = Assert.ThrowsAsync<ServiceResultException>(
                async () => await alarms.ResetAsync(tripAlarm, ct: ct).ConfigureAwait(false));

            Assert.That(
                tooEarly.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState),
                "An alarm which is still active must not let go of its latch.");

            // acknowledge and confirm; confirming is what makes the simulation drop the trip
            await alarms
                .AcknowledgeAsync(tripAlarm, EventIdOf(active), new LocalizedText("Seen"), ct)
                .ConfigureAwait(false);

            CapturedEvent acknowledged = await capture.WaitAsync(
                candidate => IsTheTrip(candidate) && IsTrue(candidate, kAcked),
                TimeSpan.FromSeconds(30),
                "the trip alarm reporting the acknowledgement",
                ct).ConfigureAwait(false);

            await alarms
                .ConfirmAsync(tripAlarm, EventIdOf(acknowledged), new LocalizedText("Fixed"), ct)
                .ConfigureAwait(false);

            CapturedEvent inactive = await capture.WaitAsync(
                candidate => IsTheTrip(candidate) && IsFalse(candidate, kActive),
                TimeSpan.FromSeconds(30),
                "the trip alarm going inactive after it was confirmed",
                ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    IsTrue(inactive, kLatched),
                    Is.True,
                    "An inactive latching alarm stays latched until it is reset.");

                Assert.That(
                    IsTrue(inactive, BrowseNames.Retain),
                    Is.True,
                    "A latched alarm stays retained, so a condition refresh replays it.");
            });

            // an inactive, acknowledged and confirmed latch satisfies everything Part 9 asks
            // of a Reset, which is what lets the veto of the sample be seen at all: it
            // refuses to clear a trip while the source cannot confirm that the trip is gone
            NodeId dialog = await ChildAsync(tank, kOnlineDialog, ct).ConfigureAwait(false);

            await alarms.RespondAsync(dialog, kOffline, ct).ConfigureAwait(false);

            ServiceResultException vetoed = Assert.ThrowsAsync<ServiceResultException>(
                async () => await alarms.ResetAsync(tripAlarm, ct: ct).ConfigureAwait(false));

            await TestContext.Out
                .WriteLineAsync($"Reset while offline: {vetoed.StatusCode}")
                .ConfigureAwait(false);

            Assert.That(
                vetoed.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied),
                "The sample refuses a reset while the source is offline, and the refusal has to " +
                "reach the client as the status of the call.");

            // the dialog arms itself again after every answer, so the source can come back
            await alarms.RespondAsync(dialog, kOnline, ct).ConfigureAwait(false);

            await alarms.ResetAsync(tripAlarm, ct: ct).ConfigureAwait(false);

            CapturedEvent reset = await capture.WaitAsync(
                candidate => IsTheTrip(candidate) && IsFalse(candidate, kLatched),
                TimeSpan.FromSeconds(30),
                "the trip alarm letting go of its latch",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Reset: {reset}").ConfigureAwait(false);
        }

        /// <summary>
        /// The trip alarm of a metal source suppresses the alarms which follow it.
        /// </summary>
        /// <remarks>
        /// The FirstInGroup pattern of Part 9: the alarm which leads its group carries the
        /// flag, and while it is active the consequences it drags along are suppressed so
        /// that an operator is not flooded with them. Only the metal sources run it - the
        /// trip alarm of this simulation is active most of the time, and a server which ran
        /// the pattern everywhere would leave a client with the default filter looking at
        /// an almost empty list.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheLeadingAlarmOfAMetalSourceSuppressesItsFollowers(CancellationToken ct)
        {
            await using EventCapture capture = await CaptureConditionStatesAsync(ct).ConfigureAwait(false);

            CapturedEvent leading = await capture.WaitAsync(
                candidate => candidate.SourceName == kMetalsSource
                    && candidate.Field(BrowseNames.ConditionName).TryGetValue(out string name)
                    && name == kMetalsTripAlarm
                    && IsTrue(candidate, BrowseNames.FirstInGroupFlag),
                TimeSpan.FromSeconds(60),
                "the trip alarm of a metal source taking the lead of its group",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Leading: {leading}").ConfigureAwait(false);

            CapturedEvent follower = await capture.WaitAsync(
                candidate => candidate.SourceName == kMetalsSource
                    && candidate.Field(BrowseNames.ConditionName).TryGetValue(out string name)
                    && name != kMetalsTripAlarm
                    && IsTrue(candidate, kSuppressed),
                TimeSpan.FromSeconds(60),
                "an alarm which the leading alarm of its group suppressed",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Follower: {follower}").ConfigureAwait(false);
        }

        /// <summary>
        /// The alarm metrics of a source report the rate its alarms are activating at.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AlarmMetricsReportTheRateOfTheSource(CancellationToken ct)
        {
            NodeId tank = await ResolveAreaAsync(ct, "Green", "East", "Red", kColoursSource)
                .ConfigureAwait(false);

            NodeId metrics = await ChildAsync(tank, kAlarmMetrics, ct).ConfigureAwait(false);
            NodeId alarmCount = await ChildAsync(metrics, BrowseNames.AlarmCount, ct).ConfigureAwait(false);
            NodeId currentRate = await ChildAsync(metrics, BrowseNames.CurrentAlarmRate, ct)
                .ConfigureAwait(false);

            // the metrics are filled in by the simulation cycle of the server, so the
            // first values arrive a tick after the address space does
            DataValue count = await Poll.UntilAsync(
                token => SessionOps.ReadValueAsync(Session, alarmCount, token),
                value => value.WrappedValue.TryGetValue(out uint alarms) && alarms > 0,
                "the metrics of the source to count the alarms it owns",
                ct: ct).ConfigureAwait(false);

            DataValue rate = await SessionOps.ReadValueAsync(Session, currentRate, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"AlarmCount={count.Value}, CurrentAlarmRate={rate.Value}")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(rate.StatusCode),
                Is.True,
                $"The current alarm rate has to be readable: {rate.StatusCode}");
        }

        /// <summary>
        /// Subscribes to the conditions of the whole server with the Part 9 state fields.
        /// </summary>
        private Task<EventCapture> CaptureConditionStatesAsync(CancellationToken ct)
        {
            return EventCapture.CreateAsync(
                Session,
                ObjectIds.Server,
                ct,
                ObjectTypeIds.ConditionType,
                [new QualifiedName(BrowseNames.ConditionName)],
                [new QualifiedName(BrowseNames.Retain)],
                [new QualifiedName(BrowseNames.SuppressedOrShelved)],
                [new QualifiedName(BrowseNames.FirstInGroupFlag)],
                [new QualifiedName(BrowseNames.ReAlarmRepeatCount)],
                [new QualifiedName(BrowseNames.ActiveState), new QualifiedName(BrowseNames.Id)],
                [new QualifiedName(BrowseNames.AckedState), new QualifiedName(BrowseNames.Id)],
                [new QualifiedName(BrowseNames.LatchedState), new QualifiedName(BrowseNames.Id)],
                [new QualifiedName(BrowseNames.SilenceState), new QualifiedName(BrowseNames.Id)],
                [new QualifiedName(BrowseNames.SuppressedState), new QualifiedName(BrowseNames.Id)],
                [new QualifiedName(BrowseNames.OutOfServiceState), new QualifiedName(BrowseNames.Id)]);
        }

        /// <summary>
        /// The identifier of an event, as the condition Methods want it.
        /// </summary>
        private static ByteString EventIdOf(CapturedEvent captured)
        {
            return captured.Field(BrowseNames.EventId).TryGetValue(out ByteString eventId)
                ? eventId
                : default;
        }

        /// <summary>
        /// Whether a boolean field of an event is present and true.
        /// </summary>
        /// <remarks>
        /// The two state variables are selected through their Id child, which is what
        /// carries the boolean; the field is named after the state it belongs to below.
        /// </remarks>
        private static bool IsTrue(CapturedEvent captured, string field)
        {
            return captured.Field(field).TryGetValue(out bool value) && value;
        }

        /// <summary>
        /// Whether a boolean field of an event is present and false.
        /// </summary>
        private static bool IsFalse(CapturedEvent captured, string field)
        {
            return captured.Field(field).TryGetValue(out bool value) && !value;
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
