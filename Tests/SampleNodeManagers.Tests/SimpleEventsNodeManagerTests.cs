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

using EventBrowseNames = Quickstarts.SimpleEvents.BrowseNames;
using EventObjectTypes = Quickstarts.SimpleEvents.ObjectTypes;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the simple events sample does: it declares an event type of its own and
    /// reports it on a timer.
    /// </summary>
    /// <remarks>
    /// The simulation reports two events every three seconds and has been running since
    /// the server started, so nothing here assumes it sees the first one. Each event
    /// carries a cycle number which counts up, and that number also appears in the
    /// message, which is what lets the counter be checked even though the field which is
    /// supposed to carry it is currently empty - see CustomEventFieldsAreReported.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class SimpleEventsNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "SimpleEvents";

        private const string SimpleEventsNamespace = Quickstarts.SimpleEvents.Namespaces.SimpleEvents;

        /// <summary>
        /// The type the sample declares is in the address space, with its own fields.
        /// </summary>
        /// <remarks>
        /// The fields are spread over two levels of the type hierarchy, which is how the
        /// model was written: the cycle id and the current step belong to the status event,
        /// and only the list of steps is added by the started event.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EventTypeDeclaresTheSamplesOwnFields(CancellationToken ct)
        {
            IReadOnlyList<string> onStarted = await BrowseNamesAsync(CycleStartedType, ct)
                .ConfigureAwait(false);

            NodeId statusType = new(EventObjectTypes.SystemCycleStatusEventType, SimpleEventsIndex);

            IReadOnlyList<string> onStatus = await BrowseNamesAsync(statusType, ct).ConfigureAwait(false);

            await ReportAsync("SystemCycleStartedEventType", onStarted).ConfigureAwait(false);
            await ReportAsync("SystemCycleStatusEventType", onStatus).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    onStarted,
                    Does.Contain(EventBrowseNames.Steps),
                    "The started event declares the list of steps.");

                Assert.That(
                    onStatus,
                    Does.Contain(EventBrowseNames.CycleId).And.Contain(EventBrowseNames.CurrentStep),
                    "The status event declares the cycle id and the current step.");
            });
        }

        /// <summary>
        /// The simulation reports the sample's own event type on the server object.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SystemCycleStartedEventIsReported(CancellationToken ct)
        {
            await using EventCapture capture = await SubscribeAsync(ct).ConfigureAwait(false);

            CapturedEvent reported = await capture.WaitAsync(
                candidate => candidate.EventType == CycleStartedType,
                TimeSpan.FromSeconds(20),
                "a system cycle started event",
                ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Event: {reported}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    reported.SourceName,
                    Is.EqualTo("System"),
                    "The sample reports its cycle events as coming from the system.");

                Assert.That(
                    reported.Message,
                    Does.Contain("has started"),
                    "The message comes from the translation info the sample builds.");
            });
        }

        /// <summary>
        /// The simulation reports events of two different severities.
        /// </summary>
        /// <remarks>
        /// The loop in the simulation runs twice per tick and uses the loop counter as the
        /// severity, so a client sees the two alternating. It is the only thing which
        /// distinguishes the two events of one tick from each other today.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SimulationReportsBothSeverities(CancellationToken ct)
        {
            await using EventCapture capture = await SubscribeAsync(ct).ConfigureAwait(false);

            var severities = new HashSet<ushort>();

            CapturedEvent last = await capture.WaitAsync(
                candidate => {
                    if (candidate.EventType == CycleStartedType
                        && candidate.Field(Opc.Ua.BrowseNames.Severity).AsBoxedObject() is ushort severity)
                    {
                        severities.Add(severity);
                    }

                    return severities.Count >= 2;
                },
                TimeSpan.FromSeconds(25),
                "cycle events of two different severities",
                ct).ConfigureAwait(false);

            await ReportAsync("Severities", severities.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);

            Assert.That(last, Is.Not.Null);

            Assert.That(
                severities,
                Is.EquivalentTo(new ushort[] { 1, 2 }),
                "The simulation reports one event of each severity per tick.");
        }

        /// <summary>
        /// The cycle counter moves on from one tick of the simulation to the next.
        /// </summary>
        /// <remarks>
        /// The counter is read out of the message, because the field which is meant to
        /// carry it is empty today. That makes this test a slightly awkward way of
        /// checking the counter, but it does check it, and it will keep working once the
        /// field is filled in again.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CycleCounterAdvances(CancellationToken ct)
        {
            await using EventCapture capture = await SubscribeAsync(ct).ConfigureAwait(false);

            CapturedEvent first = await capture.WaitAsync(
                candidate => CycleNumberOf(candidate) != null,
                TimeSpan.FromSeconds(20),
                "a cycle event whose message names its cycle",
                ct).ConfigureAwait(false);

            int firstCycle = CycleNumberOf(first).Value;

            CapturedEvent later = await capture.WaitAsync(
                candidate => CycleNumberOf(candidate) > firstCycle,
                TimeSpan.FromSeconds(20),
                "a cycle event from a later cycle",
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Cycles: {firstCycle} then {CycleNumberOf(later)}")
                .ConfigureAwait(false);

            Assert.That(
                CycleNumberOf(later),
                Is.GreaterThan(firstCycle),
                "The cycle counter has to count up.");
        }

        /// <summary>
        /// The events ought to carry the sample's own fields, and today they do not.
        /// </summary>
        /// <remarks>
        /// The type model is complete: the cycle id, the current step and the list of steps
        /// are all declared, which EventTypeDeclaresTheSamplesOwnFields checks. The events
        /// themselves arrive with those fields empty, so the one thing this sample exists
        /// to demonstrate - an event with fields of its own - does not reach a client.
        ///
        /// This is not the node manager getting the event wrong, which is worth knowing
        /// before anybody migrates it. The simulation was building an event that had none
        /// of these fields on it at all, and that part is fixed: the event is now created
        /// from its type model, so the fields exist, carry their browse names and hold
        /// their values. Asking the event object itself to resolve `2:CycleId` in process,
        /// through the very method the server uses to apply an event filter, returns the
        /// value. The server accepts the select clauses without complaint - the filter
        /// result it returns for the monitored item is empty - and then delivers a null for
        /// every one of them.
        ///
        /// So the field is on the event, resolves against the event, and is asked for by a
        /// clause the server accepted, and it still arrives empty. Whatever is dropping it
        /// sits below this sample. The standard fields of the same event, selected the same
        /// way, arrive normally.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task CustomEventFieldsAreReported(CancellationToken ct)
        {
            return KnownIssueAsync(
                async () => {
                    await using EventCapture capture = await SubscribeAsync(ct).ConfigureAwait(false);

                    CapturedEvent reported = await capture.WaitAsync(
                        candidate => candidate.EventType == CycleStartedType
                            && candidate.Field(EventBrowseNames.CycleId).AsBoxedObject() != null,
                        TimeSpan.FromSeconds(15),
                        "a cycle event carrying a cycle id",
                        ct).ConfigureAwait(false);

                    Assert.That(
                        reported.Field(EventBrowseNames.CycleId).AsBoxedObject() as string,
                        Is.Not.Null.And.Not.Empty,
                        "The cycle id is a field of the sample's own event type.");
                },
                "the events arrive with the sample's own fields empty. The fields are on the " +
                "event and resolve against it in process, and the server accepted the select " +
                "clauses which ask for them, so this is below the sample rather than in it.");
        }

        private ushort SimpleEventsIndex => NamespaceIndex(SimpleEventsNamespace);

        private NodeId CycleStartedType
            => new(EventObjectTypes.SystemCycleStartedEventType, SimpleEventsIndex);

        /// <summary>
        /// The cycle number the message of an event names, if it names one.
        /// </summary>
        private static int? CycleNumberOf(CapturedEvent reported)
        {
            string message = reported.Message;

            if (message == null)
            {
                return null;
            }

            int open = message.IndexOf('\'');
            int close = open < 0 ? -1 : message.IndexOf('\'', open + 1);

            if (open < 0 || close < 0)
            {
                return null;
            }

            return int.TryParse(
                message.AsSpan(open + 1, close - open - 1),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int cycle)
                ? cycle
                : null;
        }

        private Task<EventCapture> SubscribeAsync(CancellationToken ct)
        {
            ushort ns = SimpleEventsIndex;

            return EventCapture.CreateAsync(
                Session,
                ObjectIds.Server,
                ct,
                CycleStartedType,
                [new QualifiedName(EventBrowseNames.CycleId, ns)],
                [new QualifiedName(EventBrowseNames.CurrentStep, ns)],
                [new QualifiedName(EventBrowseNames.Steps, ns)]);
        }
    }
}
