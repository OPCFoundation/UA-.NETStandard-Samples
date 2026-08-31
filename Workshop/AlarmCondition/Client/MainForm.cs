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
using System.Linq;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using System.Threading.Tasks;
using System.Threading;

namespace Quickstarts.AlarmConditionClient
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// A form which displays the condition events produced by the server.
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

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62544/Quickstarts/AlarmConditionServer";
            this.Text = m_configuration.ApplicationName;

            // a table used to track event types.
            m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

            // the filter to use.
            m_filter = new FilterDefinition();
            m_telemetry = telemetry;

            m_filter.AreaId = ObjectIds.Server;
            m_filter.Severity = EventSeverity.Min;
            m_filter.IgnoreSuppressedOrShelved = true;
            m_filter.EventTypes = new NodeId[] { ObjectTypeIds.ConditionType };

            // the V2 engine takes the notification handler when the subscription is created,
            // so the form owns one for its whole lifetime and points it at its own methods.
            m_callbacks.EventCallback = OnEvents;

            // initialize controls.
            Conditions_Severity_AllMI.Checked = true;
            Conditions_Severity_AllMI.Tag = EventSeverity.Min;
            Conditions_Severity_LowMI.Tag = EventSeverity.Low;
            Conditions_Severity_MediumMI.Tag = EventSeverity.Medium;
            Conditions_Severity_HighMI.Tag = EventSeverity.High;

            Condition_Type_AllMI.Checked = true;
            Condition_Type_DialogsMI.Checked = false;
            Condition_Type_AlarmsMI.Checked = false;
            Condition_Type_LimitAlarmsMI.Checked = false;
            Condition_Type_DiscreteAlarmsMI.Checked = false;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// How long the form waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        private ApplicationConfiguration m_configuration;
        private ISession m_session;
#pragma warning disable CA2213 // Justification: disposed asynchronously by DeleteSubscriptionAsync.
        private ISubscription m_subscription;
#pragma warning restore CA2213
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();
        private MonitoredItemHandle m_monitoredItem;
        private EventFilter m_eventFilter;
        private int m_nextItemId;
        private FilterDefinition m_filter;
        private Dictionary<NodeId, NodeId> m_eventTypeMappings;
#pragma warning disable CA2213 // Justification: Audit event form is closed by existing UI disconnect logic.
        private AuditEventForm m_auditEventForm;
#pragma warning restore CA2213
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

                // check for disconnect.
                if (m_session == null)
                {
                    m_subscription = null;
                    m_monitoredItem = null;

                    if (m_auditEventForm != null)
                    {
                        m_auditEventForm.Close();
                        m_auditEventForm = null;
                    }

                    return;
                }

                // set a suitable initial state.
#pragma warning disable CA1508 // Justification: Analyzer does not account for session state changes in UI callbacks.
                if (m_session != null && !m_connectedOnce)
#pragma warning restore CA1508
                {
                    m_connectedOnce = true;
                }

                // create the default subscription. The V2 engine takes the settings through an
                // options monitor and creates the subscription on the server on its own worker.
                var options = new OptionsMonitor<SubscriptionOptions>(ClientUtils.DefaultSubscriptionOptions);

                m_subscription = ClientUtils.AddSubscription(m_session, m_callbacks, options);

                // must specify the fields that the form is interested in.
                m_filter.SelectClauses = await m_filter.ConstructSelectClausesAsync(
                    m_session,
                    default,
                    NodeId.Parse("ns=2;s=4:2"),
                    NodeId.Parse("ns=2;s=4:1"),
                    ObjectTypeIds.DialogConditionType,
                    ObjectTypeIds.ExclusiveLimitAlarmType,
                    ObjectTypeIds.NonExclusiveLimitAlarmType);

                // create a monitored item based on the current filter settings.
                m_monitoredItem = AddEventItem();

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout);

                // send an initial refresh.
                Conditions_RefreshMI_ClickAsync(sender, e);

                ConditionsMI.Enabled = true;
                ViewMI.Enabled = true;
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
                ConditionsMI.Enabled = false;
                ViewMI.Enabled = false;
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
                // survives the reconnect together with its monitored items, so neither the
                // subscription nor the item has to be replaced here.
                m_session = ConnectServerCTRL.Session;

                if (m_auditEventForm != null)
                {
                    m_auditEventForm.ReconnectComplete(m_session);
                }

                // send a refresh.
                await m_subscription.ConditionRefreshAsync();

                ConditionsMI.Enabled = true;
                ViewMI.Enabled = true;
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

        /// <summary>
        /// Deletes the subscription on the server and drops it from the subscription manager.
        /// </summary>
        /// <remarks>
        /// Done before the session is closed: closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async Task DeleteSubscriptionAsync()
        {
            ISubscription subscription = m_subscription;

            m_subscription = null;
            m_monitoredItem = null;

            if (subscription != null)
            {
                await subscription.DisposeAsync();
            }
        }
        #endregion

        #region Condition Methods
        /// <summary>
        /// Adds a monitored item for the current filter settings to the subscription.
        /// </summary>
        /// <remarks>
        /// The V2 engine identifies an item by a name which is unique within its subscription,
        /// and adding it to the collection is the create request.
        /// </remarks>
        private MonitoredItemHandle AddEventItem()
        {
            MonitoredItemOptions options = m_filter.CreateMonitoredItemOptions(m_session);

            // the fields of a notification line up with the select clauses of this filter, so
            // the form keeps it: the engine does not report the filter of an item back.
            m_eventFilter = (EventFilter)options.Filter;

            var handle = new MonitoredItemHandle(Utils.Format("Events{0}", ++m_nextItemId), options) {
                NodeClass = NodeClass.Object,
            };

            m_subscription.MonitoredItems.TryAdd(handle.Name, handle.Options, out IMonitoredItem item);
            handle.Item = item;

            return handle;
        }

        /// <summary>
        /// Updates the filter.
        /// </summary>
        private async Task UpdateFilterAsync(CancellationToken ct = default)
        {
            if (m_subscription != null)
            {
                // changing the filter changes the fields requested. this makes it
                // impossible to process notifications sent before the change.
                // to avoid this problem we create a new item and remove the old one - the
                // event filter of an item cannot be modified after it was created anyway.
                MonitoredItemHandle previous = m_monitoredItem;

                m_monitoredItem = AddEventItem();

                if (previous?.Item != null)
                {
                    m_subscription.MonitoredItems.TryRemove(previous.Item.ClientHandle);
                }

                await ClientUtils.WaitForPendingChangesAsync(m_subscription, kApplyTimeout, ct);

                // send a refresh since previously filtered conditions may be now available.
                Conditions_RefreshMI_ClickAsync(this, null);
            }
        }

        /// <summary>
        /// Calls <paramref name="callAsync"/> for every selected condition and reports a
        /// failed call in the status column of the row it belongs to.
        /// </summary>
        /// <remarks>
        /// The generated <c>*TypeClient</c> proxies call one object at a time and throw on
        /// a bad status, so the per-condition result handling which used to accompany a
        /// batched Call request lives here instead.
        /// </remarks>
        private async Task ForEachSelectedConditionAsync(
            Func<ConditionState, CancellationToken, Task> callAsync,
            CancellationToken ct)
        {
            foreach (ListViewItem item in ConditionsLV.SelectedItems.Cast<ListViewItem>().ToList())
            {
                if (item.Tag is not ConditionState condition)
                {
                    continue;
                }

                try
                {
                    await callAsync(condition, ct);
                }
                catch (ServiceResultException exception)
                {
                    item.SubItems[8].Text = Utils.Format("{0}", exception.StatusCode);
                }
            }
        }

        /// <summary>
        /// Enables or disables the selected conditions.
        /// </summary>
        /// <param name="enable">if set to <c>true</c> the conditions are enabled.</param>
        private Task EnableDisableConditionAsync(bool enable, CancellationToken ct = default)
        {
            return ForEachSelectedConditionAsync(
                (condition, token) => {
                    var client = new ConditionTypeClient(m_session, condition.NodeId, m_telemetry);
                    return enable
                        ? client.EnableAsync(token).AsTask()
                        : client.DisableAsync(token).AsTask();
                },
                ct);
        }

        /// <summary>
        /// Adds a comment to the selected conditions.
        /// </summary>
        private async Task AddCommentAsync(CancellationToken ct = default)
        {
            using (var dialog = new AddCommentDlg())
            {
#pragma warning disable CA1849 // Justification: Sample dialog API is synchronous and preserves current WinForms flow.
                string comment = dialog.ShowDialog(String.Empty);
#pragma warning restore CA1849

                if (comment == null)
                {
                    return;
                }

                await ForEachSelectedConditionAsync(
                    (condition, token) => new ConditionTypeClient(m_session, condition.NodeId, m_telemetry)
                        .AddCommentAsync(condition.EventId.Value, new LocalizedText(comment), token)
                        .AsTask(),
                    ct);
            }
        }

        /// <summary>
        /// Acknowledges the selected conditions.
        /// </summary>
        private async Task AcknowledgeAsync(CancellationToken ct = default)
        {
            using (var dialog = new AddCommentDlg())
            {
#pragma warning disable CA1849 // Justification: Sample dialog API is synchronous and preserves current WinForms flow.
                string comment = dialog.ShowDialog(String.Empty);
#pragma warning restore CA1849

                if (comment == null)
                {
                    return;
                }

                await ForEachSelectedConditionAsync(
                    (condition, token) => new AcknowledgeableConditionTypeClient(m_session, condition.NodeId, m_telemetry)
                        .AcknowledgeAsync(condition.EventId.Value, new LocalizedText(comment), token)
                        .AsTask(),
                    ct);
            }
        }

        /// <summary>
        /// Confirms the selected conditions.
        /// </summary>
        private async Task ConfirmAsync(CancellationToken ct = default)
        {
            using (var dialog = new AddCommentDlg())
            {
#pragma warning disable CA1849 // Justification: Sample dialog API is synchronous and preserves current WinForms flow.
                string comment = dialog.ShowDialog(String.Empty);
#pragma warning restore CA1849

                if (comment == null)
                {
                    return;
                }

                await ForEachSelectedConditionAsync(
                    (condition, token) => new AcknowledgeableConditionTypeClient(m_session, condition.NodeId, m_telemetry)
                        .ConfirmAsync(condition.EventId.Value, new LocalizedText(comment), token)
                        .AsTask(),
                    ct);
            }
        }

        /// <summary>
        /// Confirms the selected conditions.
        /// </summary>
        private Task ShelveAsync(bool shelving, bool oneShot, double shelvingTime, CancellationToken ct = default)
        {
            return ForEachSelectedConditionAsync(
                (condition, token) => {
                    // check if the node supports shelving. The event already carries the
                    // NodeId of the ShelvingState object, so no browse is needed here -
                    // AlarmConditionTypeClient.GetShelvingStateAsync resolves it from the
                    // server when the client does not have it.
                    if (condition.FindChild(
                            m_session.SystemContext,
                            new QualifiedName(BrowseNames.ShelvingState)) is not BaseObjectState shelvingState)
                    {
                        return Task.CompletedTask;
                    }

                    var client = new ShelvedStateMachineTypeClient(m_session, shelvingState.NodeId, m_telemetry);

                    if (!shelving)
                    {
                        return client.UnshelveAsync(token).AsTask();
                    }

                    return oneShot
                        ? client.OneShotShelveAsync(token).AsTask()
                        : client.TimedShelveAsync(shelvingTime, token).AsTask();
                },
                ct);
        }

        /// <summary>
        /// Responds to the dialog.
        /// </summary>
        private Task RespondAsync(int selectedResponse, CancellationToken ct = default)
        {
            // the caller should always make sure that only one dialog is selected.
            return ForEachSelectedConditionAsync(
                (condition, token) => condition is not DialogConditionState dialog
                    ? Task.CompletedTask
                    : new DialogConditionTypeClient(m_session, dialog.NodeId, m_telemetry)
                        .RespondAsync(selectedResponse, token)
                        .AsTask(),
                ct);
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Updates the display with the events the server reported.
        /// </summary>
        /// <remarks>
        /// The V2 engine calls this on a publish worker instead of on the UI thread, and it
        /// reports the whole notification instead of one event at a time.
        /// </remarks>
        private void OnEvents(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            EventNotification[] notifications,
            PublishState publishState)
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(
                    () => OnEvents(subscription, sequenceNumber, publishTime, notifications, publishState)));
                return;
            }

            DisplayEventsAsync(notifications);
        }

        /// <summary>
        /// Updates the display with the events of one notification, one at a time.
        /// </summary>
        /// <remarks>
        /// A later event of a condition updates the entry an earlier one created, and
        /// processing an event awaits, so the events of a notification are awaited in turn
        /// rather than all started at once.
        /// </remarks>
        private async void DisplayEventsAsync(EventNotification[] notifications)
        {
            foreach (EventNotification notification in notifications)
            {
                await ProcessEventAsync(notification);
            }
        }

        /// <summary>
        /// Updates the display with a single event notification.
        /// </summary>
        private async Task ProcessEventAsync(EventNotification eventNotification)
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

                // check for refresh start.
                if (eventTypeId == ObjectTypeIds.RefreshStartEventType)
                {
                    ConditionsLV.Items.Clear();
                    return;
                }

                // check for refresh end.
                if (eventTypeId == ObjectTypeIds.RefreshEndEventType)
                {
                    return;
                }

                // construct the condition object.
                ConditionState condition = await FormUtils.ConstructEventAsync(
                    m_session,
                    m_eventFilter,
                    notification,
                    m_eventTypeMappings) as ConditionState;

                if (condition == null)
                {
                    return;
                }

                // look up the condition type metadata in the local cache. this has to happen
                // before the list view is touched: the handler is called once per event and
                // every await lets the message loop deliver the next one, which would then
                // walk a list view holding an entry that has no condition in its Tag yet.
                INode type = await m_session.NodeCache.FindAsync(condition.TypeDefinitionId);

                // look for existing entry.
                ListViewItem item = null;

                for (int ii = 0; ii < ConditionsLV.Items.Count; ii++)
                {
                    ConditionState current = (ConditionState)ConditionsLV.Items[ii].Tag;

                    // the combination of a condition and branch id uniquely identify an item in the display.
                    if (current.NodeId == condition.NodeId && current.BranchId.Value == condition.BranchId.Value)
                    {
                        // match found but watch out for out of order events (async processing can cause this to happen).
                        if (current.Time.Value > condition.Time.Value)
                        {
                            return;
                        }

                        item = ConditionsLV.Items[ii];
                        break;
                    }
                }

                // create a new entry.
                if (item == null)
                {
                    item = new ListViewItem(String.Empty);

                    item.SubItems.Add(String.Empty); // Condition
                    item.SubItems.Add(String.Empty); // Branch
                    item.SubItems.Add(String.Empty); // Type
                    item.SubItems.Add(String.Empty); // Severity
                    item.SubItems.Add(String.Empty); // Time
                    item.SubItems.Add(String.Empty); // State
                    item.SubItems.Add(String.Empty); // Message
                    item.SubItems.Add(String.Empty); // Comment

                    ConditionsLV.Items.Add(item);
                }

                // from here to the end of the handler nothing is awaited, so the entry is
                // complete and carries its condition before the next event is processed.

                // Source
                if (condition.SourceName != null)
                {
                    item.SubItems[0].Text = Utils.Format("{0}", condition.SourceName.Value);
                }
                else
                {
                    item.SubItems[0].Text = null;
                }

                // Condition
                if (condition.ConditionName != null)
                {
                    item.SubItems[1].Text = Utils.Format("{0}", condition.ConditionName.Value);
                }
                else
                {
                    item.SubItems[1].Text = null;
                }

                // Branch
                if (condition.BranchId != null && !(condition.BranchId.Value).IsNull)
                {
                    item.SubItems[2].Text = Utils.Format("{0}", condition.BranchId.Value);
                }
                else
                {
                    item.SubItems[2].Text = null;
                }

                // Type
                if (type != null)
                {
                    item.SubItems[3].Text = Utils.Format("{0}", type);
                }
                else
                {
                    item.SubItems[3].Text = null;
                }

                // Severity
                if (condition.Severity != null)
                {
                    item.SubItems[4].Text = Utils.Format("{0}", (EventSeverity)condition.Severity.Value);
                }
                else
                {
                    item.SubItems[4].Text = null;
                }

                // Time
                if (condition.Time != null)
                {
                    item.SubItems[5].Text = Utils.Format("{0:HH:mm:ss.fff}", condition.Time.Value.ToLocalTime());
                }
                else
                {
                    item.SubItems[5].Text = null;
                }

                // State
                if (condition.EnabledState != null && condition.EnabledState.EffectiveDisplayName != null)
                {
                    item.SubItems[6].Text = Utils.Format("{0}", condition.EnabledState.EffectiveDisplayName.Value);
                }
                else
                {
                    item.SubItems[6].Text = null;
                }

                // Message
                if (condition.Message != null)
                {
                    item.SubItems[7].Text = Utils.Format("{0}", condition.Message.Value);
                }
                else
                {
                    item.SubItems[7].Text = null;
                }

                // Comment
                if (condition.Comment != null)
                {
                    item.SubItems[8].Text = Utils.Format("{0}", condition.Comment.Value);
                }
                else
                {
                    item.SubItems[8].Text = null;
                }

                item.Tag = condition;

                // set the color based on the retain bit.
                if (!condition.Retain.Value)
                {
                    item.ForeColor = Color.DimGray;
                }
                else
                {
                    if ((condition.BranchId.Value).IsNull)
                    {
                        item.ForeColor = Color.Empty;
                    }
                    else
                    {
                        item.ForeColor = Color.DarkGray;
                    }
                }

                // adjust the width of the columns.
                for (int ii = 0; ii < ConditionsLV.Columns.Count; ii++)
                {
                    ConditionsLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_RefreshMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_RefreshMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // the server id of a V2 subscription is not public, and it does not have to
                // be: the engine calls ConditionRefresh for the subscription itself. The
                // generated ConditionTypeClient proxy is used for the per condition methods
                // below, which name the condition they act on.
                if (m_subscription != null)
                {
                    await m_subscription.ConditionRefreshAsync();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_EnableMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_EnableMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await EnableDisableConditionAsync(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_DisableMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_DisableMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await EnableDisableConditionAsync(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the DropDownOpening event of the ConditionsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void ConditionsMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                bool connected = m_session != null && m_session.Connected;

                Conditions_SetAreaFilterMI.Enabled = connected;
                Conditions_SetTypeMI.Enabled = connected;
                Conditions_SetSeverityMI.Enabled = connected;
                Conditions_EnableMI.Enabled = connected;
                Conditions_DisableMI.Enabled = connected;
                Conditions_AddCommentMI.Enabled = connected;
                Conditions_RefreshMI.Enabled = connected;
                Conditions_AcknowledgeMI.Enabled = connected;
                Conditions_ConfirmMI.Enabled = connected;
                Conditions_RespondMI.Enabled = connected;
                Conditions_ShelvingMI.Enabled = connected;
                Conditions_MonitorMI.Enabled = connected;

                if (ConditionsLV.SelectedItems.Count == 0)
                {
                    Conditions_EnableMI.Enabled = false;
                    Conditions_DisableMI.Enabled = false;
                    Conditions_AddCommentMI.Enabled = false;
                    Conditions_AcknowledgeMI.Enabled = false;
                    Conditions_ConfirmMI.Enabled = false;
                    Conditions_RespondMI.Enabled = false;
                    Conditions_ShelvingMI.Enabled = false;
                    Conditions_MonitorMI.Enabled = false;
                }

                if (ConditionsLV.SelectedItems.Count > 1)
                {
                    Conditions_RespondMI.Enabled = false;
                    Conditions_MonitorMI.Enabled = false;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_AddCommentMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_AddCommentMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await AddCommentAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_AcknowledgeMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_AcknowledgeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await AcknowledgeAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_ConfirmMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_ConfirmMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConfirmAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_UnshelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_UnshelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ShelveAsync(false, false, 0);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_ManualShelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_ManualShelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ShelveAsync(true, false, 0);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_OneShotShelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_OneShotShelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ShelveAsync(true, true, 0);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_TimedShelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_TimedShelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ShelveAsync(true, false, 30000);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_MonitorMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_MonitorMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (ConditionsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                ConditionState condition = (ConditionState)ConditionsLV.SelectedItems[0].Tag;
                using (var dialog = new ViewEventDetailsDlg())
                {
                    dialog.ShowDialog(m_eventFilter, condition.Handle as EventFieldList);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_SeverityMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_SeverityMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Conditions_Severity_AllMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_AllMI);
                Conditions_Severity_LowMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_LowMI);
                Conditions_Severity_MediumMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_MediumMI);
                Conditions_Severity_HighMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_HighMI);

                m_filter.Severity = (EventSeverity)((ToolStripMenuItem)sender).Tag;

                await UpdateFilterAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_TypeMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_TypeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Condition_Type_AllMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_AllMI);
                Condition_Type_DialogsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_DialogsMI);
                Condition_Type_AlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_AlarmsMI);
                Condition_Type_LimitAlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_LimitAlarmsMI);
                Condition_Type_DiscreteAlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_DiscreteAlarmsMI);

                List<NodeId> selectedTypes = new List<NodeId>();

                if (Condition_Type_AllMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.ConditionType);
                }

                if (Condition_Type_DialogsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.DialogConditionType);
                }

                if (Condition_Type_AlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.AlarmConditionType);
                }

                if (Condition_Type_LimitAlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.ExclusiveLimitAlarmType);
                    selectedTypes.Add(ObjectTypeIds.NonExclusiveLimitAlarmType);
                }

                if (Condition_Type_DiscreteAlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.DiscreteAlarmType);
                }

                m_filter.EventTypes = selectedTypes;

                await UpdateFilterAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_SetAreaFilterMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_SetAreaFilterMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                NodeId areaId;
                using (var dialog = new SetAreaFilterDlg())
                {
                    areaId = dialog.ShowDialog(m_session);
                }

                if (areaId.IsNull)
                {
                    return;
                }

                m_filter.AreaId = areaId;

                await UpdateFilterAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the View_AuditEventsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void View_AuditEventsMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_auditEventForm == null)
                {
                    m_auditEventForm = new AuditEventForm();
                    await m_auditEventForm.InitializeAsync(m_session, m_telemetry);

                    m_auditEventForm.FormClosing += new FormClosingEventHandler(AuditEventForm_FormClosing);
                }

                m_auditEventForm.Show();
                m_auditEventForm.BringToFront();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the FormClosing event of the AuditEventForm control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.FormClosingEventArgs"/> instance containing the event data.</param>
        void AuditEventForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Object.ReferenceEquals(m_auditEventForm, sender))
            {
                m_auditEventForm = null;
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_RespondMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void Conditions_RespondMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (ConditionsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                DialogConditionState dialog = ConditionsLV.SelectedItems[0].Tag as DialogConditionState;

                if (dialog == null)
                {
                    return;
                }

                int selectedResponse;
                using (var responseDialog = new DialogResponseDlg())
                {
                    selectedResponse = responseDialog.ShowDialog(dialog);
                }

                if (selectedResponse != -1)
                {
                    await RespondAsync(selectedResponse);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Help_ContentsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\acclientoverview.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
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
    }
}
