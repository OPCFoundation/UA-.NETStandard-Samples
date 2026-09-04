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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Quickstarts.StateMachines.Client
{
    /// <summary>
    /// The main form of the state machines Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client never browses the state or transition variables of a machine itself. The
    /// generic Part 16 client API does that: <see cref="FiniteStateMachineTypeClient"/> wraps
    /// any finite state machine, and the extension methods on it read the current state
    /// (<c>GetCurrentFiniteStateAsync</c>) and stream a fresh snapshot per transition
    /// (<c>ObserveFiniteTransitionsAsync</c>). That is the same code for the vendor machine on
    /// the left of the window and for the standard program machine on the right.
    /// </para>
    /// <para>
    /// What differs is how the two are driven. The vendor machine has no type definition, so
    /// its causes are plain method calls. The program machine is a standard type, so the
    /// source generated <see cref="ProgramStateMachineTypeClient"/> proxy exposes its causes
    /// as <c>StartAsync</c>, <c>SuspendAsync</c> and so on.
    /// </para>
    /// <para>
    /// The vendor machine is also hierarchical, and the window shows the two halves of that.
    /// <c>GetAvailableStatesAsync</c> and <c>GetAvailableTransitionsAsync</c> read the model a
    /// Part 16 machine publishes - a node per state and per transition, each with its number -
    /// and <c>GetSubStateMachineAsync</c> follows the <c>HasSubStateMachine</c> reference of a
    /// state node to the machine which runs while the parent is in it.
    /// <c>ObserveEffectiveStateAsync</c> then streams both machines at once, so a transition
    /// of either is reported as one combined snapshot.
    /// </para>
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62571/Quickstarts/StateMachinesServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The names the transitions of the two machines are reported under.
        /// </summary>
        private const string kOperation = "Operation";
        private const string kProgram = "Program";

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private ISession m_session;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed asynchronously by StopWatchingAsync.")]
        private StreamingSubscription m_streaming;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed by StopWatchingAsync.")]
        private CancellationTokenSource m_cts;
        private FiniteStateMachineTypeClient m_operation;
        private ProgramStateMachineTypeClient m_program;

        /// <summary>
        /// The machine which runs below one of the states of the Operation machine, found
        /// through the HasSubStateMachine reference of that state rather than by name.
        /// </summary>
        private FiniteStateMachineTypeClient m_production;

        private NodeId m_interlockNode;
        private readonly Dictionary<Button, NodeId> m_causes = new();
        private readonly Dictionary<Button, NodeId> m_programCauses = new();
        private readonly Dictionary<Button, NodeId> m_productionCauses = new();
        private bool m_updatingInterlock;
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await StopWatchingAsync();
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    await StopWatchingAsync();
                    EnableControls(false);
                    return;
                }

                await StartWatchingAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                EnableControls(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                // a V2 subscription belongs to the subscription manager of the session and
                // survives the reconnect together with its monitored items, so both streams
                // keep running and there is nothing to re-create here.
                m_session = ConnectServerCTRL.Session;
                EnableControls(true);

                // EnableControls only knows that there is a session again, so it offers every
                // cause. Which of them the machines actually permit has to be read back before
                // the user can press one, or this client offers a cause the server refuses -
                // and the machine may well have moved while the connection was down.
                await RefreshPermittedCausesAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClientUtils.WaitForTeardown(StopWatchingAsync);
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Connecting
        /// <summary>
        /// Resolves both state machines and starts watching their transitions.
        /// </summary>
        private async Task StartWatchingAsync(CancellationToken ct = default)
        {
            await StopWatchingAsync();

            TransitionsLV.Items.Clear();

            // this client has built-in knowledge of the address space of its server: the two
            // machines and the interlock the guard of the Start cause reads.
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.StateMachines);

            List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                m_session,
                ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                "1:Machine/1:Operation",
                "1:Machine/1:Program",
                "1:Machine/1:SafetyInterlockClear",
                "1:Machine/1:Operation/1:PowerOn",
                "1:Machine/1:Operation/1:PowerOff",
                "1:Machine/1:Operation/1:Start",
                "1:Machine/1:Operation/1:Stop",
                "1:Machine/1:Operation/1:Fault",
                "1:Machine/1:Operation/1:Reset");

            if (nodes.Count < 9 || nodes[0].IsNull || nodes[1].IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The server does not expose the state machines of this sample.");
            }

            // the generic proxy works for any finite state machine, the typed one adds the
            // methods the standard program state machine declares.
            m_operation = new FiniteStateMachineTypeClient(m_session, nodes[0], m_telemetry);
            m_program = new ProgramStateMachineTypeClient(m_session, nodes[1], m_telemetry);
            m_interlockNode = nodes[2];

            m_causes.Clear();
            m_programCauses.Clear();
            m_causes[PowerOnBTN] = nodes[3];
            m_causes[PowerOffBTN] = nodes[4];
            m_causes[StartBTN] = nodes[5];
            m_causes[StopBTN] = nodes[6];
            m_causes[FaultBTN] = nodes[7];
            m_causes[ResetBTN] = nodes[8];

            await ResolveProgramCausesAsync(ct);

            // what the Operation machine is made of, and which of its states has a machine of
            // its own below it. That is where m_production comes from.
            await ShowMachineModelAsync(ct);
            await ResolveProductionCauseAsync(ct);

            // where the machines are right now, before anything moves them.
            ShowSnapshot(kOperation, await m_operation.GetCurrentFiniteStateAsync(ct), append: false);
            ShowSnapshot(kProgram, await m_program.GetCurrentFiniteStateAsync(ct), append: false);
            await ShowProductionAsync(null, ct);
            await ReadInterlockAsync(ct);

            // the streaming subscription lives as long as the connection: the underlying OPC
            // UA subscription is created when the first enumeration starts.
            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, ClientUtils.DefaultSubscriptionOptions);
            m_cts = new CancellationTokenSource();

            // nothing is awaited here on purpose: both enumerations run for as long as the
            // client is connected, and cancelling the token is what unsubscribes them.
            //
            // The Operation machine is watched through ObserveEffectiveStateAsync, which
            // subscribes to the machine and to the sub state machines of its states at once
            // and reports a transition of any of them; the Program machine has no sub state
            // machine, so watching it as one machine is all there is to do.
            _ = PumpTransitionsAsync(
                kOperation,
                m_operation.ObserveEffectiveStateAsync(m_streaming, m_telemetry, null, m_cts.Token),
                m_cts.Token);

            _ = PumpTransitionsAsync(
                kProgram,
                m_program.ObserveFiniteTransitionsAsync(m_streaming, null, m_cts.Token),
                m_cts.Token);

            EnableControls(true);

            await RefreshPermittedCausesAsync(ct);
        }

        /// <summary>
        /// Resolves the five methods of the program machine.
        /// </summary>
        /// <remarks>
        /// They carry the browse names of the standard type, so they live in the OPC UA
        /// namespace rather than in the one of the sample.
        /// </remarks>
        private async Task ResolveProgramCausesAsync(CancellationToken ct)
        {
            m_programCauses.Clear();

            var wellKnownNamespaceUris = new NamespaceTable();

            var buttons = new (Button Button, string BrowseName)[] {
                (ProgramStartBTN, BrowseNames.Start),
                (ProgramSuspendBTN, BrowseNames.Suspend),
                (ProgramResumeBTN, BrowseNames.Resume),
                (ProgramHaltBTN, BrowseNames.Halt),
                (ProgramResetBTN, BrowseNames.Reset),
            };

            List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                m_session,
                m_program.ObjectId,
                wellKnownNamespaceUris,
                ct,
                buttons.Select(entry => entry.BrowseName).ToArray());

            for (int ii = 0; ii < buttons.Length && ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    m_programCauses[buttons[ii].Button] = nodes[ii];
                }
            }
        }

        /// <summary>
        /// Shows what the Operation machine is made of, and finds its sub state machine.
        /// </summary>
        /// <remarks>
        /// Nothing here is browsed by hand. A Part 16 machine publishes its own model through
        /// the optional <c>AvailableStates</c> and <c>AvailableTransitions</c> properties,
        /// which name one node per state and per transition, and the two Get methods read them
        /// together with the number each node carries. A server which materializes none of
        /// them answers with nothing, and this list stays empty instead of the client falling
        /// back to guessing what the machine can do.
        /// </remarks>
        private async Task ShowMachineModelAsync(CancellationToken ct)
        {
            ModelLV.Items.Clear();
            m_production = null;

            IReadOnlyList<FiniteStateInfo> states = await m_operation
                .GetAvailableStatesAsync(ct);

            IReadOnlyList<FiniteTransitionInfo> transitions = await m_operation
                .GetAvailableTransitionsAsync(ct);

            foreach (FiniteStateInfo state in states)
            {
                // OPC 10000-16 §4.4.16 hangs HasSubStateMachine off the state node rather than
                // off the machine, because a machine can have one per state. So a client looks
                // for it there, with the NodeId AvailableStates just gave it.
                FiniteStateMachineTypeClient subMachine = await m_operation
                    .GetSubStateMachineAsync(state.NodeId, m_telemetry, ct);

                string subMachineName = string.Empty;

                if (subMachine != null)
                {
                    subMachineName = await ReadBrowseNameAsync(subMachine.ObjectId, ct);

                    // the sample's server declares exactly one, below its Running state.
                    m_production = subMachine;
                }

                AddModelItem("State", state.BrowseName, state.StateNumber, state.NodeId, subMachineName);
            }

            foreach (FiniteTransitionInfo transition in transitions)
            {
                AddModelItem(
                    "Transition",
                    transition.BrowseName,
                    transition.TransitionNumber,
                    transition.NodeId,
                    string.Empty);
            }
        }

        /// <summary>
        /// Adds one state or transition of the machine to the model list.
        /// </summary>
        private void AddModelItem(
            string kind,
            QualifiedName browseName,
            uint number,
            NodeId nodeId,
            string subMachine)
        {
            var item = new ListViewItem(kind);

            item.SubItems.Add(browseName.Name ?? string.Empty);
            item.SubItems.Add(number.ToString(CultureInfo.CurrentCulture));

            // the NodeId is worth showing: it is what CurrentState/Id and LastTransition/Id
            // answer with, and a machine which materializes no nodes has nothing to put here.
            item.SubItems.Add(nodeId.ToString());
            item.SubItems.Add(subMachine);

            ModelLV.Items.Add(item);
        }

        /// <summary>
        /// Resolves the cause of the sub state machine, if the server has one.
        /// </summary>
        /// <remarks>
        /// A sub state machine has causes of its own, and they are methods of the child rather
        /// than of the machine the client started from.
        /// </remarks>
        private async Task ResolveProductionCauseAsync(CancellationToken ct)
        {
            m_productionCauses.Clear();

            if (m_production == null)
            {
                return;
            }

            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.StateMachines);

            List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                m_session,
                m_production.ObjectId,
                wellKnownNamespaceUris,
                ct,
                "1:StartBatch");

            if (nodes.Count > 0 && !nodes[0].IsNull)
            {
                m_productionCauses[StartBatchBTN] = nodes[0];
            }
        }

        /// <summary>
        /// The browse name of a node, as text.
        /// </summary>
        private async Task<string> ReadBrowseNameAsync(NodeId nodeId, CancellationToken ct)
        {
            var nodesToRead = new[] {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
            }.ToArrayOf();

            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct);

            List<DataValue> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, nodesToRead.ToArray());

            return results[0].WrappedValue.TryGetValue(out QualifiedName browseName)
                ? browseName.Name
                : string.Empty;
        }

        /// <summary>
        /// Offers only the causes which apply to the state each machine is in right now.
        /// </summary>
        /// <remarks>
        /// A cause is only declared for some of the states of its machine, so calling one in
        /// any other state is refused with BadNotSupported. Which ones apply is not something
        /// a client should work out for itself: OPC 10000-16 has the server say so through the
        /// <c>Executable</c> and <c>UserExecutable</c> attributes of the method nodes, which
        /// the sample server answers from <c>IsCausePermitted</c>. Reading them after every
        /// transition keeps the buttons in step with the machine, and keeps this client
        /// correct if the server ever changes its state table. The cause of the sub state
        /// machine is answered the same way, and applies in none of its states while the
        /// machine is suspended.
        /// </remarks>
        private async Task RefreshPermittedCausesAsync(CancellationToken ct = default)
        {
            List<KeyValuePair<Button, NodeId>> causes = m_causes
                .Concat(m_programCauses)
                .Concat(m_productionCauses)
                .ToList();

            if (m_session == null || causes.Count == 0)
            {
                return;
            }

            var nodesToRead = causes
                .Select(cause => new ReadValueId {
                    NodeId = cause.Value,
                    AttributeId = Attributes.UserExecutable,
                })
                .ToArrayOf();

            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct);

            List<DataValue> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, nodesToRead.ToArray());

            for (int ii = 0; ii < causes.Count && ii < results.Count; ii++)
            {
                // a server which does not answer the attribute leaves the button enabled, so
                // that this client stays usable against one which is not the sample.
                causes[ii].Key.Enabled = !results[ii].WrappedValue.TryGetValue(out bool executable)
                    || executable;
            }
        }

        /// <summary>
        /// Reports every transition of one state machine until the client disconnects.
        /// </summary>
        /// <remarks>
        /// Both streams subscribe to <c>CurrentState/Id</c> and yield a snapshot of the state
        /// and the transition variables per change, so the window sees consistent data per
        /// transition and never has to assemble it from single values. The effective state
        /// stream of a hierarchical machine yields the same snapshot with the state of the
        /// active sub state machine attached to it.
        /// </remarks>
        private async Task PumpTransitionsAsync(
            string name,
            IAsyncEnumerable<FiniteStateSnapshot> snapshots,
            CancellationToken ct)
        {
            try
            {
                await foreach (FiniteStateSnapshot snapshot in snapshots.ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    // without a window there is nothing to update, and the enumeration keeps
                    // running rather than ending for good.
                    if (!IsHandleCreated)
                    {
                        continue;
                    }

                    // the enumeration runs on a publish worker, so the display is updated on
                    // the UI thread.
                    BeginInvoke(new Action(() => OnTransitionObserved(name, snapshot)));
                }
            }
            catch (OperationCanceledException)
            {
                // the client disconnected.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown.
                m_telemetry?.CreateLogger<MainForm>()
                    .LogError(exception, "Failed to watch the {StateMachine} state machine.", name);
            }
        }

        /// <summary>
        /// Stops both streams and deletes the subscription on the server.
        /// </summary>
        /// <remarks>
        /// Done before the session is closed: closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async Task StopWatchingAsync()
        {
            StreamingSubscription streaming = m_streaming;
            CancellationTokenSource cts = m_cts;

            m_streaming = null;
            m_cts = null;
            m_operation = null;
            m_program = null;
            m_production = null;
            m_causes.Clear();
            m_programCauses.Clear();
            m_productionCauses.Clear();

            if (cts != null)
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            if (streaming == null)
            {
                return;
            }

            try
            {
                await streaming.DisposeAsync();
            }
            catch (Exception exception)
            {
                // this also runs when the session has already gone away, and then the
                // subscription cannot be deleted on the server any more.
                m_telemetry?.CreateLogger<MainForm>()
                    .LogError(exception, "Failed to delete the state machine subscription.");
            }
        }
        #endregion

        #region Driving The Machines
        /// <summary>
        /// Calls the cause the pressed button stands for.
        /// </summary>
        /// <remarks>
        /// The Operation machine has no type definition, so there is no generated proxy for
        /// it and its causes are called as the plain methods they are. Which transition a
        /// call takes is the server's business: a method whose cause cannot apply in the
        /// current state is refused as BadNotExecutable, and the guard on Start answers
        /// BadInvalidState while the interlock is open.
        /// </remarks>
        private async void OperationCauseBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || sender is not Button button ||
                    !m_causes.TryGetValue(button, out NodeId methodId))
                {
                    return;
                }

                await m_session.CallAsync(m_operation.ObjectId, methodId, default);

                // the call just moved the machine, so what it permits has changed. Re-read it
                // here rather than waiting for the transition to come back on the stream: the
                // buttons then follow the user's own action immediately.
                await RefreshPermittedCausesAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Starts a batch of the sub state machine.
        /// </summary>
        /// <remarks>
        /// The cause belongs to the machine below the Running state, so the call names that
        /// object: the parent knows nothing about the methods of its child. Which is also why
        /// the button is only offered while the child is active - a suspended sub state
        /// machine reports none of its causes as executable.
        /// </remarks>
        private async void StartBatchBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || m_production == null ||
                    !m_productionCauses.TryGetValue(StartBatchBTN, out NodeId methodId))
                {
                    return;
                }

                await m_session.CallAsync(m_production.ObjectId, methodId, default);

                await RefreshPermittedCausesAsync();
                await ShowProductionAsync(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Starts the program.
        /// </summary>
        /// <remarks>
        /// ProgramStateMachineType is a standard type, so the generated proxy carries its five
        /// methods and the client does not have to know their NodeIds.
        /// </remarks>
        private async void ProgramStartBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ct => m_program.StartAsync(ct));
        }

        /// <summary>
        /// Suspends the running program.
        /// </summary>
        private async void ProgramSuspendBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ct => m_program.SuspendAsync(ct));
        }

        /// <summary>
        /// Resumes the suspended program.
        /// </summary>
        private async void ProgramResumeBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ct => m_program.ResumeAsync(ct));
        }

        /// <summary>
        /// Halts the program.
        /// </summary>
        private async void ProgramHaltBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ct => m_program.HaltAsync(ct));
        }

        /// <summary>
        /// Returns the halted program to Ready.
        /// </summary>
        private async void ProgramResetBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ct => m_program.ResetAsync(ct));
        }

        /// <summary>
        /// Invokes one of the methods of the program proxy and reports what it answered.
        /// </summary>
        private async Task CallProgramAsync(Func<CancellationToken, ValueTask> call)
        {
            try
            {
                if (m_program == null)
                {
                    return;
                }

                await call(default);

                // same as for the vendor machine: the causes the program permits changed.
                await RefreshPermittedCausesAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Opens and closes the interlock the guard on the Start cause reads.
        /// </summary>
        private async void InterlockCB_CheckedChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || m_interlockNode.IsNull || m_updatingInterlock)
                {
                    return;
                }

                var value = new WriteValue {
                    NodeId = m_interlockNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(InterlockCB.Checked)),
                };

                var valuesToWrite = new List<WriteValue> { value };

                WriteResponse response = await m_session.WriteAsync(null, valuesToWrite, default);

                List<StatusCode> results = response.Results.ToList();

                ClientBase.ValidateResponse(results, valuesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(results[0]);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shows the interlock the server currently has, without writing it back.
        /// </summary>
        private async Task ReadInterlockAsync(CancellationToken ct)
        {
            DataValue value = await m_session.ReadValueAsync(m_interlockNode, ct);

            m_updatingInterlock = true;

            try
            {
                InterlockCB.Checked = value.WrappedValue.TryGetValue(out bool clear) && clear;
            }
            finally
            {
                m_updatingInterlock = false;
            }
        }
        #endregion

        #region Display
        /// <summary>
        /// Records a transition and re-reads which causes the machines now permit.
        /// </summary>
        /// <remarks>
        /// Runs on the UI thread, marshalled from the publish worker the stream runs on. The
        /// causes are re-read here rather than in the pump because a transition is exactly
        /// what changes them: leaving Idle takes Start away and offers Stop instead.
        /// </remarks>
        private async void OnTransitionObserved(string name, FiniteStateSnapshot snapshot)
        {
            try
            {
                ShowSnapshot(name, snapshot, append: true);

                if (name == kOperation)
                {
                    await ShowProductionAsync(snapshot.SubMachine);
                }

                await RefreshPermittedCausesAsync();
            }
            catch (Exception exception)
            {
                // this runs as an async void continuation, so an escaping exception would
                // take the process down rather than reach a handler.
                m_telemetry?.CreateLogger<MainForm>()
                    .LogError(exception, "Failed to update the causes the machines permit.");
            }
        }

        /// <summary>
        /// Shows where a machine is, and optionally records how it got there.
        /// </summary>
        private void ShowSnapshot(string name, FiniteStateSnapshot snapshot, bool append)
        {
            if (IsDisposed)
            {
                return;
            }

            string state = snapshot.CurrentState.Text ?? string.Empty;
            string transition = snapshot.LastTransition.Text ?? string.Empty;

            if (name == kOperation)
            {
                OperationStateTB.Text = state;
                OperationTransitionTB.Text = transition;

                // where a hierarchical machine is, is both states at once: the row records
                // them together, so a transition of the child is not mistaken for one of the
                // parent standing still.
                if (snapshot.SubMachine != null)
                {
                    state = $"{state} / {DescribeSubMachine(snapshot.SubMachine)}";
                }
            }
            else
            {
                ProgramStateTB.Text = state;
                ProgramTransitionTB.Text = transition;
            }

            if (!append)
            {
                return;
            }

            var item = new ListViewItem(
                snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture));

            item.SubItems.Add(name);
            item.SubItems.Add(state);
            item.SubItems.Add(transition);

            TransitionsLV.Items.Add(item);
            item.EnsureVisible();
        }

        /// <summary>
        /// Shows where the sub state machine of the Operation machine is.
        /// </summary>
        /// <remarks>
        /// The effective state stream only carries the child while the parent is in the state
        /// the child belongs to, and drops what a child reports while the parent is elsewhere -
        /// otherwise a machine which is not running would appear to be producing. So a yield
        /// without a sub machine is the moment to ask the child itself, which is also what
        /// answers <c>Bad_StateNotActive</c> while it is suspended.
        /// </remarks>
        private async Task ShowProductionAsync(
            FiniteStateSnapshot fromStream,
            CancellationToken ct = default)
        {
            if (m_production == null)
            {
                ProductionStateTB.Text = "(no sub state machine)";
                return;
            }

            FiniteStateSnapshot snapshot = fromStream
                ?? await m_production.GetCurrentFiniteStateAsync(ct);

            ProductionStateTB.Text = DescribeSubMachine(snapshot);
        }

        /// <summary>
        /// The state of a sub state machine, or why there is none to report.
        /// </summary>
        private static string DescribeSubMachine(FiniteStateSnapshot snapshot)
        {
            // OPC 10000-16 §4.4.6: a suspended sub state machine reports its state variables
            // with Bad_StateNotActive rather than with the state it stopped in, so the status
            // is the answer and the value below it means nothing.
            if (StatusCode.IsBad(snapshot.Status))
            {
                return $"({snapshot.Status})";
            }

            return snapshot.CurrentState.Text ?? string.Empty;
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void EnableControls(bool enabled)
        {
            InterlockCB.Enabled = enabled;

            PowerOnBTN.Enabled = enabled;
            PowerOffBTN.Enabled = enabled;
            StartBTN.Enabled = enabled;
            StopBTN.Enabled = enabled;
            FaultBTN.Enabled = enabled;
            ResetBTN.Enabled = enabled;

            // only a server which has a sub state machine has a cause to offer for it
            StartBatchBTN.Enabled = enabled && m_productionCauses.Count > 0;

            ProgramStartBTN.Enabled = enabled;
            ProgramSuspendBTN.Enabled = enabled;
            ProgramResumeBTN.Enabled = enabled;
            ProgramHaltBTN.Enabled = enabled;
            ProgramResetBTN.Enabled = enabled;
        }
        #endregion
    }
}
