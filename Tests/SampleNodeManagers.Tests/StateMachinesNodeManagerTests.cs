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
using Quickstarts.StateMachines.Server;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the state machines sample does: two OPC UA Part 16 state machines, one declared
    /// with the builder and one taken from the stack, both driven by method calls.
    /// </summary>
    /// <remarks>
    /// The two machines are what the sample exists to show, so the tests hold it to the
    /// behaviour a client can see of each: which state it starts in, that a cause only works
    /// in the state it is declared for, that a guard can refuse one, and that a machine can
    /// move on its own after a delay.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class StateMachinesNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "StateMachines";

        /// <summary>
        /// The namespace the sample serves its nodes in.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than imported, because Opc.Ua defines a Namespaces class too
        /// and the tests live in a namespace below Opc.Ua.
        /// </remarks>
        private const string StateMachinesNamespace =
            Quickstarts.StateMachines.Server.Namespaces.StateMachines;

        /// <summary>
        /// Why CurrentState/Id does not follow the model yet: the sample needs
        /// UA-.NETStandard #4368 (state reporting for fluent state machines), which
        /// landed one master build after the 2.0.262.32744-preview package set this
        /// repository pins - and every later GitHub Packages publish failed on an
        /// unrelated signing error. The first newer package set lifts this.
        /// </summary>
        private const string kStateReportingIssue =
            "CurrentState/Id needs UA-.NETStandard #4368, which is newer than the " +
            "2.0.262.32744-preview packages the samples build against.";

        private QualifiedName Machine => Name(StateMachinesNamespace, "Machine");
        private QualifiedName Operation => Name(StateMachinesNamespace, "Operation");
        private QualifiedName Program => Name(StateMachinesNamespace, "Program");
        private QualifiedName Interlock => Name(StateMachinesNamespace, "SafetyInterlockClear");
        private QualifiedName TransitionCount => Name(StateMachinesNamespace, "TransitionCount");

        private static QualifiedName CurrentState => new(BrowseNames.CurrentState);
        private static QualifiedName LastTransition => new(BrowseNames.LastTransition);
        private static QualifiedName Id => new(BrowseNames.Id);

        /// <summary>
        /// Both machines are left where they started, so the order the tests run in does not
        /// decide what they see.
        /// </summary>
        [SetUp]
        public async Task ResetMachinesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;

            await WriteInterlockAsync(true, ct).ConfigureAwait(false);
            await DriveOperationToOffAsync(ct).ConfigureAwait(false);
            await DriveProgramToReadyAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The machine, its two state machines and the methods which drive them are where the
        /// sample says they are.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task BothStateMachinesAreExposedWithTheirCauses(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);
            NodeId operationId = await ResolveAsync(ct, Machine, Operation).ConfigureAwait(false);
            NodeId programId = await ResolveAsync(ct, Machine, Program).ConfigureAwait(false);

            ushort ns = NamespaceIndex(StateMachinesNamespace);

            Assert.Multiple(() => {
                Assert.That(
                    machineId,
                    Is.EqualTo(new NodeId(StateMachinesNodeManager.MachineId, ns)),
                    "The machine object moved.");
                Assert.That(
                    operationId,
                    Is.EqualTo(new NodeId(StateMachinesNodeManager.OperationId, ns)),
                    "The Operation state machine moved.");
                Assert.That(
                    programId,
                    Is.EqualTo(new NodeId(StateMachinesNodeManager.ProgramId, ns)),
                    "The Program state machine moved.");
            });

            IReadOnlyList<string> machineChildren = await BrowseNamesAsync(machineId, ct)
                .ConfigureAwait(false);
            IReadOnlyList<string> operationChildren = await BrowseNamesAsync(operationId, ct)
                .ConfigureAwait(false);
            IReadOnlyList<string> programChildren = await BrowseNamesAsync(programId, ct)
                .ConfigureAwait(false);

            await ReportAsync("Machine", machineChildren).ConfigureAwait(false);
            await ReportAsync("Operation", operationChildren).ConfigureAwait(false);
            await ReportAsync("Program", programChildren).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    machineChildren,
                    Is.SupersetOf(new[] { "SafetyInterlockClear", "TransitionCount", "Operation", "Program" }),
                    "The machine object lost one of its children.");

                // CurrentState is what a client reads, LastTransition what it subscribes to,
                // and the six methods are the causes of the declared transitions.
                Assert.That(
                    operationChildren,
                    Is.SupersetOf(new[] {
                        "CurrentState", "LastTransition",
                        "PowerOn", "PowerOff", "Start", "Stop", "Fault", "Reset",
                    }),
                    "The Operation state machine lost a child.");

                Assert.That(
                    programChildren,
                    Is.SupersetOf(new[] {
                        "CurrentState", "LastTransition",
                        "Start", "Suspend", "Resume", "Halt", "Reset",
                    }),
                    "The Program state machine lost a child.");
            });
        }

        /// <summary>
        /// The Operation machine reports the state it declared as its initial one, and it
        /// reports it the way Part 16 asks for: as a name and as the NodeId of the state.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperationMachineStartsInItsInitialState(CancellationToken ct)
        {
            string state = await ReadOperationStateNameAsync(ct).ConfigureAwait(false);
            NodeId stateId = await ReadOperationStateIdAsync(ct).ConfigureAwait(false);

            Assert.That(
                state,
                Is.EqualTo("Off"),
                "The Operation machine has to start in Off.");

            await KnownIssueAsync(
                () => {
                    Assert.That(
                        stateId,
                        Is.EqualTo(OperationState(StateMachinesNodeManager.OffState)),
                        "CurrentState/Id has to name the state node of the machine's own namespace.");
                    return Task.CompletedTask;
                },
                kStateReportingIssue).ConfigureAwait(false);
        }

        /// <summary>
        /// Calling the methods drives the machine along the transitions which were declared
        /// for them, and the machine reports both where it is and how it got there.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CausesDriveTheDeclaredTransitions(CancellationToken ct)
        {
            uint before = await ReadTransitionCountAsync(ct).ConfigureAwait(false);

            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);

            string idle = await ReadOperationStateNameAsync(ct).ConfigureAwait(false);
            string offToIdle = await ReadOperationTransitionNameAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    idle,
                    Is.EqualTo("Idle"),
                    "PowerOn has to move the machine from Off to Idle.");
                Assert.That(
                    offToIdle,
                    Is.EqualTo("OffToIdle"),
                    "LastTransition has to name the transition which was taken.");
            });

            await CallOperationAsync(StateMachinesNodeManager.StartCause, ct).ConfigureAwait(false);

            string running = await ReadOperationStateNameAsync(ct).ConfigureAwait(false);
            NodeId runningId = await ReadOperationStateIdAsync(ct).ConfigureAwait(false);

            Assert.That(
                running,
                Is.EqualTo("Running"),
                "Start has to move the machine from Idle to Running.");

            await CallOperationAsync(StateMachinesNodeManager.StopCause, ct).ConfigureAwait(false);

            Assert.That(
                await ReadOperationStateNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("Idle"),
                "Stop has to move the machine from Running back to Idle.");

            // the transition observer of the sample counts every completed transition
            uint after = await ReadTransitionCountAsync(ct).ConfigureAwait(false);

            Assert.That(
                after - before,
                Is.EqualTo(3u),
                "The transition observer has to see all three transitions.");

            // checked last, so the known issue does not keep the rest of the test
            // from proving the transitions themselves work
            await KnownIssueAsync(
                () => {
                    Assert.That(
                        runningId,
                        Is.EqualTo(OperationState(StateMachinesNodeManager.RunningState)),
                        "CurrentState/Id has to follow the state.");
                    return Task.CompletedTask;
                },
                kStateReportingIssue).ConfigureAwait(false);
        }

        /// <summary>
        /// A cause which is not declared for the state the machine is in is refused, and the
        /// machine stays where it was.
        /// </summary>
        /// <remarks>
        /// Two layers refuse this, and which one answers is worth recording. Because the
        /// sample answers <c>Executable</c> / <c>UserExecutable</c> from
        /// <c>IsCausePermitted</c>, the Call service refuses a method which cannot apply with
        /// <see cref="StatusCodes.BadNotExecutable"/> before the cause pipeline is reached.
        /// The <see cref="StatusCodes.BadNotSupported"/> which <c>DoCause</c> answers for an
        /// unmapped cause is the layer behind it, and is what a server which does not maintain
        /// the attributes would return instead.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CauseWhichIsNotDeclaredForTheCurrentStateIsRefused(CancellationToken ct)
        {
            // the machine is Off, and Start is only declared for Idle
            CallMethodResult result = await CallOperationRawAsync(
                StateMachinesNodeManager.StartCause,
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Start() while Off -> {result.StatusCode}")
                .ConfigureAwait(false);

            string state = await ReadOperationStateNameAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNotExecutable),
                    "A cause which cannot apply in the current state has to be refused as " +
                    "not executable, which is what the Executable attribute already said.");

                Assert.That(
                    state,
                    Is.EqualTo("Off"),
                    "A refused cause must not move the machine.");
            });
        }

        /// <summary>
        /// The guard on the Start cause refuses the transition while the interlock is clear,
        /// with the status code the sample gave it.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GuardRefusesTheStartCauseWhileTheInterlockIsNotClear(CancellationToken ct)
        {
            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);
            await WriteInterlockAsync(false, ct).ConfigureAwait(false);

            CallMethodResult refused = await CallOperationRawAsync(
                StateMachinesNodeManager.StartCause,
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Start() with the interlock open -> {refused.StatusCode}")
                .ConfigureAwait(false);

            string afterVeto = await ReadOperationStateNameAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    refused.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                    "The guard has to refuse Start with the status code it was given.");

                Assert.That(
                    afterVeto,
                    Is.EqualTo("Idle"),
                    "A vetoed transition must leave the machine where it was.");
            });

            // and the very same call works again once the interlock is closed
            await WriteInterlockAsync(true, ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.StartCause, ct).ConfigureAwait(false);

            Assert.That(
                await ReadOperationStateNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("Running"),
                "Start has to be permitted again once the interlock is closed.");
        }

        /// <summary>
        /// The machine leaves the Faulted state on its own, without anybody calling a method.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task FaultedMachineResetsItselfAfterTheDelay(CancellationToken ct)
        {
            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.StartCause, ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.FaultCause, ct).ConfigureAwait(false);

            Assert.That(
                await ReadOperationStateNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("Faulted"),
                "Fault has to move the machine from Running to Faulted.");

            string reached = await Poll.UntilAsync(
                token => ReadOperationStateNameAsync(token),
                state => state == "Idle",
                "the machine to reset itself out of Faulted",
                timeout: StateMachinesNodeManager.AutomaticResetDelay + TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            Assert.That(reached, Is.EqualTo("Idle"));

            Assert.That(
                await ReadOperationTransitionNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("FaultedToIdle"),
                "The automatic reset has to be reported as the transition it takes.");
        }

        /// <summary>
        /// The server says which causes apply right now, so a client does not have to call one
        /// and be refused to find out.
        /// </summary>
        /// <remarks>
        /// This is the Executable / UserExecutable contract of OPC 10000-16: a cause method is
        /// executable exactly in the states its cause was declared for. Without it a client
        /// has to either duplicate the server's state table or offer every cause always.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ExecutableFollowsTheCausesPermittedInTheCurrentState(CancellationToken ct)
        {
            // Off: only PowerOn applies
            IReadOnlyDictionary<string, bool> inOff = await ReadCauseExecutableAsync(ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Executable in Off",
                inOff.Select(entry => $"{entry.Key}={entry.Value}")).ConfigureAwait(false);

            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);

            // Idle: PowerOff and Start apply
            IReadOnlyDictionary<string, bool> inIdle = await ReadCauseExecutableAsync(ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Executable in Idle",
                inIdle.Select(entry => $"{entry.Key}={entry.Value}")).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(inOff["PowerOn"], Is.True, "PowerOn is the only cause declared for Off.");
                Assert.That(inOff["Start"], Is.False, "Start is not declared for Off.");
                Assert.That(inOff["Stop"], Is.False, "Stop is not declared for Off.");
                Assert.That(inOff["Reset"], Is.False, "Reset is not declared for Off.");

                Assert.That(inIdle["Start"], Is.True, "Start has to become executable in Idle.");
                Assert.That(inIdle["PowerOff"], Is.True, "PowerOff is declared for Idle.");
                Assert.That(inIdle["PowerOn"], Is.False, "PowerOn is not declared for Idle.");
            });
        }

        /// <summary>
        /// A transition is reported as a Part 16 transition event, and the sample makes the
        /// machine object a notifier so that the event reaches the server object.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TransitionsAreReportedAsEvents(CancellationToken ct)
        {
            await using EventCapture capture = await EventCapture
                .CreateAsync(Session, ObjectIds.Server, ct, ObjectTypeIds.TransitionEventType)
                .ConfigureAwait(false);

            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);

            CapturedEvent reported = await capture.WaitAsync(
                candidate => candidate.EventType == ObjectTypeIds.TransitionEventType,
                TimeSpan.FromSeconds(30),
                "the transition event of the Operation machine",
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Reported: {reported}")
                .ConfigureAwait(false);

            Assert.That(
                reported.SourceName,
                Is.EqualTo("Operation"),
                "The transition event has to name the state machine it belongs to.");
        }

        /// <summary>
        /// The Program machine follows the state and transition tables of OPC 10000-10, which
        /// the sample did not have to declare: it only attached behaviour to them.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ProgramMachineFollowsTheStandardStateTable(CancellationToken ct)
        {
            Assert.That(
                await ReadProgramStateIdAsync(ct).ConfigureAwait(false),
                Is.EqualTo(new NodeId(Objects.ProgramStateMachineType_Ready)),
                "A program starts in Ready.");

            await CallProgramAsync(BrowseNames.Start, ct).ConfigureAwait(false);

            Assert.That(
                await ReadProgramStateIdAsync(ct).ConfigureAwait(false),
                Is.EqualTo(new NodeId(Objects.ProgramStateMachineType_Running)),
                "Start has to move the program to Running.");

            await CallProgramAsync(BrowseNames.Suspend, ct).ConfigureAwait(false);

            Assert.That(
                await ReadProgramStateIdAsync(ct).ConfigureAwait(false),
                Is.EqualTo(new NodeId(Objects.ProgramStateMachineType_Suspended)),
                "Suspend has to move the program to Suspended.");

            await CallProgramAsync(BrowseNames.Resume, ct).ConfigureAwait(false);
            await CallProgramAsync(BrowseNames.Halt, ct).ConfigureAwait(false);

            Assert.That(
                await ReadProgramStateIdAsync(ct).ConfigureAwait(false),
                Is.EqualTo(new NodeId(Objects.ProgramStateMachineType_Halted)),
                "Halt has to move the program to Halted.");

            await CallProgramAsync(BrowseNames.Reset, ct).ConfigureAwait(false);

            Assert.That(
                await ReadProgramStateIdAsync(ct).ConfigureAwait(false),
                Is.EqualTo(new NodeId(Objects.ProgramStateMachineType_Ready)),
                "Reset has to move the program back to Ready.");
        }

        /// <summary>
        /// The program completes on its own: the automatic transition attached in lifecycle
        /// mode returns it to Ready once it has run for long enough.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ProgramReturnsToReadyOnItsOwn(CancellationToken ct)
        {
            await CallProgramAsync(BrowseNames.Start, ct).ConfigureAwait(false);

            NodeId reached = await Poll.UntilAsync(
                token => ReadProgramStateIdAsync(token),
                state => state == new NodeId(Objects.ProgramStateMachineType_Ready),
                "the program to complete on its own",
                timeout: StateMachinesNodeManager.ProgramRunDuration + TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            Assert.That(reached, Is.EqualTo(new NodeId(Objects.ProgramStateMachineType_Ready)));
        }

        #region Helpers
        private NodeId OperationState(uint stateId)
        {
            return new NodeId(stateId, NamespaceIndex(StateMachinesNamespace));
        }

        private Task<NodeId> OperationNodeAsync(CancellationToken ct)
        {
            return ResolveAsync(ct, Machine, Operation);
        }

        private Task<NodeId> ProgramNodeAsync(CancellationToken ct)
        {
            return ResolveAsync(ct, Machine, Program);
        }

        private async Task<string> ReadOperationStateNameAsync(CancellationToken ct)
        {
            NodeId stateId = await ResolveAsync(ct, Machine, Operation, CurrentState)
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, stateId, ct)
                .ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out LocalizedText text) ? text.Text : null;
        }

        private async Task<string> ReadOperationTransitionNameAsync(CancellationToken ct)
        {
            NodeId transitionId = await ResolveAsync(ct, Machine, Operation, LastTransition)
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, transitionId, ct)
                .ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out LocalizedText text) ? text.Text : null;
        }

        private async Task<NodeId> ReadOperationStateIdAsync(CancellationToken ct)
        {
            NodeId idNode = await ResolveAsync(ct, Machine, Operation, CurrentState, Id)
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, idNode, ct)
                .ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out NodeId state) ? state : NodeId.Null;
        }

        private async Task<NodeId> ReadProgramStateIdAsync(CancellationToken ct)
        {
            NodeId idNode = await ResolveAsync(ct, Machine, Program, CurrentState, Id)
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, idNode, ct)
                .ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out NodeId state) ? state : NodeId.Null;
        }

        private async Task<uint> ReadTransitionCountAsync(CancellationToken ct)
        {
            NodeId counterId = await ResolveAsync(ct, Machine, TransitionCount).ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, counterId, ct)
                .ConfigureAwait(false);

            return value.WrappedValue.ConvertTo(BuiltInType.UInt32).TryGetValue(out uint count)
                ? count
                : 0;
        }

        /// <summary>
        /// Reads the UserExecutable attribute of every cause method of the Operation machine.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, bool>> ReadCauseExecutableAsync(CancellationToken ct)
        {
            var causes = new (string Name, uint Id)[] {
                ("PowerOn", StateMachinesNodeManager.PowerOnCause),
                ("PowerOff", StateMachinesNodeManager.PowerOffCause),
                ("Start", StateMachinesNodeManager.StartCause),
                ("Stop", StateMachinesNodeManager.StopCause),
                ("Fault", StateMachinesNodeManager.FaultCause),
                ("Reset", StateMachinesNodeManager.ResetCause),
            };

            ushort ns = NamespaceIndex(StateMachinesNamespace);
            var executable = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach ((string name, uint id) in causes)
            {
                DataValue value = await SessionOps
                    .ReadAttributeAsync(Session, new NodeId(id, ns), Attributes.UserExecutable, ct)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(value.StatusCode),
                    Is.True,
                    $"Reading UserExecutable of {name} failed: {value.StatusCode}");

                executable[name] = value.WrappedValue.TryGetValue(out bool flag) && flag;
            }

            return executable;
        }

        private async Task WriteInterlockAsync(bool clear, CancellationToken ct)
        {
            NodeId interlockId = await ResolveAsync(ct, Machine, Interlock).ConfigureAwait(false);

            StatusCode status = await SessionOps
                .WriteValueAsync(Session, interlockId, Variant.From(clear), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(status),
                Is.True,
                $"Writing the interlock failed: {status}");
        }

        /// <summary>
        /// Calls one of the cause methods of the Operation machine and reports the result.
        /// </summary>
        private async Task<CallMethodResult> CallOperationRawAsync(uint causeId, CancellationToken ct)
        {
            NodeId operationId = await OperationNodeAsync(ct).ConfigureAwait(false);

            return await SessionOps
                .CallAsync(
                    Session,
                    operationId,
                    new NodeId(causeId, NamespaceIndex(StateMachinesNamespace)),
                    ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Calls one of the cause methods of the Operation machine and expects it to work.
        /// </summary>
        private async Task CallOperationAsync(uint causeId, CancellationToken ct)
        {
            CallMethodResult result = await CallOperationRawAsync(causeId, ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Calling the cause {causeId} failed: {result.StatusCode}");
        }

        /// <summary>
        /// Calls one of the methods of the Program machine by its standard browse name.
        /// </summary>
        private async Task<CallMethodResult> CallProgramRawAsync(string browseName, CancellationToken ct)
        {
            NodeId programId = await ProgramNodeAsync(ct).ConfigureAwait(false);
            NodeId methodId = await ChildAsync(programId, browseName, ct).ConfigureAwait(false);

            return await SessionOps
                .CallAsync(Session, programId, methodId, ct)
                .ConfigureAwait(false);
        }

        private async Task CallProgramAsync(string browseName, CancellationToken ct)
        {
            CallMethodResult result = await CallProgramRawAsync(browseName, ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Calling {browseName} on the program failed: {result.StatusCode}");
        }

        /// <summary>
        /// Walks the Operation machine back to Off, whichever state a previous test left it in.
        /// </summary>
        private async Task DriveOperationToOffAsync(CancellationToken ct)
        {
            for (int i = 0; i < 5; i++)
            {
                switch (await ReadOperationStateNameAsync(ct).ConfigureAwait(false))
                {
                    case "Off":
                        return;
                    case "Idle":
                        await CallOperationRawAsync(StateMachinesNodeManager.PowerOffCause, ct)
                            .ConfigureAwait(false);
                        break;
                    case "Running":
                        await CallOperationRawAsync(StateMachinesNodeManager.StopCause, ct)
                            .ConfigureAwait(false);
                        break;
                    case "Faulted":
                        await CallOperationRawAsync(StateMachinesNodeManager.ResetCause, ct)
                            .ConfigureAwait(false);
                        break;
                    default:
                        return;
                }
            }
        }

        /// <summary>
        /// Walks the Program machine back to Ready.
        /// </summary>
        private async Task DriveProgramToReadyAsync(CancellationToken ct)
        {
            for (int i = 0; i < 5; i++)
            {
                NodeId state = await ReadProgramStateIdAsync(ct).ConfigureAwait(false);

                if (state == new NodeId(Objects.ProgramStateMachineType_Ready))
                {
                    return;
                }

                if (state == new NodeId(Objects.ProgramStateMachineType_Halted))
                {
                    await CallProgramRawAsync(BrowseNames.Reset, ct).ConfigureAwait(false);
                    continue;
                }

                await CallProgramRawAsync(BrowseNames.Halt, ct).ConfigureAwait(false);
            }
        }
        #endregion
    }
}
