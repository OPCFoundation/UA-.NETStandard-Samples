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
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditMonitoredItemDlg : Form
    {
        private ITelemetryContext m_telemetry;
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditMonitoredItemDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            // add the attributes in numerical order.
            foreach (uint attributeId in Attributes.Identifiers)
            {
                AttributeCB.Items.Add(Attributes.GetBrowseName(attributeId));
            }

            AttributeCB.SelectedIndex = 0;

            MonitoringModeCB.Items.Add(MonitoringMode.Reporting);
            MonitoringModeCB.Items.Add(MonitoringMode.Sampling);
            MonitoringModeCB.Items.Add(MonitoringMode.Disabled);
            MonitoringModeCB.SelectedIndex = 0;

            DeadbandTypeCB.Items.Add(DeadbandType.None);
            DeadbandTypeCB.Items.Add(DeadbandType.Absolute);
            DeadbandTypeCB.Items.Add(DeadbandType.Percent);
            DeadbandTypeCB.SelectedIndex = 0;

            TriggerTypeCB.Items.Add(DataChangeTrigger.StatusValue);
            TriggerTypeCB.Items.Add(DataChangeTrigger.Status);
            TriggerTypeCB.Items.Add(DataChangeTrigger.StatusValueTimestamp);
            TriggerTypeCB.SelectedIndex = 0;
        }
        #endregion

        #region EncodingInfo Class
        /// <summary>
        /// Stores information about a data encoding.
        /// </summary>
        private sealed class EncodingInfo
        {
            public QualifiedName EncodingName;

            public override string ToString()
            {
                if (!EncodingName.IsNull)
                {
                    return EncodingName.ToString();
                }

                return "Not Set";
            }
        }
        #endregion

        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit the monitored item.
        /// </summary>
        public async Task<bool> ShowDialogAsync(ISession session, MonitoredItemHandle handle, bool isEvent, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(handle);

            m_telemetry = telemetry;
            MonitoredItemOptions settings = handle.Settings;
            bool created = handle.Created;

            if (!created)
            {
                NodeBTN.ChangeSession(session, telemetry);
                await NodeBTN.SetSelectedNodeIdAsync(settings.StartNodeId, ct);
            }

            // hide fields not used for events.
            NodeLB.Visible = !created;
            NodeTB.Visible = !created;
            NodeBTN.Visible = !created;
            AttributeLB.Visible = !isEvent && !created;
            AttributeCB.Visible = !isEvent && !created;
            IndexRangeLB.Visible = !isEvent && !created;
            IndexRangeTB.Visible = !isEvent && !created;
            DataEncodingLB.Visible = !isEvent && !created;
            DataEncodingCB.Visible = !isEvent && !created;
            MonitoringModeLB.Visible = !created;
            MonitoringModeCB.Visible = !created;
            SamplingIntervalLB.Visible = true;
            SamplingIntervalUP.Visible = true;
            QueueSizeLB.Visible = !isEvent;
            QueueSizeUP.Visible = !isEvent;
            DiscardOldestLB.Visible = true;
            DiscardOldestCK.Visible = true;
            DeadbandTypeLB.Visible = !isEvent;
            DeadbandTypeCB.Visible = !isEvent;
            DeadbandValueLB.Visible = !isEvent;
            DeadbandValueUP.Visible = !isEvent;
            TriggerTypeLB.Visible = !isEvent;
            TriggerTypeCB.Visible = !isEvent;

            // fill in values.
            SamplingIntervalUP.Value = (decimal)settings.SamplingInterval.TotalMilliseconds;
            DiscardOldestCK.Checked = settings.DiscardOldest;

            if (!isEvent)
            {
                AttributeCB.SelectedIndex = (int)(settings.AttributeId - 1);
                IndexRangeTB.Text = settings.IndexRange;
                MonitoringModeCB.SelectedItem = settings.MonitoringMode;
                QueueSizeUP.Value = settings.QueueSize;

                DataChangeFilter filter = settings.Filter as DataChangeFilter;

                if (filter != null)
                {
                    DeadbandTypeCB.SelectedItem = (DeadbandType)filter.DeadbandType;
                    DeadbandValueUP.Value = (decimal)filter.DeadbandValue;
                    TriggerTypeCB.SelectedItem = filter.Trigger;
                }

                if (!created)
                {
                    // fetch the available encodings for the first node in the list from the server.
                    IVariableBase variable = await session.NodeCache.FindAsync(settings.StartNodeId, ct) as IVariableBase;

                    DataEncodingCB.Items.Add(new EncodingInfo());
                    DataEncodingCB.SelectedIndex = 0;

                    if (variable != null)
                    {
                        if (await session.NodeCache.IsTypeOfAsync(variable.DataType, Opc.Ua.DataTypeIds.Structure, ct))
                        {
                            foreach (INode encoding in await session.NodeCache.FindAsync(variable.DataType, Opc.Ua.ReferenceTypeIds.HasEncoding, false, true, ct))
                            {
                                DataEncodingCB.Items.Add(new EncodingInfo() { EncodingName = encoding.BrowseName });

                                if (settings.Encoding == encoding.BrowseName)
                                {
                                    DataEncodingCB.SelectedIndex = DataEncodingCB.Items.Count - 1;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                AttributeCB.SelectedIndex = ((int)Attributes.EventNotifier - 1);
            }

            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            // read the controls before the update, so the callback below stays a pure function
            // of the dialog state.
            NodeId startNodeId = NodeBTN.SelectedNode;
            var samplingInterval = TimeSpan.FromMilliseconds((double)SamplingIntervalUP.Value);
            bool discardOldest = DiscardOldestCK.Checked;
            uint attributeId = (uint)(AttributeCB.SelectedIndex + 1);
            var monitoringMode = (MonitoringMode)MonitoringModeCB.SelectedItem;
            string indexRange = IndexRangeTB.Text.Trim();
            QualifiedName dataEncoding = ((EncodingInfo)DataEncodingCB.SelectedItem)?.EncodingName ?? QualifiedName.Null;
            uint queueSize = (uint)QueueSizeUP.Value;
            var trigger = (DataChangeTrigger)TriggerTypeCB.SelectedItem;
            var deadbandType = (DeadbandType)DeadbandTypeCB.SelectedItem;
            double deadbandValue = (double)DeadbandValueUP.Value;

            // update monitored item.
            handle.Configure(options => {
                if (!created)
                {
                    options = options with {
                        StartNodeId = startNodeId,
                        AttributeId = attributeId,
                        MonitoringMode = monitoringMode,
                    };
                }

                options = options with {
                    SamplingInterval = samplingInterval,
                    DiscardOldest = discardOldest,
                };

                if (!isEvent)
                {
                    if (!created)
                    {
                        options = options with { IndexRange = indexRange, Encoding = dataEncoding };
                    }

                    options = options with { QueueSize = queueSize };

                    if (options.Filter != null || deadbandType != DeadbandType.None || trigger != DataChangeTrigger.StatusValue)
                    {
                        options = options with {
                            Filter = new DataChangeFilter {
                                DeadbandType = (uint)deadbandType,
                                DeadbandValue = deadbandValue,
                                Trigger = trigger,
                            },
                        };
                    }

                    return options;
                }

                if (!created)
                {
                    options = options with { IndexRange = null, Encoding = QualifiedName.Null };
                }

                return options with { QueueSize = 0, Filter = new EventFilter() };
            });

            return true;
        }

        /// <summary>
        /// Prompts the user to specify a monitoring mode.
        /// </summary>
        public MonitoringMode ShowDialog(MonitoringMode monitoringMode)
        {
            NodeLB.Visible = false;
            NodeTB.Visible = false;
            NodeBTN.Visible = false;
            AttributeLB.Visible = false;
            AttributeCB.Visible = false;
            IndexRangeLB.Visible = false;
            IndexRangeTB.Visible = false;
            DataEncodingLB.Visible = false;
            DataEncodingCB.Visible = false;
            MonitoringModeLB.Visible = true;
            MonitoringModeCB.Visible = true;
            SamplingIntervalLB.Visible = false;
            SamplingIntervalUP.Visible = false;
            QueueSizeLB.Visible = false;
            QueueSizeUP.Visible = false;
            DiscardOldestLB.Visible = false;
            DiscardOldestCK.Visible = false;
            DeadbandTypeLB.Visible = false;
            DeadbandTypeCB.Visible = false;
            DeadbandValueLB.Visible = false;
            DeadbandValueUP.Visible = false;
            TriggerTypeLB.Visible = false;
            TriggerTypeCB.Visible = false;

            MonitoringModeCB.SelectedItem = monitoringMode;

            if (base.ShowDialog() != DialogResult.OK)
            {
                return monitoringMode;
            }

            return (MonitoringMode)MonitoringModeCB.SelectedItem;
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (IndexRangeTB.Visible)
                {
                    NumericRange.Parse(IndexRangeTB.Text);
                }

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
