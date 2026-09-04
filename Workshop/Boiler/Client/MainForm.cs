/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.Boiler.Client.Model;

namespace Quickstarts.Boiler.Client
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="BoilerClientModel"/>, which finds the boilers and watches the one the
    /// user picks. The window only fills the combo box from the model, tells it which
    /// boiler was picked, and writes the values the model reports into the text boxes.
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

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62567/Quickstarts/BoilerServer";
            this.Text = configuration.ApplicationName;
            m_telemetry = telemetry;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new BoilerClientModel(telemetry);
            m_model.ValueChanged += Model_ValueChanged;
            m_model.Error += Model_Error;

            m_displays = new Dictionary<BoilerVariable, Control> {
                [BoilerVariable.InputPipeFlow] = InputPipeFlowTB,
                [BoilerVariable.DrumLevel] = DrumLevelTB,
                [BoilerVariable.OutputPipeFlow] = OutputPipeFlowTB,
                [BoilerVariable.DrumLevelSetPoint] = DrumLevelSetPointTB,
            };
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly BoilerClientModel m_model;

        /// <summary>
        /// The control which displays each variable of the selected boiler.
        /// </summary>
        private readonly Dictionary<BoilerVariable, Control> m_displays;
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
        /// The model is detached first: it deletes its subscription before the control
        /// closes the session, because closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
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
                    BoilerCB.Items.Clear();
                    BoilerCB.Enabled = false;
                    return;
                }

                // the model finds the boilers of the server while it attaches.
                await m_model.AttachAsync(session);

                BoilerCB.Items.Clear();

                foreach (BoilerInfo boiler in m_model.Boilers)
                {
                    BoilerCB.Items.Add(boiler);
                }

                BoilerCB.Enabled = true;

                // selecting the first boiler is what starts watching it.
                if (BoilerCB.Items.Count > 0)
                {
                    BoilerCB.SelectedIndex = 0;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                m_model.NotifyReconnectStarting();
                BoilerCB.Enabled = false;
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
                // survives the reconnect together with its monitored items, so the model
                // has nothing to re-create.
                await m_model.NotifyReconnectCompletedAsync();
                BoilerCB.Enabled = true;
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

        /// <summary>
        /// Watches the boiler the user picked.
        /// </summary>
        private async void BoilerCB_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                foreach (Control control in m_displays.Values)
                {
                    control.Text = "---";
                }

                await m_model.SelectBoilerAsync(BoilerCB.SelectedItem as BoilerInfo);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display with the new value of a monitored variable.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window, so the control is written
        /// directly. A value can still arrive after the window was closed.
        /// </remarks>
        private void Model_ValueChanged(object sender, BoilerValueChangedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (m_displays.TryGetValue(e.Variable, out Control control))
            {
                control.Text = e.Value.WrappedValue.ToString();
            }
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
        #endregion
    }
}
