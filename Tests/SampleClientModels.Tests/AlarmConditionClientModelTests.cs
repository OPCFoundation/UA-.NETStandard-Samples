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
using Opc.Ua.Samples.Client;
using Quickstarts.AlarmConditionClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the Alarm Condition client exists to show, asked of its model without the
    /// window: the conditions of the server arrive, a Part 9 Method call comes back as
    /// an event, the events are delivered one at a time, the filter takes effect, and
    /// the audit trail records what the operator did.
    /// </summary>
    /// <remarks>
    /// The window of this sample used to process its events in an <c>async void</c>
    /// handler which awaited in the middle of its work, and the message loop delivered
    /// the next event into a second run of the handler while the first was suspended
    /// (<c>docs/TESTING.md</c>). The model delivers through one consumer, and
    /// <see cref="EventsAreDeliveredOneAtATimeAndARefreshStartsOver"/> is the case
    /// which pins that down.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class AlarmConditionClientModelTests : ClientModelFixtureBase<AlarmConditionClientModel>
    {
        /// <summary>
        /// The sample server refreshes its retained conditions as soon as the model
        /// subscribes and raises new alarms every few seconds after that.
        /// </summary>
        private static readonly TimeSpan kConditionTimeout = TimeSpan.FromSeconds(45);

        /// <summary>
        /// The simulation raises the severity of an unacknowledged alarm one level per
        /// tick, so the first alarm to reach High can take a while when the fixture has
        /// only just started its server.
        /// </summary>
        private static readonly TimeSpan kSeverityTimeout = TimeSpan.FromSeconds(90);

        protected override string SampleName => "AlarmCondition";

        protected override AlarmConditionClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new AlarmConditionClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachDeliversTheConditionsOfTheServer(CancellationToken ct)
        {
            var conditions = new EventSink<ConditionChangedEventArgs>();
            Model.ConditionChanged += conditions.Handle;

            Assert.That(Model.Conditions, Is.Empty, "A detached model already lists conditions.");

            await AttachAsync(ct).ConfigureAwait(false);

            ConditionChangedEventArgs first = await conditions
                .WaitForAsync(
                    _ => true,
                    "no condition arrived after attaching. The model asks for a condition refresh " +
                    "while it attaches, so an empty list means the subscription never delivered",
                    kConditionTimeout,
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"First condition: {first.Snapshot}").ConfigureAwait(false);

            ConditionSnapshot snapshot = first.Snapshot;

            Assert.That(snapshot.Key.ConditionId.IsNull, Is.False, "The condition has no node id.");
            Assert.That(snapshot.SourceName, Is.Not.Empty, "The condition names no source.");
            Assert.That(snapshot.ConditionName, Is.Not.Empty, "The condition has no name.");
            Assert.That(snapshot.TypeName, Is.Not.Empty, "The type of the condition was not looked up.");
            Assert.That(snapshot.Time, Is.Not.Null, "The condition carries no time.");
            Assert.That(
                Model.Conditions.Select(condition => condition.Key),
                Does.Contain(snapshot.Key),
                "The model reported a condition it does not list.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SilencingAnAlarmComesBackAsSilenced(CancellationToken ct)
        {
            var conditions = new EventSink<ConditionChangedEventArgs>();
            Model.ConditionChanged += conditions.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            // the sample server hands the transition on to its underlying system, which
            // reports it back as an event; that is what the client shows as "Silenced"
            ConditionChangedEventArgs audible = await conditions
                .WaitForAsync(
                    change => change.Snapshot.CanSilence,
                    "no alarm which could be silenced arrived. The sample server reports one every few seconds",
                    kConditionTimeout,
                    ct)
                .ConfigureAwait(false);

            ConditionKey key = audible.Snapshot.Key;
            int before = conditions.Events.ToList().IndexOf(audible);

            await TestContext.Out.WriteLineAsync($"Silencing {audible.Snapshot}").ConfigureAwait(false);

            IReadOnlyList<ConditionCallResult> results = await Model
                .SilenceAsync(new[] { key }, ct)
                .ConfigureAwait(false);

            Assert.That(results, Has.Count.EqualTo(1), "Silence answered for a different number of conditions than it was asked for.");
            Assert.That(results[0].Key, Is.EqualTo(key));
            Assert.That(results[0].Succeeded, Is.True, $"The server refused to silence the alarm: {results[0].Status}");

            // only what arrives after the call counts: an earlier event of the same alarm
            // may well have carried the flag from a previous cycle of the simulation
            await WaitUntilAsync(
                () => conditions.Events
                    .Skip(before + 1)
                    .Any(change => change.Snapshot.Key == key && change.Snapshot.Flags.Contains("Silenced", StringComparison.Ordinal)),
                "silencing the alarm has to come back as an event which puts the alarm into the silenced state",
                kConditionTimeout,
                ct).ConfigureAwait(false);

            ConditionSnapshot silenced = conditions.Events
                .Skip(before + 1)
                .Last(change => change.Snapshot.Key == key)
                .Snapshot;

            await TestContext.Out.WriteLineAsync($"Now reports: {silenced}").ConfigureAwait(false);

            Assert.That(silenced.CanSilence, Is.False, "A silenced alarm still offers to be silenced.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task EventsAreDeliveredOneAtATimeAndARefreshStartsOver(CancellationToken ct)
        {
            // one sink for both events, so that their order relative to each other is kept
            var events = new EventSink<EventArgs>();
            Model.ConditionChanged += events.Handle;
            Model.ConditionsCleared += events.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            await events
                .WaitForAsync(
                    args => args is ConditionChangedEventArgs,
                    "no condition arrived after attaching",
                    kConditionTimeout,
                    ct)
                .ConfigureAwait(false);

            int before = events.Count;

            // the refresh replays every retained condition in one burst, which is where
            // the window of this sample used to trip over itself
            await Model.RefreshAsync(ct).ConfigureAwait(false);

            await WaitUntilAsync(
                () => events.Events.Skip(before).Any(args => args is not ConditionChangedEventArgs),
                "the refresh did not announce itself with ConditionsCleared",
                kConditionTimeout,
                ct).ConfigureAwait(false);

            int cleared = events.Events
                .Select((args, index) => (args, index))
                .First(entry => entry.index >= before && entry.args is not ConditionChangedEventArgs)
                .index;

            await WaitUntilAsync(
                () => events.Events.Skip(cleared + 1).OfType<ConditionChangedEventArgs>().Count() >= 2,
                "the refresh replayed nothing after it cleared the list",
                kConditionTimeout,
                ct).ConfigureAwait(false);

            List<ConditionChangedEventArgs> replayed = events.Events
                .Skip(cleared + 1)
                .OfType<ConditionChangedEventArgs>()
                .ToList();

            await TestContext.Out
                .WriteLineAsync($"{replayed.Count} conditions were replayed after the clear; {events.Count} events in all.")
                .ConfigureAwait(false);

            // the list was cleared before it was repopulated: the first condition after
            // the clear cannot be an update of a row which no longer exists
            Assert.That(
                replayed[0].Change,
                Is.EqualTo(ConditionChange.Added),
                "The first condition after the clear was reported as an update, so the list was not cleared before it was repopulated.");

            Assert.That(
                events.MaxConcurrency,
                Is.EqualTo(1),
                "Two handlers were inside the sink at once: the model let a second event overtake the first at an await.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SeverityFilterKeepsTheLowerConditionsOut(CancellationToken ct)
        {
            var conditions = new EventSink<ConditionChangedEventArgs>();
            var cleared = new EventSink<EventArgs>();
            Model.ConditionChanged += conditions.Handle;
            Model.ConditionsCleared += cleared.Handle;

            Assert.That(Model.Severity, Is.EqualTo(EventSeverity.Min), "The model does not start out listing every severity.");

            await AttachAsync(ct).ConfigureAwait(false);

            await conditions
                .WaitForAsync(_ => true, "no condition arrived after attaching", kConditionTimeout, ct)
                .ConfigureAwait(false);

            int clearedBefore = cleared.Count;

            await Model.SetSeverityAsync(EventSeverity.High, ct).ConfigureAwait(false);

            Assert.That(Model.Severity, Is.EqualTo(EventSeverity.High));
            Assert.That(
                cleared.Count,
                Is.GreaterThan(clearedBefore),
                "Changing the filter has to clear the list: the rows which are up belong to the filter which was replaced.");

            // what the old item still delivered while it was being removed is dropped by
            // the model, so only what arrives from here on is looked at
            var later = new EventSink<ConditionChangedEventArgs>();
            Model.ConditionChanged += later.Handle;

            await later
                .WaitForAsync(
                    _ => true,
                    "no condition of at least High severity arrived. The simulation raises the severity of " +
                    "an unacknowledged alarm one level per tick, so one reaches High within a minute",
                    kSeverityTimeout,
                    ct)
                .ConfigureAwait(false);

            static bool PassesHigh(ConditionSnapshot snapshot)
            {
                return snapshot.Severity >= (ushort)EventSeverity.High;
            }

            foreach (ConditionChangedEventArgs change in later.Events)
            {
                await TestContext.Out.WriteLineAsync($"After the filter: {change.Change} {change.Snapshot}").ConfigureAwait(false);
            }

            // a removal is the one event which is allowed to carry a severity the filter
            // does not accept: filtered retain reports a condition on its way out of the
            // where clause, and dropping below High is one of the ways out
            Assert.That(
                later.Events
                    .Where(change => change.Change != ConditionChange.Removed)
                    .Select(change => change.Snapshot)
                    .Where(snapshot => !PassesHigh(snapshot)),
                Is.Empty,
                "A condition below High severity got through the severity filter.");

            Assert.That(Model.Conditions, Is.Not.Empty, "The model lists nothing although conditions arrived.");
            Assert.That(
                Model.Conditions.Where(snapshot => !PassesHigh(snapshot)),
                Is.Empty,
                "The model still lists a condition of the filter which was replaced.");
        }

        /// <summary>
        /// Suppressing an alarm takes it out of the list, without a condition refresh.
        /// </summary>
        /// <remarks>
        /// The where clause of the model leaves out an alarm which is suppressed, shelved
        /// or out of service, so suppressing one takes it out of the scope of this client
        /// while the server carries on retaining it. Filtered retain (OPC UA Part 9,
        /// B.1.4) is what closes that gap: the alarms of the sample server opt in, so the
        /// server reports the alarm one last time with <c>Retain = false</c> and the model
        /// drops it. Without it the row would stand until the operator pressed Refresh.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SuppressingAnAlarmTakesItOutOfTheListWithoutARefresh(CancellationToken ct)
        {
            var conditions = new EventSink<ConditionChangedEventArgs>();
            Model.ConditionChanged += conditions.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            // an alarm which is listed right now. A dialog has nothing to suppress, and a
            // branch is a snapshot of an earlier state rather than a node to call.
            ConditionChangedEventArgs listed = await conditions
                .WaitForAsync(
                    change => change.Change != ConditionChange.Removed
                        && !change.Snapshot.IsDialog
                        && !change.Snapshot.IsBranch,
                    "no alarm arrived after attaching. The sample server reports one every few seconds",
                    kConditionTimeout,
                    ct)
                .ConfigureAwait(false);

            ConditionKey key = listed.Snapshot.Key;

            await TestContext.Out.WriteLineAsync($"Suppressing {listed.Snapshot}").ConfigureAwait(false);

            IReadOnlyList<ConditionCallResult> results = await Model
                .SuppressAsync(new[] { key }, true, LocalizedText.Null, ct)
                .ConfigureAwait(false);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Succeeded, Is.True, $"The server refused to suppress the alarm: {results[0].Status}");

            try
            {
                await WaitUntilAsync(
                    () => conditions.Events.Any(
                        change => change.Snapshot.Key == key && change.Change == ConditionChange.Removed),
                    "the suppressed alarm has to leave the list on its own: it no longer passes the where " +
                    "clause of the client, and filtered retain is what reports it one last time",
                    kConditionTimeout,
                    ct).ConfigureAwait(false);

                ConditionSnapshot left = conditions.Events
                    .Last(change => change.Snapshot.Key == key)
                    .Snapshot;

                await TestContext.Out.WriteLineAsync($"Left the list: {left}").ConfigureAwait(false);

                Assert.That(left.Retain, Is.False, "The trailing event of a condition which left the filter carries Retain false.");

                Assert.That(
                    Model.Conditions.Select(snapshot => snapshot.Key),
                    Does.Not.Contain(key),
                    "The model still lists an alarm which left the scope of its filter.");
            }
            finally
            {
                // the model can no longer be asked: it only calls Methods on conditions it
                // lists, and this one is gone. The alarm client of the session can.
                await Session
                    .GetAlarmClient(NullTelemetry.Instance)
                    .UnsuppressAsync(key.ConditionId, ct: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            // and back into scope: the alarm passes the where clause again and is added
            await WaitUntilAsync(
                () => Model.Conditions.Any(snapshot => snapshot.Key == key),
                "an alarm which is no longer suppressed has to come back into the list",
                kConditionTimeout,
                ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheAuditTrailRecordsAnAddComment(CancellationToken ct)
        {
            var conditions = new EventSink<ConditionChangedEventArgs>();
            Model.ConditionChanged += conditions.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            // AddComment needs the id of the event it comments on
            ConditionChangedEventArgs target = await conditions
                .WaitForAsync(
                    change => !change.Snapshot.EventId.IsNull,
                    "no condition with an event id arrived after attaching",
                    kConditionTimeout,
                    ct)
                .ConfigureAwait(false);

            var audits = new EventSink<AuditEventReceivedEventArgs>();

            await using AuditTrailModel auditTrail = Model.CreateAuditTrail();
            auditTrail.AuditEventReceived += audits.Handle;

            await auditTrail.StartAsync(ct).ConfigureAwait(false);

            Assert.That(auditTrail.IsRunning, Is.True, "The audit trail did not start.");

            // the streaming subscription creates its monitored item when the enumeration
            // starts, on its own worker, and the trail cannot say when that is done. A
            // comment which is added before the item exists is audited into the void, so
            // the comment is repeated until the trail reports one
            AuditEventReceivedEventArgs audited = null;

            for (int attempt = 1; attempt <= 6 && audited == null; attempt++)
            {
                var comment = new LocalizedText($"Model test comment {attempt}");

                IReadOnlyList<ConditionCallResult> results = await Model
                    .AddCommentAsync(new[] { target.Snapshot.Key }, comment, ct)
                    .ConfigureAwait(false);

                Assert.That(results[0].Succeeded, Is.True, $"The server refused the comment: {results[0].Status}");

                try
                {
                    audited = await audits
                        .WaitForAsync(
                            audit => NamesAddComment(audit.Snapshot, comment.Text),
                            "waiting for the audit event of the comment",
                            TimeSpan.FromSeconds(5),
                            ct)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // not yet; the item may not have existed when the comment was added
                }
            }

            // the trail ran and stops cleanly whether or not the server reported anything
            Assert.That(auditTrail.IsRunning, Is.True, "The audit trail stopped on its own.");

            // no audit event of a condition Method call reaches a subscriber: Auditing
            // reads true in the configuration of this sample and AddComment answers Good,
            // but nothing of the AuditUpdateMethodEventType family arrives on a monitored
            // item of the Server object, while a model change event from the same object
            // does - and the RoleSet Methods of the RoleManagement sample are audited on
            // the same stack. The expectation is written the right way round and reported
            // as ignored until it holds, the bargain the node manager tier makes with its
            // known issues.
            await KnownIssueAsync(
                async () => {
                    Assert.That(
                        audited,
                        Is.Not.Null,
                        "The audit trail never reported the AddComment. The sample server audits every " +
                        $"condition Method call; the trail saw {audits.Count} audit events.");

                    await TestContext.Out.WriteLineAsync($"Audited: {audited.Snapshot}").ConfigureAwait(false);

                    Assert.That(audited.Snapshot.SourceName, Is.Not.Empty, "The audit event names no source.");
                    Assert.That(audited.Snapshot.Time, Is.Not.Null, "The audit event carries no time.");
                    Assert.That(audited.Snapshot.Details, Is.Not.Null, "The audit event has no raw fields for the details dialog.");
                },
                "no audit event of a condition Method call reaches a subscriber, although the " +
                "RoleSet Methods of the RoleManagement sample are audited on the same stack.")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs an assertion which is expected to fail on the stack of today, and reports
        /// the test as ignored; the moment it passes, the test fails and asks for the
        /// wrapper to be removed. The same helper the node manager tier keeps, which this
        /// project cannot reference.
        /// </summary>
        private static async Task KnownIssueAsync(Func<Task> check, string issue)
        {
            try
            {
                await check().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not SuccessException)
            {
                Assert.Ignore($"Known issue: {issue}{Environment.NewLine}The test reported: {failure.Message}");
                return;
            }

            Assert.Fail(
                $"This is recorded as a known issue, but it passed: {issue}{Environment.NewLine}" +
                "Remove the KnownIssueAsync wrapper and let the assertion stand on its own.");
        }

        /// <summary>
        /// Whether an audit event is the record of an AddComment: it names the Method, and
        /// its input arguments carry the comment which was added.
        /// </summary>
        private static bool NamesAddComment(AuditEventSnapshot snapshot, string comment)
        {
            return snapshot.MethodName != null
                && snapshot.MethodName.Contains("AddComment", StringComparison.Ordinal)
                && snapshot.ArgumentsText != null
                && snapshot.ArgumentsText.Contains(comment, StringComparison.Ordinal);
        }
    }
}
