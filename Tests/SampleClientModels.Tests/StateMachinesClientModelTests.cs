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
using Quickstarts.StateMachines.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The model of the StateMachines client, driven the way its window drives it.
    /// </summary>
    /// <remarks>
    /// The two machines keep their state across sessions, so every test starts by driving
    /// them back to Off and Ready over a plain session, the way the node manager tests do.
    /// The model is attached only afterwards, so what it reads on attach is the initial
    /// state the sample documents.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class StateMachinesClientModelTests : ClientModelFixtureBase<StateMachinesClientModel>
    {
        private static readonly TimeSpan kTransitionTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Two publishing intervals of the subscription the model creates.
        /// </summary>
        private static readonly TimeSpan kQuietPeriod = TimeSpan.FromSeconds(3);

        protected override string SampleName => "StateMachines";

        protected override StateMachinesClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new StateMachinesClientModel(telemetry);
        }

        [SetUp]
        public async Task ResetMachinesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;

            NodeId interlockId = await PathAsync(ct, "Machine", "SafetyInterlockClear").ConfigureAwait(false);

            StatusCode status = await SessionOps
                .WriteValueAsync(Session, interlockId, Variant.From(true), ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(status), Is.True, $"Closing the interlock answered {status}.");

            await DriveOperationToOffAsync(ct).ConfigureAwait(false);
            await DriveProgramToReadyAsync(ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachReadsTheInitialStatesAndTheCausesTheyPermit(CancellationToken ct)
        {
            Assert.That(Model.OperationState, Is.Null, "A detached model already knows a state.");
            Assert.That(
                Model.PermittedCauses.IsPermitted(OperationCause.Start),
                Is.True,
                "Until the attributes were read every cause counts as permitted.");

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.OperationState?.State, Is.EqualTo("Off"), "The Operation machine starts in Off.");
                Assert.That(Model.ProgramState?.State, Is.EqualTo("Ready"), "The Program machine starts in Ready.");
                Assert.That(Model.InterlockClear, Is.True, "The interlock was closed before the attach.");
            });

            // PermittedCauses is final when the attach returns: the window offers the
            // buttons right after it, without another round trip
            PermittedCauses causes = Model.PermittedCauses;

            Assert.Multiple(() => {
                Assert.That(causes.IsPermitted(OperationCause.PowerOn), Is.True, "PowerOn is declared for Off.");
                Assert.That(causes.IsPermitted(OperationCause.Start), Is.False, "Start is not declared for Off.");
                Assert.That(causes.IsPermitted(OperationCause.Stop), Is.False, "Stop is not declared for Off.");
                Assert.That(causes.IsPermitted(ProgramCause.Start), Is.True, "Start is declared for Ready.");
                Assert.That(causes.IsPermitted(ProgramCause.Suspend), Is.False, "Suspend is not declared for Ready.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task PowerOnReportsTheTransitionAndOffersStart(CancellationToken ct)
        {
            var transitions = new EventSink<TransitionObservedEventArgs>();
            var causes = new EventSink<PermittedCausesChangedEventArgs>();
            Model.TransitionObserved += transitions.Handle;
            Model.PermittedCausesChanged += causes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            int causesSeen = causes.Count;

            await Model.CallOperationCauseAsync(OperationCause.PowerOn, ct).ConfigureAwait(false);

            // the call re-reads the causes before it returns, so the caller sees the
            // consequence of its own action without waiting for the stream
            Assert.Multiple(() => {
                Assert.That(Model.PermittedCauses.IsPermitted(OperationCause.Start), Is.True, "Start is declared for Idle.");
                Assert.That(Model.PermittedCauses.IsPermitted(OperationCause.PowerOn), Is.False, "PowerOn is not declared for Idle.");
                Assert.That(causes.Count, Is.GreaterThan(causesSeen), "The re-read was not reported.");
            });

            TransitionObservedEventArgs observed = await transitions
                .WaitForAsync(
                    transition => transition.Snapshot.Machine == StateMachineKind.Operation
                        && transition.Snapshot.State == "Idle",
                    "the stream did not report the transition to Idle",
                    kTransitionTimeout,
                    ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(observed.Snapshot.Transition, Is.Not.Empty, "A transition names how the machine got there.");
                Assert.That(Model.OperationState.State, Is.EqualTo("Idle"), "The property follows the stream.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task GuardRefusesStartWhileTheInterlockIsOpen(CancellationToken ct)
        {
            var transitions = new EventSink<TransitionObservedEventArgs>();
            Model.TransitionObserved += transitions.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            await Model.CallOperationCauseAsync(OperationCause.PowerOn, ct).ConfigureAwait(false);
            await Model.WriteInterlockAsync(false, ct).ConfigureAwait(false);

            Assert.That(Model.InterlockClear, Is.False);

            ServiceResultException refused = Assert.ThrowsAsync<ServiceResultException>(
                () => Model.CallOperationCauseAsync(OperationCause.Start, ct),
                "The guard has to refuse Start while the interlock is open.");

            Assert.That(
                (StatusCode)refused.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                "The guard refuses Start with the status code the sample gave it.");

            await Assert.MultipleAsync(async () => {
                Assert.That(
                    await Model.ReadInterlockAsync(ct).ConfigureAwait(false),
                    Is.False,
                    "The refusal must not touch the interlock.");

                Assert.That(
                    await ReadOperationStateAsync(ct).ConfigureAwait(false),
                    Is.EqualTo("Idle"),
                    "A vetoed transition must leave the machine where it was.");
            });

            // and the very same call works again once the interlock is closed
            await Model.WriteInterlockAsync(true, ct).ConfigureAwait(false);
            await Model.CallOperationCauseAsync(OperationCause.Start, ct).ConfigureAwait(false);

            await transitions
                .WaitForAsync(
                    transition => transition.Snapshot.Machine == StateMachineKind.Operation
                        && transition.Snapshot.State == "Running",
                    "the stream did not report the transition to Running",
                    kTransitionTimeout,
                    ct)
                .ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ProgramCausesDriveTheStandardMachine(CancellationToken ct)
        {
            var transitions = new EventSink<TransitionObservedEventArgs>();
            Model.TransitionObserved += transitions.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            await Model.CallProgramCauseAsync(ProgramCause.Start, ct).ConfigureAwait(false);

            await WaitForProgramStateAsync(transitions, "Running", ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.PermittedCauses.IsPermitted(ProgramCause.Suspend), Is.True, "Suspend is declared for Running.");
                Assert.That(Model.PermittedCauses.IsPermitted(ProgramCause.Start), Is.False, "Start is not declared for Running.");
            });

            await Model.CallProgramCauseAsync(ProgramCause.Halt, ct).ConfigureAwait(false);
            await WaitForProgramStateAsync(transitions, "Halted", ct).ConfigureAwait(false);

            await Model.CallProgramCauseAsync(ProgramCause.Reset, ct).ConfigureAwait(false);
            await WaitForProgramStateAsync(transitions, "Ready", ct).ConfigureAwait(false);

            Assert.That(
                Model.ProgramState.State,
                Is.EqualTo("Ready"),
                "Halt and Reset return the program to where it started.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachStopsTheStreamsAndIsIdempotent(CancellationToken ct)
        {
            var transitions = new EventSink<TransitionObservedEventArgs>();
            var causes = new EventSink<PermittedCausesChangedEventArgs>();
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.TransitionObserved += transitions.Handle;
            Model.PermittedCausesChanged += causes.Handle;
            Model.ConnectionChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.CallOperationCauseAsync(OperationCause.PowerOn, ct).ConfigureAwait(false);

            await transitions
                .WaitForAsync(
                    transition => transition.Snapshot.Machine == StateMachineKind.Operation,
                    "the stream did not report the first transition",
                    kTransitionTimeout,
                    ct)
                .ConfigureAwait(false);

            await Model.DetachAsync().ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.IsConnected, Is.False);
                Assert.That(Model.OperationState, Is.Null, "A detached model still knows a state.");
                Assert.That(Model.PermittedCauses, Is.SameAs(PermittedCauses.All), "A detached model still holds a read map.");
            });

            int transitionsSeen = transitions.Count;
            int causesSeen = causes.Count;

            // the machine moves on while nobody watches it, over a plain session
            await CallOperationOverSessionAsync("PowerOff", ct).ConfigureAwait(false);
            await Task.Delay(kQuietPeriod, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(transitions.Count, Is.EqualTo(transitionsSeen), "Transitions kept arriving after the detach.");
                Assert.That(causes.Count, Is.EqualTo(causesSeen), "The causes were read again after the detach.");
                Assert.That(
                    changes.Events.Select(change => change.Change),
                    Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached }));
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public void CallingACauseBeforeTheAttachIsRefused()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                () => Model.CallOperationCauseAsync(OperationCause.PowerOn),
                "A detached model has no session to call on.");
        }

        #region Helpers
        private async Task WaitForProgramStateAsync(
            EventSink<TransitionObservedEventArgs> transitions,
            string state,
            CancellationToken ct)
        {
            await transitions
                .WaitForAsync(
                    transition => transition.Snapshot.Machine == StateMachineKind.Program
                        && transition.Snapshot.State == state,
                    $"the stream did not report the program reaching {state}",
                    kTransitionTimeout,
                    ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Calls a cause of the Operation machine over the plain session of the fixture.
        /// </summary>
        private async Task CallOperationOverSessionAsync(string cause, CancellationToken ct)
        {
            NodeId operationId = await PathAsync(ct, "Machine", "Operation").ConfigureAwait(false);
            NodeId methodId = await ChildAsync(operationId, cause, ct).ConfigureAwait(false);

            CallMethodResult result = await SessionOps
                .CallAsync(Session, operationId, methodId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Calling {cause} over the session answered {result.StatusCode}.");
        }

        private async Task<string> ReadOperationStateAsync(CancellationToken ct)
        {
            NodeId stateId = await PathAsync(ct, "Machine", "Operation", BrowseNames.CurrentState).ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, stateId, ct).ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out LocalizedText text) ? text.Text : null;
        }

        private async Task DriveOperationToOffAsync(CancellationToken ct)
        {
            for (int ii = 0; ii < 5; ii++)
            {
                switch (await ReadOperationStateAsync(ct).ConfigureAwait(false))
                {
                    case "Off":
                        return;
                    case "Idle":
                        await CallOperationOverSessionAsync("PowerOff", ct).ConfigureAwait(false);
                        break;
                    case "Running":
                        await CallOperationOverSessionAsync("Stop", ct).ConfigureAwait(false);
                        break;
                    case "Faulted":
                        await CallOperationOverSessionAsync("Reset", ct).ConfigureAwait(false);
                        break;
                    default:
                        return;
                }
            }
        }

        private async Task DriveProgramToReadyAsync(CancellationToken ct)
        {
            NodeId programId = await PathAsync(ct, "Machine", "Program").ConfigureAwait(false);
            NodeId stateId = await ChildAsync(programId, BrowseNames.CurrentState, ct).ConfigureAwait(false);
            NodeId stateIdId = await ChildAsync(stateId, BrowseNames.Id, ct).ConfigureAwait(false);

            for (int ii = 0; ii < 5; ii++)
            {
                DataValue value = await SessionOps.ReadValueAsync(Session, stateIdId, ct).ConfigureAwait(false);
                NodeId state = value.WrappedValue.TryGetValue(out NodeId id) ? id : NodeId.Null;

                if (state == new NodeId(Objects.ProgramStateMachineType_Ready))
                {
                    return;
                }

                string cause = state == new NodeId(Objects.ProgramStateMachineType_Halted)
                    ? BrowseNames.Reset
                    : BrowseNames.Halt;

                NodeId methodId = await ChildAsync(programId, cause, ct).ConfigureAwait(false);

                await SessionOps.CallAsync(Session, programId, methodId, ct).ConfigureAwait(false);
            }
        }
        #endregion
    }
}
