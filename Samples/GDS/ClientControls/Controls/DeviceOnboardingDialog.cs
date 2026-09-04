/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Gds.Client.Controls
{
    /// <summary>
    /// Loads and removes OPC 10000-21 onboarding tickets on the registrar the GDS exposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 21 splits device provisioning in two. A manufacturer issues *tickets* - signed
    /// blobs that name the devices a customer is entitled to onboard - and the customer loads
    /// them into a registrar; later, a device that presents a matching identity is accepted
    /// without an administrator registering it by hand. This dialog is the first half: it
    /// drives <c>RegisterTickets</c> / <c>UnregisterTickets</c> on the registrar
    /// administration Object through the SDK's <see cref="OnboardingClient"/>.
    /// </para>
    /// <para>
    /// The registrar is found by browsing the Objects folder for the
    /// <c>DeviceRegistrarAdmin</c> Object rather than by a well-known NodeId, because the
    /// Onboarding companion model is not part of the shipped GDS packages and the sample
    /// server therefore builds the node in a namespace of its own.
    /// </para>
    /// </remarks>
    public partial class DeviceOnboardingDialog : SampleForm
    {
        private const string kRegistrarBrowseName = "DeviceRegistrarAdmin";

        private ISession m_session;
        private readonly ITelemetryContext m_telemetry;
        private NodeId m_registrarNodeId;
        private readonly List<(string Path, byte[] Ticket)> m_tickets =
            new List<(string, byte[])>();

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        public DeviceOnboardingDialog(ITelemetryContext telemetry)
        {
            InitializeComponent();
            Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
        }

        /// <summary>
        /// Shows the dialog against the supplied session.
        /// </summary>
        /// <param name="owner">The owning window.</param>
        /// <param name="session">A connected session with the GDS.</param>
        /// <param name="m_telemetry">The m_telemetry context used to report failures.</param>
        public void ShowDialog(IWin32Window owner, ISession session)
        {
            m_session = session;
            m_registrarNodeId = NodeId.Null;
            m_tickets.Clear();
            TicketsListView.Items.Clear();

            RegistrarTextBox.Text = "---";
            RegisterButton.Enabled = false;
            UnregisterButton.Enabled = false;

            ShowDialog(owner);
        }

        /// <summary>
        /// Looks the registrar up once the dialog is up.
        /// </summary>
        /// <remarks>
        /// The browse has to happen here rather than before <c>ShowDialog</c>: its
        /// continuations are posted to the message loop of the UI thread, so waiting for it
        /// from the thread that has not started that loop yet would deadlock.
        /// </remarks>
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (m_session == null)
            {
                StatusLabel.Text = "Connect to the GDS first.";
                return;
            }

            m_registrarNodeId = await FindRegistrarAsync(m_session);

            RegistrarTextBox.Text = m_registrarNodeId.IsNull
                ? "not found"
                : m_registrarNodeId.ToString();

            StatusLabel.Text = m_registrarNodeId.IsNull
                ? "This server does not expose a Part 21 registrar administration Object."
                : String.Empty;

            RegisterButton.Enabled = !m_registrarNodeId.IsNull;
            UnregisterButton.Enabled = !m_registrarNodeId.IsNull;
        }

        /// <summary>
        /// Browses the Objects folder for the registrar administration Object.
        /// </summary>
        private async Task<NodeId> FindRegistrarAsync(ISession session, CancellationToken ct = default)
        {
            try
            {
                var nodeToBrowse = new BrowseDescription {
                    NodeId = Opc.Ua.ObjectIds.ObjectsFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                };

                List<ReferenceDescription> references =
                    await ClientUtils.BrowseAsync(session, nodeToBrowse, false, ct);

                foreach (ReferenceDescription reference in references ?? new List<ReferenceDescription>())
                {
                    if (String.Equals(
                            reference.BrowseName.Name,
                            kRegistrarBrowseName,
                            StringComparison.Ordinal))
                    {
                        return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                    }
                }
            }
            catch (Exception exception)
            {
                #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                ExceptionDlg.Show(m_telemetry, Text, exception);
                #pragma warning restore CA1849
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Adds ticket files to the list.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A ticket is an <c>EncodedTicket</c> - a ByteString whose content Part 21 leaves to
        /// the device manufacturer. There is no standard file extension for one, and the
        /// sample cannot decode it either: the Onboarding companion model that defines
        /// <c>BaseTicketType</c> and its subtypes is not part of the shipped GDS packages, so
        /// there is nothing to parse it with. The registrar consequently stores the blob
        /// verbatim and keys it by its SHA-256 hash.
        /// </para>
        /// <para>
        /// The filter therefore leads with the extensions a manufacturer is likely to use but
        /// keeps All Files, because any file is a valid stand-in - which is what the hint on
        /// the dialog says, so an empty filter does not read as an oversight.
        /// </para>
        /// </remarks>
        private void AddButton_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog {
                Title = "Select the onboarding ticket(s)",
                Filter =
                    "Onboarding tickets (*.ticket;*.tkt;*.bin)|*.ticket;*.tkt;*.bin|" +
                    "All Files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            foreach (string path in dialog.FileNames)
            {
                try
                {
                    byte[] ticket = File.ReadAllBytes(path);
                    m_tickets.Add((path, ticket));

                    var item = new ListViewItem(Path.GetFileName(path));
                    item.SubItems.Add(ticket.Length.ToString(CultureInfo.CurrentCulture));
                    item.SubItems.Add(String.Empty);
                    TicketsListView.Items.Add(item);
                }
                catch (Exception exception)
                {
                    #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                    ExceptionDlg.Show(m_telemetry, Text, exception);
                    #pragma warning restore CA1849
                }
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            m_tickets.Clear();
            TicketsListView.Items.Clear();
            StatusLabel.Text = String.Empty;
        }

        private async void RegisterButton_Click(object sender, EventArgs e)
        {
            await CallRegistrarAsync(register: true);
        }

        private async void UnregisterButton_Click(object sender, EventArgs e)
        {
            await CallRegistrarAsync(register: false);
        }

        /// <summary>
        /// Calls <c>RegisterTickets</c> or <c>UnregisterTickets</c> and shows the per-ticket
        /// result the registrar reported.
        /// </summary>
        /// <remarks>
        /// The Method succeeds as a whole even when individual tickets are rejected - the
        /// outcome of each one is in the returned array, which is why the results are shown
        /// per row rather than as a single message.
        /// </remarks>
        private async Task CallRegistrarAsync(bool register)
        {
            if (m_session == null || m_registrarNodeId.IsNull || m_tickets.Count == 0)
            {
                return;
            }

            try
            {
                var client = new OnboardingClient(m_session, m_registrarNodeId, m_telemetry);

                byte[][] tickets = new byte[m_tickets.Count][];

                for (int ii = 0; ii < m_tickets.Count; ii++)
                {
                    tickets[ii] = m_tickets[ii].Ticket;
                }

                int[] results = register
                    ? await client.RegisterTicketsAsync(tickets)
                    : await client.UnregisterTicketsAsync(tickets);

                for (int ii = 0; ii < TicketsListView.Items.Count; ii++)
                {
                    TicketsListView.Items[ii].SubItems[2].Text = ii < results.Length
                        ? new StatusCode((uint)results[ii]).ToString()
                        : "---";
                }

                StatusLabel.Text = String.Format(
                    CultureInfo.CurrentCulture,
                    "{0} {1} ticket(s).",
                    register ? "Registered" : "Unregistered",
                    results.Length);
            }
            catch (Exception exception)
            {
                #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                ExceptionDlg.Show(m_telemetry, Text, exception);
                #pragma warning restore CA1849
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
