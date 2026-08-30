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
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    public partial class MonitoredItemEditDlg : Form
    {
        public MonitoredItemEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            AttributeIdCB.Items.AddRange(Attributes.BrowseNames.ToArray());

            foreach (MonitoringMode value in Enum.GetValues<MonitoringMode>())
            {
                MonitoringModeCB.Items.Add(value);
            }

            foreach (NodeClass value in Enum.GetValues<NodeClass>())
            {
                NodeClassCB.Items.Add(value);
            }
        }

        private ISession m_session;

        /// <summary>
        /// Prompts the user to specify the settings for a monitored item.
        /// </summary>
        public bool ShowDialog(ISession session, MonitoredItemHandle monitoredItem, ITelemetryContext telemetry)
        {
            return ShowDialog(session, monitoredItem, false, telemetry);
        }

        /// <summary>
        /// Prompts the user to specify the settings for a monitored item.
        /// </summary>
        /// <remarks>
        /// Reconfiguring the options monitor of the handle is the modify request: for an item
        /// which already exists the V2 engine applies the new settings on its own worker, and
        /// for one which does not it uses them when it is created.
        /// </remarks>
        public bool ShowDialog(ISession session, MonitoredItemHandle monitoredItem, bool editMonitoredItem, ITelemetryContext telemetry)
        {
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

            m_session = session;

            NodeIdCTRL.Telemetry = telemetry;
            NodeIdCTRL.Browser = new Browser(session);

            // the V2 engine identifies the monitored node by its node id, relative paths are
            // not part of the item options.
            RelativePathTB.Enabled = false;

            if (editMonitoredItem)
            {
                // Disable the not changeable values
                NodeIdCTRL.Enabled = false;
                NodeClassCB.Enabled = false;
                AttributeIdCB.Enabled = false;
                IndexRangeTB.Enabled = false;
                EncodingCB.Enabled = false;
                MonitoringModeCB.Enabled = false;
            }

            MonitoredItemOptions settings = monitoredItem.Settings;

            DisplayNameTB.Text = monitoredItem.DisplayName;
            NodeIdCTRL.Identifier = settings.StartNodeId;
            NodeClassCB.SelectedItem = monitoredItem.NodeClass;
            AttributeIdCB.SelectedItem = Attributes.GetBrowseName(settings.AttributeId);
            IndexRangeTB.Text = settings.IndexRange;
            EncodingCB.Text = (settings.Encoding.HasValue && !settings.Encoding.Value.IsNull) ? settings.Encoding.Value.Name : null;
            MonitoringModeCB.SelectedItem = settings.MonitoringMode;
            SamplingIntervalNC.Value = 1000;
            DisableOldestCK.Checked = settings.DiscardOldest;

            if (settings.SamplingInterval >= TimeSpan.Zero)
            {
                SamplingIntervalNC.Value = (decimal)settings.SamplingInterval.TotalMilliseconds;
            }

            QueueSizeNC.Value = settings.QueueSize;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            monitoredItem.DisplayName = DisplayNameTB.Text;
            monitoredItem.NodeClass = (NodeClass)NodeClassCB.SelectedItem;

            NodeId startNodeId = NodeIdCTRL.Identifier;
            uint attributeId = Attributes.GetIdentifier((string)AttributeIdCB.SelectedItem);
            string indexRange = IndexRangeTB.Text;
            MonitoringMode monitoringMode = (MonitoringMode)MonitoringModeCB.SelectedItem;
            TimeSpan samplingInterval = TimeSpan.FromMilliseconds((double)SamplingIntervalNC.Value);
            uint queueSize = (uint)QueueSizeNC.Value;
            bool discardOldest = DisableOldestCK.Checked;
            QualifiedName? encoding = (!String.IsNullOrEmpty(EncodingCB.Text)) ? new QualifiedName(EncodingCB.Text) : null;

            monitoredItem.Configure(options => options with {
                StartNodeId = startNodeId,
                AttributeId = attributeId,
                IndexRange = indexRange,
                MonitoringMode = monitoringMode,
                SamplingInterval = samplingInterval,
                QueueSize = queueSize,
                DiscardOldest = discardOldest,
                Encoding = encoding ?? options.Encoding,
            });

            return true;
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                NodeId nodeId = NodeIdCTRL.Identifier;
            }
            catch (Exception)
            {
                MessageBox.Show("Please enter a valid node id.", this.Text);
            }

            try
            {
                if (!String.IsNullOrEmpty(IndexRangeTB.Text))
                {
                    NumericRange indexRange = NumericRange.Parse(IndexRangeTB.Text);
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Please enter a valid index range.", this.Text);
            }

            DialogResult = DialogResult.OK;
        }

        private async void NodeIdCTRL_IdentifierChangedAsync(object sender, EventArgs e)
        {
            if (NodeIdCTRL.Reference != null)
            {
                DisplayNameTB.Text = await m_session.NodeCache.GetDisplayTextAsync(NodeIdCTRL.Reference);
                NodeClassCB.SelectedItem = (NodeClass)NodeIdCTRL.Reference.NodeClass;
            }
        }
    }
}
