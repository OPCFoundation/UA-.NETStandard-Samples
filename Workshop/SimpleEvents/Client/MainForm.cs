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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using System.Threading;
using System.Threading.Tasks;

namespace Quickstarts.SimpleEvents.Client
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    /// <remarks>
    /// This sample exists to show one thing - the events of a server arriving in a list - and
    /// it watches them for exactly as long as it is connected. That is what the streaming API
    /// of the V2 subscription engine is for: <see cref="IStreamingSubscription"/> hands the
    /// notifications out as an <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/>,
    /// creates the monitored item when the enumeration starts and removes it again when it
    /// ends, so the sample reads events in a plain <c>await foreach</c> instead of wiring up a
    /// notification handler. The AlarmCondition sample shows the callback based
    /// <see cref="ISubscriptionNotificationHandler"/>, which is the better fit for a
    /// subscription that outlives one screen and has to serve condition refreshes.
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
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62563/Quickstarts/SimpleEventsServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed asynchronously by DeleteSubscriptionAsync.")]
        private StreamingSubscription m_streaming;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed by DeleteSubscriptionAsync.")]
        private CancellationTokenSource m_cts;
        private EventFilter m_eventFilter;
        private Opc.Ua.Client.Controls.FilterDeclaration m_filter;
        private Dictionary<NodeId, Type> m_knownEventTypes;
        private Dictionary<NodeId, NodeId> m_eventTypeMappings;
        private bool m_connectedOnce;
        private readonly ITelemetryContext m_telemetry;
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
                await DeleteSubscriptionAsync();
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
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    await DeleteSubscriptionAsync();
                    return;
                }

                // set a suitable initial state.
                if (!m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                await CreateSubscriptionAsync();
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
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                // a V2 subscription belongs to the subscription manager of the session and
                // survives the reconnect together with its monitored items, so the stream
                // keeps running and there is nothing to re-create here.
                m_session = ConnectServerCTRL.Session;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            DeleteSubscriptionAsync().GetAwaiter().GetResult();
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private async Task CreateSubscriptionAsync(CancellationToken ct = default)
        {
            await DeleteSubscriptionAsync();

            // the streaming subscription lives as long as the connection: the underlying OPC UA
            // subscription is created when the first SubscribeXxxAsync enumeration starts.
            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine.");
            }

            m_streaming = new StreamingSubscription(manager, ClientUtils.DefaultSubscriptionOptions);
            m_cts = new CancellationTokenSource();

            // a table used to track event types.
            m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

            NodeId knownEventId = ExpandedNodeId.ToNodeId(ObjectTypeIds.SystemCycleStatusEventType, m_session.NamespaceUris);

            m_knownEventTypes = new Dictionary<NodeId, Type>();
            m_knownEventTypes.Add(knownEventId, typeof(SystemCycleStatusEventState));

            TypeDeclaration type = new TypeDeclaration();
            type.NodeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.SystemCycleStatusEventType, m_session.NamespaceUris);
            type.Declarations = await ClientUtils.CollectInstanceDeclarationsForTypeAsync(m_session, type.NodeId, ct);

            // the filter to use. The fields of a notification line up with its select clauses,
            // so the form keeps it: the engine does not report the filter of an item back.
            m_filter = new FilterDeclaration(type, null);
            m_eventFilter = m_filter.GetFilter();

            // start reading the events. Nothing is awaited here on purpose: the enumeration
            // runs for as long as the client is connected.
            _ = PumpEventsAsync(m_cts.Token);
        }

        /// <summary>
        /// Reads the events off the streaming subscription until the client disconnects.
        /// </summary>
        /// <remarks>
        /// Each notification arrives on the enumeration instead of on a callback, so the form
        /// drives the loop and cancelling the token both ends the loop and removes the
        /// monitored item again.
        /// </remarks>
        private async Task PumpEventsAsync(CancellationToken ct)
        {
            IStreamingSubscription streaming = m_streaming;

            var options = new MonitoredItemOptions {
                StartNodeId = Opc.Ua.ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(Opc.Ua.ObjectIds.Server, m_eventFilter, options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    // without a window there is nothing to update, and the enumeration keeps
                    // running rather than ending for good.
                    if (!IsHandleCreated)
                    {
                        continue;
                    }

                    // the enumeration runs on a publish worker, so the display is updated on
                    // the UI thread.
                    BeginInvoke(new Action<EventNotification>(DisplayEventAsync), notification);
                }
            }
            catch (OperationCanceledException)
            {
                // the client disconnected.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown.
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to read the events.");
            }
        }

        /// <summary>
        /// Stops the stream and deletes the subscription on the server.
        /// </summary>
        /// <remarks>
        /// Done before the session is closed: closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async Task DeleteSubscriptionAsync()
        {
            StreamingSubscription streaming = m_streaming;
            CancellationTokenSource cts = m_cts;

            m_streaming = null;
            m_cts = null;
            m_filter = null;
            m_eventFilter = null;

            if (cts != null)
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            if (streaming == null)
            {
                return;
            }

            try
            {
                await streaming.DisposeAsync();
            }
            catch (Exception exception)
            {
                // this also runs when the session has already gone away, and then the
                // subscription cannot be deleted on the server any more.
                m_telemetry?.CreateLogger<MainForm>()
                    .LogError(exception, "Failed to delete the event subscription.");
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Updates the display with an event read off the stream.
        /// </summary>
        private async void DisplayEventAsync(EventNotification eventNotification)
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
                NodeId eventTypeId = ClientUtils.FindEventType(m_eventFilter, notification);

                // ignore unknown events.
                if ((eventTypeId).IsNull)
                {
                    return;
                }

                // construct the audit object.
                SystemCycleStatusEventState status = await ClientUtils.ConstructEventAsync(
                    m_session,
                    m_eventFilter,
                    notification,
                    m_knownEventTypes,
                    m_eventTypeMappings) as SystemCycleStatusEventState;

                if (status == null)
                {
                    return;
                }

                ListViewItem item = new ListViewItem(String.Empty);

                item.SubItems.Add(String.Empty); // Source
                item.SubItems.Add(String.Empty); // Type
                item.SubItems.Add(String.Empty); // CycleId
                item.SubItems.Add(String.Empty); // Step
                item.SubItems.Add(String.Empty); // Time
                item.SubItems.Add(String.Empty); // Message

                // look up the condition type metadata in the local cache.
                INode type = await m_session.NodeCache.FindAsync(status.TypeDefinitionId);

                // Source
                if (status.SourceName != null)
                {
                    item.SubItems[0].Text = Utils.Format("{0}", status.SourceName.Value);
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

                // CycleId
                if (status.CycleId != null)
                {
                    item.SubItems[2].Text = Utils.Format("{0}", status.CycleId.Value);
                }
                else
                {
                    item.SubItems[2].Text = null;
                }

                // Step
                if (status.CurrentStep != null && status.CurrentStep.Value != null)
                {
                    item.SubItems[3].Text = Utils.Format("{0}", status.CurrentStep.Value.Name);
                }
                else
                {
                    item.SubItems[3].Text = null;
                }

                // Time
                if (status.Time != null)
                {
                    item.SubItems[4].Text = Utils.Format("{0:HH:mm:ss.fff}", status.Time.Value.ToLocalTime());
                }
                else
                {
                    item.SubItems[4].Text = null;
                }

                // Message
                if (status.Message != null)
                {
                    item.SubItems[5].Text = Utils.Format("{0}", status.Message.Value);
                }
                else
                {
                    item.SubItems[5].Text = null;
                }

                item.Tag = status;
                EventsLV.Items.Add(item);

                // adjust the width of the columns.
                for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
                {
                    EventsLV.Columns[ii].Width = -2;
                }
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
                if (m_session == null)
                {
                    return;
                }

                string locale;
                using (SelectLocaleDlg dialog = new SelectLocaleDlg())
                {
                    locale = await dialog.ShowDialogAsync(m_session);
                }

                if (locale == null)
                {
                    return;
                }

                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                await m_session.ChangePreferredLocalesAsync(new List<string>(ConnectServerCTRL.PreferredLocales));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
