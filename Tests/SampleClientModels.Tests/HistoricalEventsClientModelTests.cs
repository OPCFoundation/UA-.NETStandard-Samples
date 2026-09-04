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
using Opc.Ua.Samples.Client;
using Quickstarts;
using Quickstarts.HistoricalEvents.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the Historical Events client exists to show, asked of its model without the
    /// window: the well test reports of the platforms arrive live and are read back from
    /// the history, page by page, with the texts the list shows already computed.
    /// </summary>
    /// <remarks>
    /// The sample server generates a report every ten seconds, so the waits are generous.
    /// The area is found by browsing for the child the model spells "Plaforms"; naming the
    /// generated node ids would collide with the server assembly (CS0433).
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class HistoricalEventsClientModelTests : ClientModelFixtureBase<HistoricalEventsClientModel>
    {
        private static readonly TimeSpan kEventTimeout = TimeSpan.FromSeconds(45);

        // the whole archive: the sample server wants both bounds of the window
        private static readonly DateTime kHistoryStart = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime kHistoryEnd = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        protected override string SampleName => "HistoricalEvents";

        protected override HistoricalEventsClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new HistoricalEventsClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachingSubscribedDeliversLiveReports(CancellationToken ct)
        {
            var events = new EventSink<EventReceivedEventArgs>();
            var filters = new EventSink<FilterChangedEventArgs>();
            Model.EventReceived += events.Handle;
            Model.FilterChanged += filters.Handle;

            // the menu of the window is checked by default, and the model remembers the
            // choice until there is a session to apply it to
            await Model.SetSubscribedAsync(true, ct).ConfigureAwait(false);

            Assert.That(Model.IsSubscribed, Is.True);
            Assert.That(Model.Filter, Is.Null, "A detached model already has a filter.");

            await AttachAsync(ct).ConfigureAwait(false);

            NodeId platforms = await PathAsync(ct, "Plaforms").ConfigureAwait(false);

            Assert.That(Model.AreaId, Is.EqualTo(platforms), "The first session did not pick the platforms as the area.");
            Assert.That(Model.Filter, Is.Not.Null, "The first session did not pick the default filter.");

            int shownFields = Model.Filter.Fields.Count(field => field.DisplayInList);

            Assert.That(shownFields, Is.GreaterThan(0), "The default filter shows no field at all.");
            Assert.That(filters.Count, Is.GreaterThanOrEqualTo(1), "The filter was picked without being reported.");
            Assert.That(filters.Events[^1].ColumnNames, Is.EqualTo(Model.ColumnNames));
            Assert.That(Model.ColumnNames.Count, Is.EqualTo(shownFields));

            EventReceivedEventArgs live = await events
                .WaitForAsync(candidate => candidate.IsLive, "no live report arrived", kEventTimeout, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync("Live event: " + string.Join(" | ", live.Record.DisplayTexts))
                .ConfigureAwait(false);

            Assert.That(
                live.Record.Fields.Count,
                Is.EqualTo(Model.Filter.Fields.Count + 1),
                "The fields of an event are the node id followed by one value per field of the filter.");
            Assert.That(
                live.Record.DisplayTexts.Count,
                Is.EqualTo(shownFields),
                "The model has to compute one text per column the list shows.");
            Assert.That(events.MaxConcurrency, Is.EqualTo(1), "Events were delivered concurrently.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task HistoryIsReadPageByPage(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            NodeId platforms = Model.AreaId;
            FilterDeclaration filter = Model.Filter;
            int shownFields = filter.Fields.Count(field => field.DisplayInList);

            // the archive fills as the simulation runs, so the first read may come too early
            EventHistoryPage page = await Poll.UntilAsync(
                token => Model.ReadHistoryAsync(platforms, filter, new EventHistoryRequest(kHistoryStart, kHistoryEnd, 10), token),
                candidate => candidate.Events.Count >= 2,
                "the event history to hold two generated reports",
                kEventTimeout,
                ct: ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync("First event: " + string.Join(" | ", page.Events[0].DisplayTexts))
                .ConfigureAwait(false);

            Assert.That(page.Events.Select(record => record.DisplayTexts.Count), Has.All.EqualTo(shownFields));

            // the Time field is the DateTimeUtc path of the display texts: it is rendered as
            // a local time of day, never as an empty string or a raw value
            int timeColumn = Model.ColumnNames.ToList().IndexOf("Time");

            Assert.That(timeColumn, Is.GreaterThanOrEqualTo(0), "The default filter does not show the Time of the event.");
            Assert.That(page.Events[0].DisplayTexts[timeColumn], Does.Match(@"^\d{2}:\d{2}:\d{2}\.\d{3}$"));

            if (page.HasMore)
            {
                await Model.ReleaseContinuationPointAsync(page.Continuation, ct).ConfigureAwait(false);
            }

            // a page of one leaves the server holding the rest behind a continuation point
            EventHistoryPage first = await Model
                .ReadHistoryAsync(platforms, filter, new EventHistoryRequest(kHistoryStart, kHistoryEnd, 1), ct)
                .ConfigureAwait(false);

            Assert.That(first.Events, Has.Count.EqualTo(1));
            Assert.That(first.HasMore, Is.True, "A read of one event out of at least two did not hand back a continuation point.");

            EventHistoryPage next = await Model.ReadNextAsync(first.Continuation, ct).ConfigureAwait(false);

            Assert.That(next.Events, Has.Count.EqualTo(1));
            Assert.That(next.Events[0].Fields[0], Is.Not.EqualTo(first.Events[0].Fields[0]), "The next page repeated the first event.");

            if (next.HasMore)
            {
                await Model.ReleaseContinuationPointAsync(next.Continuation, ct).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheFirstEventTimeIsSane(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            DateTime started = DateTime.UtcNow;

            // the read throws BadNoDataAvailable until the simulation has archived a report
            DateTime first = await Poll.UntilNoThrowAsync(
                token => Model.ReadFirstEventTimeAsync(Model.AreaId, token),
                _ => true,
                "the archive to hold a first event",
                kEventTimeout,
                ct: ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"First event at {first:O}").ConfigureAwait(false);

            Assert.That(first, Is.GreaterThan(new DateTime(2000, 1, 1)), "The first event time is the default of an empty field.");
            Assert.That(first, Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(1)), "The first event lies in the future.");
            Assert.That(first, Is.LessThanOrEqualTo(started.AddSeconds(kEventTimeout.TotalSeconds + 60)));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachStopsTheEvents(CancellationToken ct)
        {
            var events = new EventSink<EventReceivedEventArgs>();
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.EventReceived += events.Handle;
            Model.ConnectionChanged += changes.Handle;

            await Model.SetSubscribedAsync(true, ct).ConfigureAwait(false);
            await AttachAsync(ct).ConfigureAwait(false);
            await events.WaitForAsync(candidate => candidate.IsLive, "no live report arrived", kEventTimeout, ct).ConfigureAwait(false);

            await Model.DetachAsync().ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.IsConnected, Is.False);
            Assert.That(Model.IsSubscribed, Is.True, "Detaching forgot the choice to subscribe.");
            Assert.That(Model.Filter, Is.Not.Null, "Detaching forgot the filter the user chose.");

            int seen = events.Count;
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            Assert.That(events.Count, Is.EqualTo(seen), "Events kept arriving after the model was detached.");
            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached }));
        }
    }
}
