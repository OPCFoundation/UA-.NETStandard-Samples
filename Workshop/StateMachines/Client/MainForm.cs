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
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.StateMachines.Client.Model;

namespace Quickstarts.StateMachines.Client
{
    /// <summary>
    /// The main form of the state machines Quickstart client.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="StateMachinesClientModel"/>, which resolves the two machines, streams
    /// their transitions and reads which causes they permit. The window only maps its
    /// buttons to the causes of the model, records the transitions the model reports and
    /// enables the buttons the model says apply.
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
            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62571/Quickstarts/StateMachinesServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new StateMachinesClientModel(telemetry);
            m_model.TransitionObserved += Model_TransitionObserved;
            m_model.PermittedCausesChanged += Model_PermittedCausesChanged;
            m_model.Error += Model_Error;

            m_operationButtons = new Dictionary<OperationCause, Button> {
                [OperationCause.PowerOn] = PowerOnBTN,
                [OperationCause.PowerOff] = PowerOffBTN,
                [OperationCause.Start] = StartBTN,
                [OperationCause.Stop] = StopBTN,
                [OperationCause.Fault] = FaultBTN,
                [OperationCause.Reset] = ResetBTN,
            };

            m_programButtons = new Dictionary<ProgramCause, Button> {
                [ProgramCause.Start] = ProgramStartBTN,
                [ProgramCause.Suspend] = ProgramSuspendBTN,
                [ProgramCause.Resume] = ProgramResumeBTN,
                [ProgramCause.Halt] = ProgramHaltBTN,
                [ProgramCause.Reset] = ProgramResetBTN,
            };

            // the six buttons of the Operation machine share one click handler, which
            // needs the cause a button stands for
            m_operationCauses = new Dictionary<Button, OperationCause>();

            foreach (KeyValuePair<OperationCause, Button> entry in m_operationButtons)
            {
                m_operationCauses[entry.Value] = entry.Key;
            }
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Detached asynchronously by MainForm_FormClosing, which cannot await a DisposeAsync.")]
        private readonly StateMachinesClientModel m_model;
        private readonly Dictionary<OperationCause, Button> m_operationButtons;
        private readonly Dictionary<ProgramCause, Button> m_programButtons;
        private readonly Dictionary<Button, OperationCause> m_operationCauses;
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
        /// <remarks>
        /// The model is detached first: it stops its streams and deletes its subscription
        /// before the control closes the session, because closing a session which still
        /// carries a subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.DetachAsync();
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
                ISession session = ConnectServerCTRL.Session;

                if (session == null)
                {
                    await m_model.DetachAsync();
                    EnableControls(false);
                    return;
                }

                TransitionsLV.Items.Clear();

                // the model resolves both machines, reads where they are, starts the streams
                // and reads which causes apply; all of that is known when it returns.
                await m_model.AttachAsync(session);

                ShowSnapshot(m_model.OperationState, append: false);
                ShowSnapshot(m_model.ProgramState, append: false);
                ShowInterlock(m_model.InterlockClear);

                EnableControls(true);
                ApplyCauses(m_model.PermittedCauses);
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
                m_model.NotifyReconnectStarting();
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
                // the streams survive the reconnect; the model reads back which causes the
                // machines permit, because they may well have moved while the connection
                // was down, and the buttons are offered only once that is known.
                await m_model.NotifyReconnectCompletedAsync();
                EnableControls(true);
                ApplyCauses(m_model.PermittedCauses);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        /// <remarks>
        /// FormClosing cannot await, so the model is detached on a thread pool thread and
        /// waited for; only then does the control close the session.
        /// </remarks>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClientUtils.WaitForTeardown(m_model.DetachAsync);
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Driving The Machines
        /// <summary>
        /// Calls the cause the pressed button stands for.
        /// </summary>
        /// <remarks>
        /// A cause the server refuses - BadNotExecutable outside its state, BadInvalidState
        /// from the guard on Start while the interlock is open - is thrown by the model and
        /// shown here, which is the point of the sample.
        /// </remarks>
        private async void OperationCauseBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected || sender is not Button button ||
                    !m_operationCauses.TryGetValue(button, out OperationCause cause))
                {
                    return;
                }

                await m_model.CallOperationCauseAsync(cause);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Starts the program.
        /// </summary>
        private async void ProgramStartBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ProgramCause.Start);
        }

        /// <summary>
        /// Suspends the running program.
        /// </summary>
        private async void ProgramSuspendBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ProgramCause.Suspend);
        }

        /// <summary>
        /// Resumes the suspended program.
        /// </summary>
        private async void ProgramResumeBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ProgramCause.Resume);
        }

        /// <summary>
        /// Halts the program.
        /// </summary>
        private async void ProgramHaltBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ProgramCause.Halt);
        }

        /// <summary>
        /// Returns the halted program to Ready.
        /// </summary>
        private async void ProgramResetBTN_ClickAsync(object sender, EventArgs e)
        {
            await CallProgramAsync(ProgramCause.Reset);
        }

        /// <summary>
        /// Calls one cause of the program machine and reports what it answered.
        /// </summary>
        private async Task CallProgramAsync(ProgramCause cause)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                await m_model.CallProgramCauseAsync(cause);
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
                // the box is also set from what the server has, and that must not be
                // written straight back
                if (!m_model.IsConnected || m_updatingInterlock)
                {
                    return;
                }

                await m_model.WriteInterlockAsync(InterlockCB.Checked);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion

        #region Display
        /// <summary>
        /// Records a transition the model observed on the stream.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window, so the controls are written
        /// directly. A transition can still arrive after the window was closed.
        /// </remarks>
        private void Model_TransitionObserved(object sender, TransitionObservedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ShowSnapshot(e.Snapshot, append: true);
        }

        /// <summary>
        /// Offers the causes the model found to apply.
        /// </summary>
        private void Model_PermittedCausesChanged(object sender, PermittedCausesChangedEventArgs e)
        {
            if (IsDisposed || !m_model.IsConnected)
            {
                return;
            }

            ApplyCauses(e.Causes);
        }

        /// <summary>
        /// Reports a failure on a background path of the model.
        /// </summary>
        private void Model_Error(object sender, ModelErrorEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ClientUtils.HandleException(m_telemetry, this.Text, e.Exception);
        }

        /// <summary>
        /// Shows where a machine is, and optionally records how it got there.
        /// </summary>
        private void ShowSnapshot(StateMachineSnapshot snapshot, bool append)
        {
            if (snapshot == null)
            {
                return;
            }

            if (snapshot.Machine == StateMachineKind.Operation)
            {
                OperationStateTB.Text = snapshot.State;
                OperationTransitionTB.Text = snapshot.Transition;
            }
            else
            {
                ProgramStateTB.Text = snapshot.State;
                ProgramTransitionTB.Text = snapshot.Transition;
            }

            if (!append)
            {
                return;
            }

            var item = new ListViewItem(
                snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture));

            item.SubItems.Add(snapshot.Machine.ToString());
            item.SubItems.Add(snapshot.State);
            item.SubItems.Add(snapshot.Transition);

            TransitionsLV.Items.Add(item);
            item.EnsureVisible();
        }

        /// <summary>
        /// Shows the interlock the server has, without writing it back.
        /// </summary>
        private void ShowInterlock(bool clear)
        {
            m_updatingInterlock = true;

            try
            {
                InterlockCB.Checked = clear;
            }
            finally
            {
                m_updatingInterlock = false;
            }
        }

        /// <summary>
        /// Offers only the causes which apply to the state each machine is in right now.
        /// </summary>
        private void ApplyCauses(PermittedCauses causes)
        {
            foreach (KeyValuePair<OperationCause, Button> entry in m_operationButtons)
            {
                entry.Value.Enabled = causes.IsPermitted(entry.Key);
            }

            foreach (KeyValuePair<ProgramCause, Button> entry in m_programButtons)
            {
                entry.Value.Enabled = causes.IsPermitted(entry.Key);
            }
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void EnableControls(bool enabled)
        {
            InterlockCB.Enabled = enabled;

            foreach (Button button in m_operationButtons.Values)
            {
                button.Enabled = enabled;
            }

            foreach (Button button in m_programButtons.Values)
            {
                button.Enabled = enabled;
            }
        }
        #endregion
    }
}
