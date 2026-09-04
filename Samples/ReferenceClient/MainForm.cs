/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;

namespace Quickstarts.ReferenceClient
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// The connect control creates a <see cref="ManagedSession"/>, so the reconnect is driven
    /// by the connection state machine of the session rather than by this form. The session the
    /// form holds stays the same instance across a reconnect, which is why nothing here swaps
    /// it out or rebuilds the browse tree when the connection drops.
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
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry of the sample.</param>
        /// <param name="reverseConnectListeners">Creates the listener the Server menu
        /// arms, one per wait.</param>
        public MainForm(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            Func<ReverseConnectListener> reverseConnectListeners)
        {
            InitializeComponent();

            m_reverseConnectListeners = reverseConnectListeners;
            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            this.Text = m_configuration.ApplicationName;
            m_telemetry = telemetry;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private readonly Func<ReverseConnectListener> m_reverseConnectListeners;

        /// <summary>
        /// The listener for incoming server connections while the reverse connect of the
        /// Server menu is armed, null while it is not.
        /// </summary>
        /// <remarks>
        /// Both are released by <see cref="StopReverseConnectAsync"/>, which the wait
        /// itself, an error and the closing window all run through; releasing the listener
        /// is asynchronous, so it cannot happen in the generated <c>Dispose</c>.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Released asynchronously by StopReverseConnectAsync.")]
        private ReverseConnectListener m_reverseConnect;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Released asynchronously by StopReverseConnectAsync.")]
        private CancellationTokenSource m_reverseConnectCts;
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
                await ConnectServerCTRL.DisconnectAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Arms or disarms the wait for a server which connects out to this client.
        /// </summary>
        /// <remarks>
        /// This is the half of reverse connect a firewall makes necessary: the server can
        /// reach the client but not the other way round, so the client listens on the
        /// endpoint its <c>ClientConfiguration/ReverseConnect</c> block names and the
        /// server dials it. Nothing here opens a socket to the server - the session is
        /// created on the connection the server offered.
        /// </remarks>
        private async void Server_ReverseConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_reverseConnect != null)
                {
                    await StopReverseConnectAsync();
                    return;
                }

                if (!ReverseConnectListener.IsConfigured(m_configuration))
                {
                    MessageBox.Show(
                        "The client configuration carries no ClientConfiguration/ReverseConnect block, so there is no endpoint to listen on.",
                        this.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                ReverseConnectListener reverseConnect = m_reverseConnectListeners();
                var cts = new CancellationTokenSource();

                // binding the listener is what can fail here - the port may be taken - so
                // it happens before the menu claims the client is waiting.
                await reverseConnect.StartAsync(cts.Token);

                m_reverseConnect = reverseConnect;
                m_reverseConnectCts = cts;

                Server_ReverseConnectMI.Text = "Stop Reverse Connect";
                ConnectServerCTRL.UpdateStatus(
                    false,
                    DateTime.UtcNow,
                    "Waiting for a reverse connection on [{0}]",
                    String.Join(", ", ReverseConnectListener.GetClientEndpointUrls(m_configuration)));

                await WaitForReverseConnectionAsync(reverseConnect, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // the user disarmed the wait, which is not an error.
            }
            catch (Exception exception)
            {
                await StopReverseConnectAsync();
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Waits for the server named in the connect control and opens a session on the
        /// connection it offers.
        /// </summary>
        /// <remarks>
        /// A secure reverse connection costs more than one <c>ReverseHello</c>: the connect
        /// control spends the first one on <c>GetEndpoints</c> to learn the server
        /// certificate and reports that by returning a null session, so the wait runs again
        /// for the connection the server opens next.
        /// </remarks>
        private async Task WaitForReverseConnectionAsync(ReverseConnectListener reverseConnect, CancellationToken ct)
        {
            // a wait is always for one named server: the manager matches the ReverseHello
            // against this URL, so the connect control has to name the server which dials.
            if (String.IsNullOrEmpty(ConnectServerCTRL.ServerUrl) ||
                !Uri.TryCreate(ConnectServerCTRL.ServerUrl, UriKind.Absolute, out Uri serverUrl))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Enter the endpoint URL of the server which connects out to this client before waiting for it.");
            }

            ISession session = await reverseConnect.ConnectAsync(
                serverUrl,
                null,
                (connection, token) => ConnectServerCTRL.ConnectAsync(
                    connection,
                    ConnectServerCTRL.UseSecurity,
                    m_telemetry,
                    ConnectServerCTRL.DiscoverTimeout,
                    ct: token),
                ct: ct);

            if (session == null)
            {
                ConnectServerCTRL.UpdateStatus(true, DateTime.UtcNow, "The reverse connection was not completed.");
            }

            // the listener is released once a session exists: this sample connects to one
            // server, so nothing is left to wait for.
            await StopReverseConnectAsync();
        }

        /// <summary>
        /// Releases the reverse connect listener and puts the menu item back.
        /// </summary>
        private async Task StopReverseConnectAsync()
        {
            ReverseConnectListener reverseConnect = m_reverseConnect;
            CancellationTokenSource cts = m_reverseConnectCts;

            m_reverseConnect = null;
            m_reverseConnectCts = null;

            Server_ReverseConnectMI.Text = "Reverse Connect";

            if (cts != null)
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            if (reverseConnect != null)
            {
                await reverseConnect.DisposeAsync();
            }
        }

        /// <summary>
        /// Writes the address space of the connected server to a NodeSet2 XML file.
        /// </summary>
        /// <remarks>
        /// The export starts at the node selected in the browse tree, so that a user can
        /// take a single machine out of a large address space, and at the Objects folder
        /// when nothing is selected.
        /// </remarks>
        private async void Server_ExportNodeSetMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    MessageBox.Show(
                        "Connect to a server before exporting its address space.",
                        this.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                NodeId startNodeId = ObjectIds.ObjectsFolder;
                string startNodeName = "Objects";

                ReferenceDescription selection = BrowseCTRL.SelectedNode;

                if (selection != null)
                {
                    NodeId selectedId = ExpandedNodeId.ToNodeId(selection.NodeId, m_session.NamespaceUris);

                    if (!selectedId.IsNull)
                    {
                        startNodeId = selectedId;
                        startNodeName = selection.ToString();
                    }
                }

                using (var dialog = new SaveFileDialog {
                    Title = Utils.Format("Export the address space below {0}", startNodeName),
                    DefaultExt = "xml",
                    Filter = "NodeSet2 Files (*.NodeSet2.xml)|*.NodeSet2.xml|XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                    FileName = "ReferenceServer.NodeSet2.xml",
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    var settings = new NodeSetExportSettings {
                        StartNodeId = startNodeId,
                        NodeOptions = NodeSetExportOptions.Complete,
                    };

                    Cursor = Cursors.WaitCursor;

                    try
                    {
                        NodeSetExportResult result = await NodeSetExport.ExportToFileAsync(
                            m_session,
                            dialog.FileName,
                            settings);

                        MessageBox.Show(
                            Utils.Format(
                                "Exported {0} nodes of {1} namespace(s) to\r\n{2}",
                                result.NodeCount,
                                result.NamespaceUris.Count,
                                result.FilePath),
                            this.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    finally
                    {
                        Cursor = Cursors.Default;
                    }
                }
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
        /// <remarks>
        /// This also runs after a disconnect, where the connect control reports a null session
        /// and the browse tree empties itself.
        /// </remarks>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                // browse the instances in the server.
                await BrowseCTRL.InitializeAsync(m_session, ObjectIds.ObjectsFolder, m_telemetry, default, ReferenceTypeIds.Organizes, ReferenceTypeIds.Aggregates);
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
        /// The form is on its way out and there is nothing left to await on, so this closes the
        /// session synchronously rather than starting work which would outlive the window.
        /// </remarks>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // the reverse connect listener holds a bound port, so it goes before the
            // window does; cancelling the wait is what lets its task finish.
            m_reverseConnectCts?.Cancel();

            ConnectServerCTRL.Disconnect();
        }
        #endregion

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit this application?", "Reference Client", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\overview_-_reference_client.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }
    }
}
