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
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SubscribeEventsDlg : Form, ISessionForm
    {
        /// <summary>
        /// How long the dialog waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SubscribeEventsDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            BrowseCTRL.BrowseTV.CheckBoxes = true;
            BrowseCTRL.BrowseTV.AfterCheck += new TreeViewEventHandler(BrowseTV_AfterCheck);

            m_subscription.Callbacks.EventCallback = OnEvents;
            m_subscription.Callbacks.KeepAliveCallback = OnKeepAlive;
            m_subscription.Callbacks.StateChangedCallback = OnSubscriptionStateChanged;
            ItemsDV.AutoGenerateColumns = false;
            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            ImageList = new ClientUtils().ImageList;
            #pragma warning restore CA2000

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Items");

            m_dataset.Tables[0].Columns.Add("MonitoredItem", typeof(MonitoredItemHandle));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("NodeAttribute", typeof(string));
            m_dataset.Tables[0].Columns.Add("MonitoringMode", typeof(MonitoringMode));
            m_dataset.Tables[0].Columns.Add("SamplingInterval", typeof(double));
            m_dataset.Tables[0].Columns.Add("DiscardOldest", typeof(bool));
            m_dataset.Tables[0].Columns.Add("OperationStatus", typeof(StatusCode));

            ItemsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        private FilterDeclaration m_filter;
        private DisplayState m_state;
        private ITelemetryContext m_telemetry;

        // the subscription, its items and everything the engine needs said to it; the
        // grid below is what shows them.
        private readonly SampleSubscription m_subscription = new SampleSubscription();
        #endregion

        private enum DisplayState
        {
            EditItems,
            SelectEventType,
            SelectEventFields,
            ApplyChanges,
            ViewUpdates
        }

        #region Public Interface
        /// <summary>
        /// Changes the session used.
        /// </summary>
        public async Task ChangeSessionAsync(ISession session, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            m_telemetry = telemetry;

            if (Object.ReferenceEquals(session, m_subscription.Session))
            {
                return;
            }

            await BrowseCTRL.ChangeSessionAsync(session, ct);
            EventTypeCTRL.ChangeSession(session);
            EventFilterCTRL.ChangeSession(session);
            EventsCTRL.ChangeSession(session);

            // a V2 subscription belongs to the subscription manager of the session it was
            // created on, and it survives a reconnect together with its monitored items.
            // It only has to be dropped when it does not belong to the new session.
            if (m_subscription.ChangeSession(session))
            {
                m_dataset.Tables[0].Rows.Clear();
            }
        }

        /// <summary>
        /// Returns true if the dialog has an active subscription assigned.
        /// </summary>
        public bool HasSubscription => m_subscription.HasSubscription;

        /// <summary>
        /// The handler the dialog needs when a subscription is created on its behalf.
        /// </summary>
        /// <remarks>
        /// The V2 engine takes the notification handler when the subscription is created, so a
        /// caller which creates the subscription itself has to pass this one.
        /// </remarks>
        public ISubscriptionNotificationHandler NotificationHandler => m_subscription.NotificationHandler;

        /// <summary>
        /// Creates the subscription the dialog displays on the session.
        /// </summary>
        public ISubscription CreateSubscription(ISession session, SubscriptionOptions options = null)
        {
            ISubscription subscription = m_subscription.Create(session, options);

            m_dataset.Tables[0].Rows.Clear();

            return subscription;
        }

        /// <summary>
        /// Sets the subscription used with the control.
        /// </summary>
        /// <param name="subscription">The subscription, created with <see cref="NotificationHandler"/>.</param>
        /// <param name="session">The session the subscription was created on. A V2 subscription
        /// does not point back at its session, so the dialog has to be told.</param>
        /// <param name="options">The options monitor the subscription was created with, so the
        /// dialog can reconfigure it. Optional: without it the subscription cannot be edited.</param>
        public void SetSubscription(ISubscription subscription, ISession session, OptionsMonitor<SubscriptionOptions> options = null)
        {
            m_subscription.Adopt(subscription, session, options);
            m_dataset.Tables[0].Rows.Clear();
        }

        /// <summary>
        /// Adds items to the subscription.
        /// </summary>
        public async Task AddItemsAsync(CancellationToken ct, params NodeId[] itemsToMonitor)
        {
            if (itemsToMonitor != null)
            {
                SetDisplayState(DisplayState.EditItems);

                for (int ii = 0; ii < itemsToMonitor.Length; ii++)
                {
                    if (itemsToMonitor[ii].IsNull)
                    {
                        continue;
                    }

                    DataRow row = m_dataset.Tables[0].NewRow();

                    MonitoredItemHandle handle = m_subscription.Add(new MonitoredItemOptions {
                        StartNodeId = itemsToMonitor[ii],
                        AttributeId = Attributes.EventNotifier,
                        IndexRange = null,
                        Encoding = QualifiedName.Null,
                    });

                    handle.Row = row;

                    await UpdateRowAsync(row, handle);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
        }

        /// <summary>
        /// Moves the sequence forward.
        /// </summary>
        public async Task NextAsync(CancellationToken ct = default)
        {
            if (m_state == DisplayState.ViewUpdates)
            {
                return;
            }

            if (m_state == DisplayState.SelectEventType)
            {
                await UpdateFilterAsync(ct);
            }

            SetDisplayState(++m_state);

            if (m_state == DisplayState.SelectEventType)
            {
                await BrowseCTRL.InitializeAsync(m_subscription.Session, Opc.Ua.ObjectTypeIds.BaseEventType, m_telemetry, ct, Opc.Ua.ReferenceTypeIds.HasSubtype);
                BrowseCTRL.SelectNode((m_filter == null || m_filter.EventTypeId.IsNull) ? Opc.Ua.ObjectTypeIds.BaseEventType : m_filter.EventTypeId);
                await EventTypeCTRL.ShowTypeAsync(Opc.Ua.ObjectTypeIds.BaseEventType, ct);
                return;
            }

            if (m_state == DisplayState.SelectEventFields)
            {
                await EventFilterCTRL.SetFilterAsync(m_filter, ct);
                return;
            }

            if (m_state == DisplayState.ApplyChanges)
            {
                await UpdateItemsAsync(ct);
                return;
            }

            if (m_state == DisplayState.ViewUpdates)
            {
                EventsCTRL.SetFilter(m_filter);
                return;
            }
        }

        /// <summary>
        /// Moves the sequence backward.
        /// </summary>
        public async Task BackAsync(CancellationToken ct = default)
        {
            if (m_state == DisplayState.EditItems)
            {
                return;
            }

            SetDisplayState(--m_state);

            if (m_state == DisplayState.SelectEventFields)
            {
                await EventFilterCTRL.SetFilterAsync(m_filter, ct);
                return;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets the display state for the control.
        /// </summary>
        private void SetDisplayState(DisplayState state)
        {
            m_state = state;

            switch (m_state)
            {
                case DisplayState.EditItems:
                {
                    ItemsDV.Visible = true;
                    EventTypePN.Visible = false;
                    EventsCTRL.Visible = false;
                    EventFilterCTRL.Visible = false;
                    SamplingIntervalCH.Visible = true;
                    DiscardOldestCH.Visible = true;
                    DiscardOldestCH.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    OperationStatusCH.Visible = false;
                    BackBTN.Visible = false;
                    NextBTN.Visible = true;
                    NextBTN.Enabled = true;
                    OkBTN.Visible = false;
                    break;
                }

                case DisplayState.SelectEventType:
                {
                    ItemsDV.Visible = false;
                    EventTypePN.Visible = true;
                    EventsCTRL.Visible = false;
                    EventFilterCTRL.Visible = false;
                    BackBTN.Visible = true;
                    NextBTN.Visible = true;
                    NextBTN.Enabled = true;
                    OkBTN.Visible = false;
                    break;
                }

                case DisplayState.SelectEventFields:
                {
                    ItemsDV.Visible = false;
                    EventTypePN.Visible = false;
                    EventsCTRL.Visible = false;
                    EventFilterCTRL.Visible = true;
                    BackBTN.Visible = true;
                    NextBTN.Visible = true;
                    NextBTN.Enabled = true;
                    OkBTN.Visible = false;
                    break;
                }

                case DisplayState.ApplyChanges:
                {
                    ItemsDV.Visible = true;
                    EventTypePN.Visible = false;
                    EventsCTRL.Visible = false;
                    SamplingIntervalCH.Visible = true;
                    DiscardOldestCH.Visible = true;
                    DiscardOldestCH.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    OperationStatusCH.Visible = true;
                    BackBTN.Visible = true;
                    NextBTN.Visible = true;
                    NextBTN.Enabled = true;
                    OkBTN.Visible = false;
                    break;
                }

                case DisplayState.ViewUpdates:
                {
                    ItemsDV.Visible = false;
                    EventTypePN.Visible = false;
                    EventsCTRL.Visible = true;
                    BackBTN.Visible = true;
                    NextBTN.Enabled = false;
                    OkBTN.Visible = false;
                    break;
                }
            }
        }

        /// <summary>
        /// Updates the row with the monitored item.
        /// </summary>
        private async Task UpdateRowAsync(DataRow row, MonitoredItemHandle handle, CancellationToken ct = default)
        {
            MonitoredItemOptions settings = handle.Settings;

            row[0] = handle;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(settings.AttributeId, Variant.Null)];
            row[2] = await m_subscription.Session.NodeCache.GetDisplayTextAsync(settings.StartNodeId, ct) + "/" + Attributes.GetBrowseName(settings.AttributeId);
            row[3] = settings.MonitoringMode;
            row[4] = settings.SamplingInterval.TotalMilliseconds;
            row[5] = settings.DiscardOldest;
        }

        /// <summary>
        /// Updates the row with the values the server revised the monitored item to.
        /// </summary>
        private static void UpdateRevisedValues(DataRow row, MonitoredItemHandle handle)
        {
            IMonitoredItem item = handle.Item;

            if (item == null)
            {
                return;
            }

            row[4] = item.CurrentSamplingInterval.TotalMilliseconds;

            if (ServiceResult.IsBad(item.Error))
            {
                row[6] = item.Error.StatusCode;
            }
            else
            {
                row[6] = (StatusCode)StatusCodes.Good;
            }
        }

        /// <summary>
        /// Updates the items with the current filter.
        /// </summary>
        private async Task UpdateItemsAsync(CancellationToken ct = default)
        {
            List<FilterDeclarationField> fields = new List<FilterDeclarationField>();

            foreach (FilterDeclarationField field in m_filter.Fields)
            {
                // only keep fields that are used.
                if (field.Selected || field.FilterEnabled)
                {
                    fields.Add(field);
                    continue;
                }

                // add mandatory fields.
                switch (field.InstanceDeclaration.BrowsePathDisplayText)
                {
                    case Opc.Ua.BrowseNames.EventId:
                    case Opc.Ua.BrowseNames.EventType:
                    case Opc.Ua.BrowseNames.Time:
                    {
                        field.Selected = true;
                        fields.Add(field);
                        break;
                    }
                }
            }

            m_filter.Fields = fields;

            // construct filter.
            EventFilter filter = m_filter.GetFilter();

            // every item carries the filter the wizard just built, and the ones which are
            // still pending are created with it.
            foreach (MonitoredItemHandle handle in m_subscription.Items)
            {
                handle.Configure(options => options with { Filter = filter });
            }

            await m_subscription.ApplyAsync(kApplyTimeout, ct);

            // show results.
            for (int ii = 0; ii < m_dataset.Tables[0].Rows.Count; ii++)
            {
                DataRow row = m_dataset.Tables[0].Rows[ii];
                UpdateRevisedValues(row, (MonitoredItemHandle)row[0]);
            }
        }

        /// <summary>
        /// Updates the filter from the controls.
        /// </summary>
        private async Task UpdateFilterAsync(CancellationToken ct = default)
        {
            // get selected declarations.
            List<InstanceDeclaration> declarations = new List<InstanceDeclaration>();
            NodeId eventTypeId = await CollectInstanceDeclarationsAsync(declarations, ct);

            if (m_filter == null)
            {
                m_filter = new FilterDeclaration();
            }

            if (m_filter.Fields == null || m_filter.Fields.Count == 0)
            {
                m_filter.Fields = new List<FilterDeclarationField>();

                // select some default values to display in the list.
                AddDefaultFilter(m_filter.Fields, Opc.Ua.BrowseNames.EventType, true);
                AddDefaultFilter(m_filter.Fields, Opc.Ua.BrowseNames.SourceName, true);
                AddDefaultFilter(m_filter.Fields, Opc.Ua.BrowseNames.SourceNode, true);
                AddDefaultFilter(m_filter.Fields, Opc.Ua.BrowseNames.Time, true);
                AddDefaultFilter(m_filter.Fields, Opc.Ua.BrowseNames.Severity, true);
                AddDefaultFilter(m_filter.Fields, Opc.Ua.BrowseNames.Message, true);
            }

            // copy settings from existing filter.
            List<FilterDeclarationField> fields = new List<FilterDeclarationField>();

            foreach (InstanceDeclaration declaration in declarations)
            {
                if (declaration.NodeClass != NodeClass.Variable)
                {
                    continue;
                }

                FilterDeclarationField field = new FilterDeclarationField(declaration);

                foreach (FilterDeclarationField field2 in m_filter.Fields)
                {
                    if (field2.InstanceDeclaration.BrowsePathDisplayText == field.InstanceDeclaration.BrowsePathDisplayText)
                    {
                        field.DisplayInList = field2.DisplayInList;
                        field.FilterEnabled = field2.FilterEnabled;
                        field.FilterOperator = field2.FilterOperator;
                        field.FilterValue = field2.FilterValue;
                        break;
                    }
                }

                fields.Add(field);
            }

            // update filter.
            m_filter.EventTypeId = eventTypeId;
            m_filter.Fields = fields;
        }

        private void AddDefaultFilter(IList<FilterDeclarationField> fields, string browsePath, bool displayInList)
        {
            FilterDeclarationField field = new FilterDeclarationField();
            field.InstanceDeclaration = new InstanceDeclaration();
            field.InstanceDeclaration.BrowsePathDisplayText = browsePath;
            field.DisplayInList = displayInList;
            fields.Add(field);
        }

        /// <summary>
        /// Collects the instance declarations for the selected types.
        /// </summary>
        private async Task<NodeId> CollectInstanceDeclarationsAsync(List<InstanceDeclaration> declarations, CancellationToken ct = default)
        {
            List<NodeId> typeIds = new List<NodeId>();

            // get list of selected types.
            NodeId baseTypeId = CollectTypeIds(BrowseCTRL.BrowseTV.Nodes[0], typeIds);

            // merge declarations from the selected types.
            foreach (NodeId typeId in typeIds)
            {
                List<InstanceDeclaration> declarations2 = await ClientUtils.CollectInstanceDeclarationsForTypeAsync(m_subscription.Session, typeId, ct);

                for (int ii = 0; ii < declarations2.Count; ii++)
                {
                    bool found = false;

                    for (int jj = 0; jj < declarations.Count; jj++)
                    {
                        if (declarations[jj].BrowsePathDisplayText == declarations2[ii].BrowsePathDisplayText)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        declarations.Add(declarations2[ii]);
                    }
                }
            }

            return baseTypeId;
        }

        /// <summary>
        /// Collects the types selected in the control.
        /// </summary>
        private NodeId CollectTypeIds(TreeNode node, List<NodeId> typeIds)
        {
            if (!node.Checked)
            {
                return NodeId.Null;
            }

            ReferenceDescription reference = node.Tag as ReferenceDescription;

            NodeId typeId = NodeId.Null;
            int childCount = 0;

            foreach (TreeNode child in node.Nodes)
            {
                NodeId childTypeId = CollectTypeIds(child, typeIds);

                if (!childTypeId.IsNull)
                {
                    typeId = childTypeId;
                    childCount++;
                }
            }

            if (reference != null)
            {
                if (childCount != 1)
                {
                    typeId = (NodeId)reference.NodeId;
                }

                if (childCount == 0)
                {
                    typeIds.Add((NodeId)reference.NodeId);
                }
            }

            return typeId;
        }

        /// <summary>
        /// Sets the checks for the currently checked event type.
        /// </summary>
        private void SetEventTypeChecks(TreeNode node, bool isChecked)
        {
            if (!isChecked)
            {
                foreach (TreeNode child in node.Nodes)
                {
                    child.Checked = false;
                }
            }

            if (node.Parent == null || node.Parent.Checked == isChecked)
            {
                return;
            }

            if (isChecked)
            {
                node.Parent.Checked = true;
                return;
            }

            bool found = false;

            foreach (TreeNode child in node.Parent.Nodes)
            {
                if (child.Checked)
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                return;
            }

            node.Parent.Checked = false;
        }
        #endregion

        #region Event Handlers
        private void OnKeepAlive(ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, uint, DateTime, PublishState>(OnKeepAlive), subscription, sequenceNumber, publishTime, publishStateMask);
                return;
            }

            UpdatePublishStatus(subscription, sequenceNumber, publishTime, publishStateMask);
        }

        private void OnSubscriptionStateChanged(ISubscription subscription, Opc.Ua.Client.Subscriptions.SubscriptionState state, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, Opc.Ua.Client.Subscriptions.SubscriptionState, PublishState>(OnSubscriptionStateChanged), subscription, state, publishStateMask);
                return;
            }

            if (!Object.ReferenceEquals(subscription, m_subscription.Subscription))
            {
                return;
            }

            try
            {
                SubscriptionStateTB.Text = SampleSubscription.Describe(subscription);
                SubscriptionStateTB.ForeColor = Color.Empty;

                // the state change is what reports that the engine applied the pending item
                // changes, so this is where the revised values become visible.
                if (m_state == DisplayState.ApplyChanges)
                {
                    foreach (DataRow row in m_dataset.Tables[0].Rows)
                    {
                        UpdateRevisedValues(row, (MonitoredItemHandle)row[0]);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shows the publish state the engine reported with a notification.
        /// </summary>
        private void UpdatePublishStatus(ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            if (!Object.ReferenceEquals(subscription, m_subscription.Subscription))
            {
                return;
            }

            try
            {
                if ((publishStateMask & PublishState.Stopped) != 0)
                {
                    SubscriptionStateTB.Text = "STOPPED";
                    SubscriptionStateTB.ForeColor = Color.Red;
                }
                else if ((publishStateMask & PublishState.Recovered) != 0)
                {
                    SubscriptionStateTB.Text = SampleSubscription.Describe(subscription);
                    SubscriptionStateTB.ForeColor = Color.Empty;
                }

                SequenceNumberTB.Text = sequenceNumber.ToString();
                LastNotificationTB.Text = publishTime.ToLocalTime().ToString("hh:mm:ss");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void OnEvents(ISubscription subscription, uint sequenceNumber, DateTime publishTime, EventNotification[] notifications, PublishState publishStateMask)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<ISubscription, uint, DateTime, EventNotification[], PublishState>(OnEvents), subscription, sequenceNumber, publishTime, notifications, publishStateMask);
                return;
            }

            if (!Object.ReferenceEquals(subscription, m_subscription.Subscription))
            {
                return;
            }

            try
            {
                UpdatePublishStatus(subscription, sequenceNumber, publishTime, publishStateMask);

                foreach (EventNotification notification in notifications)
                {
                    EventsCTRL.DisplayEvent(new List<Variant>(notification.Fields.ToArray()));
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void BackBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await BackAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void NextBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await NextAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void SubscriptionStateTB_DropDownItemClickedAsync(object sender, ToolStripItemClickedEventArgs e)
        {
            try
            {
                if (!Object.ReferenceEquals(e.ClickedItem, Subscription_EditMI))
                {
                    return;
                }

                if (!m_subscription.CanEditSubscription)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                if (!new EditSubscriptionDlg().ShowDialog(m_subscription.Options, m_telemetry))
                #pragma warning restore CA2000
                {
                    return;
                }

                // the engine applies the new options on its own worker, so the revised values
                // are only there once the pending change settled.
                await m_subscription.WaitForChangesAsync(kApplyTimeout);

                SubscriptionStateTB.Text = SampleSubscription.Describe(m_subscription.Subscription);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void BrowseCTRL_AfterSelectAsync(object sender, EventArgs e)
        {
            try
            {
                ReferenceDescription reference = BrowseCTRL.SelectedNode;

                if (reference == null || (reference.NodeId).IsNull || reference.NodeId.IsAbsolute)
                {
                    await EventTypeCTRL.ShowTypeAsync(NodeId.Null);
                    return;
                }

                await EventTypeCTRL.ShowTypeAsync((NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void BrowseTV_AfterCheck(object sender, TreeViewEventArgs e)
        {
            try
            {
                SetEventTypeChecks(e.Node, e.Node.Checked);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void CancelBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.Modal)
                {
                    DialogResult = DialogResult.Cancel;
                }
                else
                {
                    Close();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void NewMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_state != DisplayState.EditItems)
                {
                    return;
                }

                MonitoredItemHandle selected = null;

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    selected = (MonitoredItemHandle)source.Row[0];
                    break;
                }

                // a new item starts from the settings of the selected one, or from the defaults.
                // It is added straight away because the dialog edits it in place; a cancelled
                // dialog takes it back off again, before it ever reached the server.
                MonitoredItemHandle handle = m_subscription.Add(selected?.Settings ?? new MonitoredItemOptions {
                    AttributeId = Attributes.EventNotifier,
                });

                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_subscription.Session, handle, true, m_telemetry))
                #pragma warning restore CA2000
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    handle.Row = row;
                    await UpdateRowAsync(row, handle);
                    m_dataset.Tables[0].Rows.Add(row);
                }
                else
                {
                    m_subscription.Remove(handle);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_state != DisplayState.EditItems)
                {
                    return;
                }

                MonitoredItemHandle handle = null;

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    handle = (MonitoredItemHandle)source.Row[0];
                    break;
                }

                if (handle == null)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_subscription.Session, handle, true, m_telemetry))
                #pragma warning restore CA2000
                {
                    await UpdateRowAsync(handle.Row, handle);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_state != DisplayState.EditItems)
                {
                    return;
                }

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    var handle = (MonitoredItemHandle)source.Row[0];

                    m_subscription.Remove(handle);
                    source.Row.Delete();
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void ItemsDV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (m_state == DisplayState.EditItems)
                {
                    EditMI_ClickAsync(sender, e);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void SetMonitoringModeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_state != DisplayState.EditItems && m_state != DisplayState.ApplyChanges)
                {
                    return;
                }

                var handles = new List<MonitoredItemHandle>();

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    handles.Add((MonitoredItemHandle)source.Row[0]);
                }

                if (handles.Count == 0)
                {
                    return;
                }

                MonitoringMode oldMonitoringMode = handles[0].Settings.MonitoringMode;
                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                MonitoringMode newMonitoringMode = new EditMonitoredItemDlg().ShowDialog(oldMonitoringMode);
                #pragma warning restore CA2000

                if (oldMonitoringMode != newMonitoringMode)
                {
                    foreach (MonitoredItemHandle handle in handles)
                    {
                        handle.Row[3] = newMonitoringMode;
                    }

                    await m_subscription.SetMonitoringModeAsync(handles, newMonitoringMode, kApplyTimeout);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
