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

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SubscribeEventsDlg : Form, ISessionForm
    {
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

            m_PublishStatusChanged = new PublishStateChangedEventHandler(OnPublishStatusChanged);
            ItemsDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Items");

            m_dataset.Tables[0].Columns.Add("MonitoredItem", typeof(MonitoredItem));
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
        private DataSet m_dataset;
        private FilterDeclaration m_filter;
        private DisplayState m_state;
        private ISession m_session;
        private Subscription m_subscription;
        private PublishStateChangedEventHandler m_PublishStatusChanged;
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
        public async Task ChangeSessionAsync(ISession session, CancellationToken ct = default)
        {
            if (!Object.ReferenceEquals(session, m_session))
            {
                m_session = session;

                await BrowseCTRL.ChangeSessionAsync(m_session, ct);
                EventTypeCTRL.ChangeSession(m_session);
                EventFilterCTRL.ChangeSession(m_session);
                EventsCTRL.ChangeSession(m_session);

                if (m_subscription != null)
                {
                    m_subscription.PublishStatusChanged -= m_PublishStatusChanged;
                    m_subscription.FastEventCallback = null;
                    m_subscription = null;
                }

                if (m_session != null)
                {
                    // find new subscription.
                    foreach (Subscription subscription in m_session.Subscriptions)
                    {
                        if (Object.ReferenceEquals(subscription.Handle, this))
                        {
                            m_subscription = subscription;
                            m_subscription.PublishStatusChanged += m_PublishStatusChanged;
                            m_subscription.FastEventCallback = OnEvent;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the dialog has an active subscription assigned.
        /// </summary>
        public bool HasSubscription
        {
            get
            {
                return m_subscription != null;
            }
        }

        /// <summary>
        /// Sets the subscription used with the control.
        /// </summary>
        public void SetSubscription(Subscription subscription)
        {
            if (m_subscription != null)
            {
                m_subscription.PublishStatusChanged -= m_PublishStatusChanged;
                m_subscription.FastDataChangeCallback = null;
                m_subscription = null;
            }

            m_session = null;
            m_subscription = subscription;
            m_subscription.DisableMonitoredItemCache = true;
            m_subscription.PublishStatusChanged += m_PublishStatusChanged;
            m_subscription.FastEventCallback = OnEvent;
            m_dataset.Tables[0].Rows.Clear();

            if (m_subscription != null)
            {
                m_session = subscription.Session as Session;
                m_subscription.Handle = this;
            }
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
                    if (itemsToMonitor[ii] == null)
                    {
                        continue;
                    }

                    DataRow row = m_dataset.Tables[0].NewRow();

                    MonitoredItem monitoredItem = new MonitoredItem(m_subscription.DefaultItem);
                    monitoredItem.StartNodeId = itemsToMonitor[ii];
                    monitoredItem.AttributeId = Attributes.EventNotifier;
                    monitoredItem.NodeClass = NodeClass.Object;
                    monitoredItem.IndexRange = null;
                    monitoredItem.Encoding = null;
                    monitoredItem.Handle = row;
                    m_subscription.AddItem(monitoredItem);

                    await UpdateRowAsync(row, monitoredItem);
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
                await BrowseCTRL.InitializeAsync(m_session, Opc.Ua.ObjectTypeIds.BaseEventType, ct, Opc.Ua.ReferenceTypeIds.HasSubtype);
                BrowseCTRL.SelectNode((m_filter == null || m_filter.EventTypeId == null) ? Opc.Ua.ObjectTypeIds.BaseEventType : m_filter.EventTypeId);
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
        private async Task UpdateRowAsync(DataRow row, MonitoredItem monitoredItem, CancellationToken ct = default)
        {
            row[0] = monitoredItem;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(monitoredItem.AttributeId, null)];
            row[2] = await m_session.NodeCache.GetDisplayTextAsync(monitoredItem.StartNodeId, ct) + "/" + Attributes.GetBrowseName(monitoredItem.AttributeId);
            row[3] = monitoredItem.MonitoringMode;
            row[4] = monitoredItem.SamplingInterval;
            row[5] = monitoredItem.DiscardOldest;
        }

        /// <summary>
        /// Updates the row with the monitored item status.
        /// </summary>
        private void UpdateRow(DataRow row, MonitoredItemStatus status)
        {
            row[4] = status.SamplingInterval;

            if (ServiceResult.IsBad(status.Error))
            {
                row[6] = status.Error.StatusCode;
            }
            else
            {
                row[6] = (StatusCode)StatusCodes.Good;
            }
        }

        /// <summary>
        /// Gets the display string for the subscription status.
        /// </summary>
        private string GetDisplayString(Subscription subscription)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append((subscription.CurrentPublishingEnabled) ? "Enabled" : "Disabled");
            buffer.Append(" (");
            buffer.Append(subscription.CurrentPublishingInterval);
            buffer.Append("ms/");
            buffer.Append(subscription.CurrentPublishingInterval * subscription.CurrentKeepAliveCount / 1000);
            buffer.Append("s/");
            buffer.Append(subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount / 1000);
            buffer.Append("s}");

            return buffer.ToString();
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

            // update items.
            for (int ii = 0; ii < m_dataset.Tables[0].Rows.Count; ii++)
            {
                MonitoredItem monitoredItem = (MonitoredItem)m_dataset.Tables[0].Rows[ii][0];
                monitoredItem.Filter = filter;
            }

            // apply changes.
            await m_subscription.ApplyChangesAsync(ct);

            // show results.
            for (int ii = 0; ii < m_dataset.Tables[0].Rows.Count; ii++)
            {
                DataRow row = m_dataset.Tables[0].Rows[ii];
                MonitoredItem monitoredItem = (MonitoredItem)row[0];
                UpdateRow(row, monitoredItem.Status);
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

        private void AddDefaultFilter(List<FilterDeclarationField> fields, string browsePath, bool displayInList)
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
                List<InstanceDeclaration> declarations2 = await ClientUtils.CollectInstanceDeclarationsForTypeAsync(m_session, typeId, ct);

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
                return null;
            }

            ReferenceDescription reference = node.Tag as ReferenceDescription;

            NodeId typeId = null;
            int childCount = 0;

            foreach (TreeNode child in node.Nodes)
            {
                NodeId childTypeId = CollectTypeIds(child, typeIds);

                if (childTypeId != null)
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
        private void OnPublishStatusChanged(object sender, PublishStateChangedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, m_subscription))
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(m_PublishStatusChanged, sender, e);
                return;
            }

            try
            {
                if ((e.Status & PublishStateChangedMask.Stopped) != 0)
                {
                    SubscriptionStateTB.Text = "STOPPED";
                    SubscriptionStateTB.ForeColor = Color.Red;
                }
                else if ((e.Status & PublishStateChangedMask.Recovered) != 0)
                {
                    SubscriptionStateTB.Text = GetDisplayString(m_subscription);
                    SubscriptionStateTB.ForeColor = Color.Empty;
                }

                SequenceNumberTB.Text = m_subscription.SequenceNumber.ToString();
                LastNotificationTB.Text = m_subscription.LastNotificationTime.ToLocalTime().ToString("hh:mm:ss");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void OnEvent(Subscription subscription, EventNotificationList notification, IList<string> stringTable)
        {
            if (!Object.ReferenceEquals(subscription, m_subscription))
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new FastEventNotificationEventHandler(OnEvent), subscription, notification, stringTable);
                return;
            }

            try
            {
                foreach (EventFieldList e in notification.Events)
                {
                    EventsCTRL.DisplayEvent(e);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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

                if (!new EditSubscriptionDlg().ShowDialog(m_subscription))
                {
                    return;
                }

                await m_subscription.ModifyAsync();

                if (m_subscription.PublishingEnabled != m_subscription.CurrentPublishingEnabled)
                {
                    await m_subscription.SetPublishingModeAsync(m_subscription.PublishingEnabled);
                }

                SubscriptionStateTB.Text = GetDisplayString(m_subscription);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private async void BrowseCTRL_AfterSelectAsync(object sender, EventArgs e)
        {
            try
            {
                ReferenceDescription reference = BrowseCTRL.SelectedNode;

                if (reference == null || NodeId.IsNull(reference.NodeId) || reference.NodeId.IsAbsolute)
                {
                    await EventTypeCTRL.ShowTypeAsync(null);
                    return;
                }

                await EventTypeCTRL.ShowTypeAsync((NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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

                MonitoredItem monitoredItem = null;

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItem = (MonitoredItem)source.Row[0];
                    break;
                }

                if (monitoredItem == null)
                {
                    monitoredItem = new MonitoredItem(m_subscription.DefaultItem);
                }
                else
                {
                    monitoredItem = new MonitoredItem(monitoredItem);
                }

                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_session, monitoredItem, true))
                {
                    m_subscription.AddItem(monitoredItem);
                    DataRow row = m_dataset.Tables[0].NewRow();
                    monitoredItem.Handle = row;
                    await UpdateRowAsync(row, monitoredItem);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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

                MonitoredItem monitoredItem = null;

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItem = (MonitoredItem)source.Row[0];
                    break;
                }

                if (monitoredItem == null)
                {
                    return;
                }

                if (await new EditMonitoredItemDlg().ShowDialogAsync(m_session, monitoredItem, true))
                {
                    DataRow row = (DataRow)monitoredItem.Handle;
                    await UpdateRowAsync(row, monitoredItem);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                    MonitoredItem monitoredItem = (MonitoredItem)source.Row[0];
                    m_subscription.RemoveItem(monitoredItem);
                    source.Row.Delete();
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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

                List<MonitoredItem> monitoredItems = new List<MonitoredItem>();

                foreach (DataGridViewRow row in ItemsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    monitoredItems.Add((MonitoredItem)source.Row[0]);
                }

                if (monitoredItems.Count == 0)
                {
                    return;
                }

                MonitoringMode oldMonitoringMode = monitoredItems[0].MonitoringMode;
                MonitoringMode newMonitoringMode = new EditMonitoredItemDlg().ShowDialog(oldMonitoringMode);

                if (oldMonitoringMode != newMonitoringMode)
                {
                    List<MonitoredItem> itemsToModify = new List<MonitoredItem>();

                    foreach (MonitoredItem monitoredItem in monitoredItems)
                    {
                        DataRow row = (DataRow)monitoredItem.Handle;
                        row[5] = newMonitoringMode;

                        if (monitoredItem.Created)
                        {
                            itemsToModify.Add(monitoredItem);
                            continue;
                        }

                        monitoredItem.MonitoringMode = newMonitoringMode;
                    }

                    if (itemsToModify.Count != 0)
                    {
                        await m_subscription.SetMonitoringModeAsync(newMonitoringMode, itemsToModify);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
