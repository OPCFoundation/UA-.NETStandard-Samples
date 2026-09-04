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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Sample.Controls
{
    public partial class EventNotificationListListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public EventNotificationListListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private int m_maxEventCount = 20;
        private SubscriptionHandle m_subscription;
        private MonitoredItemHandle m_monitoredItem;

        /// <summary>
        /// An event displayed in the control.
        /// </summary>
        private sealed class ItemEvent
        {
            public MonitoredItemHandle MonitoredItem;
            public EventNotification Notification;
        }

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "Item",     HorizontalAlignment.Left, null },
            new object[] { "Type",     HorizontalAlignment.Left, null },
            new object[] { "Source",   HorizontalAlignment.Left, String.Empty },
            new object[] { "Time",     HorizontalAlignment.Center, String.Empty },
            new object[] { "Severity", HorizontalAlignment.Center, String.Empty },
            new object[] { "Message",  HorizontalAlignment.Left, String.Empty }
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// The maximum number of events to display in the control.
        /// </summary>
        [DefaultValue(20)]
        public int MaxEventCount
        {
            get { return m_maxEventCount; }
            set { m_maxEventCount = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Sets the subscription (and optionally the single item) displayed in the control.
        /// </summary>
        /// <remarks>
        /// The V2 engine keeps no notification cache, so the control starts empty and fills
        /// itself from the notifications the owner forwards.
        /// </remarks>
        public void Initialize(SubscriptionHandle subscription, MonitoredItemHandle monitoredItem)
        {
            Clear();

            m_subscription = subscription;
            m_monitoredItem = monitoredItem;
            Telemetry = m_subscription?.Session?.MessageContext?.Telemetry;

            AdjustColumns();
        }

        /// <summary>
        /// Processes the events of a notification.
        /// </summary>
        public void NotificationReceived(EventNotification[] notifications)
        {
            // get the events.
            List<ItemEvent> events = new List<ItemEvent>();

            foreach (EventNotification notification in notifications)
            {
                MonitoredItemHandle handle = m_subscription?.FindItem(notification.MonitoredItem);

                if (handle == null)
                {
                    continue;
                }

                if (m_monitoredItem != null && !Object.ReferenceEquals(handle, m_monitoredItem))
                {
                    continue;
                }

                events.Add(new ItemEvent { MonitoredItem = handle, Notification = notification });

                if (events.Count >= MaxEventCount)
                {
                    break;
                }
            }

            // check if nothing more to do.
            if (events.Count == 0)
            {
                return;
            }

            int offset = events.Count;

            // fill in earlier events.
            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                ItemEvent earlierEvent = listItem.Tag as ItemEvent;

                if (earlierEvent == null)
                {
                    continue;
                }

                events.Add(earlierEvent);

                if (events.Count >= MaxEventCount)
                {
                    break;
                }
            }

            UpdateEvents(events, offset);
            AdjustColumns();
        }

        /// <summary>
        /// Processes a change to the subscription.
        /// </summary>
        public void SubscriptionChanged()
        {
            // collect events for items that have been deleted.
            List<ListViewItem> itemsToRemove = new List<ListViewItem>();

            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                ItemEvent itemEvent = listItem.Tag as ItemEvent;

                if (itemEvent != null && m_subscription != null && !m_subscription.Items.Contains(itemEvent.MonitoredItem))
                {
                    itemsToRemove.Add(listItem);
                }
            }

            // remove events for items that have been deleted.
            foreach (ListViewItem listItem in itemsToRemove)
            {
                listItem.Remove();
            }
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            ViewMI.Enabled = ItemsLV.SelectedItems.Count == 1;
            DeleteMI.Enabled = ItemsLV.SelectedItems.Count > 0;
        }

        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            ViewMI_Click(this, null);
        }

        /// <summary>
        /// Updates the events displayed in the control.
        /// </summary>
        private void UpdateEvents(IList<ItemEvent> events, int offset)
        {
            // save selected indexes.
            List<int> indexes = new List<int>(ItemsLV.SelectedIndices.Count);

            foreach (int index in ItemsLV.SelectedIndices)
            {
                indexes.Add(index);
            }

            // update items.
            BeginUpdate();

            foreach (ItemEvent itemEvent in events)
            {
                AddItem(itemEvent);
            }

            EndUpdate();

            // preserve selection.
            foreach (int index in indexes)
            {
                ItemsLV.Items[index].Selected = false;

                if (index + offset < ItemsLV.Items.Count)
                {
                    ItemsLV.Items[index + offset].Selected = true;
                }
            }
        }

        /// <see cref="BaseListCtrl.UpdateItemAsync" />
        protected override async Task UpdateItemAsync(ListViewItem listItem, object item, CancellationToken ct = default)
        {
            ItemEvent itemEvent = item as ItemEvent;

            if (itemEvent == null)
            {
                await base.UpdateItemAsync(listItem, item, ct);
                return;
            }

            MonitoredItemHandle monitoredItem = itemEvent.MonitoredItem;
            EventNotification notification = itemEvent.Notification;

            // get the event fields selected by the filter the item was created with.
            NodeId eventType = SubscriptionHandle.GetEventFieldValue(monitoredItem, notification, new QualifiedName(Opc.Ua.BrowseNames.EventType)).TryGetValue(out NodeId eventTypeId) ? eventTypeId : NodeId.Null;
            string sourceName = SubscriptionHandle.GetEventFieldValue(monitoredItem, notification, new QualifiedName(Opc.Ua.BrowseNames.SourceName)).TryGetValue(out string name) ? name : null;
            DateTime? time = SubscriptionHandle.GetEventFieldValue(monitoredItem, notification, new QualifiedName(Opc.Ua.BrowseNames.Time)).TryGetValue(out DateTimeUtc eventTime) ? (DateTime)eventTime : null;
            ushort? severity = SubscriptionHandle.GetEventFieldValue(monitoredItem, notification, new QualifiedName(Opc.Ua.BrowseNames.Severity)).TryGetValue(out ushort eventSeverity) ? eventSeverity : null;
            LocalizedText message = SubscriptionHandle.GetEventFieldValue(monitoredItem, notification, new QualifiedName(Opc.Ua.BrowseNames.Message)).TryGetValue(out LocalizedText messageText) ? messageText : LocalizedText.Null;

            // fill in the columns.
            listItem.SubItems[0].Text = String.Format("[{0}]", (monitoredItem.Item != null) ? monitoredItem.Item.ServerId : 0);

            INode typeNode = await m_subscription.Session.NodeCache.FindAsync(eventType, ct);

            if (typeNode == null)
            {
                listItem.SubItems[1].Text = String.Format("{0}", eventType);
            }
            else
            {
                listItem.SubItems[1].Text = String.Format("{0}", typeNode);
            }

            listItem.SubItems[2].Text = String.Format("{0}", sourceName);

            if (time != null && time.Value != DateTime.MinValue)
            {
                listItem.SubItems[3].Text = String.Format("{0:HH:mm:ss.fff}", time.Value.ToLocalTime());
            }
            else
            {
                listItem.SubItems[3].Text = String.Empty;
            }

            listItem.SubItems[4].Text = String.Format("{0}", severity);

            if (!message.IsNull)
            {
                listItem.SubItems[5].Text = String.Format("{0}", message.Text);
            }
            else
            {
                listItem.SubItems[5].Text = String.Empty;
            }

            listItem.Tag = item;
        }
        #endregion

        #region Event Handlers
        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                ItemEvent itemEvent = SelectedTag as ItemEvent;

                if (itemEvent == null)
                {
                    return;
                }

                Windows.Create<ComplexValueEditDlg>().TryShowDialog(Variant.From(itemEvent.Notification.Fields), out _);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
