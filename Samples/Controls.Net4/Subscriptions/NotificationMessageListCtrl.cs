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
    public partial class NotificationMessageListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public NotificationMessageListCtrl()
        {
            MaxMessageCount = 10;

            InitializeComponent();
            SetColumns(m_ColumnNames);

            ItemsLV.Sorting = SortOrder.Descending;

            m_DataChangeCallback = new Action<ISubscription, uint, DateTime, DataValueChange[], PublishState>(OnDataChangeNotification);
            m_EventCallback = new Action<ISubscription, uint, DateTime, EventNotification[], PublishState>(OnEventNotification);
            m_KeepAliveCallback = new Action<ISubscription, uint, DateTime, PublishState>(OnKeepAliveNotification);
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private SubscriptionHandle m_subscription;
        private readonly List<SubscriptionHandle> m_attached = new List<SubscriptionHandle>();
        private readonly Action<ISubscription, uint, DateTime, DataValueChange[], PublishState> m_DataChangeCallback;
        private readonly Action<ISubscription, uint, DateTime, EventNotification[], PublishState> m_EventCallback;
        private readonly Action<ISubscription, uint, DateTime, PublishState> m_KeepAliveCallback;
        private int m_maxMessageCount;

        /// <summary>
        /// The columns to display in the control.
        /// </summary>
        private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "Subscription",  HorizontalAlignment.Left,   null   },
            new object[] { "Message ID",    HorizontalAlignment.Center, null   },
            new object[] { "Publish Time",  HorizontalAlignment.Center, null   },
            new object[] { "Data Changes",  HorizontalAlignment.Center, null   },
            new object[] { "EventTypes",    HorizontalAlignment.Center, null   }
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// The maximum number of messages displayed in the control.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int MaxMessageCount
        {
            get { return m_maxMessageCount; }
            set { m_maxMessageCount = value; }
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
        /// Initializes the control with the session/subscription indicated.
        /// </summary>
        /// <remarks>
        /// The V2 engine delivers its notifications through the handler a subscription was
        /// created with, so the control combines its delegates into the callbacks of the
        /// subscriptions it reports on instead of the session-wide notification event the
        /// classic engine raised.
        /// </remarks>
        public void Initialize(ISession session, IList<SubscriptionHandle> subscriptions, SubscriptionHandle subscription)
        {
            // do nothing if nothing has changed. Unlike the session-wide notification event of
            // the classic engine the callbacks are per subscription, so a subscription created
            // since the last call also forces a re-attach.
            if (Object.ReferenceEquals(session, m_session) &&
                Object.ReferenceEquals(subscription, m_subscription) &&
                (subscription != null || subscriptions == null || m_attached.Count == subscriptions.Count))
            {
                return;
            }

            // stop receiving notifications from the previous subscriptions.
            Detach();

            Clear();

            m_session = session;
            m_subscription = subscription;
            Telemetry = session?.MessageContext?.Telemetry;

            // nothing to do if no session provided.
            if (m_session == null)
            {
                return;
            }

            // display only messages for the current subscription, or for all of them.
            if (subscription != null)
            {
                Attach(subscription);
            }
            else if (subscriptions != null)
            {
                foreach (SubscriptionHandle handle in subscriptions)
                {
                    Attach(handle);
                }
            }
        }
        #endregion

        #region ItemData Class
        /// <summary>
        /// Stores the data associated with a list view item.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Sample code preserves existing public API and behavior.")]
        public class ItemData
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Sample code preserves existing public API and behavior.")]
            public SubscriptionHandle Subscription;
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Sample code preserves existing public API and behavior.")]
            public uint SequenceNumber;
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Sample code preserves existing public API and behavior.")]
            public DateTime PublishTime;
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Sample code preserves existing public API and behavior.")]
            public int DataChanges;
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Sample code preserves existing public API and behavior.")]
            public int Events;

            public ItemData(
                SubscriptionHandle subscription,
                uint sequenceNumber,
                DateTime publishTime,
                int dataChanges,
                int events)
            {
                Subscription = subscription;
                SequenceNumber = sequenceNumber;
                PublishTime = publishTime;
                DataChanges = dataChanges;
                Events = events;
            }
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            if (m_session != null)
            {
                OptionsMI.Enabled = true;
                ClearMI.Enabled = true;

                // the V2 engine republishes missed messages on its own.
                RepublishMI.Enabled = false;

                if (clickedItem != null)
                {
                    ItemData itemData = clickedItem.Tag as ItemData;

                    if (itemData != null)
                    {
                        ViewMI.Enabled = true;
                        DeleteMI.Enabled = true;
                    }
                }
            }
        }

        /// <see cref="BaseListCtrl.UpdateItemAsync" />
        protected override async Task UpdateItemAsync(ListViewItem listItem, object item, CancellationToken ct = default)
        {
            ItemData itemData = item as ItemData;

            if (itemData == null)
            {
                await base.UpdateItemAsync(listItem, item, ct);
                return;
            }

            listItem.SubItems[0].Text = String.Format("{0}", itemData.Subscription.DisplayName);
            listItem.SubItems[1].Text = String.Format("{0}", itemData.SequenceNumber);
            listItem.SubItems[2].Text = String.Format("{0:HH:mm:ss.fff}", itemData.PublishTime.ToLocalTime());
            listItem.SubItems[3].Text = String.Format("{0}", itemData.DataChanges);
            listItem.SubItems[4].Text = String.Format("{0}", itemData.Events);

            listItem.Tag = item;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Starts receiving the notifications of a subscription.
        /// </summary>
        private void Attach(SubscriptionHandle subscription)
        {
            subscription.Callbacks.DataChangeCallback += m_DataChangeCallback;
            subscription.Callbacks.EventCallback += m_EventCallback;
            subscription.Callbacks.KeepAliveCallback += m_KeepAliveCallback;
            m_attached.Add(subscription);
        }

        /// <summary>
        /// Stops receiving the notifications of the attached subscriptions.
        /// </summary>
        private void Detach()
        {
            foreach (SubscriptionHandle subscription in m_attached)
            {
                subscription.Callbacks.DataChangeCallback -= m_DataChangeCallback;
                subscription.Callbacks.EventCallback -= m_EventCallback;
                subscription.Callbacks.KeepAliveCallback -= m_KeepAliveCallback;
            }

            m_attached.Clear();
        }

        /// <summary>
        /// Adds a message to the list and trims it to the maximum count.
        /// </summary>
        private void AddMessage(ItemData itemData)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            AddItem(itemData);

            if (ItemsLV.Items.Count > MaxMessageCount)
            {
                for (int i = 0; i < (ItemsLV.Items.Count - MaxMessageCount); i++)
                {
                    ItemsLV.Items.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Finds the handle for a subscription of the V2 engine.
        /// </summary>
        private SubscriptionHandle FindSubscription(ISubscription subscription)
        {
            foreach (SubscriptionHandle handle in m_attached)
            {
                if (Object.ReferenceEquals(handle.Subscription, subscription))
                {
                    return handle;
                }
            }

            return null;
        }

        private void OnDataChangeNotification(ISubscription subscription, uint sequenceNumber, DateTime publishTime, DataValueChange[] notifications, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_DataChangeCallback, subscription, sequenceNumber, publishTime, notifications, publishStateMask);
                return;
            }

            try
            {
                SubscriptionHandle handle = FindSubscription(subscription);

                if (handle != null)
                {
                    AddMessage(new ItemData(handle, sequenceNumber, publishTime, notifications.Length, 0));
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void OnEventNotification(ISubscription subscription, uint sequenceNumber, DateTime publishTime, EventNotification[] notifications, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_EventCallback, subscription, sequenceNumber, publishTime, notifications, publishStateMask);
                return;
            }

            try
            {
                SubscriptionHandle handle = FindSubscription(subscription);

                if (handle != null)
                {
                    AddMessage(new ItemData(handle, sequenceNumber, publishTime, 0, notifications.Length));
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void OnKeepAliveNotification(ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_KeepAliveCallback, subscription, sequenceNumber, publishTime, publishStateMask);
                return;
            }

            try
            {
                SubscriptionHandle handle = FindSubscription(subscription);

                if (handle != null)
                {
                    AddMessage(new ItemData(handle, sequenceNumber, publishTime, 0, 0));
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                for (int ii = 0; ii < ItemsLV.SelectedItems.Count;)
                {
                    ItemsLV.SelectedItems[ii].Remove();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                Clear();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void RepublishMI_Click(object sender, EventArgs e)
        {
            // the V2 engine republishes missed messages on its own, so there is nothing left
            // to trigger by hand.
        }
    }
}
