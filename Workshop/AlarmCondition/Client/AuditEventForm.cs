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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Quickstarts.AlarmConditionClient
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// A form which displays the audit events produced by the server.
    /// </summary>
    /// <remarks>
    /// This window watches the audit trail for as long as it is open, which is what the
    /// streaming API of the V2 subscription engine is for: <see cref="IStreamingSubscription"/>
    /// hands the notifications out as an <see cref="IAsyncEnumerable{T}"/>, creates the
    /// monitored item when the enumeration starts and removes it again when it ends. The
    /// callback based <see cref="ISubscriptionNotificationHandler"/> the main form uses is the
    /// better fit there, because that subscription lives as long as the session and has to
    /// serve condition refreshes.
    /// </remarks>
    public partial class AuditEventForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public AuditEventForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuditEventForm"/> class.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="telemetry">The telemetry context the window logs with.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task InitializeAsync(ISession session, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            // the constructor has already built the controls; calling it again adds a second
            // copy of every one of them, which is what used to put two tool strips and two
            // lists on top of each other.
            m_session = session;
            m_telemetry = telemetry;

            // a table used to track event types.
            m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

            // the filter to use.
            m_filter = new FilterDefinition();

            m_filter.AreaId = ObjectIds.Server;
            m_filter.Severity = EventSeverity.Min;
            m_filter.IgnoreSuppressedOrShelved = true;
            m_filter.EventTypes = new NodeId[] { ObjectTypeIds.AuditUpdateMethodEventType };

            // find the fields of interest.
            m_filter.SelectClauses = await m_filter.ConstructSelectClausesAsync(m_session, ct, ObjectTypeIds.AuditUpdateMethodEventType);

            // the streaming subscription belongs to this window, so closing the window is what
            // deletes it on the server again.
            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, ClientUtils.DefaultSubscriptionOptions);

            // start pumping the audit events into the list. The underlying subscription and
            // its monitored item are created when the enumeration starts.
            m_pump = PumpAuditEventsAsync(m_cts.Token);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Handles a server reconnect event.
        /// </summary>
        /// <param name="session">The new session.</param>
        /// <remarks>
        /// The streaming subscription belongs to the subscription manager of the session and
        /// survives the reconnect together with its monitored item, so the stream keeps
        /// running and there is nothing to re-create here.
        /// </remarks>
        public void ReconnectComplete(ISession session)
        {
            m_session = session;
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private ITelemetryContext m_telemetry;
#pragma warning disable CA2213 // Justification: disposed asynchronously by AuditEventForm_FormClosing.
        private StreamingSubscription m_streaming;
#pragma warning restore CA2213
#pragma warning disable CA2213 // Justification: disposed by AuditEventForm_FormClosing.
        private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
#pragma warning restore CA2213
        private Task m_pump;
        private EventFilter m_eventFilter;
        private FilterDefinition m_filter;
        private Dictionary<NodeId, NodeId> m_eventTypeMappings;
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the audit events off the streaming subscription until the window is closed.
        /// </summary>
        /// <remarks>
        /// Each notification arrives on the enumeration instead of on a callback, so the
        /// window drives the loop and cancelling the token both ends the loop and removes the
        /// monitored item.
        /// </remarks>
        private async Task PumpAuditEventsAsync(CancellationToken ct)
        {
            MonitoredItemOptions options = m_filter.CreateMonitoredItemOptions(m_session);

            // the fields of a notification line up with the select clauses of this filter, so
            // the form keeps it: the engine does not report the filter of an item back.
            m_eventFilter = (EventFilter)options.Filter;

            try
            {
                await foreach (EventNotification notification in m_streaming
                    .SubscribeEventsAsync(m_filter.AreaId, m_eventFilter, options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    // the enumeration runs on a publish worker, so the display is updated on
                    // the UI thread. Without a window there is nothing to update, and the
                    // enumeration keeps running rather than ending for good.
                    if (!IsHandleCreated)
                    {
                        continue;
                    }

                    BeginInvoke(new Action<EventNotification>(DisplayAuditEventAsync), notification);
                }
            }
            catch (OperationCanceledException)
            {
                // the window was closed.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown.
                m_telemetry?.CreateLogger<AuditEventForm>()
                    .LogError(exception, "Failed to read the audit events.");
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Updates the display with an audit event read off the stream.
        /// </summary>
        private async void DisplayAuditEventAsync(EventNotification eventNotification)
        {
            try
            {
                // the engine reports the fields of an event, which line up with the select
                // clauses of the filter the item was created with.
                var notification = new EventFieldList {
                    ClientHandle = eventNotification.MonitoredItem?.ClientHandle ?? 0,
                    EventFields = eventNotification.Fields,
                };

                // check the type of event.
                NodeId eventTypeId = FormUtils.FindEventType(m_eventFilter, notification);

                // ignore unknown events.
                if ((eventTypeId).IsNull)
                {
                    return;
                }

                // construct the audit object.
                AuditUpdateMethodEventState audit = await FormUtils.ConstructEventAsync(
                    m_session,
                    m_eventFilter,
                    notification,
                    m_eventTypeMappings) as AuditUpdateMethodEventState;

                if (audit == null)
                {
                    return;
                }

                ListViewItem item = new ListViewItem(String.Empty);

                item.SubItems.Add(String.Empty); // Source
                item.SubItems.Add(String.Empty); // Type
                item.SubItems.Add(String.Empty); // Method
                item.SubItems.Add(String.Empty); // Status
                item.SubItems.Add(String.Empty); // Time
                item.SubItems.Add(String.Empty); // Message
                item.SubItems.Add(String.Empty); // Arguments

                // look up the condition type metadata in the local cache.
                INode type = await m_session.NodeCache.FindAsync(audit.TypeDefinitionId);

                // Source
                if (audit.SourceName != null)
                {
                    item.SubItems[0].Text = Utils.Format("{0}", audit.SourceName.Value);
                }
                else
                {
                    item.SubItems[0].Text = null;
                }

                // Type
                if (type != null)
                {
                    item.SubItems[1].Text = Utils.Format("{0}", type);
                }
                else
                {
                    item.SubItems[1].Text = null;
                }

                // look up the method metadata in the local cache.
                INode method = await m_session.NodeCache.FindAsync(audit.MethodId.Value);

                // Method
                if (method != null)
                {
                    item.SubItems[2].Text = Utils.Format("{0}", method);
                }
                else
                {
                    item.SubItems[2].Text = null;
                }

                // Status
                if (audit.Status != null)
                {
                    item.SubItems[3].Text = Utils.Format("{0}", audit.Status.Value);
                }
                else
                {
                    item.SubItems[3].Text = null;
                }

                // Time
                if (audit.Time != null)
                {
                    item.SubItems[4].Text = Utils.Format("{0:HH:mm:ss.fff}", audit.Time.Value.ToLocalTime());
                }
                else
                {
                    item.SubItems[4].Text = null;
                }

                // Message
                if (audit.Message != null)
                {
                    item.SubItems[5].Text = Utils.Format("{0}", audit.Message.Value);
                }
                else
                {
                    item.SubItems[5].Text = null;
                }

                // Arguments
                if (audit.InputArguments != null)
                {
                    item.SubItems[6].Text = Utils.Format("{0}", new Variant(audit.InputArguments.Value));
                }
                else
                {
                    item.SubItems[6].Text = null;
                }

                item.Tag = audit;
                EventsLV.Items.Add(item);

                // adjust the width of the columns.
                for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
                {
                    EventsLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_MonitorMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Events_ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                AuditUpdateMethodEventState audit = (AuditUpdateMethodEventState)EventsLV.SelectedItems[0].Tag;
                using (var dialog = new ViewEventDetailsDlg())
                {
                    dialog.ShowDialog(m_eventFilter, audit.Handle as EventFieldList);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Events_ClearMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Events_ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                EventsLV.Items.Clear();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the FormClosing event of the AuditEventForm control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.FormClosingEventArgs"/> instance containing the event data.</param>
        private void AuditEventForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StreamingSubscription streaming = m_streaming;

            m_streaming = null;
            m_pump = null;

            try
            {
                // cancelling the token ends the enumeration, which removes the monitored item,
                // and disposing the streaming subscription deletes it on the server.
                m_cts.Cancel();

                streaming?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                // the main form also closes this window when the session goes away, and then
                // the subscription cannot be deleted on the server any more. That is not
                // worth a dialog on the way out.
                m_telemetry?.CreateLogger<AuditEventForm>()
                    .LogError(exception, "Failed to delete the audit event subscription.");
            }
            finally
            {
                m_cts.Dispose();
            }
        }
        #endregion
    }
}
