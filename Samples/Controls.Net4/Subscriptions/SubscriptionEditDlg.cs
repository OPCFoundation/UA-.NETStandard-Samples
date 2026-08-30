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
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    public partial class SubscriptionEditDlg : Form
    {
        public SubscriptionEditDlg()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Prompts the user to specify the subscription parameters.
        /// </summary>
        /// <remarks>
        /// Reconfiguring the options monitor of the handle is the modify request: for a
        /// subscription which already exists the V2 engine applies the new settings on its own
        /// worker, and for one which does not it uses them when it is created.
        /// </remarks>
        public bool ShowDialog(SubscriptionHandle subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            ISubscription created = subscription.Created ? subscription.Subscription : null;
            SubscriptionOptions options = subscription.Settings;

            DisplayNameTB.Text = subscription.DisplayName;
            PublishingIntervalNC.Value = (decimal)((created != null) ? created.CurrentPublishingInterval : options.PublishingInterval).TotalMilliseconds;
            KeepAliveCountNC.Value = (created != null) ? created.CurrentKeepAliveCount : options.KeepAliveCount;
            LifetimeCountCTRL.Value = (created != null) ? created.CurrentLifetimeCount : options.LifetimeCount;
            MaxNotificationsCTRL.Value = (created != null) ? created.CurrentMaxNotificationsPerPublish : options.MaxNotificationsPerPublish;
            PriorityNC.Value = (created != null) ? created.CurrentPriority : options.Priority;
            PublishingEnabledCK.Checked = (created != null) ? created.CurrentPublishingEnabled : options.PublishingEnabled;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            subscription.DisplayName = DisplayNameTB.Text;

            TimeSpan publishingInterval = TimeSpan.FromMilliseconds((double)PublishingIntervalNC.Value);
            uint keepAliveCount = (uint)KeepAliveCountNC.Value;
            uint lifetimeCount = (uint)LifetimeCountCTRL.Value;
            uint maxNotifications = (uint)MaxNotificationsCTRL.Value;
            byte priority = (byte)PriorityNC.Value;
            bool publishingEnabled = PublishingEnabledCK.Checked;

            subscription.Configure(current => current with {
                PublishingInterval = publishingInterval,
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                MaxNotificationsPerPublish = maxNotifications,
                Priority = priority,
                PublishingEnabled = publishingEnabled,
            });

            return true;
        }
    }
}
