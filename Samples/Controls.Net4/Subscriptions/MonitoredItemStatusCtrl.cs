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
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Sample.Controls
{
    public partial class MonitoredItemStatusCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MonitoredItemStatusCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }
        #endregion

        #region Private Fields
        private SubscriptionHandle m_subscription;
        private MonitoredItemHandle m_monitoredItem;
        private readonly Dictionary<MonitoredItemHandle, LastNotification> m_notifications = new Dictionary<MonitoredItemHandle, LastNotification>();

        /// <summary>
        /// The most recent notification of a monitored item. The V2 engine keeps no value
        /// cache on the item, so the control remembers what it displayed last.
        /// </summary>
        private sealed class LastNotification
        {
            public string Value;
            public DateTime Timestamp;
        }

        /// <summary>
        /// The columns to display in the control.
        /// </summary>
        private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "ID",             HorizontalAlignment.Center, null       },
            new object[] { "Name",           HorizontalAlignment.Left,   null       },
            new object[] { "Class",          HorizontalAlignment.Left,   "Variable" },
            new object[] { "Sampling Rate",  HorizontalAlignment.Center, null       },
            new object[] { "Queue Size",     HorizontalAlignment.Center, "0"        },
            new object[] { "Value",          HorizontalAlignment.Left,   "",        200 },
            new object[] { "Status",         HorizontalAlignment.Left,   "",        },
            new object[] { "Timestamp",      HorizontalAlignment.Center, ""         },
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            m_notifications.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Displays the status of a single monitored item in the control.
        /// </summary>
        public void Initialize(SubscriptionHandle subscription, MonitoredItemHandle monitoredItem)
        {
            // do nothing if same item provided.
            if (Object.ReferenceEquals(m_subscription, subscription) && Object.ReferenceEquals(m_monitoredItem, monitoredItem))
            {
                return;
            }

            m_subscription = subscription;
            m_monitoredItem = monitoredItem;
            Telemetry = m_subscription?.Session?.MessageContext?.Telemetry;

            Clear();

            if (m_subscription != null)
            {
                UpdateItems();
            }
        }

        /// <summary>
        /// Displays the items for the specified subscription in the control.
        /// </summary>
        public void Initialize(SubscriptionHandle subscription)
        {
            Initialize(subscription, null);
        }

        /// <summary>
        /// Called when the state of the subscription changes, which includes the engine
        /// finishing to apply monitored item changes.
        /// </summary>
        public void SubscriptionChanged()
        {
            UpdateItems();
        }

        /// <summary>
        /// Updates the value cells with the data changes of a notification.
        /// </summary>
        public void NotificationReceived(DataValueChange[] changes)
        {
            foreach (DataValueChange change in changes)
            {
                MonitoredItemHandle handle = m_subscription?.FindItem(change.MonitoredItem);

                if (handle == null || (m_monitoredItem != null && !Object.ReferenceEquals(handle, m_monitoredItem)))
                {
                    continue;
                }

                m_notifications[handle] = new LastNotification {
                    Value = String.Format("{0}", change.Value.WrappedValue),
                    Timestamp = change.Value.SourceTimestamp.ToDateTime(),
                };
            }

            UpdateItems();
        }

        /// <summary>
        /// Updates the value cells with the events of a notification.
        /// </summary>
        public async void NotificationReceived(EventNotification[] notifications)
        {
            try
            {
                foreach (EventNotification notification in notifications)
                {
                    MonitoredItemHandle handle = m_subscription?.FindItem(notification.MonitoredItem);

                    if (handle == null || (m_monitoredItem != null && !Object.ReferenceEquals(handle, m_monitoredItem)))
                    {
                        continue;
                    }

                    string value = null;

                    if (SubscriptionHandle.GetEventFieldValue(handle, notification, new QualifiedName(Opc.Ua.BrowseNames.EventType)).TryGetValue(out NodeId eventTypeId))
                    {
                        INode eventType = await m_subscription.Session.NodeCache.FindAsync(eventTypeId);
                        value = String.Format("{0}", (object)eventType ?? eventTypeId);
                    }

                    DateTime timestamp = SubscriptionHandle.GetEventFieldValue(handle, notification, new QualifiedName(Opc.Ua.BrowseNames.Time)).TryGetValue(out DateTimeUtc eventTime) ? (DateTime)eventTime : DateTime.MinValue;

                    m_notifications[handle] = new LastNotification {
                        Value = value,
                        Timestamp = timestamp,
                    };
                }

                UpdateItems();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Refreshes the state of all items displayed in the control.
        /// </summary>
        public void UpdateItems()
        {
            if (m_subscription != null)
            {
                BeginUpdate();

                foreach (MonitoredItemHandle monitoredItem in m_subscription.Items)
                {
                    if (m_monitoredItem == null || Object.ReferenceEquals(monitoredItem, m_monitoredItem))
                    {
                        AddItem(monitoredItem);
                    }
                }

                EndUpdate();

                AdjustColumns();
            }
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            // no menu defined at this time.
        }

        /// <see cref="BaseListCtrl.UpdateItemAsync" />
        protected override async Task UpdateItemAsync(ListViewItem listItem, object item, CancellationToken ct = default)
        {
            MonitoredItemHandle handle = item as MonitoredItemHandle;

            if (handle == null)
            {
                await base.UpdateItemAsync(listItem, item, ct);
                return;
            }

            IMonitoredItem monitoredItem = handle.Item;

            listItem.SubItems[0].Text = String.Format("{0}", (monitoredItem != null) ? monitoredItem.ServerId : 0);
            listItem.SubItems[1].Text = String.Format("{0}", handle.DisplayName);
            listItem.SubItems[2].Text = String.Format("{0}", handle.NodeClass);
            listItem.SubItems[3].Text = String.Format("{0}", (monitoredItem != null) ? monitoredItem.CurrentSamplingInterval.TotalMilliseconds : handle.Settings.SamplingInterval.TotalMilliseconds);
            listItem.SubItems[4].Text = String.Format("{0}", (monitoredItem != null) ? monitoredItem.CurrentQueueSize : handle.Settings.QueueSize);
            listItem.SubItems[5].Text = String.Empty;
            listItem.SubItems[6].Text = String.Format("{0}", monitoredItem?.Error);
            listItem.SubItems[7].Text = String.Empty;

            if (m_notifications.TryGetValue(handle, out LastNotification notification))
            {
                listItem.SubItems[5].Text = notification.Value;

                if (notification.Timestamp != DateTime.MinValue)
                {
                    listItem.SubItems[7].Text = String.Format("{0:HH:mm:ss.fff}", notification.Timestamp.ToLocalTime());
                }
            }

            listItem.Tag = item;
        }
        #endregion
    }
}
