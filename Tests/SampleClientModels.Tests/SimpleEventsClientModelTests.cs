/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Samples.Client;
using Quickstarts.SimpleEvents.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the SimpleEvents client exists to show, asked of its model without the
    /// window: the system cycle events of the server arrive decoded, with their type
    /// resolved to a name, for exactly as long as the model is attached.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class SimpleEventsClientModelTests : ClientModelFixtureBase<SimpleEventsClientModel>
    {
        /// <summary>
        /// The server raises its events on its own simulation timer, so this is generous.
        /// </summary>
        private static readonly TimeSpan kEventTimeout = TimeSpan.FromSeconds(30);

        protected override string SampleName => "SimpleEvents";

        protected override SimpleEventsClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new SimpleEventsClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task FirstEventNamesTheSystemAndItsCycle(CancellationToken ct)
        {
            var events = new EventSink<SimpleEventReceivedEventArgs>();
            Model.EventReceived += events.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            SimpleEventReceivedEventArgs first = await events
                .WaitForAsync(_ => true, "no event arrived", kEventTimeout, ct)
                .ConfigureAwait(false);

            // the sample server raises its cycle events from a source called System
            Assert.Multiple(() => {
                Assert.That(first.Event.SourceName, Is.EqualTo("System"));
                Assert.That(first.Event.CycleId, Is.Not.Null.And.Not.Empty, "The event carries no cycle id.");
                Assert.That(first.Event.Message, Is.Not.Null.And.Not.Empty, "The event carries no message.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task CurrentStepBecomesKnown(CancellationToken ct)
        {
            var events = new EventSink<SimpleEventReceivedEventArgs>();
            Model.EventReceived += events.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            // the step is a structure of the sample's own data type: it only arrives with
            // a name when the generated activators were registered on the session
            SimpleEventReceivedEventArgs stepped = await events
                .WaitForAsync(
                    evt => !string.IsNullOrEmpty(evt.Event.CurrentStep),
                    "no event carried a decoded current step",
                    kEventTimeout,
                    ct)
                .ConfigureAwait(false);

            Assert.That(stepped.Event.TimeUtc, Is.Not.Null, "The event carries no time.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task EventTypeNameIsResolved(CancellationToken ct)
        {
            var events = new EventSink<SimpleEventReceivedEventArgs>();
            Model.EventReceived += events.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            SimpleEventReceivedEventArgs first = await events
                .WaitForAsync(_ => true, "no event arrived", kEventTimeout, ct)
                .ConfigureAwait(false);

            // the model looks the type up in the node cache; a bare node id means the
            // lookup was skipped or failed
            Assert.That(first.Event.EventTypeName, Is.Not.Null.And.Not.Empty);
            Assert.That(
                first.Event.EventTypeName,
                Does.Not.StartWith("ns=").And.Not.StartWith("i="),
                "The event type arrived as a node id instead of as a name.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachStopsTheStreamAndASecondAttachWorks(CancellationToken ct)
        {
            var events = new EventSink<SimpleEventReceivedEventArgs>();
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.EventReceived += events.Handle;
            Model.ConnectionChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await events.WaitForAsync(_ => true, "no event arrived on the first attach", kEventTimeout, ct).ConfigureAwait(false);

            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.IsConnected, Is.False);

            int seen = events.Count;
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

            Assert.That(events.Count, Is.EqualTo(seen), "Events kept arriving after the model was detached.");

            // attaching the same session again registers the generated types a second
            // time, which has to be harmless
            await AttachAsync(ct).ConfigureAwait(false);
            await events.WaitForCountAsync(seen + 1, "no event arrived on the second attach", kEventTimeout, ct).ConfigureAwait(false);

            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached, ConnectionChange.Attached }));
        }
    }
}
