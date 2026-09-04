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
using Quickstarts.EmptyClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The Empty client is the template a new sample is copied from, and its model has
    /// nothing of its own. What is under test here is therefore the base class every model
    /// inherits: the attach and detach lifecycle, the reconnect entry points, and the way
    /// events reach the thread the model was created on.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class EmptyClientModelTests : ClientModelFixtureBase<EmptyClientModel>
    {
        protected override string SampleName => "Empty";

        protected override EmptyClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new EmptyClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachExposesTheSessionAndRaisesAttached(CancellationToken ct)
        {
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.ConnectionChanged += changes.Handle;

            Assert.That(Model.IsConnected, Is.False, "A freshly created model claims to be connected.");
            Assert.That(Model.Session, Is.Null);

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(Model.Session, Is.SameAs(Session), "The model does not hold the session it was given.");
            Assert.That(Model.IsReconnecting, Is.False);
            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] { ConnectionChange.Attached }),
                "Attaching has to report exactly one Attached.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachIsIdempotentAndReportsOnce(CancellationToken ct)
        {
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.ConnectionChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.IsConnected, Is.False);
            Assert.That(Model.Session, Is.Null);
            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached }),
                "A second detach must not report anything.");

            // and a model can be attached again, which is what a reconnect through the
            // connect control does after a disconnect
            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(changes.Events.Last().Change, Is.EqualTo(ConnectionChange.Attached));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReconnectEntryPointsFlipTheStateAndReport(CancellationToken ct)
        {
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.ConnectionChanged += changes.Handle;

            // before an attach the entry points have nothing to report on
            Model.NotifyReconnectStarting();
            await Model.NotifyReconnectCompletedAsync(ct).ConfigureAwait(false);

            Assert.That(changes.Count, Is.Zero, "A detached model reported a reconnect.");

            await AttachAsync(ct).ConfigureAwait(false);

            Model.NotifyReconnectStarting();

            Assert.That(Model.IsReconnecting, Is.True);
            Assert.That(Model.IsConnected, Is.True, "A reconnecting model still holds its session.");

            await Model.NotifyReconnectCompletedAsync(ct).ConfigureAwait(false);

            Assert.That(Model.IsReconnecting, Is.False);
            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] {
                    ConnectionChange.Attached,
                    ConnectionChange.ReconnectStarting,
                    ConnectionChange.ReconnectCompleted,
                }));
        }

        /// <summary>
        /// The one thing a window relies on and no other test here can see: events are
        /// posted to the context the model was created on, never raised on the thread of
        /// the operation. A recording context stands in for the message loop.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EventsArePostedToTheContextTheModelWasCreatedOn(CancellationToken ct)
        {
            var context = new RecordingSynchronizationContext();
            SynchronizationContext previous = SynchronizationContext.Current;

            EmptyClientModel model;

            SynchronizationContext.SetSynchronizationContext(context);

            try
            {
                model = new EmptyClientModel(NullTelemetry.Instance);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            await using (model.ConfigureAwait(false))
            {
                var changes = new EventSink<ConnectionChangedEventArgs>();
                model.ConnectionChanged += changes.Handle;

                await model.AttachAsync(Session, ct).ConfigureAwait(false);
                await model.DetachAsync().ConfigureAwait(false);

                Assert.That(context.Posted, Is.EqualTo(2), "Every event has to go through Post on the captured context.");
                Assert.That(context.Sent, Is.Zero, "Send would deadlock a window which is blocking on the teardown.");
                Assert.That(
                    changes.Events.Select(change => change.Change),
                    Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached }));
            }
        }

        /// <summary>
        /// Counts the posts and runs them inline, which is enough to prove the model went
        /// through the context rather than around it.
        /// </summary>
        private sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            private int m_posted;
            private int m_sent;

            public int Posted => m_posted;

            public int Sent => m_sent;

            public override void Post(SendOrPostCallback d, object state)
            {
                Interlocked.Increment(ref m_posted);
                d(state);
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                Interlocked.Increment(ref m_sent);
                d(state);
            }
        }
    }
}
