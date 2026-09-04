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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Samples.Client;

namespace Quickstarts.StateMachines.Client.Model
{
    /// <summary>
    /// The two state machines of the sample server.
    /// </summary>
    public enum StateMachineKind
    {
        /// <summary>The vendor defined machine which drives the equipment (Off, Idle, Running, Faulted).</summary>
        Operation,

        /// <summary>The standard ProgramStateMachineType instance beside it.</summary>
        Program,
    }

    /// <summary>
    /// The causes of the Operation machine, in the order the server declares them.
    /// </summary>
    public enum OperationCause
    {
        /// <summary>Off to Idle.</summary>
        PowerOn,

        /// <summary>Idle to Off.</summary>
        PowerOff,

        /// <summary>Idle to Running, guarded by the interlock.</summary>
        Start,

        /// <summary>Running to Idle.</summary>
        Stop,

        /// <summary>Running to Faulted.</summary>
        Fault,

        /// <summary>Faulted to Idle.</summary>
        Reset,
    }

    /// <summary>
    /// The five methods the standard program state machine declares.
    /// </summary>
    public enum ProgramCause
    {
        /// <summary>Ready to Running.</summary>
        Start,

        /// <summary>Running to Suspended.</summary>
        Suspend,

        /// <summary>Suspended to Running.</summary>
        Resume,

        /// <summary>Running or Suspended to Halted.</summary>
        Halt,

        /// <summary>Halted to Ready.</summary>
        Reset,
    }

    /// <summary>
    /// The causes of the sub state machine below the Running state of the Operation machine.
    /// </summary>
    /// <remarks>
    /// A sub state machine has causes of its own, and they are methods of the child rather
    /// than of the machine the client started from. The sample server declares one.
    /// </remarks>
    public enum ProductionCause
    {
        /// <summary>Loading to Processing; the rest of the batch is timed by the server.</summary>
        StartBatch,
    }

    /// <summary>
    /// What a row of the model of a machine describes.
    /// </summary>
    public enum StateMachineElement
    {
        /// <summary>A state the machine can be in.</summary>
        State,

        /// <summary>A transition between two of its states.</summary>
        Transition,
    }

    /// <summary>
    /// One state or transition of the model a Part 16 machine publishes.
    /// </summary>
    /// <param name="Kind">Whether the row is a state or a transition.</param>
    /// <param name="BrowseName">The browse name of the node, as text.</param>
    /// <param name="Number">The StateNumber or TransitionNumber the node carries.</param>
    /// <param name="NodeId">
    /// The node, which is what CurrentState/Id and LastTransition/Id answer with.
    /// </param>
    /// <param name="SubMachineName">
    /// The browse name of the machine which runs while the parent is in this state, or
    /// empty for a state without one and for every transition.
    /// </param>
    public sealed record StateMachineModelRow(
        StateMachineElement Kind,
        string BrowseName,
        uint Number,
        NodeId NodeId,
        string SubMachineName);

    /// <summary>
    /// Where a sub state machine is, or why it has nothing to report.
    /// </summary>
    /// <param name="State">The display name of the current state, or empty while suspended.</param>
    /// <param name="Status">The status the server reported the state with.</param>
    /// <param name="Timestamp">When the server observed it, in UTC.</param>
    public sealed record SubStateMachineSnapshot(
        string State,
        StatusCode Status,
        DateTime Timestamp)
    {
        /// <summary>
        /// True while the parent is in the state the machine belongs to.
        /// </summary>
        public bool IsActive => !StatusCode.IsBad(Status);

        /// <summary>
        /// The state as text, or the status while the machine is suspended.
        /// </summary>
        /// <remarks>
        /// OPC 10000-16 §4.4.6: a suspended sub state machine reports its state variables
        /// with Bad_StateNotActive rather than with the state it stopped in, so the status
        /// is the answer and the value below it means nothing.
        /// </remarks>
        public string Describe()
        {
            return IsActive ? State : $"({Status})";
        }
    }

    /// <summary>
    /// Where one machine is, and how it got there.
    /// </summary>
    /// <param name="Machine">Which machine the snapshot describes.</param>
    /// <param name="State">The display name of the current state.</param>
    /// <param name="Transition">The display name of the last transition, or empty.</param>
    /// <param name="Timestamp">When the server observed it, in UTC.</param>
    /// <param name="SubMachine">
    /// Where the sub state machine of the current state is, or null when the state has none.
    /// </param>
    public sealed record StateMachineSnapshot(
        StateMachineKind Machine,
        string State,
        string Transition,
        DateTime Timestamp,
        SubStateMachineSnapshot SubMachine = null)
    {
        /// <summary>
        /// The state as text: both states at once for a hierarchical machine, so that a
        /// transition of the child is not mistaken for one of the parent standing still.
        /// </summary>
        public string Describe()
        {
            return SubMachine == null ? State : $"{State} / {SubMachine.Describe()}";
        }
    }

    /// <summary>
    /// Which causes the machines permit in the state they are in right now.
    /// </summary>
    /// <remarks>
    /// A cause the server did not answer for counts as permitted: the client then stays
    /// usable against a server which does not fill in the UserExecutable attribute, and
    /// a call which cannot apply is still refused by the server itself. The one exception
    /// is a cause of the sub state machine: a server without a sub state machine has no
    /// method to call, so a cause which was not resolved is not permitted.
    /// </remarks>
    /// <param name="Operation">The causes of the Operation machine which are permitted.</param>
    /// <param name="Program">The causes of the Program machine which are permitted.</param>
    /// <param name="Production">The causes of the sub state machine which are permitted.</param>
    public sealed record PermittedCauses(
        IReadOnlyDictionary<OperationCause, bool> Operation,
        IReadOnlyDictionary<ProgramCause, bool> Program,
        IReadOnlyDictionary<ProductionCause, bool> Production)
    {
        /// <summary>
        /// Every cause permitted: what the client assumes until it has read the attributes.
        /// </summary>
        public static PermittedCauses All { get; } = new PermittedCauses(
            Enum.GetValues<OperationCause>().ToDictionary(cause => cause, _ => true),
            Enum.GetValues<ProgramCause>().ToDictionary(cause => cause, _ => true),
            Enum.GetValues<ProductionCause>().ToDictionary(cause => cause, _ => true));

        /// <summary>
        /// Whether a cause of the Operation machine may be called.
        /// </summary>
        public bool IsPermitted(OperationCause cause)
        {
            return !Operation.TryGetValue(cause, out bool permitted) || permitted;
        }

        /// <summary>
        /// Whether a cause of the Program machine may be called.
        /// </summary>
        public bool IsPermitted(ProgramCause cause)
        {
            return !Program.TryGetValue(cause, out bool permitted) || permitted;
        }

        /// <summary>
        /// Whether a cause of the sub state machine may be called: only while the server
        /// has one, and only while the machine it belongs to is active.
        /// </summary>
        public bool IsPermitted(ProductionCause cause)
        {
            return Production.TryGetValue(cause, out bool permitted) && permitted;
        }
    }

    /// <summary>
    /// The payload of <see cref="StateMachinesClientModel.TransitionObserved"/>.
    /// </summary>
    public sealed class TransitionObservedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public TransitionObservedEventArgs(StateMachineSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>
        /// Where the machine is after the transition.
        /// </summary>
        public StateMachineSnapshot Snapshot { get; }
    }

    /// <summary>
    /// The payload of <see cref="StateMachinesClientModel.ProductionStateChanged"/>.
    /// </summary>
    public sealed class ProductionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public ProductionStateChangedEventArgs(SubStateMachineSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>
        /// Where the sub state machine is now.
        /// </summary>
        public SubStateMachineSnapshot Snapshot { get; }
    }

    /// <summary>
    /// The payload of <see cref="StateMachinesClientModel.PermittedCausesChanged"/>.
    /// </summary>
    public sealed class PermittedCausesChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public PermittedCausesChangedEventArgs(PermittedCauses causes)
        {
            Causes = causes;
        }

        /// <summary>
        /// What the machines permit now.
        /// </summary>
        public PermittedCauses Causes { get; }
    }

    /// <summary>
    /// The client model of the StateMachines client: drives the two state machines of the
    /// sample server and streams their transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client never browses the state or transition variables of a machine itself. The
    /// generic Part 16 client API does that: <see cref="FiniteStateMachineTypeClient"/> wraps
    /// any finite state machine, and the extension methods on it read the current state
    /// (<c>GetCurrentFiniteStateAsync</c>) and stream a fresh snapshot per transition
    /// (<c>ObserveFiniteTransitionsAsync</c>). That is the same code for the vendor machine
    /// and for the standard program machine.
    /// </para>
    /// <para>
    /// What differs is how the two are driven. The vendor machine has no type definition, so
    /// its causes are plain method calls. The program machine is a standard type, so the
    /// source generated <see cref="ProgramStateMachineTypeClient"/> proxy exposes its causes
    /// as <c>StartAsync</c>, <c>SuspendAsync</c> and so on.
    /// </para>
    /// <para>
    /// The vendor machine is also hierarchical, and the model exposes the two halves of that.
    /// <c>GetAvailableStatesAsync</c> and <c>GetAvailableTransitionsAsync</c> read the model a
    /// Part 16 machine publishes - a node per state and per transition, each with its number -
    /// into <see cref="OperationModel"/>, and <c>GetSubStateMachineAsync</c> follows the
    /// <c>HasSubStateMachine</c> reference of a state node to the machine which runs while
    /// the parent is in it. <c>ObserveEffectiveStateAsync</c> then streams both machines at
    /// once, so a transition of either is reported as one combined snapshot, and
    /// <see cref="ProductionState"/> follows the child.
    /// </para>
    /// <para>
    /// <see cref="PermittedCauses"/> is final when <see cref="SampleClientModel.AttachAsync"/>
    /// returns, and every later change of it - after a call, after a transition on the
    /// stream, after a reconnect - is reported through <see cref="PermittedCausesChanged"/>.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The handles below are taken, cleared and released by OnDetachingAsync, which the detach of the base class runs - on a detach as well as on a dispose. The analyzer does not follow an asynchronous release through a virtual hook.")]
    public sealed class StateMachinesClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the sample, for a caller which cannot name the constant.
        /// </summary>
        public const string StateMachinesNamespaceUri = Namespaces.StateMachines;

        /// <summary>
        /// The browse names of the causes of the Operation machine, in the sample namespace.
        /// </summary>
        private static readonly (OperationCause Cause, string BrowseName)[] s_operationCauses =
        {
            (OperationCause.PowerOn, "PowerOn"),
            (OperationCause.PowerOff, "PowerOff"),
            (OperationCause.Start, "Start"),
            (OperationCause.Stop, "Stop"),
            (OperationCause.Fault, "Fault"),
            (OperationCause.Reset, "Reset"),
        };

        /// <summary>
        /// The browse names of the methods of the program machine. They carry the names of
        /// the standard type, so they live in the OPC UA namespace rather than in the one
        /// of the sample.
        /// </summary>
        private static readonly (ProgramCause Cause, string BrowseName)[] s_programCauses =
        {
            (ProgramCause.Start, BrowseNames.Start),
            (ProgramCause.Suspend, BrowseNames.Suspend),
            (ProgramCause.Resume, BrowseNames.Resume),
            (ProgramCause.Halt, BrowseNames.Halt),
            (ProgramCause.Reset, BrowseNames.Reset),
        };

        /// <summary>
        /// The browse names of the causes of the sub state machine, in the sample namespace.
        /// </summary>
        private static readonly (ProductionCause Cause, string BrowseName)[] s_productionCauses =
        {
            (ProductionCause.StartBatch, "StartBatch"),
        };

        // the reads of the UserExecutable attributes are serialised: the stream refreshes
        // on a publish worker while a click refreshes on the caller's thread, and a slow
        // older read must not report a stale map over a newer one.
        private readonly SemaphoreSlim m_refresh = new SemaphoreSlim(1, 1);

        private FiniteStateMachineTypeClient m_operation;
        private ProgramStateMachineTypeClient m_program;

        /// <summary>
        /// The machine which runs below one of the states of the Operation machine, found
        /// through the HasSubStateMachine reference of that state rather than by name.
        /// </summary>
        private FiniteStateMachineTypeClient m_production;

        private NodeId m_interlockNode = NodeId.Null;

        // replaced as a whole when the session changes, so a refresh never sees a half
        // built table.
        private IReadOnlyDictionary<OperationCause, NodeId> m_operationCauseIds = new Dictionary<OperationCause, NodeId>();
        private IReadOnlyDictionary<ProgramCause, NodeId> m_programCauseIds = new Dictionary<ProgramCause, NodeId>();
        private IReadOnlyDictionary<ProductionCause, NodeId> m_productionCauseIds = new Dictionary<ProductionCause, NodeId>();

        private StreamingSubscription m_streaming;
        private SubscriptionPump m_pump;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public StateMachinesClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// Where the Operation machine is, or null while detached.
        /// </summary>
        public StateMachineSnapshot OperationState { get; private set; }

        /// <summary>
        /// Where the Program machine is, or null while detached.
        /// </summary>
        public StateMachineSnapshot ProgramState { get; private set; }

        /// <summary>
        /// What the Operation machine is made of: a row per state and per transition, in
        /// the order the server lists them. Empty against a server which materializes
        /// neither AvailableStates nor AvailableTransitions, and while detached.
        /// </summary>
        public IReadOnlyList<StateMachineModelRow> OperationModel { get; private set; } = Array.Empty<StateMachineModelRow>();

        /// <summary>
        /// The browse name of the sub state machine of the Operation machine, or null when
        /// the server declares none.
        /// </summary>
        public string SubStateMachineName { get; private set; }

        /// <summary>
        /// Where the sub state machine is, or null when there is none and while detached.
        /// </summary>
        public SubStateMachineSnapshot ProductionState { get; private set; }

        /// <summary>
        /// The interlock the guard of the Start cause reads, as last read or written.
        /// </summary>
        public bool InterlockClear { get; private set; }

        /// <summary>
        /// Which causes the machines permit right now.
        /// </summary>
        public PermittedCauses PermittedCauses { get; private set; } = PermittedCauses.All;

        /// <summary>
        /// Raised for every transition either machine reports on the stream.
        /// </summary>
        public event EventHandler<TransitionObservedEventArgs> TransitionObserved;

        /// <summary>
        /// Raised whenever the state of the sub state machine was read or observed again.
        /// </summary>
        public event EventHandler<ProductionStateChangedEventArgs> ProductionStateChanged;

        /// <summary>
        /// Raised whenever the causes the machines permit were read again.
        /// </summary>
        public event EventHandler<PermittedCausesChangedEventArgs> PermittedCausesChanged;

        /// <summary>
        /// The state of the sub state machine as text, for a display.
        /// </summary>
        /// <param name="snapshot">The snapshot, or null when the server has no sub state machine.</param>
        public static string DescribeProduction(SubStateMachineSnapshot snapshot)
        {
            return snapshot == null ? "(no sub state machine)" : snapshot.Describe();
        }

        /// <summary>
        /// Calls a cause of the Operation machine.
        /// </summary>
        /// <remarks>
        /// The Operation machine has no type definition, so there is no generated proxy for
        /// it and its causes are called as the plain methods they are. Which transition a
        /// call takes is the server's business: a method whose cause cannot apply in the
        /// current state is refused as BadNotExecutable, and the guard on Start answers
        /// BadInvalidState while the interlock is open. The refusal is thrown to the caller.
        /// </remarks>
        /// <param name="cause">The cause to call.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task CallOperationCauseAsync(OperationCause cause, CancellationToken ct = default)
        {
            ISession session = RequireSession();
            FiniteStateMachineTypeClient operation = m_operation
                ?? throw new InvalidOperationException("The Operation machine has not been resolved.");

            if (!m_operationCauseIds.TryGetValue(cause, out NodeId methodId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"The server does not declare the {cause} cause of the Operation machine.");
            }

            await session.CallAsync(operation.ObjectId, methodId, ct).ConfigureAwait(false);

            // the call just moved the machine, so what it permits has changed. Re-read it
            // here rather than waiting for the transition to come back on the stream: the
            // caller then sees the consequence of its own action immediately.
            await RefreshPermittedCausesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls a cause of the Program machine.
        /// </summary>
        /// <remarks>
        /// ProgramStateMachineType is a standard type, so the generated proxy carries its
        /// five methods and the client does not have to know their NodeIds.
        /// </remarks>
        /// <param name="cause">The cause to call.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task CallProgramCauseAsync(ProgramCause cause, CancellationToken ct = default)
        {
            RequireSession();

            ProgramStateMachineTypeClient program = m_program
                ?? throw new InvalidOperationException("The Program machine has not been resolved.");

            switch (cause)
            {
                case ProgramCause.Start:
                    await program.StartAsync(ct).ConfigureAwait(false);
                    break;
                case ProgramCause.Suspend:
                    await program.SuspendAsync(ct).ConfigureAwait(false);
                    break;
                case ProgramCause.Resume:
                    await program.ResumeAsync(ct).ConfigureAwait(false);
                    break;
                case ProgramCause.Halt:
                    await program.HaltAsync(ct).ConfigureAwait(false);
                    break;
                case ProgramCause.Reset:
                    await program.ResetAsync(ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cause));
            }

            // same as for the vendor machine: the causes the program permits changed.
            await RefreshPermittedCausesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls a cause of the sub state machine.
        /// </summary>
        /// <remarks>
        /// The cause belongs to the machine below the Running state, so the call names that
        /// object: the parent knows nothing about the methods of its child. Which is also why
        /// the cause is only permitted while the child is active - a suspended sub state
        /// machine reports none of its causes as executable, and the server refuses the call
        /// with BadNotExecutable.
        /// </remarks>
        /// <param name="cause">The cause to call.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task CallProductionCauseAsync(ProductionCause cause, CancellationToken ct = default)
        {
            ISession session = RequireSession();
            FiniteStateMachineTypeClient production = m_production
                ?? throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The server does not declare a sub state machine.");

            if (!m_productionCauseIds.TryGetValue(cause, out NodeId methodId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"The server does not declare the {cause} cause of the sub state machine.");
            }

            await session.CallAsync(production.ObjectId, methodId, ct).ConfigureAwait(false);

            await RefreshPermittedCausesAsync(ct).ConfigureAwait(false);
            await UpdateProductionStateAsync(null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens or closes the interlock the guard on the Start cause reads.
        /// </summary>
        /// <param name="clear">True to close the interlock, which lets Start through.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task WriteInterlockAsync(bool clear, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (m_interlockNode.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The server does not expose the interlock of this sample.");
            }

            var valuesToWrite = new List<WriteValue> {
                new WriteValue {
                    NodeId = m_interlockNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(clear)),
                },
            };

            WriteResponse response = await session.WriteAsync(null, valuesToWrite, ct).ConfigureAwait(false);

            List<StatusCode> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, valuesToWrite);

            if (StatusCode.IsBad(results[0]))
            {
                throw new ServiceResultException(results[0]);
            }

            InterlockClear = clear;
        }

        /// <summary>
        /// Reads the interlock the server currently has.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task<bool> ReadInterlockAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            DataValue value = await session.ReadValueAsync(m_interlockNode, ct).ConfigureAwait(false);

            InterlockClear = value.WrappedValue.TryGetValue(out bool clear) && clear;

            return InterlockClear;
        }

        /// <summary>
        /// Reads again where the sub state machine is, and reports it.
        /// </summary>
        /// <remarks>
        /// Asking the child itself is also what answers Bad_StateNotActive while it is
        /// suspended. Nothing is read, and nothing reported, against a server without one.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<SubStateMachineSnapshot> ReadProductionStateAsync(CancellationToken ct = default)
        {
            RequireSession();

            await UpdateProductionStateAsync(null, ct).ConfigureAwait(false);

            return ProductionState;
        }

        /// <summary>
        /// Reads again which causes apply to the state each machine is in right now.
        /// </summary>
        /// <remarks>
        /// A cause is only declared for some of the states of its machine, so calling one in
        /// any other state is refused with BadNotSupported. Which ones apply is not something
        /// a client should work out for itself: OPC 10000-16 has the server say so through the
        /// <c>Executable</c> and <c>UserExecutable</c> attributes of the method nodes, which
        /// the sample server answers from <c>IsCausePermitted</c>. Reading them after every
        /// transition keeps the client in step with the machine, and keeps it correct if the
        /// server ever changes its state table. The cause of the sub state machine is answered
        /// the same way, and applies in none of its states while the machine is suspended.
        /// The result is stored in <see cref="PermittedCauses"/> and reported through
        /// <see cref="PermittedCausesChanged"/>.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task RefreshPermittedCausesAsync(CancellationToken ct = default)
        {
            await m_refresh.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ISession session = Session;

                List<KeyValuePair<OperationCause, NodeId>> operationCauseIds = m_operationCauseIds.ToList();
                List<KeyValuePair<ProgramCause, NodeId>> programCauseIds = m_programCauseIds.ToList();
                List<KeyValuePair<ProductionCause, NodeId>> productionCauseIds = m_productionCauseIds.ToList();

                if (session == null || operationCauseIds.Count + programCauseIds.Count + productionCauseIds.Count == 0)
                {
                    return;
                }

                var nodesToRead = operationCauseIds
                    .Select(cause => cause.Value)
                    .Concat(programCauseIds.Select(cause => cause.Value))
                    .Concat(productionCauseIds.Select(cause => cause.Value))
                    .Select(methodId => new ReadValueId {
                        NodeId = methodId,
                        AttributeId = Attributes.UserExecutable,
                    })
                    .ToArrayOf();

                ReadResponse response = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct).ConfigureAwait(false);

                List<DataValue> results = response.Results.ToList();

                ClientBase.ValidateResponse(results, nodesToRead.ToArray());

                var operation = new Dictionary<OperationCause, bool>();
                var program = new Dictionary<ProgramCause, bool>();
                var production = new Dictionary<ProductionCause, bool>();
                int ii = 0;

                foreach (KeyValuePair<OperationCause, NodeId> cause in operationCauseIds)
                {
                    operation[cause.Key] = IsPermitted(results, ii++);
                }

                foreach (KeyValuePair<ProgramCause, NodeId> cause in programCauseIds)
                {
                    program[cause.Key] = IsPermitted(results, ii++);
                }

                foreach (KeyValuePair<ProductionCause, NodeId> cause in productionCauseIds)
                {
                    production[cause.Key] = IsPermitted(results, ii++);
                }

                PermittedCauses = new PermittedCauses(operation, program, production);

                // raised while the semaphore is held, so the reports arrive in the order the
                // attributes were read.
                Raise(PermittedCausesChanged, new PermittedCausesChangedEventArgs(PermittedCauses));
            }
            finally
            {
                m_refresh.Release();
            }
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // this client has built-in knowledge of the address space of its server: the two
            // machines, the interlock the guard of the Start cause reads, and the causes.
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.StateMachines);

            string[] browsePaths = new[] {
                    "1:Machine/1:Operation",
                    "1:Machine/1:Program",
                    "1:Machine/1:SafetyInterlockClear",
                }
                .Concat(s_operationCauses.Select(cause => $"1:Machine/1:Operation/1:{cause.BrowseName}"))
                .ToArray();

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                browsePaths).ConfigureAwait(false);

            if (nodes.Count < browsePaths.Length || nodes[0].IsNull || nodes[1].IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The server does not expose the state machines of this sample.");
            }

            // the generic proxy works for any finite state machine, the typed one adds the
            // methods the standard program state machine declares.
            m_operation = new FiniteStateMachineTypeClient(session, nodes[0], Telemetry);
            m_program = new ProgramStateMachineTypeClient(session, nodes[1], Telemetry);
            m_interlockNode = nodes[2];

            var operationCauseIds = new Dictionary<OperationCause, NodeId>();

            for (int ii = 0; ii < s_operationCauses.Length; ii++)
            {
                if (!nodes[3 + ii].IsNull)
                {
                    operationCauseIds[s_operationCauses[ii].Cause] = nodes[3 + ii];
                }
            }

            m_operationCauseIds = operationCauseIds;
            m_programCauseIds = await ResolveProgramCausesAsync(session, m_program, ct).ConfigureAwait(false);

            // what the Operation machine is made of, and which of its states has a machine of
            // its own below it. That is where m_production comes from.
            await ReadOperationModelAsync(session, ct).ConfigureAwait(false);
            m_productionCauseIds = await ResolveProductionCausesAsync(session, m_production, wellKnownNamespaceUris, ct).ConfigureAwait(false);

            // where the machines are right now, before anything moves them.
            OperationState = ToSnapshot(
                StateMachineKind.Operation,
                await m_operation.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false));
            ProgramState = ToSnapshot(
                StateMachineKind.Program,
                await m_program.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false));

            await UpdateProductionStateAsync(null, ct).ConfigureAwait(false);
            await ReadInterlockAsync(ct).ConfigureAwait(false);

            // the streaming subscription lives as long as the connection: the underlying OPC
            // UA subscription is created when the first enumeration starts.
            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, SampleSession.DefaultSubscriptionOptions);

            // nothing is awaited here on purpose: both enumerations run for as long as the
            // model is attached, and stopping the pump is what unsubscribes them.
            //
            // The Operation machine is watched through ObserveEffectiveStateAsync, which
            // subscribes to the machine and to the sub state machines of its states at once
            // and reports a transition of any of them; the Program machine has no sub state
            // machine, so watching it as one machine is all there is to do.
            m_pump = new SubscriptionPump();
            m_pump.Run(token => PumpTransitionsAsync(
                StateMachineKind.Operation,
                m_operation.ObserveEffectiveStateAsync(m_streaming, Telemetry, null, token),
                token));
            m_pump.Run(token => PumpTransitionsAsync(
                StateMachineKind.Program,
                m_program.ObserveFiniteTransitionsAsync(m_streaming, null, token),
                token));

            // read last, so that what the machines permit is known when the attach returns
            // and a caller can offer the causes without a further round trip.
            await RefreshPermittedCausesAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            SubscriptionPump pump = m_pump;
            StreamingSubscription streaming = m_streaming;

            m_pump = null;
            m_streaming = null;
            m_operation = null;
            m_program = null;
            m_production = null;
            m_interlockNode = NodeId.Null;
            m_operationCauseIds = new Dictionary<OperationCause, NodeId>();
            m_programCauseIds = new Dictionary<ProgramCause, NodeId>();
            m_productionCauseIds = new Dictionary<ProductionCause, NodeId>();
            OperationState = null;
            ProgramState = null;
            ProductionState = null;
            OperationModel = Array.Empty<StateMachineModelRow>();
            SubStateMachineName = null;
            PermittedCauses = PermittedCauses.All;

            // the enumerations end first, so that nothing is reported after the detach
            // returns, then the subscription is deleted on the server. Done before the
            // session is closed: closing a session which still carries a subscription
            // waits for the publish pipeline to drain.
            if (pump != null)
            {
                await pump.DisposeAsync().ConfigureAwait(false);
            }

            if (streaming != null)
            {
                await streaming.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases the refresh lock, which outlives a detach.
        /// </summary>
        /// <remarks>
        /// The subscription and its pump belong to the session and are released by
        /// <see cref="OnDetachingAsync"/>, so that they are gone again whenever the model
        /// is detached. The semaphore which serialises the refreshes belongs to the model
        /// itself and only goes with it.
        /// </remarks>
        /// <param name="disposing">True if managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                m_refresh.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override Task OnReconnectCompletedAsync(CancellationToken ct)
        {
            // a V2 subscription belongs to the subscription manager of the session and
            // survives the reconnect together with its monitored items, so both streams
            // keep running and there is nothing to re-create. Which causes the machines
            // permit has to be read back, though: the machine may well have moved while the
            // connection was down.
            return RefreshPermittedCausesAsync(ct);
        }

        /// <summary>
        /// Resolves the five methods of the program machine.
        /// </summary>
        private static async Task<IReadOnlyDictionary<ProgramCause, NodeId>> ResolveProgramCausesAsync(
            ISession session,
            ProgramStateMachineTypeClient program,
            CancellationToken ct)
        {
            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                program.ObjectId,
                new NamespaceTable(),
                ct,
                s_programCauses.Select(cause => cause.BrowseName).ToArray()).ConfigureAwait(false);

            var causes = new Dictionary<ProgramCause, NodeId>();

            for (int ii = 0; ii < s_programCauses.Length && ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    causes[s_programCauses[ii].Cause] = nodes[ii];
                }
            }

            return causes;
        }

        /// <summary>
        /// Reads what the Operation machine is made of, and finds its sub state machine.
        /// </summary>
        /// <remarks>
        /// Nothing here is browsed by hand. A Part 16 machine publishes its own model through
        /// the optional <c>AvailableStates</c> and <c>AvailableTransitions</c> properties,
        /// which name one node per state and per transition, and the two Get methods read them
        /// together with the number each node carries. A server which materializes none of
        /// them answers with nothing, and the model stays empty instead of the client falling
        /// back to guessing what the machine can do.
        /// </remarks>
        private async Task ReadOperationModelAsync(ISession session, CancellationToken ct)
        {
            m_production = null;
            SubStateMachineName = null;

            IReadOnlyList<FiniteStateInfo> states = await m_operation
                .GetAvailableStatesAsync(ct)
                .ConfigureAwait(false);

            IReadOnlyList<FiniteTransitionInfo> transitions = await m_operation
                .GetAvailableTransitionsAsync(ct)
                .ConfigureAwait(false);

            var rows = new List<StateMachineModelRow>(states.Count + transitions.Count);

            foreach (FiniteStateInfo state in states)
            {
                // OPC 10000-16 §4.4.16 hangs HasSubStateMachine off the state node rather than
                // off the machine, because a machine can have one per state. So a client looks
                // for it there, with the NodeId AvailableStates just gave it.
                FiniteStateMachineTypeClient subMachine = await m_operation
                    .GetSubStateMachineAsync(state.NodeId, Telemetry, ct)
                    .ConfigureAwait(false);

                string subMachineName = string.Empty;

                if (subMachine != null)
                {
                    subMachineName = await ReadBrowseNameAsync(session, subMachine.ObjectId, ct).ConfigureAwait(false);

                    // the sample's server declares exactly one, below its Running state.
                    m_production = subMachine;
                    SubStateMachineName = subMachineName;
                }

                rows.Add(new StateMachineModelRow(
                    StateMachineElement.State,
                    state.BrowseName.Name ?? string.Empty,
                    state.StateNumber,
                    state.NodeId,
                    subMachineName));
            }

            foreach (FiniteTransitionInfo transition in transitions)
            {
                rows.Add(new StateMachineModelRow(
                    StateMachineElement.Transition,
                    transition.BrowseName.Name ?? string.Empty,
                    transition.TransitionNumber,
                    transition.NodeId,
                    string.Empty));
            }

            OperationModel = rows;
        }

        /// <summary>
        /// Resolves the causes of the sub state machine, if the server has one.
        /// </summary>
        /// <remarks>
        /// A sub state machine has causes of its own, and they are methods of the child rather
        /// than of the machine the client started from.
        /// </remarks>
        private static async Task<IReadOnlyDictionary<ProductionCause, NodeId>> ResolveProductionCausesAsync(
            ISession session,
            FiniteStateMachineTypeClient production,
            NamespaceTable wellKnownNamespaceUris,
            CancellationToken ct)
        {
            var causes = new Dictionary<ProductionCause, NodeId>();

            if (production == null)
            {
                return causes;
            }

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                production.ObjectId,
                wellKnownNamespaceUris,
                ct,
                s_productionCauses.Select(cause => $"1:{cause.BrowseName}").ToArray()).ConfigureAwait(false);

            for (int ii = 0; ii < s_productionCauses.Length && ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    causes[s_productionCauses[ii].Cause] = nodes[ii];
                }
            }

            return causes;
        }

        /// <summary>
        /// The browse name of a node, as text.
        /// </summary>
        private static async Task<string> ReadBrowseNameAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            var nodesToRead = new[] {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
            }.ToArrayOf();

            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct).ConfigureAwait(false);

            List<DataValue> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, nodesToRead.ToArray());

            return results[0].WrappedValue.TryGetValue(out QualifiedName browseName)
                ? browseName.Name
                : string.Empty;
        }

        /// <summary>
        /// Updates where the sub state machine is, and reports it.
        /// </summary>
        /// <remarks>
        /// The effective state stream only carries the child while the parent is in the state
        /// the child belongs to, and drops what a child reports while the parent is elsewhere -
        /// otherwise a machine which is not running would appear to be producing. So a yield
        /// without a sub machine is the moment to ask the child itself, which is also what
        /// answers <c>Bad_StateNotActive</c> while it is suspended.
        /// </remarks>
        /// <param name="fromStream">What the stream carried, or null to ask the child.</param>
        /// <param name="ct">The cancellation token.</param>
        private async Task UpdateProductionStateAsync(FiniteStateSnapshot fromStream, CancellationToken ct)
        {
            FiniteStateMachineTypeClient production = m_production;

            if (production == null)
            {
                return;
            }

            FiniteStateSnapshot snapshot = fromStream
                ?? await production.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);

            ProductionState = ToSubSnapshot(snapshot);

            Raise(ProductionStateChanged, new ProductionStateChangedEventArgs(ProductionState));
        }

        /// <summary>
        /// Reports every transition of one state machine until the pump is stopped.
        /// </summary>
        /// <remarks>
        /// Both streams subscribe to <c>CurrentState/Id</c> and yield a snapshot of the state
        /// and the transition variables per change, so the model sees consistent data per
        /// transition and never has to assemble it from single values. The effective state
        /// stream of a hierarchical machine yields the same snapshot with the state of the
        /// active sub state machine attached to it. The enumeration runs on a publish worker;
        /// the base class posts the event to the thread the model was created on. A transition
        /// is exactly what changes the causes a machine permits - leaving Idle takes Start
        /// away and offers Stop instead - so they are read again right after it.
        /// </remarks>
        private async Task PumpTransitionsAsync(
            StateMachineKind machine,
            IAsyncEnumerable<FiniteStateSnapshot> snapshots,
            CancellationToken ct)
        {
            try
            {
                await foreach (FiniteStateSnapshot snapshot in snapshots.ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    StateMachineSnapshot observed = ToSnapshot(machine, snapshot);

                    if (machine == StateMachineKind.Operation)
                    {
                        OperationState = observed;
                    }
                    else
                    {
                        ProgramState = observed;
                    }

                    Raise(TransitionObserved, new TransitionObservedEventArgs(observed));

                    try
                    {
                        if (machine == StateMachineKind.Operation)
                        {
                            await UpdateProductionStateAsync(snapshot.SubMachine, ct).ConfigureAwait(false);
                        }

                        await RefreshPermittedCausesAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        // one failed read must not end the stream.
                        ReportError("Reading the causes the machines permit", exception);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // the model was detached.
            }
            catch (Exception exception)
            {
                // the pump has no caller to throw to.
                ReportError($"Watching the {machine} state machine", exception);
            }
        }

        /// <summary>
        /// Turns what the Part 16 client observed into the payload of this model.
        /// </summary>
        private static StateMachineSnapshot ToSnapshot(StateMachineKind machine, FiniteStateSnapshot snapshot)
        {
            return new StateMachineSnapshot(
                machine,
                snapshot.CurrentState.Text ?? string.Empty,
                snapshot.LastTransition.Text ?? string.Empty,
                snapshot.Timestamp,
                snapshot.SubMachine == null ? null : ToSubSnapshot(snapshot.SubMachine));
        }

        /// <summary>
        /// Turns what the Part 16 client observed of a sub state machine into the payload
        /// of this model.
        /// </summary>
        private static SubStateMachineSnapshot ToSubSnapshot(FiniteStateSnapshot snapshot)
        {
            return new SubStateMachineSnapshot(
                snapshot.CurrentState.Text ?? string.Empty,
                snapshot.Status,
                snapshot.Timestamp);
        }

        /// <summary>
        /// Whether the attribute the server answered says a cause may be called.
        /// </summary>
        private static bool IsPermitted(List<DataValue> results, int index)
        {
            // a server which does not answer the attribute leaves the cause permitted, so
            // that this client stays usable against one which is not the sample.
            return index >= results.Count
                || !results[index].WrappedValue.TryGetValue(out bool executable)
                || executable;
        }
    }
}
