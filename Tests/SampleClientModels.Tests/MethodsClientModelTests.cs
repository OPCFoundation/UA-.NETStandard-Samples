/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Quickstarts.MethodsClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the Methods client exists to show, asked of its model without the window: the
    /// process of the server is found, its Start method answers with the states it
    /// accepted, and the state it then ramps through arrives over the subscription.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class MethodsClientModelTests : ClientModelFixtureBase<MethodsClientModel>
    {
        private static readonly TimeSpan kStateTimeout = TimeSpan.FromSeconds(30);

        protected override string SampleName => "Methods";

        protected override MethodsClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new MethodsClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachResolvesTheProcessAndItsStartMethod(CancellationToken ct)
        {
            Assert.That(Model.ProcessNodeId.IsNull, Is.True, "A detached model already knows the process.");

            await AttachAsync(ct).ConfigureAwait(false);

            // the model resolves its nodes by browse path from its built-in knowledge of the
            // model; the test walks the same path by browsing, so the two have to agree
            NodeId process = await ResolveAsync(ct, Name(MethodsClientModel.MethodsNamespaceUri, "My Process")).ConfigureAwait(false);
            NodeId state = await ResolveAsync(ct, Name(MethodsClientModel.MethodsNamespaceUri, "My Process"), Name(MethodsClientModel.MethodsNamespaceUri, "State")).ConfigureAwait(false);
            NodeId start = await ResolveAsync(ct, Name(MethodsClientModel.MethodsNamespaceUri, "My Process"), Name(MethodsClientModel.MethodsNamespaceUri, "Start")).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.ProcessNodeId, Is.EqualTo(process), "The model did not resolve the process object.");
                Assert.That(Model.StateNodeId, Is.EqualTo(state), "The model did not resolve the state variable.");
                Assert.That(Model.StartMethodNodeId, Is.EqualTo(start), "The model did not resolve the Start method.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartReturnsTheRevisedStates(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            StartResult result = await Model.StartAsync(1, 100, ct).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null, "The Start method did not answer with its two output arguments.");

            // the revised values echo the accepted arguments
            Assert.Multiple(() => {
                Assert.That(result.RevisedInitialState, Is.EqualTo(1u));
                Assert.That(result.RevisedFinalState, Is.EqualTo(100u));
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartReportsTheStateThroughTheSubscription(CancellationToken ct)
        {
            var states = new EventSink<StateChangedEventArgs>();
            Model.StateChanged += states.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            const uint final = 20;

            StartResult result = await Model.StartAsync(1, final, ct).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);

            // the process moves one step per second, so the state passes through every
            // value up to the final one and the subscription reports each of them
            StateChangedEventArgs reached = await states
                .WaitForAsync(
                    state => state.Value.WrappedValue.TryGetValue(out uint value) && value == final,
                    $"the state never reached {final}",
                    kStateTimeout,
                    ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(reached.Value.StatusCode), Is.True, "The state arrived with a bad status.");
            Assert.That(states.Count, Is.GreaterThan(1), "Only one value arrived, so the ramp was not reported.");
        }

        [Test]
        public void StartBeforeAttachThrows()
        {
            Assert.That(
                async () => await Model.StartAsync(1, 100).ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>(),
                "A detached model has no session to call the method on.");
        }
    }
}
