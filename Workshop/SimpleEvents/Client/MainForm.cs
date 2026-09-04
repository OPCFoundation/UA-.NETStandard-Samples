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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.SimpleEvents.Client.Model;
using Opc.Ua.Samples.WinForms;

namespace Quickstarts.SimpleEvents.Client
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="SimpleEventsClientModel"/>, which streams the events of the server for
    /// as long as it is attached. The window only turns each event the model reports into
    /// a row of its list.
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
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="model">The client model of the sample, from the container.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry, SimpleEventsClientModel model)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62563/Quickstarts/SimpleEventsServer";
            this.Text = configuration.ApplicationName;

            // created by the container while this constructor runs, so on the thread of
            // the window: that is the context the model captures for its events, and it is
            // why the handlers below can touch the controls directly
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_model.EventReceived += Model_EventReceived;
            m_model.Error += Model_Error;

            // the designer of this window never wired the closing of the window, so the
            // subscription used to outlive it; the model has to be detached before the
            // control closes the session.
            FormClosing += MainForm_FormClosing;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly SimpleEventsClientModel m_model;
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
        /// The model is detached first: it stops the stream and deletes its subscription
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
                    return;
                }

                // the model starts streaming the events while it attaches.
                await m_model.AttachAsync(session);
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
                // a V2 subscription belongs to the subscription manager of the session and
                // survives the reconnect together with its monitored items, so the stream
                // keeps running and the model has nothing to re-create.
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
            ClientUtils.WaitForTeardown(m_model.DetachAsync);
            ConnectServerCTRL.Disconnect();
        }

        /// <summary>
        /// Adds an event the model read off the stream to the list.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window, so the list is written
        /// directly. An event can still arrive after the window was closed.
        /// </remarks>
        private void Model_EventReceived(object sender, SimpleEventReceivedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            SimpleEventRecord status = e.Event;

            ListViewItem item = new ListViewItem(status.SourceName);

            item.SubItems.Add(status.EventTypeName);
            item.SubItems.Add(status.CycleId);
            item.SubItems.Add(status.CurrentStep);
            item.SubItems.Add(status.TimeUtc != null ? Utils.Format("{0:HH:mm:ss.fff}", status.TimeUtc.Value.ToLocalTime()) : null);
            item.SubItems.Add(status.Message);

            item.Tag = status;
            EventsLV.Items.Add(item);

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
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

        /// <summary>
        /// Sets the locale to use.
        /// </summary>
        private async void Server_SetLocaleMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                string locale;
                using (SelectLocaleDlg dialog = Windows.Create<SelectLocaleDlg>())
                {
                    locale = await dialog.ShowDialogAsync(m_model.Session);
                }

                if (locale == null)
                {
                    return;
                }

                // the control remembers the choice for the next session it opens; the
                // model changes the one which is open.
                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                await m_model.ChangePreferredLocalesAsync(ConnectServerCTRL.PreferredLocales);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
