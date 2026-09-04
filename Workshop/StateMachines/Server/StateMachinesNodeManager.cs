/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.StateMachines;

namespace Quickstarts.StateMachines.Server
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class StateMachinesNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new StateMachinesNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.StateMachines];
    }

    /// <summary>
    /// A node manager which exposes the two ways of building an OPC UA Part 16 state machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both machines hang below a single <c>Machine</c> object and are built with the same
    /// <see cref="StateMachineBuilder"/>, in its two modes:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Definition mode</b> (<see cref="StateMachineBuilder.Create"/>) builds the
    /// <c>Operation</c> machine. There is no ObjectType for it anywhere - the states, the
    /// transitions and the mapping from a method call to a transition are declared inline,
    /// and the builder produces a <see cref="FluentFiniteStateMachineState"/> which serves
    /// them. That is what a vendor machine which is in no companion specification looks like.
    /// </description></item>
    /// <item><description>
    /// <b>Lifecycle mode</b> (<see cref="StateMachineBuilder.For{TState}"/>) attaches
    /// behaviour to the <c>Program</c> machine, which is a stack shipped
    /// <see cref="ProgramStateMachineState"/>: its state, transition and cause tables come
    /// from OPC 10000-10, and the sample only adds handlers and an automatic transition on
    /// top of them. A generator emitted or vendor state machine subclass is used the same way.
    /// </description></item>
    /// </list>
    /// <para>
    /// The address space is built without an information model, because that keeps the
    /// state machines and nothing else in view. A server which declares its machine in a
    /// ModelDesign gets a generated <c>*StateMachineState</c> subclass and uses lifecycle
    /// mode for it, exactly the way <c>Program</c> is used here.
    /// </para>
    /// <para>
    /// The node manager is a hand-written <see cref="FluentNodeManagerBase"/>: it drives the
    /// fluent builder itself in <see cref="CreateAddressSpaceAsync"/>, the way the generated
    /// node manager of a ModelDesign does. What the sample numbers itself - the machine
    /// object, the two state machines and the cause methods - is built by hand and registered
    /// as predefined nodes, because the builder mints the identifiers of what it creates.
    /// Everything else goes through the builder in <see cref="Configure"/>: the link below
    /// the Objects folder, the notifier link to the server object and the two properties of
    /// the machine, whose references to nodes of other node managers are published by
    /// <see cref="FluentNodeManagerBase.CompleteConfigureAsync"/>.
    /// </para>
    /// </remarks>
    public class StateMachinesNodeManager : FluentNodeManagerBase
    {
        #region Node Identifiers
        /// <summary>
        /// The object both state machines belong to.
        /// </summary>
        public const uint MachineId = 1;

        /// <summary>
        /// The state machine built in definition mode.
        /// </summary>
        public const uint OperationId = 10;

        /// <summary>
        /// The stack shipped program state machine, used in lifecycle mode.
        /// </summary>
        public const uint ProgramId = 20;

        /// <summary>
        /// The optional LastTransition variable of the Program machine.
        /// </summary>
        public const uint ProgramLastTransitionId = 21;

        // The cause methods of the Operation machine. A cause id is the numeric identifier of
        // the method node which triggers it: that is the convention WithCause relies on, and
        // the one the standard state machines follow.

        /// <summary>The PowerOn method, and the id of the cause it triggers.</summary>
        public const uint PowerOnCause = 101;

        /// <summary>The PowerOff method, and the id of the cause it triggers.</summary>
        public const uint PowerOffCause = 102;

        /// <summary>The Start method, and the id of the cause it triggers.</summary>
        public const uint StartCause = 103;

        /// <summary>The Stop method, and the id of the cause it triggers.</summary>
        public const uint StopCause = 104;

        /// <summary>The Fault method, and the id of the cause it triggers.</summary>
        public const uint FaultCause = 105;

        /// <summary>The Reset method, and the id of the cause it triggers.</summary>
        public const uint ResetCause = 106;

        // The states and transitions of the Operation machine. They are numbered from a range
        // of their own: the framework materializes a node per state and per transition, and
        // the number becomes its StateNumber or TransitionNumber, so a state id which is also
        // the id of an object of the sample would be confusing to read.

        /// <summary>The machine is powered down.</summary>
        public const uint OffState = 1001;

        /// <summary>The machine is powered up and waiting to be started.</summary>
        public const uint IdleState = 1002;

        /// <summary>The machine is producing.</summary>
        public const uint RunningState = 1003;

        /// <summary>The machine stopped on a fault.</summary>
        public const uint FaultedState = 1004;

        /// <summary>Off to Idle, caused by PowerOn.</summary>
        public const uint OffToIdleTransition = 2001;

        /// <summary>Idle to Off, caused by PowerOff.</summary>
        public const uint IdleToOffTransition = 2002;

        /// <summary>Idle to Running, caused by Start.</summary>
        public const uint IdleToRunningTransition = 2003;

        /// <summary>Running to Idle, caused by Stop.</summary>
        public const uint RunningToIdleTransition = 2004;

        /// <summary>Running to Faulted, caused by Fault.</summary>
        public const uint RunningToFaultedTransition = 2005;

        /// <summary>Faulted to Idle, caused by Reset or by the automatic reset.</summary>
        public const uint FaultedToIdleTransition = 2006;
        #endregion

        #region Timings
        /// <summary>
        /// How long the Operation machine stays in Faulted before it resets itself.
        /// </summary>
        public static readonly TimeSpan AutomaticResetDelay = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How long the Program machine runs before it completes on its own.
        /// </summary>
        public static readonly TimeSpan ProgramRunDuration = TimeSpan.FromSeconds(15);
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public StateMachinesNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.StateMachines)
        {
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId of a node which does not carry one of the sample's own.
        /// </summary>
        /// <remarks>
        /// The sample numbers the nodes it declares itself, and those keep their id. The
        /// children a standard type definition brings along - <c>CurrentState</c>, its
        /// <c>Id</c>, the arguments of a method - are created from the type node and come out
        /// carrying the NodeId of that <i>type</i>, which would put them in the OPC UA
        /// namespace and outside this node manager. They are given a fresh id here instead.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            NodeId nodeId = node?.NodeId ?? NodeId.Null;

            if (!nodeId.IsNull && nodeId.NamespaceIndex == NamespaceIndex)
            {
                return nodeId;
            }

            return new NodeId(Utils.IncrementIdentifier(ref m_lastUsedId), NamespaceIndex);
        }
        #endregion

        #region IAsyncNodeManager Members
        /// <summary>
        /// Builds the address space of the sample.
        /// </summary>
        /// <remarks>
        /// The sequence is the one the generated node manager of a ModelDesign follows: the
        /// predefined nodes first, then the builder, then
        /// <see cref="FluentNodeManagerBase.CompleteConfigureAsync"/>, which publishes the
        /// references written in <see cref="Configure"/> to the node managers which own their
        /// targets, and finally <see cref="NodeManagerBuilder.Seal"/>.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // the object which owns both machines. It carries a numeric id of the sample's
            // own, as do the machines and the cause methods below it, so it is built by hand
            // and registered before the builder runs: the builder resolves the nodes it is
            // asked to wire among the predefined nodes.
            BaseObjectState machine = CreateMachine();
            await AddPredefinedNodeAsync(SystemContext, machine, cancellationToken).ConfigureAwait(false);

            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndex).Configure(Configure);

            // the inverse references Configure wrote to the Objects folder and to the Server
            // object belong to other node managers. This pass hands them over, and registers
            // the machine as a root notifier on the strength of its HasNotifier reference.
            await CompleteConfigureAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            builder.Seal();
        }

        /// <summary>
        /// Wires what the builder can express: where the machine hangs, whom it notifies and
        /// the two properties a client reads and writes.
        /// </summary>
        /// <remarks>
        /// The properties are created by the builder, so they carry the identifiers it mints
        /// rather than numbers of the sample; a client finds them by browse name. The
        /// interlock is written by a client and read by the guard of the Start cause, the
        /// counter is written by the transition observer through the updater it binds.
        /// </remarks>
        private void Configure(INodeManagerBuilder builder)
        {
            builder.Node(new NodeId(MachineId, NamespaceIndex))
                // ensure the machine can be found below the objects folder.
                .UnderObjectsFolder()

                // a transition reports a TransitionEventType, so the machine is a notifier
                // and the events find their way to the server object from here.
                .AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server)

                // writable, so that a client can watch a guard refuse a transition: clear
                // the interlock, call Start, and the call is answered with BadInvalidState
                // while the machine stays where it is.
                .WithProperty(
                    "SafetyInterlockClear",
                    Variant.From(true),
                    interlock => interlock
                        .Writable()
                        .AsVariable<bool>()
                        .OnWrite(clear => m_interlockClear = clear))

                // counts the transitions of the Operation machine.
                .WithProperty(
                    "TransitionCount",
                    Variant.From(0u),
                    counter => counter
                        .AsVariable<uint>()
                        .Bind(out m_transitionCount));
        }

        /// <summary>
        /// Creates the object both state machines belong to, and the machines below it.
        /// </summary>
        private BaseObjectState CreateMachine()
        {
#pragma warning disable CA2000 // Justification: node ownership is transferred to the server address space.
            var machine = new BaseObjectState(null);
#pragma warning restore CA2000

            machine.NodeId = new NodeId(MachineId, NamespaceIndex);
            machine.BrowseName = new QualifiedName("Machine", NamespaceIndex);
            machine.DisplayName = new LocalizedText(machine.BrowseName.Name);
            machine.TypeDefinitionId = ObjectTypeIds.BaseObjectType;

            // the machine is a notifier; Configure links it to the server object.
            machine.EventNotifier = EventNotifiers.SubscribeToEvents;

            CreateOperationStateMachine(machine);
            CreateProgramStateMachine(machine);

            return machine;
        }
        #endregion

        #region Definition Mode
        /// <summary>
        /// Builds the Operation machine, which has no type definition of its own.
        /// </summary>
        /// <remarks>
        /// Everything the machine is, is in this one chain: the states, the transitions
        /// between them, which method call causes which transition, and the behaviour which
        /// hangs off entering and leaving a state. The builder freezes the definition as soon
        /// as the first lifecycle method is called - <c>WithInitialState</c> below - and
        /// creates the node in the address space at that point.
        /// </remarks>
        private void CreateOperationStateMachine(BaseObjectState machine)
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder = StateMachineBuilder
                .Create(
                    machine,
                    SystemContext,
                    new NodeId(OperationId, NamespaceIndex),
                    new QualifiedName("Operation", NamespaceIndex))

                // CurrentState/Id and LastTransition/Id are NodeIds built from the state and
                // transition ids below. Without this they would land in the OPC UA namespace,
                // which must never carry the nodes of a vendor.
                .UseElementNamespace(Namespaces.StateMachines)

                .AddState(OffState, "Off", isInitial: true)
                .AddState(IdleState, "Idle")
                .AddState(RunningState, "Running")
                .AddState(FaultedState, "Faulted")

                .AddTransition(OffToIdleTransition, "OffToIdle", from: OffState, to: IdleState)
                .AddTransition(IdleToOffTransition, "IdleToOff", from: IdleState, to: OffState)
                .AddTransition(IdleToRunningTransition, "IdleToRunning", from: IdleState, to: RunningState)
                .AddTransition(RunningToIdleTransition, "RunningToIdle", from: RunningState, to: IdleState)
                .AddTransition(RunningToFaultedTransition, "RunningToFaulted", from: RunningState, to: FaultedState)
                .AddTransition(FaultedToIdleTransition, "FaultedToIdle", from: FaultedState, to: IdleState)

                // a cause is only permitted in the state it is declared for: calling Start
                // while the machine is Off is answered with BadNotSupported, because no
                // mapping for the pair (Start, Off) exists.
                .OnCause(PowerOnCause, from: OffState, transition: OffToIdleTransition)
                .OnCause(PowerOffCause, from: IdleState, transition: IdleToOffTransition)
                .OnCause(StartCause, from: IdleState, transition: IdleToRunningTransition)
                .OnCause(StopCause, from: RunningState, transition: RunningToIdleTransition)
                .OnCause(FaultCause, from: RunningState, transition: RunningToFaultedTransition)
                .OnCause(ResetCause, from: FaultedState, transition: FaultedToIdleTransition)

                .WithInitialState(OffState);

            // the machine now exists, with a node per state and per transition below it, and
            // with LastTransition: the optional child a client watches to see the transitions
            // rather than only the states is materialized along with the transitions it names.
            FluentFiniteStateMachineState operation = builder.StateMachine;

            operation.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            machine.AddChild(operation);

            // the methods have to be children of the machine before they can be bound to a
            // cause: WithCause looks the method up below the state machine node.
            AddCauseMethod(operation, PowerOnCause, "PowerOn");
            AddCauseMethod(operation, PowerOffCause, "PowerOff");
            AddCauseMethod(operation, StartCause, "Start");
            AddCauseMethod(operation, StopCause, "Stop");
            AddCauseMethod(operation, FaultCause, "Fault");
            AddCauseMethod(operation, ResetCause, "Reset");

            builder
                // one line per method: an inbound call now runs the cause pipeline, which
                // resolves the transition for the current state and drives it.
                .WithCause(new NodeId(PowerOnCause, NamespaceIndex))
                .WithCause(new NodeId(PowerOffCause, NamespaceIndex))
                .WithCause(new NodeId(StartCause, NamespaceIndex))
                .WithCause(new NodeId(StopCause, NamespaceIndex))
                .WithCause(new NodeId(FaultCause, NamespaceIndex))
                .WithCause(new NodeId(ResetCause, NamespaceIndex))

                // a guard on one cause. Returning false vetoes the transition before any state
                // is changed, with the status code given here.
                .WhenCause(
                    StartCause,
                    (context, stateMachine) => m_interlockClear,
                    new ServiceResult(StatusCodes.BadInvalidState))

                // the observers. OnExitState, OnTransition and OnEnterState run in that order,
                // once the framework has moved the machine.
                .OnEnterState(
                    RunningState,
                    (context, stateMachine) => m_logger.LogInformation("The machine started producing."))
                .OnExitState(
                    RunningState,
                    (context, stateMachine) => m_logger.LogInformation("The machine stopped producing."))
                .OnTransition(OnOperationTransition)

                // the async overload runs on the thread pool, without holding up the
                // transition or the client which caused it.
                .OnEnterStateAsync(FaultedState, OnFaultedAsync)

                // and a transition nothing calls: the machine leaves Faulted on its own once
                // the delay has passed. The timer is armed on every entry into Faulted and
                // cancelled again when the state is left.
                .WithTimedTransition(
                    fromStateId: FaultedState,
                    timeout: AutomaticResetDelay,
                    transitionId: FaultedToIdleTransition,
                    causeId: ResetCause);
        }

        /// <summary>
        /// Adds one of the methods which cause a transition of the Operation machine.
        /// </summary>
        /// <remarks>
        /// The method needs no call handler of its own: <c>WithCause</c> installs one which
        /// calls <c>DoCause</c> with the numeric identifier of the method node.
        /// </remarks>
        private void AddCauseMethod(FluentFiniteStateMachineState stateMachine, uint causeId, string name)
        {
#pragma warning disable CA2000 // Justification: node ownership is transferred to the server address space.
            var method = new MethodState(stateMachine);
#pragma warning restore CA2000

            method.NodeId = new NodeId(causeId, NamespaceIndex);
            method.BrowseName = new QualifiedName(name, NamespaceIndex);
            method.DisplayName = new LocalizedText(name);
            method.ReferenceTypeId = ReferenceTypeIds.HasComponent;

            BindExecutable(method, stateMachine, causeId);

            stateMachine.AddChild(method);
        }

        /// <summary>
        /// Answers the Executable attributes of a cause method from the state machine.
        /// </summary>
        /// <remarks>
        /// A cause is only permitted in the states it was declared for, so whether a method
        /// can be called right now is a property of the machine rather than a constant. OPC
        /// 10000-16 has the server say so through <c>Executable</c> / <c>UserExecutable</c>,
        /// which is what lets a client offer only the causes which currently apply instead of
        /// calling one and being refused with BadNotSupported. The callbacks run per read, so
        /// the answer follows the machine without anything having to update the node.
        /// </remarks>
        private static void BindExecutable(
            MethodState method,
            FiniteStateMachineState stateMachine,
            uint causeId)
        {
            method.Executable = true;
            method.UserExecutable = true;

            method.OnReadExecutable = (ISystemContext context, NodeState node, ref bool value) => {
                value = stateMachine.IsCausePermitted(context, causeId, checkUserAccessRights: false);
                return ServiceResult.Good;
            };

            method.OnReadUserExecutable = (ISystemContext context, NodeState node, ref bool value) => {
                value = stateMachine.IsCausePermitted(context, causeId, checkUserAccessRights: true);
                return ServiceResult.Good;
            };
        }

        /// <summary>
        /// Counts the transitions of the Operation machine.
        /// </summary>
        /// <remarks>
        /// A transition observer runs on whichever thread drove the transition - the thread of
        /// the method call, or a timer thread for the automatic reset. The updater the
        /// builder bound to the counter writes the value and flushes the change synchronously,
        /// the way the sampling groups of the server do it.
        /// </remarks>
        private void OnOperationTransition(
            ISystemContext context,
            FluentFiniteStateMachineState stateMachine,
            uint fromState,
            uint toState)
        {
            m_transitionCount.SetValue((uint)Interlocked.Increment(ref m_transitions));

            LogTransition("Operation", fromState, toState);
        }

        /// <summary>
        /// Reports a completed transition of one of the two machines.
        /// </summary>
        private void LogTransition(string stateMachine, uint fromState, uint toState)
        {
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation(
                    "The {StateMachine} machine moved from state {FromState} to state {ToState}.",
                    stateMachine,
                    fromState,
                    toState);
            }
        }

        /// <summary>
        /// Reports that the machine faulted, off the transition path.
        /// </summary>
        private ValueTask OnFaultedAsync(
            ISystemContext context,
            FluentFiniteStateMachineState stateMachine,
            CancellationToken cancellationToken)
        {
            m_logger.LogWarning(
                "The machine faulted and resets itself in {Delay}.",
                AutomaticResetDelay);

            return ValueTask.CompletedTask;
        }
        #endregion

        #region Lifecycle Mode
        /// <summary>
        /// Adds the stack shipped program state machine and attaches behaviour to it.
        /// </summary>
        /// <remarks>
        /// Nothing here declares a state or a transition. <see cref="ProgramStateMachineState"/>
        /// brings the tables of OPC 10000-10 with it - Ready, Running, Suspended, Halted and
        /// the nine transitions between them - and the builder only layers handlers on top. A
        /// state machine emitted by the source generator from a companion specification or
        /// from a vendor ModelDesign is used in exactly this way.
        /// </remarks>
        private void CreateProgramStateMachine(BaseObjectState machine)
        {
#pragma warning disable CA2000 // Justification: node ownership is transferred to the server address space.
            var program = new ProgramStateMachineState(null);
#pragma warning restore CA2000

            program.Create(
                SystemContext,
                new NodeId(ProgramId, NamespaceIndex),
                new QualifiedName("Program", NamespaceIndex),
                default,
                true);

            program.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            machine.AddChild(program);

            program.AddLastTransition(
                SystemContext,
                new NodeId(ProgramLastTransitionId, NamespaceIndex));

            // the five methods of ProgramStateMachineType are optional children. Their cause
            // ids are the ones of the standard type, so they are bound by hand rather than
            // with WithCause, which derives the cause from the id of the instance method.
            BindCause(
                program.AddStart(SystemContext, new QualifiedName(BrowseNames.Start)),
                program,
                Methods.ProgramStateMachineType_Start);
            BindCause(
                program.AddSuspend(SystemContext, new QualifiedName(BrowseNames.Suspend)),
                program,
                Methods.ProgramStateMachineType_Suspend);
            BindCause(
                program.AddResume(SystemContext, new QualifiedName(BrowseNames.Resume)),
                program,
                Methods.ProgramStateMachineType_Resume);
            BindCause(
                program.AddHalt(SystemContext, new QualifiedName(BrowseNames.Halt)),
                program,
                Methods.ProgramStateMachineType_Halt);
            BindCause(
                program.AddReset(SystemContext, new QualifiedName(BrowseNames.Reset)),
                program,
                Methods.ProgramStateMachineType_Reset);

            StateMachineBuilder.For(program, SystemContext)
                .OnEnterState(
                    Objects.ProgramStateMachineType_Running,
                    (context, stateMachine) => m_logger.LogInformation("The program is running."))
                .OnEnterState(
                    Objects.ProgramStateMachineType_Halted,
                    (context, stateMachine) => m_logger.LogInformation("The program was halted."))
                .OnTransition(
                    (context, stateMachine, fromState, toState) => LogTransition("Program", fromState, toState))

                // the program is not endless: it returns to Ready on its own once it has run
                // for long enough, which is a transition a client sees without asking for it.
                .WithTimedTransition(
                    fromStateId: Objects.ProgramStateMachineType_Running,
                    timeout: ProgramRunDuration,
                    transitionId: Objects.ProgramStateMachineType_RunningToReady);
        }

        /// <summary>
        /// Routes a method of the program machine to the cause it stands for.
        /// </summary>
        private static void BindCause(MethodState method, ProgramStateMachineState program, uint causeId)
        {
            // the same Executable contract as the Operation machine, so a client can offer the
            // program's five causes the same way it offers the vendor machine's six.
            BindExecutable(method, program, causeId);

            method.OnCallMethod2Async = (context, called, objectId, inputArguments, outputArguments, cancellationToken)
                => new ValueTask<ServiceResult>(
                    program.DoCause(context, called, causeId, inputArguments, outputArguments));
        }
        #endregion

        #region Private Fields
        private uint m_lastUsedId = 1000000;
        private long m_transitions;

        /// <summary>
        /// The interlock the guard of the Start cause reads. A client writes it through the
        /// property, the guard reads it on the thread of the method call.
        /// </summary>
        private volatile bool m_interlockClear = true;

        /// <summary>
        /// The updater of the TransitionCount property, bound by the builder.
        /// </summary>
        private IValueUpdater<uint> m_transitionCount;
        #endregion
    }
}
