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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Quickstarts.AlarmConditionClient.Model;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// A form which displays the audit events produced by the server.
    /// </summary>
    /// <remarks>
    /// The window owns an <see cref="AuditTrailModel"/>, which reads the audit trail off
    /// a streaming subscription for as long as the window is open and reports every
    /// event on the thread of the window. Closing the window disposes the model, which
    /// ends the stream and deletes the subscription on the server.
    /// </remarks>
    public partial class AuditEventForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private AuditEventForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates a window over an audit trail. <see cref="StartAsync"/> starts the trail.
        /// </summary>
        /// <param name="auditTrail">The trail, which the window disposes when it closes.</param>
        public AuditEventForm(AuditTrailModel auditTrail)
        {
            InitializeComponent();

            m_auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            m_auditTrail.AuditEventReceived += AuditTrail_AuditEventReceived;
        }
        #endregion

        #region Private Fields
#pragma warning disable CA2213 // Justification: disposed asynchronously by AuditEventForm_FormClosing.
        private readonly AuditTrailModel m_auditTrail;
#pragma warning restore CA2213
        #endregion

        #region Public Interface
        /// <summary>
        /// Starts reading the audit trail.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public Task StartAsync(CancellationToken ct = default)
        {
            return m_auditTrail.StartAsync(ct);
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Adds the row of an audit event the trail reported.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window, so the list is written
        /// directly. An event can still arrive after the window was closed.
        /// </remarks>
        private void AuditTrail_AuditEventReceived(object sender, AuditEventReceivedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                AuditEventSnapshot snapshot = e.Snapshot;

                var item = new ListViewItem(String.Empty);

                for (int ii = 1; ii < EventsLV.Columns.Count; ii++)
                {
                    item.SubItems.Add(String.Empty);
                }

                item.SubItems[0].Text = snapshot.SourceName;
                item.SubItems[1].Text = snapshot.TypeName;
                item.SubItems[2].Text = snapshot.MethodName;
                item.SubItems[3].Text = snapshot.StatusText;
                item.SubItems[4].Text = snapshot.Time.HasValue
                    ? Utils.Format("{0:HH:mm:ss.fff}", snapshot.Time.Value.ToLocalTime())
                    : null;
                item.SubItems[5].Text = snapshot.Message;
                item.SubItems[6].Text = snapshot.ArgumentsText;

                item.Tag = snapshot;
                EventsLV.Items.Add(item);

                // adjust the width of the columns.
                for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
                {
                    EventsLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_auditTrail.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shows every field of the selected audit event.
        /// </summary>
        private void Events_ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count != 1 ||
                    EventsLV.SelectedItems[0].Tag is not AuditEventSnapshot snapshot)
                {
                    return;
                }

                using (var dialog = new ViewEventDetailsDlg())
                {
                    dialog.ShowDialog(snapshot.Details.Filter, snapshot.Details.Fields);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_auditTrail.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Empties the list.
        /// </summary>
        private void Events_ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                EventsLV.Items.Clear();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_auditTrail.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Ends the trail when the window closes.
        /// </summary>
        /// <remarks>
        /// FormClosing cannot await, so the model is disposed on a thread pool thread and
        /// waited for. It logs a subscription which cannot be deleted any more - the main
        /// window closes this one when the session goes away - rather than showing it.
        /// </remarks>
        private void AuditEventForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClientUtils.WaitForTeardown(() => m_auditTrail.DisposeAsync().AsTask());
        }
        #endregion
    }
}
