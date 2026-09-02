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
        private NodeId m_interlockNode;
        private readonly Dictionary<Button, NodeId> m_causes = new();
        private readonly Dictionary<Button, NodeId> m_programCauses = new();
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
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                // a V2 subscription belongs to the subscription manager of the session and
                // survives the reconnect together with its monitored items, so both streams
                // keep running and there is nothing to re-create here.
                m_session = ConnectServerCTRL.Session;
                EnableControls(true);
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

            // where the machines are right now, before anything moves them.
            ShowSnapshot(kOperation, await m_operation.GetCurrentFiniteStateAsync(ct), append: false);
            ShowSnapshot(kProgram, await m_program.GetCurrentFiniteStateAsync(ct), append: false);
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
            _ = PumpTransitionsAsync(kOperation, m_operation, m_cts.Token);
            _ = PumpTransitionsAsync(kProgram, m_program, m_cts.Token);

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
        /// Offers only the causes which apply to the state each machine is in right now.
        /// </summary>
        /// <remarks>
        /// A cause is only declared for some of the states of its machine, so calling one in
        /// any other state is refused with BadNotSupported. Which ones apply is not something
        /// a client should work out for itself: OPC 10000-16 has the server say so through the
        /// <c>Executable</c> and <c>UserExecutable</c> attributes of the method nodes, which
        /// the sample server answers from <c>IsCausePermitted</c>. Reading them after every
        /// transition keeps the buttons in step with the machine, and keeps this client
        /// correct if the server ever changes its state table.
        /// </remarks>
        private async Task RefreshPermittedCausesAsync(CancellationToken ct = default)
        {
            List<KeyValuePair<Button, NodeId>> causes = m_causes
                .Concat(m_programCauses)
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
        /// <c>ObserveFiniteTransitionsAsync</c> subscribes to <c>CurrentState/Id</c> and yields
        /// a snapshot of the state and the transition variables per change, so the window sees
        /// consistent data per transition and never has to assemble it from single values.
        /// </remarks>
        private async Task PumpTransitionsAsync(
            string name,
            FiniteStateMachineTypeClient stateMachine,
            CancellationToken ct)
        {
            IStreamingSubscription streaming = m_streaming;

            try
            {
                await foreach (FiniteStateSnapshot snapshot in stateMachine
                    .ObserveFiniteTransitionsAsync(streaming, null, ct)
                    .ConfigureAwait(false))
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
            m_causes.Clear();
            m_programCauses.Clear();

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

            ProgramStartBTN.Enabled = enabled;
            ProgramSuspendBTN.Enabled = enabled;
            ProgramResumeBTN.Enabled = enabled;
            ProgramHaltBTN.Enabled = enabled;
            ProgramResetBTN.Enabled = enabled;
        }
        #endregion
    }
}
