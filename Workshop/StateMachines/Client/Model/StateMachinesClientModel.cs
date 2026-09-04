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
    /// Where one machine is, and how it got there.
    /// </summary>
    /// <param name="Machine">Which machine the snapshot describes.</param>
    /// <param name="State">The display name of the current state.</param>
    /// <param name="Transition">The display name of the last transition, or empty.</param>
    /// <param name="Timestamp">When the server observed it, in UTC.</param>
    public sealed record StateMachineSnapshot(
        StateMachineKind Machine,
        string State,
        string Transition,
        DateTime Timestamp);

    /// <summary>
    /// Which causes the machines permit in the state they are in right now.
    /// </summary>
    /// <remarks>
    /// A cause the server did not answer for counts as permitted: the client then stays
    /// usable against a server which does not fill in the UserExecutable attribute, and
    /// a call which cannot apply is still refused by the server itself.
    /// </remarks>
    /// <param name="Operation">The causes of the Operation machine which are permitted.</param>
    /// <param name="Program">The causes of the Program machine which are permitted.</param>
    public sealed record PermittedCauses(
        IReadOnlyDictionary<OperationCause, bool> Operation,
        IReadOnlyDictionary<ProgramCause, bool> Program)
    {
        /// <summary>
        /// Every cause permitted: what the client assumes until it has read the attributes.
        /// </summary>
        public static PermittedCauses All { get; } = new PermittedCauses(
            Enum.GetValues<OperationCause>().ToDictionary(cause => cause, _ => true),
            Enum.GetValues<ProgramCause>().ToDictionary(cause => cause, _ => true));

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
    /// <see cref="PermittedCauses"/> is final when <see cref="SampleClientModel.AttachAsync"/>
    /// returns, and every later change of it - after a call, after a transition on the
    /// stream, after a reconnect - is reported through <see cref="PermittedCausesChanged"/>.
    /// </para>
    /// </remarks>
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

        // the reads of the UserExecutable attributes are serialised: the stream refreshes
        // on a publish worker while a click refreshes on the caller's thread, and a slow
        // older read must not report a stale map over a newer one.
        private readonly SemaphoreSlim m_refresh = new SemaphoreSlim(1, 1);

        private FiniteStateMachineTypeClient m_operation;
        private ProgramStateMachineTypeClient m_program;
        private NodeId m_interlockNode = NodeId.Null;

        // replaced as a whole when the session changes, so a refresh never sees a half
        // built table.
        private IReadOnlyDictionary<OperationCause, NodeId> m_operationCauseIds = new Dictionary<OperationCause, NodeId>();
        private IReadOnlyDictionary<ProgramCause, NodeId> m_programCauseIds = new Dictionary<ProgramCause, NodeId>();

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
        /// Raised whenever the causes the machines permit were read again.
        /// </summary>
        public event EventHandler<PermittedCausesChangedEventArgs> PermittedCausesChanged;

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
        /// Reads again which causes apply to the state each machine is in right now.
        /// </summary>
        /// <remarks>
        /// A cause is only declared for some of the states of its machine, so calling one in
        /// any other state is refused with BadNotSupported. Which ones apply is not something
        /// a client should work out for itself: OPC 10000-16 has the server say so through the
        /// <c>Executable</c> and <c>UserExecutable</c> attributes of the method nodes, which
        /// the sample server answers from <c>IsCausePermitted</c>. Reading them after every
        /// transition keeps the client in step with the machine, and keeps it correct if the
        /// server ever changes its state table. The result is stored in
        /// <see cref="PermittedCauses"/> and reported through <see cref="PermittedCausesChanged"/>.
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

                if (session == null || operationCauseIds.Count + programCauseIds.Count == 0)
                {
                    return;
                }

                var nodesToRead = operationCauseIds
                    .Select(cause => cause.Value)
                    .Concat(programCauseIds.Select(cause => cause.Value))
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
                int ii = 0;

                foreach (KeyValuePair<OperationCause, NodeId> cause in operationCauseIds)
                {
                    operation[cause.Key] = IsPermitted(results, ii++);
                }

                foreach (KeyValuePair<ProgramCause, NodeId> cause in programCauseIds)
                {
                    program[cause.Key] = IsPermitted(results, ii++);
                }

                PermittedCauses = new PermittedCauses(operation, program);

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

            // where the machines are right now, before anything moves them.
            OperationState = ToSnapshot(
                StateMachineKind.Operation,
                await m_operation.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false));
            ProgramState = ToSnapshot(
                StateMachineKind.Program,
                await m_program.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false));

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
            m_pump = new SubscriptionPump();
            m_pump.Run(token => PumpTransitionsAsync(StateMachineKind.Operation, m_operation, m_streaming, token));
            m_pump.Run(token => PumpTransitionsAsync(StateMachineKind.Program, m_program, m_streaming, token));

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
            m_interlockNode = NodeId.Null;
            m_operationCauseIds = new Dictionary<OperationCause, NodeId>();
            m_programCauseIds = new Dictionary<ProgramCause, NodeId>();
            OperationState = null;
            ProgramState = null;
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
        /// Reports every transition of one state machine until the pump is stopped.
        /// </summary>
        /// <remarks>
        /// <c>ObserveFiniteTransitionsAsync</c> subscribes to <c>CurrentState/Id</c> and yields
        /// a snapshot of the state and the transition variables per change, so the model sees
        /// consistent data per transition and never has to assemble it from single values.
        /// The enumeration runs on a publish worker; the base class posts the event to the
        /// thread the model was created on. A transition is exactly what changes the causes
        /// a machine permits - leaving Idle takes Start away and offers Stop instead - so they
        /// are read again right after it.
        /// </remarks>
        private async Task PumpTransitionsAsync(
            StateMachineKind machine,
            FiniteStateMachineTypeClient stateMachine,
            IStreamingSubscription streaming,
            CancellationToken ct)
        {
            try
            {
                await foreach (FiniteStateSnapshot snapshot in stateMachine
                    .ObserveFiniteTransitionsAsync(streaming, null, ct)
                    .ConfigureAwait(false))
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
