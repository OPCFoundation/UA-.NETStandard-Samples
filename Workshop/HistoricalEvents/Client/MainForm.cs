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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.HistoricalEvents.Client.Model;

namespace Quickstarts.HistoricalEvents.Client
{
    // the SDK has an EventRecord of its own in Opc.Ua; the one the model hands out is meant.
    using EventRecord = Quickstarts.HistoricalEvents.Client.Model.EventRecord;

    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="HistoricalEventsClientModel"/>, which reads the history of the chosen
    /// area and streams its live events. The window tells the model which area, event
    /// type and filter the user picked, and writes the events the model reports into the
    /// event list.
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
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62553/Quickstarts/HistoricalEventsServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new HistoricalEventsClientModel(telemetry);
            m_model.EventReceived += Model_EventReceived;
            m_model.EventsCleared += Model_EventsCleared;
            m_model.FilterChanged += Model_FilterChanged;
            m_model.Error += Model_Error;

            // the list deletes through the model, with the area and filter of the window.
            EventsLV.Telemetry = telemetry;
            EventsLV.DeleteEvents = DeleteEventsAsync;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly HistoricalEventsClientModel m_model;
        #endregion

        #region Private Methods
        /// <summary>
        /// Deletes events from the history of the area the list shows.
        /// </summary>
        private Task DeleteEventsAsync(IReadOnlyList<EventRecord> events, CancellationToken ct)
        {
            return m_model.DeleteEventsAsync(m_model.AreaId, m_model.Filter, events, ct);
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
        /// The model is detached first: it ends its event stream and deletes the
        /// subscription before the control closes the session, because closing a session
        /// which still carries a subscription waits for the publish pipeline to drain.
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
                    EventsLV.Clear();
                    return;
                }

                // whether to stream live events is decided by the menu; the model applies
                // it while it attaches, and picks the default area and filter on the first
                // session.
                await m_model.SetSubscribedAsync(Events_EnableSubscriptionMI.Checked);
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
                // the streaming subscription belongs to the subscription manager of the
                // session and survives the reconnect together with its monitored item, so
                // the model has nothing to re-create.
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

        private async void Events_SelectEventTypeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                using var dialog = new SelectTypeDlg();
                TypeDeclaration type = await dialog.ShowDialogAsync(m_model, Opc.Ua.ObjectTypeIds.BaseEventType, "Select Event Type");

                if (type == null)
                {
                    return;
                }

                // the settings of the fields the new type shares with the old one are kept.
                await m_model.ChangeFilterAsync(new FilterDeclaration(type, m_model.Filter), true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void Events_ModifyEventFilterMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // the dialog edits the filter in place.
                using var dialog = new ModifyFilterDlg();
                if (!dialog.ShowDialog(m_model.Filter, m_telemetry))
                {
                    return;
                }

                await m_model.ChangeFilterAsync(m_model.Filter, true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void Events_SelectEventAreaMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // a shared dialog which browses the server itself.
                using var dialog = new SelectNodeDlg();
                NodeId areaId = await dialog.ShowDialogAsync(m_model.Session, Opc.Ua.ObjectIds.Server, "Select Event Area", m_telemetry, default, Opc.Ua.ReferenceTypeIds.HasEventSource);

                if (areaId.IsNull)
                {
                    return;
                }

                await m_model.ChangeAreaAsync(areaId, true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void Events_EnableSubscriptionMI_CheckedChangedAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.SetSubscribedAsync(Events_EnableSubscriptionMI.Checked);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void Events_EditEventHistoryMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // the dialog works on a copy of the filter, so what it changes stays there.
                using var dialog = new ReadEventHistoryDlg();
                await dialog.ShowDialogAsync(m_model, m_model.AreaId, new FilterDeclaration(m_model.Filter));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
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

                // a shared dialog which browses the locales of the server itself.
                using var dialog = new SelectLocaleDlg();
                string locale = await dialog.ShowDialogAsync(m_model.Session);

                if (locale == null)
                {
                    return;
                }

                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                await m_model.SetLocaleAsync(locale);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Adds an event the model reports to the list.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window, so the list is written
        /// directly. An event can still arrive after the window was closed.
        /// </remarks>
        private void Model_EventReceived(object sender, EventReceivedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            // live events go to the top, history keeps its order.
            EventsLV.AddEvent(e.Record, e.IsLive);
        }

        /// <summary>
        /// Clears the list when the events it shows no longer apply.
        /// </summary>
        private void Model_EventsCleared(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            EventsLV.Clear();
        }

        /// <summary>
        /// Rebuilds the columns of the list for a new filter.
        /// </summary>
        private void Model_FilterChanged(object sender, FilterChangedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            EventsLV.SetColumns(e.Filter);
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

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit the application?", "UA Sample Client", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\haeventsclientoverview.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }
    }
}
