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
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.PerfTestClient.Model;
using Opc.Ua.Samples.WinForms;

namespace Quickstarts.PerfTestClient
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="PerfTestClientModel"/>, which runs the test. The window only tells the
    /// model how many items to monitor and how fast, reads its counters on a timer, and
    /// stops the test when the user asks.
    /// </remarks>
    public partial class MainForm : SampleForm
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="model">The client model of the sample, from the container.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry, PerfTestClientModel model)
        {
            InitializeComponent();
            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62559/Quickstarts/PerfTestServer";
            this.Text = configuration.ApplicationName;
            m_telemetry = telemetry;

            // created by the container while this constructor runs, so on the thread of
            // the window: that is the context the model captures for its events, and it is
            // why the handlers below can touch the controls directly
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_model.Error += Model_Error;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly PerfTestClientModel m_model;
        #endregion

        #region Overrides
        /// <summary>
        /// Releases the resources of the window, and with them the model it owns.
        /// </summary>
        /// <remarks>
        /// This is hand written and therefore lives here rather than in the designer
        /// partial: the model is disposed with the window. The synchronous Dispose of
        /// the model runs its detach on a thread pool thread and waits for it, which is
        /// what a Dispose that cannot await needs. The closing handler has normally
        /// detached already by the time this runs, and a second detach returns at once.
        /// </remarks>
        /// <param name="disposing">True if managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                m_model?.Dispose();
            }

            base.Dispose(disposing);
        }
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
                    ShowRunning(false);
                    return;
                }

                LogTB.Clear();

                // the test starts as soon as the session is attached, with the settings
                // the user chose before connecting
                m_model.SamplingRate = (int)UpdateRateCTRL.Value;
                m_model.ItemCount = (int)ItemCountCTRL.Value;

                // zero leaves the bound to the engine, which discovers the effective limit
                // of the server from the first Bad_TooManyMonitoredItems it is told about
                // and fans the rest of the items out over further partitions.
                m_model.MaxMonitoredItemsPerPartition = (int)MaxItemsPerPartitionCTRL.Value;

                await m_model.AttachAsync(session);

                ShowRunning(true);
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
                // the subscription of the tester survives the reconnect, so it keeps counting
                await m_model.NotifyReconnectCompletedAsync();
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
            UpdateTimer.Enabled = false;
            ClientUtils.WaitForTeardown(m_model.DetachAsync);
            ConnectServerCTRL.Disconnect();
        }

        /// <summary>
        /// Shows what the model counted since the last tick.
        /// </summary>
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                PerfTestStatistics statistics = m_model.ReadStatistics();

                foreach (string message in m_model.TakeMessages())
                {
                    LogTB.AppendText(message);
                    LogTB.AppendText(Environment.NewLine);
                }

                MessageCountTB.Text = statistics.MessageCount.ToString(CultureInfo.CurrentCulture);
                TotalItemUpdateCountTB.Text = statistics.TotalItemUpdateCount.ToString(CultureInfo.CurrentCulture);

                ShowPartitions(m_model.ReadPartitions());

                TimeSpan delta = statistics.Elapsed;

                if (delta.TotalMilliseconds > 0)
                {
                    LogTB.AppendText(Utils.Format(
                        "Checking Update Counts. Time={0}, Min={1}, Max={2}",
                        DateTime.UtcNow.ToString("mm:ss.fff", CultureInfo.InvariantCulture),
                        statistics.MinItemUpdateCount,
                        statistics.MaxItemUpdateCount));
                    LogTB.AppendText(Environment.NewLine);

                    MessageRateTB.Text = delta.TotalSeconds.ToString(CultureInfo.CurrentCulture);
                    TotalItemUpdateRateTB.Text = (statistics.TotalItemUpdateCount / delta.TotalSeconds).ToString(CultureInfo.CurrentCulture);
                }
                else
                {
                    MessageRateTB.Text = string.Empty;
                    TotalItemUpdateRateTB.Text = string.Empty;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Ends the test the connect started.
        /// </summary>
        private async void StopBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.StopAsync();
                ShowRunning(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
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

        #region Private Methods
        /// <summary>
        /// Reads the counters while a test runs, and offers Stop.
        /// </summary>
        private void ShowRunning(bool running)
        {
            UpdateTimer.Enabled = running;
            StopBTN.Visible = running;

            if (!running)
            {
                PartitionCountTB.Text = String.Empty;
                PartitionLoadTB.Text = String.Empty;
            }
        }

        /// <summary>
        /// Shows how the items of the running test are spread over server side subscriptions.
        /// </summary>
        /// <remarks>
        /// One partition is the common case: the whole block of items fits into a single
        /// server side subscription and the logical subscription short circuits to it. More
        /// than one means the placement policy ran into the per subscription cap, and the
        /// load column shows that the updates really do arrive from each of them.
        /// </remarks>
        private void ShowPartitions(PerfTestPartitionStatistics partitions)
        {
            PartitionCountTB.Text = partitions.PartitionCount.ToString(CultureInfo.CurrentCulture);

            var load = new StringBuilder();

            foreach (KeyValuePair<uint, int> partition in partitions.UpdatesPerPartition)
            {
                if (load.Length > 0)
                {
                    load.Append(", ");
                }

                load.AppendFormat(CultureInfo.CurrentCulture, "{0}:{1}", partition.Key, partition.Value);
            }

            PartitionLoadTB.Text = load.ToString();
        }
        #endregion
    }
}
