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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditSubscriptionDlg : Form
    {
        private ITelemetryContext m_telemetry;
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditSubscriptionDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit the monitored item.
        /// </summary>
        /// <remarks>
        /// The V2 subscription engine takes the settings of a subscription through an options
        /// monitor and applies a change as soon as the monitor is reconfigured, so this replaces
        /// the classic edit-then-ModifyAsync pair the dialog used to be half of.
        /// </remarks>
        public bool ShowDialog(OptionsMonitor<SubscriptionOptions> options, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(options);

            m_telemetry = telemetry;
            SubscriptionOptions settings = options.CurrentValue;

            PublishingIntervalUP.Value = (decimal)settings.PublishingInterval.TotalMilliseconds;
            KeepAliveCountUP.Value = settings.KeepAliveCount;
            LifetimeCountUP.Value = settings.LifetimeCount;
            MaxNotificationsPerPublishUP.Value = settings.MaxNotificationsPerPublish;
            PriorityTB.Value = settings.Priority;
            PublishingEnabledCK.Checked = settings.PublishingEnabled;

            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            var publishingInterval = TimeSpan.FromMilliseconds((double)PublishingIntervalUP.Value);
            uint keepAliveCount = (uint)KeepAliveCountUP.Value;
            uint lifetimeCount = (uint)LifetimeCountUP.Value;
            uint maxNotificationsPerPublish = (uint)MaxNotificationsPerPublishUP.Value;
            byte priority = (byte)PriorityTB.Value;
            bool publishingEnabled = PublishingEnabledCK.Checked;

            options.Configure(current => current with {
                PublishingInterval = publishingInterval,
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                MaxNotificationsPerPublish = maxNotificationsPerPublish,
                Priority = priority,
                PublishingEnabled = publishingEnabled,
            });

            return true;
        }
        #endregion

        #region Event Handlers
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
        #endregion
    }
}
