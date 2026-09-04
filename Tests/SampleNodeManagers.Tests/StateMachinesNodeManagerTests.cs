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
                // the six methods are the causes of the declared transitions, the two
                // Available* properties list the model of the machine, and Production is the
                // machine which runs below the Running state.
                Assert.That(
                    operationChildren,
                    Is.SupersetOf(new[] {
                        "CurrentState", "LastTransition",
                        "PowerOn", "PowerOff", "Start", "Stop", "Fault", "Reset",
                        "AvailableStates", "AvailableTransitions",
                        "Production",
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
            NodeId offNode = await OperationStateNodeAsync("Off", ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    state,
                    Is.EqualTo("Off"),
                    "The Operation machine has to start in Off.");

                Assert.That(
                    stateId,
                    Is.EqualTo(offNode),
                    "CurrentState/Id has to name the state node of the machine's own namespace.");
            });
        }

        /// <summary>
        /// Every state and every transition the sample declared is a node a client can browse
        /// to, and the machine lists them through the two optional Available properties.
        /// </summary>
        /// <remarks>
        /// A machine which materializes none of them can only be read: <c>CurrentState/Id</c>
        /// names a node which does not exist, <c>GetAvailableStatesAsync</c> and
        /// <c>GetAvailableTransitionsAsync</c> answer nothing, and a state has nowhere to hang
        /// a sub state machine off. The builder mints the nodes from the declaration, so the
        /// sample declares its machine once and serves the whole Part 16 model of it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperationMachineMaterializesItsStatesAndTransitions(CancellationToken ct)
        {
            NodeId operationId = await OperationNodeAsync(ct).ConfigureAwait(false);

            IReadOnlyList<NodeId> availableStates = await ReadNodeIdsAsync(
                operationId,
                BrowseNames.AvailableStates,
                ct).ConfigureAwait(false);

            IReadOnlyList<NodeId> availableTransitions = await ReadNodeIdsAsync(
                operationId,
                BrowseNames.AvailableTransitions,
                ct).ConfigureAwait(false);

            // the declared model, in declaration order, and the number each element carries
            var states = new (string Name, uint Number)[] {
                ("Off", StateMachinesNodeManager.OffState),
                ("Idle", StateMachinesNodeManager.IdleState),
                ("Running", StateMachinesNodeManager.RunningState),
                ("Faulted", StateMachinesNodeManager.FaultedState),
            };

            var transitions = new (string Name, uint Number)[] {
                ("OffToIdle", StateMachinesNodeManager.OffToIdleTransition),
                ("IdleToOff", StateMachinesNodeManager.IdleToOffTransition),
                ("IdleToRunning", StateMachinesNodeManager.IdleToRunningTransition),
                ("RunningToIdle", StateMachinesNodeManager.RunningToIdleTransition),
                ("RunningToFaulted", StateMachinesNodeManager.RunningToFaultedTransition),
                ("FaultedToIdle", StateMachinesNodeManager.FaultedToIdleTransition),
            };

            await AssertElementsAsync(
                operationId,
                availableStates,
                states,
                BrowseNames.StateNumber,
                BrowseNames.AvailableStates,
                ct).ConfigureAwait(false);

            await AssertElementsAsync(
                operationId,
                availableTransitions,
                transitions,
                BrowseNames.TransitionNumber,
                BrowseNames.AvailableTransitions,
                ct).ConfigureAwait(false);

            // OPC 10000-16 §4.4.10: the state a machine activates into is an InitialStateType,
            // which is how a client tells it apart from the other states.
            NodeId offNode = await OperationStateNodeAsync("Off", ct).ConfigureAwait(false);
            NodeId idleNode = await OperationStateNodeAsync("Idle", ct).ConfigureAwait(false);

            NodeId offType = await SessionOps
                .GetTypeDefinitionAsync(Session, offNode, ct).ConfigureAwait(false);
            NodeId idleType = await SessionOps
                .GetTypeDefinitionAsync(Session, idleNode, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    offType,
                    Is.EqualTo(ObjectTypeIds.InitialStateType),
                    "The initial state has to be an InitialStateType.");

                Assert.That(
                    idleType,
                    Is.EqualTo(ObjectTypeIds.StateType),
                    "Every other state is a StateType.");
            });
        }

        /// <summary>
        /// A transition node names the two states it runs between, the event it reports and
        /// the method which causes it, the way OPC 10000-16 §4.4.11 asks for.
        /// </summary>
        /// <remarks>
        /// This is what a client needs to draw the machine without knowing it: the states come
        /// from AvailableStates, and the transitions between them from the FromState / ToState
        /// references of the nodes AvailableTransitions names. HasCause adds which method call
        /// takes each of them, which is more than the Executable attribute says - that one only
        /// answers for the state the machine is in right now.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TransitionNodesCarryTheirEndpointsCauseAndEffect(CancellationToken ct)
        {
            NodeId operationId = await OperationNodeAsync(ct).ConfigureAwait(false);
            NodeId idleToRunning = await ChildAsync(operationId, "IdleToRunning", ct)
                .ConfigureAwait(false);

            NodeId idleNode = await OperationStateNodeAsync("Idle", ct).ConfigureAwait(false);
            NodeId runningNode = await OperationStateNodeAsync("Running", ct).ConfigureAwait(false);
            NodeId startMethod = await ChildAsync(operationId, "Start", ct).ConfigureAwait(false);

            IReadOnlyList<NodeId> fromState = await TargetsAsync(
                idleToRunning, ReferenceTypeIds.FromState, ct).ConfigureAwait(false);
            IReadOnlyList<NodeId> toState = await TargetsAsync(
                idleToRunning, ReferenceTypeIds.ToState, ct).ConfigureAwait(false);
            IReadOnlyList<NodeId> hasCause = await TargetsAsync(
                idleToRunning, ReferenceTypeIds.HasCause, ct).ConfigureAwait(false);
            IReadOnlyList<NodeId> hasEffect = await TargetsAsync(
                idleToRunning, ReferenceTypeIds.HasEffect, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    fromState,
                    Is.EqualTo(new[] { idleNode }),
                    "IdleToRunning has to start at the Idle state node.");
                Assert.That(
                    toState,
                    Is.EqualTo(new[] { runningNode }),
                    "IdleToRunning has to end at the Running state node.");
                Assert.That(
                    hasCause,
                    Is.EqualTo(new[] { startMethod }),
                    "The cause of IdleToRunning is the Start method the sample bound to it.");
                Assert.That(
                    hasEffect,
                    Is.EqualTo(new[] { ObjectTypeIds.TransitionEventType }),
                    "A transition reports a TransitionEventType, and says so with HasEffect.");
            });
        }

        /// <summary>
        /// The Running state has a machine of its own below it, referenced the way OPC
        /// 10000-16 §4.4.16 asks for: from the state node rather than from the machine.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RunningStateOwnsTheProductionSubStateMachine(CancellationToken ct)
        {
            NodeId operationId = await OperationNodeAsync(ct).ConfigureAwait(false);
            NodeId runningNode = await OperationStateNodeAsync("Running", ct).ConfigureAwait(false);
            NodeId productionId = await ProductionNodeAsync(ct).ConfigureAwait(false);

            IReadOnlyList<NodeId> fromState = await TargetsAsync(
                runningNode, ReferenceTypeIds.HasSubStateMachine, ct).ConfigureAwait(false);
            IReadOnlyList<NodeId> fromMachine = await TargetsAsync(
                operationId, ReferenceTypeIds.HasSubStateMachine, ct).ConfigureAwait(false);

            IReadOnlyList<string> productionChildren = await BrowseNamesAsync(productionId, ct)
                .ConfigureAwait(false);

            await ReportAsync("Production", productionChildren).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    fromState,
                    Is.EqualTo(new[] { productionId }),
                    "The sub state machine hangs off the state it belongs to.");

                Assert.That(
                    fromMachine,
                    Is.Empty,
                    "A sub state machine is not referenced from the machine root: a client " +
                    "which browsed it there could not tell which state it belongs to.");

                // the child is a machine like any other: its own states, its own transitions,
                // its own cause method and the two variables which report where it is.
                Assert.That(
                    productionChildren,
                    Is.SupersetOf(new[] {
                        "CurrentState", "LastTransition",
                        "Loading", "Processing", "Unloading",
                        "LoadingToProcessing", "ProcessingToUnloading", "UnloadingToLoading",
                        "AvailableStates", "AvailableTransitions",
                        "StartBatch",
                    }),
                    "The Production state machine lost a child.");
            });
        }

        /// <summary>
        /// The Production machine only exists while the Operation machine is Running: outside
        /// of it there is no state to read and no cause to call.
        /// </summary>
        /// <remarks>
        /// OPC 10000-16 §4.4.6 has an inactive sub state machine report its state variables
        /// with <see cref="StatusCodes.BadStateNotActive"/> rather than with a stale value, so
        /// that a client cannot mistake where the child stopped for where it is. The framework
        /// applies it from the parent's transitions - the sample declares which state the child
        /// belongs to and writes none of it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ProductionMachineIsNotActiveWhileTheMachineIsNotRunning(CancellationToken ct)
        {
            // the fixture left the Operation machine in Off, which is not the state the
            // Production machine belongs to
            DataValue suspended = await ReadProductionStateAsync(ct).ConfigureAwait(false);
            CallMethodResult refused = await CallStartBatchRawAsync(ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Production/CurrentState while Off -> {suspended.StatusCode}, " +
                    $"StartBatch() -> {refused.StatusCode}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    suspended.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive),
                    "The state of a suspended sub state machine has to read Bad_StateNotActive.");

                Assert.That(
                    refused.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNotExecutable),
                    "A cause of a suspended sub state machine must not be executable.");
            });

            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.StartCause, ct).ConfigureAwait(false);

            DataValue active = await ReadProductionStateAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    StatusCode.IsGood(active.StatusCode),
                    Is.True,
                    $"Entering Running has to activate the child: {active.StatusCode}");

                Assert.That(
                    StateName(active),
                    Is.EqualTo("Loading"),
                    "An activated sub state machine enters the state it declared as its initial one.");
            });

            await CallOperationAsync(StateMachinesNodeManager.StopCause, ct).ConfigureAwait(false);

            Assert.That(
                (await ReadProductionStateAsync(ct).ConfigureAwait(false)).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive),
                "Leaving Running has to suspend the child again.");
        }

        /// <summary>
        /// The Production machine runs a batch of its own while the Operation machine stays in
        /// one state, and starts over when the parent leaves Running and comes back.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ProductionMachineRunsABatchAndStartsOverOnReentry(CancellationToken ct)
        {
            await CallOperationAsync(StateMachinesNodeManager.PowerOnCause, ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.StartCause, ct).ConfigureAwait(false);

            await CallStartBatchAsync(ct).ConfigureAwait(false);

            Assert.That(
                await ReadProductionStateNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("Processing"),
                "StartBatch has to move the child out of Loading.");

            // the rest of the batch is the child's own business: two timed transitions carry it
            // through unloading and back to Loading while the parent stays in Running.
            string unloading = await Poll.UntilAsync(
                token => ReadProductionStateNameAsync(token),
                state => state == "Unloading",
                "the batch to be processed",
                timeout: StateMachinesNodeManager.BatchDuration + TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            string loading = await Poll.UntilAsync(
                token => ReadProductionStateNameAsync(token),
                state => state == "Loading",
                "the batch to be unloaded",
                timeout: StateMachinesNodeManager.UnloadDuration + TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(unloading, Is.EqualTo("Unloading"));
                Assert.That(loading, Is.EqualTo("Loading"));
            });

            // the parent stays where it is through all of that
            Assert.That(
                await ReadOperationStateNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("Running"),
                "A transition of the child must not move the parent.");

            // and a run which is interrupted starts over rather than resuming: that is what
            // the sample asked for with preserveOnReentry: false.
            await CallStartBatchAsync(ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.StopCause, ct).ConfigureAwait(false);
            await CallOperationAsync(StateMachinesNodeManager.StartCause, ct).ConfigureAwait(false);

            Assert.That(
                await ReadProductionStateNameAsync(ct).ConfigureAwait(false),
                Is.EqualTo("Loading"),
                "The child has to start over when the parent re-enters Running.");
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
            NodeId runningNode = await OperationStateNodeAsync("Running", ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    running,
                    Is.EqualTo("Running"),
                    "Start has to move the machine from Idle to Running.");
                Assert.That(
                    runningId,
                    Is.EqualTo(runningNode),
                    "CurrentState/Id has to follow the state.");
            });

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
        /// <summary>
        /// The node of one of the Operation machine's states, found by browsing for it.
        /// </summary>
        /// <remarks>
        /// The state nodes are materialized by the stack, which mints their NodeIds from the
        /// machine's own identifier and the state's browse name rather than from the numeric
        /// id the sample declared - that number stays on the node as its state number. So the
        /// node is browsed for by name instead of being computed, which is also what a client
        /// comparing CurrentState/Id against a state would have to do.
        /// </remarks>
        private async Task<NodeId> OperationStateNodeAsync(string stateName, CancellationToken ct)
        {
            NodeId machine = await OperationNodeAsync(ct).ConfigureAwait(false);

            return await ChildAsync(machine, stateName, ct).ConfigureAwait(false);
        }

        private Task<NodeId> OperationNodeAsync(CancellationToken ct)
        {
            return ResolveAsync(ct, Machine, Operation);
        }

        /// <summary>
        /// The sub state machine which runs below the Running state.
        /// </summary>
        private async Task<NodeId> ProductionNodeAsync(CancellationToken ct)
        {
            NodeId operationId = await OperationNodeAsync(ct).ConfigureAwait(false);

            return await ChildAsync(operationId, "Production", ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a NodeId array property of a node - AvailableStates or AvailableTransitions.
        /// </summary>
        private async Task<IReadOnlyList<NodeId>> ReadNodeIdsAsync(
            NodeId parent,
            string browseName,
            CancellationToken ct)
        {
            NodeId listId = await ChildAsync(parent, browseName, ct).ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, listId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading {browseName} failed: {value.StatusCode}");

            return value.WrappedValue.TryGetValue(out ArrayOf<NodeId> nodes)
                ? nodes.ToArray()
                : [];
        }

        /// <summary>
        /// Holds the states or the transitions of a machine to what the sample declared: the
        /// Available property lists exactly the nodes below the machine, in declaration order,
        /// and each of them carries the number the sample gave it.
        /// </summary>
        private async Task AssertElementsAsync(
            NodeId machineId,
            IReadOnlyList<NodeId> available,
            IReadOnlyList<(string Name, uint Number)> declared,
            string numberBrowseName,
            string what,
            CancellationToken ct)
        {
            var browsed = new List<NodeId>(declared.Count);
            var numbers = new List<uint>(declared.Count);

            foreach ((string name, uint _) in declared)
            {
                NodeId elementId = await ChildAsync(machineId, name, ct).ConfigureAwait(false);

                browsed.Add(elementId);
                numbers.Add(await ReadElementNumberAsync(elementId, numberBrowseName, ct)
                    .ConfigureAwait(false));
            }

            await ReportAsync(
                what,
                declared.Select((element, index) => $"{element.Name}={numbers[index]}"))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    available,
                    Is.EqualTo(browsed),
                    $"{what} has to list the nodes of the machine, in declaration order.");

                Assert.That(
                    numbers,
                    Is.EqualTo(declared.Select(element => element.Number)),
                    $"Every node has to carry its {numberBrowseName}.");
            });
        }

        private async Task<uint> ReadElementNumberAsync(
            NodeId elementId,
            string numberBrowseName,
            CancellationToken ct)
        {
            NodeId numberId = await ChildAsync(elementId, numberBrowseName, ct)
                .ConfigureAwait(false);

            DataValue value = await SessionOps.ReadValueAsync(Session, numberId, ct)
                .ConfigureAwait(false);

            return value.WrappedValue.ConvertTo(BuiltInType.UInt32).TryGetValue(out uint number)
                ? number
                : 0;
        }

        /// <summary>
        /// The nodes one non hierarchical reference of a node points at.
        /// </summary>
        private async Task<IReadOnlyList<NodeId>> TargetsAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> references = await SessionOps
                .BrowseAsync(
                    Session,
                    nodeId,
                    ct,
                    referenceTypeId: referenceTypeId,
                    includeSubtypes: false)
                .ConfigureAwait(false);

            return references
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris))
                .ToArray();
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

        /// <summary>
        /// Reads CurrentState of the Production machine, status code and all: while the child
        /// is suspended the status is what carries the answer.
        /// </summary>
        private async Task<DataValue> ReadProductionStateAsync(CancellationToken ct)
        {
            NodeId productionId = await ProductionNodeAsync(ct).ConfigureAwait(false);
            NodeId stateId = await ChildAsync(productionId, BrowseNames.CurrentState, ct)
                .ConfigureAwait(false);

            return await SessionOps.ReadValueAsync(Session, stateId, ct).ConfigureAwait(false);
        }

        private async Task<string> ReadProductionStateNameAsync(CancellationToken ct)
        {
            return StateName(await ReadProductionStateAsync(ct).ConfigureAwait(false));
        }

        private static string StateName(DataValue state)
        {
            return state.WrappedValue.TryGetValue(out LocalizedText text) ? text.Text : null;
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
        /// Calls the cause of the Production machine and reports the result.
        /// </summary>
        /// <remarks>
        /// The method belongs to the sub state machine rather than to the Operation machine,
        /// so it is called on the child object: Part 4 has the object of a Call name the node
        /// the method is a child of.
        /// </remarks>
        private async Task<CallMethodResult> CallStartBatchRawAsync(CancellationToken ct)
        {
            NodeId productionId = await ProductionNodeAsync(ct).ConfigureAwait(false);

            return await SessionOps
                .CallAsync(
                    Session,
                    productionId,
                    new NodeId(
                        StateMachinesNodeManager.StartBatchCause,
                        NamespaceIndex(StateMachinesNamespace)),
                    ct)
                .ConfigureAwait(false);
        }

        private async Task CallStartBatchAsync(CancellationToken ct)
        {
            CallMethodResult result = await CallStartBatchRawAsync(ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Calling StartBatch failed: {result.StatusCode}");
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
