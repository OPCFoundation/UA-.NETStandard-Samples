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
    public partial class DataChangeNotificationListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public DataChangeNotificationListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private int m_maxChangeCount = 20;
        private bool m_showHistory = false;
        private SubscriptionHandle m_subscription;
        private MonitoredItemHandle m_monitoredItem;
        private bool m_publishingStopped;

        /// <summary>
        /// A data change displayed in the control.
        /// </summary>
        private sealed class ItemNotification
        {
            public MonitoredItemHandle MonitoredItem;
            public DataValue Value;
        }

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "Item",        HorizontalAlignment.Left, null },
            new object[] { "Variable",    HorizontalAlignment.Left, null },
            new object[] { "Value",       HorizontalAlignment.Left, String.Empty, 250 },
            new object[] { "Status",      HorizontalAlignment.Left, String.Empty },
            new object[] { "Source Time", HorizontalAlignment.Center, String.Empty },
            new object[] { "Server Time", HorizontalAlignment.Center, String.Empty }
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// The maximum number of changes to display in the control.
        /// </summary>
        [DefaultValue(20)]
        public int MaxChangeCount
        {
            get { return m_maxChangeCount; }
            set { m_maxChangeCount = value; }
        }

        /// <summary>
        /// Whether to show previous values in the control after an update.
        /// </summary>
        [DefaultValue(false)]
        public bool ShowHistory
        {
            get { return m_showHistory; }
            set { m_showHistory = value; }
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
            m_publishingStopped = false;
            Telemetry = m_subscription?.Session?.MessageContext?.Telemetry;

            AdjustColumns();
        }

        /// <summary>
        /// Processes the data changes of a notification.
        /// </summary>
        public async Task NotificationReceivedAsync(DataValueChange[] notifications, PublishState publishStateMask, CancellationToken ct = default)
        {
            m_publishingStopped = (publishStateMask & PublishState.Stopped) != 0;

            // get the changes.
            List<ItemNotification> changes = new List<ItemNotification>();

            foreach (DataValueChange notification in notifications)
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

                changes.Add(new ItemNotification { MonitoredItem = handle, Value = notification.Value });
            }

            // check if nothing more to do.
            if (changes.Count == 0)
            {
                return;
            }

            int offset = changes.Count;

            if (m_showHistory)
            {
                // fill in earlier changes.
                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    ItemNotification change = listItem.Tag as ItemNotification;

                    if (change == null)
                    {
                        continue;
                    }

                    changes.Add(change);

                    if (changes.Count >= MaxChangeCount)
                    {
                        break;
                    }
                }
            }

            await UpdateChangesAsync(changes, offset, ct);
            AdjustColumns();
        }

        /// <summary>
        /// Processes a change to the subscription.
        /// </summary>
        public void SubscriptionChanged()
        {
            // collect changes for items that have been deleted.
            List<ListViewItem> itemsToRemove = new List<ListViewItem>();

            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                ItemNotification change = listItem.Tag as ItemNotification;

                if (change != null && m_subscription != null && !m_subscription.Items.Contains(change.MonitoredItem))
                {
                    itemsToRemove.Add(listItem);
                }
            }

            // remove changes for items that have been deleted.
            foreach (ListViewItem listItem in itemsToRemove)
            {
                listItem.Remove();
            }
        }

        /// <summary>
        /// Updates the display after the publish status for the subscription changes.
        /// </summary>
        public async Task PublishStatusChangedAsync(PublishState publishStateMask, CancellationToken ct = default)
        {
            if ((publishStateMask & PublishState.Stopped) != 0)
            {
                m_publishingStopped = true;
            }
            else if ((publishStateMask & PublishState.Recovered) != 0)
            {
                m_publishingStopped = false;
            }

            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                ItemNotification change = listItem.Tag as ItemNotification;

                if (change != null)
                {
                    await UpdateItemAsync(listItem, change, ct);
                }
            }

            AdjustColumns();
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
        /// Updates the changes displayed in the control.
        /// </summary>
        private async Task UpdateChangesAsync(IList<ItemNotification> changes, int offset, CancellationToken ct = default)
        {
            // save selected indexes.
            List<int> indexes = new List<int>(ItemsLV.SelectedIndices.Count);

            foreach (int index in ItemsLV.SelectedIndices)
            {
                indexes.Add(index);
            }

            // add all new values.
            if (m_showHistory)
            {
                BeginUpdate();

                foreach (ItemNotification change in changes)
                {
                    AddItem(change);
                }

                EndUpdate();
            }

            // only update changed values.
            else
            {
                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    listItem.ForeColor = Color.Gray;
                }

                for (int ii = changes.Count - 1; ii >= 0; ii--)
                {
                    bool found = false;

                    foreach (ListViewItem listItem in ItemsLV.Items)
                    {
                        ItemNotification change = listItem.Tag as ItemNotification;

                        if (change != null && Object.ReferenceEquals(change.MonitoredItem, changes[ii].MonitoredItem))
                        {
                            await UpdateItemAsync(listItem, changes[ii], ct);
                            found = true;
                            listItem.ForeColor = Color.Empty;
                            break;
                        }
                    }

                    if (!found)
                    {
                        AddItem(changes[ii]);
                    }
                }
            }

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
            ItemNotification change = item as ItemNotification;

            if (change == null)
            {
                await base.UpdateItemAsync(listItem, item, ct);
                return;
            }

            // fill in the columns.
            listItem.SubItems[0].Text = String.Format("[{0}]", (change.MonitoredItem.Item != null) ? change.MonitoredItem.Item.ServerId : 0);
            listItem.SubItems[1].Text = String.Format("{0}", change.MonitoredItem.DisplayName);
            listItem.SubItems[2].Text = String.Format("{0}", change.Value.WrappedValue);

            // check if publishing has stopped for some reason.
            if (m_publishingStopped)
            {
                listItem.SubItems[3].Text = String.Format("{0}", (StatusCode)StatusCodes.UncertainNoCommunicationLastUsableValue);
            }
            else
            {
                listItem.SubItems[3].Text = change.Value.StatusCode.ToString();
            }

            DateTime time = change.Value.SourceTimestamp.ToDateTime();

            if (time != DateTime.MinValue)
            {
                listItem.SubItems[4].Text = String.Format("{0:HH:mm:ss.fff}", time.ToLocalTime());
            }
            else
            {
                listItem.SubItems[4].Text = String.Empty;
            }

            time = change.Value.ServerTimestamp.ToDateTime();

            if (time != DateTime.MinValue)
            {
                listItem.SubItems[5].Text = String.Format("{0:HH:mm:ss.fff}", time.ToLocalTime());
            }
            else
            {
                listItem.SubItems[5].Text = String.Empty;
            }

            listItem.Tag = change;
            listItem.ForeColor = (m_publishingStopped) ? Color.Red : Color.Empty;
        }
        #endregion

        #region Event Handlers
        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                ItemNotification change = SelectedTag as ItemNotification;

                if (change == null)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Windows.Create<ComplexValueEditDlg>().ShowDialog(change.Value);
                #pragma warning restore CA2000
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
