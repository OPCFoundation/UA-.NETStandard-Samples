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
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
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
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62553/Quickstarts/HistoricalEventsServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private bool m_connectedOnce;
        #endregion

        #region Private Methods
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
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    await EventsLV.SetSubscribedAsync(false);
                    await EventsLV.ChangeAreaAsync(ExpandedNodeId.ToNodeId(ObjectIds.Plaforms, m_session.NamespaceUris), true);

                    TypeDeclaration type = new TypeDeclaration();
                    type.NodeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.WellTestReportType, m_session.NamespaceUris);
                    type.Declarations = await ModelUtils.CollectInstanceDeclarationsForTypeAsync(m_session, type.NodeId);

                    await EventsLV.ChangeFilterAsync(new FilterDeclaration(type, null), true);
                    m_connectedOnce = true;
                }

                await EventsLV.SetSubscribedAsync(Events_EnableSubscriptionMI.Checked);
                await EventsLV.ChangeSessionAsync(m_session, true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;
                EventsLV.SessionReconnected(m_session);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }

        private async void Events_SelectEventTypeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                TypeDeclaration type = await new SelectTypeDlg().ShowDialogAsync(m_session, Opc.Ua.ObjectTypeIds.BaseEventType, "Select Event Type");

                if (type == null)
                {
                    return;
                }

                await EventsLV.ChangeFilterAsync(new FilterDeclaration(type, EventsLV.Filter), true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private async void Events_ModifyEventFilterMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                if (!new ModifyFilterDlg().ShowDialog(EventsLV.Filter))
                {
                    return;
                }

                await EventsLV.ChangeFilterAsync(EventsLV.Filter, true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private async void Events_SelectEventAreaMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                NodeId areaId = await new SelectNodeDlg().ShowDialogAsync(m_session, Opc.Ua.ObjectIds.Server, "Select Event Area", default, Opc.Ua.ReferenceTypeIds.HasEventSource);

                if (areaId == null)
                {
                    return;
                }

                await EventsLV.ChangeAreaAsync(areaId, true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private async void Events_EnableSubscriptionMI_CheckedChangedAsync(object sender, EventArgs e)
        {
            try
            {
                await EventsLV.SetSubscribedAsync(Events_EnableSubscriptionMI.Checked);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private async void Events_EditEventHistoryMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                await new ReadEventHistoryDlg().ShowDialogAsync(m_session, EventsLV.AreaId, new FilterDeclaration(EventsLV.Filter));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Sets the locale to use.
        /// </summary>
        private async void Server_SetLocaleMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                string locale = await new SelectLocaleDlg().ShowDialogAsync(m_session);

                if (locale == null)
                {
                    return;
                }

                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                m_session.ChangePreferredLocales(new StringCollection(ConnectServerCTRL.PreferredLocales));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
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
