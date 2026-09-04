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
using System.Linq;
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
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    public partial class MonitoredItemConfigCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        /// <summary>
        /// How long the control waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MonitoredItemConfigCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
            m_dialogs = new Dictionary<MonitoredItemHandle, MonitoredItemDlg>();
        }
        #endregion

        #region Private Fields
        private SubscriptionHandle m_subscription;
        private Dictionary<MonitoredItemHandle, MonitoredItemDlg> m_dialogs;
        private bool m_batchUpdates;

        /// <summary>
        /// The columns to display in the control.
        /// </summary>
        private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "ID",                        HorizontalAlignment.Center, null       },
            new object[] { "Name",                      HorizontalAlignment.Left,   null       },
            new object[] { "Class",                     HorizontalAlignment.Left,   "Variable" },
            new object[] { "Node ID",                   HorizontalAlignment.Left,   null       },
            new object[] { "Attribute",                 HorizontalAlignment.Left,   "Value"    },
            new object[] { "Indexes",                   HorizontalAlignment.Left,   ""         },
            new object[] { "Encoding",                  HorizontalAlignment.Left,   ""         },
            new object[] { "Mode",                      HorizontalAlignment.Left,   null       },
            new object[] { "Sampling Interval",         HorizontalAlignment.Center, null       },
            new object[] { "Revised Sampling Interval", HorizontalAlignment.Center, null       },
            new object[] { "Queue Size",                HorizontalAlignment.Center, null       },
            new object[] { "Revised Queue Size",        HorizontalAlignment.Center, null        },
            new object[] { "Discard Oldest",            HorizontalAlignment.Center, "True"     },
            new object[] { "Status",                    HorizontalAlignment.Left,   ""         },
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// Whether changes to items should be applied immediately.
        /// </summary>
        [DefaultValue(false)]
        public bool BatchUpdates
        {
            get { return m_batchUpdates; }
            set { m_batchUpdates = value; }
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
        /// Displays the items for the specified subscription in the control.
        /// </summary>
        public void Initialize(SubscriptionHandle subscription, ITelemetryContext telemetry)
        {
            // do nothing if same subscription provided.
            if (Object.ReferenceEquals(m_subscription, subscription))
            {
                return;
            }

            m_subscription = subscription;
            Telemetry = telemetry;

            Clear();
            UpdateItems();
        }

        /// <summary>
        /// Called when the state of the subscription changes, which includes the engine
        /// finishing to apply monitored item changes.
        /// </summary>
        public void SubscriptionChanged()
        {
            UpdateItems();

            // close any monitoring windows for items that are gone.
            List<MonitoredItemDlg> dialogsToClose = new List<MonitoredItemDlg>();

            foreach (KeyValuePair<MonitoredItemHandle, MonitoredItemDlg> current in m_dialogs)
            {
                if (m_subscription == null || !m_subscription.Items.Contains(current.Key))
                {
                    dialogsToClose.Add(current.Value);
                }
            }

            // this invokes a callback which will remove the dialog from the table.
            foreach (MonitoredItemDlg dialog in dialogsToClose)
            {
                dialog.Close();
            }
        }

        /// <summary>
        /// Creates a new monitored item after prompting the user for its settings.
        /// </summary>
        public MonitoredItemHandle CreateItem()
        {
            if (m_subscription == null)
            {
                return null;
            }

            // let the user edit a handle which is not part of the subscription yet.
            MonitoredItemHandle monitoredItem = new MonitoredItemHandle(
                Utils.Format("MonitoredItem {0}", m_subscription.Items.Count + 1),
                new MonitoredItemOptions { QueueSize = 1 });

            monitoredItem.DisplayName = monitoredItem.Name;

            #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
            if (!new MonitoredItemEditDlg().ShowDialog(m_subscription.Session, monitoredItem, Telemetry))
            #pragma warning restore CA2000
            {
                return null;
            }

            // stage the item so the containing dialog decides when it is handed to the engine.
            MonitoredItemHandle handle = m_subscription.StageItem(monitoredItem.DisplayName, monitoredItem.NodeClass, monitoredItem.Settings);
            UpdateItems();
            return handle;
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
                    AddItem(monitoredItem);
                }

                EndUpdate();

                AdjustColumns();
            }
        }

        /// <summary>
        /// Returns the parent for the node.
        /// </summary>
        private async Task<Node> FindParentAsync(Node node, CancellationToken ct = default)
        {
            IList<IReference> parents = node.ReferenceTable.Find(ReferenceTypeIds.Aggregates, true, true, m_subscription.Session.TypeTree);

            if (parents.Count > 0)
            {
                foreach (IReference parentReference in parents)
                {
                    return await m_subscription.Session.NodeCache.FindAsync(parentReference.TargetId, ct) as Node;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates an item from a reference.
        /// </summary>
        public async Task AddItemAsync(ReferenceDescription reference, CancellationToken ct = default)
        {
            if (reference == null || m_subscription == null)
            {
                return;
            }

            Node node = await m_subscription.Session.NodeCache.FindAsync(reference.NodeId, ct) as Node;

            if (node == null)
            {
                return;
            }

            // use the parent to build a friendlier display name.
            Node parent = await FindParentAsync(node, ct);

            string displayName = (parent != null) ? String.Format("{0}.{1}", parent, node) : String.Format("{0}", node);

            MonitoredItemOptions options = new MonitoredItemOptions {
                StartNodeId = node.NodeId,
                AttributeId = Attributes.Value,
                QueueSize = 1,
            };

            // subscribe object and view nodes to their events.
            if (node.NodeClass == NodeClass.Object || node.NodeClass == NodeClass.View)
            {
                options = options with {
                    AttributeId = Attributes.EventNotifier,
                    QueueSize = 0,
                    Filter = SubscriptionHandle.CreateDefaultEventFilter(),
                };
            }

            // stage the item so the containing dialog can apply all new items in one step.
            m_subscription.StageItem(displayName, node.NodeClass, options);
            UpdateItems();
        }

        /// <summary>
        /// Apply any changes to the set of items.
        /// </summary>
        /// <remarks>
        /// The V2 engine has no ApplyChanges: handing an item to the engine or reconfiguring
        /// its options is the request, and the engine applies it on its own worker. This step
        /// hands over the staged items and waits for that worker to settle so the revised
        /// values can be shown.
        /// </remarks>
        public async Task ApplyChangesAsync(bool force, CancellationToken ct = default)
        {
            if (m_batchUpdates && !force)
            {
                return;
            }

            if (m_subscription != null)
            {
                m_subscription.ApplyChanges();

                await m_subscription.WaitForPendingChangesAsync(kApplyTimeout, ct);

                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    await UpdateItemAsync(listItem, listItem.Tag, ct);
                }

                AdjustColumns();
            }
        }

        /// <summary>
        /// Closes all dialogs when the containing form closes.
        /// </summary>
        public void FormClosing()
        {
            List<MonitoredItemDlg> dialogsToClose = new List<MonitoredItemDlg>(m_dialogs.Values);

            // this invokes a callback which will remove the dialog from the table.
            foreach (MonitoredItemDlg dialog in dialogsToClose)
            {
                dialog.Close();
            }
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            if (m_subscription != null)
            {
                NewMI.Enabled = true;
                EditMI.Enabled = ItemsLV.SelectedItems.Count == 1;
                DeleteMI.Enabled = ItemsLV.SelectedItems.Count > 0;
                SetMonitoringModeMI.Enabled = ItemsLV.SelectedItems.Count > 0;
                SetFilterMI.Enabled = ItemsLV.SelectedItems.Count == 1;
                SetSamplingIntervalMI.Enabled = ItemsLV.SelectedItems.Count == 1;
                MonitorMI.Enabled = ItemsLV.SelectedItems.Count == 1;
            }
        }

        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            MonitorMI_Click(this, null);
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

            MonitoredItemOptions settings = handle.Settings;
            IMonitoredItem monitoredItem = handle.Item;

            listItem.SubItems[0].Text = String.Format("{0}", (monitoredItem != null) ? monitoredItem.ServerId : 0);
            listItem.SubItems[1].Text = String.Format("{0}", handle.DisplayName);
            listItem.SubItems[2].Text = String.Format("{0}", handle.NodeClass);
            listItem.SubItems[3].Text = String.Format("{0}", settings.StartNodeId);
            listItem.SubItems[4].Text = String.Format("{0}", Attributes.GetBrowseName(settings.AttributeId));
            listItem.SubItems[5].Text = String.Format("{0}", settings.IndexRange);
            listItem.SubItems[6].Text = String.Format("{0}", settings.Encoding);
            listItem.SubItems[7].Text = String.Format("{0}", (monitoredItem != null && monitoredItem.Created) ? monitoredItem.CurrentMonitoringMode : settings.MonitoringMode);
            listItem.SubItems[8].Text = String.Format("{0}", settings.SamplingInterval.TotalMilliseconds);

            double revisedSampingInterval = handle.Created ? monitoredItem.CurrentSamplingInterval.TotalMilliseconds : 0.0;

            listItem.SubItems[9].Text = String.Format("{0}", revisedSampingInterval);
            listItem.SubItems[10].Text = String.Format("{0}", settings.QueueSize);

            uint revisedQueueSize = handle.Created ? monitoredItem.CurrentQueueSize : 0;

            listItem.SubItems[11].Text = String.Format("{0}", revisedQueueSize);
            listItem.SubItems[12].Text = String.Format("{0}", settings.DiscardOldest);
            listItem.SubItems[13].Text = String.Format("{0}", monitoredItem?.Error);

            listItem.ForeColor = Color.Gray;

            if (handle.Created)
            {
                listItem.ForeColor = Color.Empty;

                if ((revisedQueueSize != settings.QueueSize) && settings.AttributeId != Opc.Ua.Attributes.EventNotifier)
                {
                    listItem.ForeColor = Color.DarkOrange;
                }

                if ((revisedSampingInterval != settings.SamplingInterval.TotalMilliseconds) && settings.AttributeId != Opc.Ua.Attributes.EventNotifier)
                {
                    listItem.ForeColor = Color.DarkOrange;
                }
            }

            if (monitoredItem == null || monitoredItem.ServerId == 0)
            {
                listItem.ForeColor = Color.DarkOrange;
            }

            if (monitoredItem != null && ServiceResult.IsBad(monitoredItem.Error))
            {
                listItem.ForeColor = Color.Red;
            }

            // the engine has not applied the latest options yet.
            if (monitoredItem is IMonitoredItemApplyState applyState && applyState.HasPendingChanges)
            {
                listItem.ForeColor = Color.Red;
            }

            listItem.Tag = item;
        }

        /// <summary>
        /// Handles a drop event.
        /// </summary>
        protected override async Task OnDragDropAsync(object sender, DragEventArgs e, CancellationToken ct = default)
        {
            try
            {
                ReferenceDescription reference = e.Data.GetData(typeof(ReferenceDescription)) as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }

                await AddItemAsync(reference, ct);
                AdjustColumns();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        #region Event Handlers
        private async void NewMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                CreateItem();
                await ApplyChangesAsync(false);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                MonitoredItemHandle monitoredItem = SelectedTag as MonitoredItemHandle;

                if (monitoredItem == null)
                {
                    return;
                }

                if (m_subscription == null)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                if (!new MonitoredItemEditDlg().ShowDialog(m_subscription.Session, monitoredItem, monitoredItem.Created, Telemetry))
                #pragma warning restore CA2000
                {
                    return;
                }

                await ApplyChangesAsync(false);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void DeleteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItemHandle[] monitoredItems = (MonitoredItemHandle[])GetSelectedItems(typeof(MonitoredItemHandle));

                foreach (MonitoredItemHandle monitoredItem in monitoredItems)
                {
                    // removing the item is the request, the engine deletes it on the server
                    // from its own worker.
                    m_subscription.RemoveItem(monitoredItem);
                }

                await m_subscription.WaitForPendingChangesAsync(kApplyTimeout);

                UpdateItems();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SetMonitoringModeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItemHandle[] monitoredItems = (MonitoredItemHandle[])GetSelectedItems(typeof(MonitoredItemHandle));

                if (monitoredItems.Length > 0)
                {
                    MonitoringMode monitoringMode = monitoredItems[0].Settings.MonitoringMode;

                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    if (!new SetMonitoringModeDlg().ShowDialog(ref monitoringMode))
                    #pragma warning restore CA2000
                    {
                        return;
                    }

                    // reconfiguring the options is the request: the engine sends
                    // SetMonitoringMode for the items which already exist and creates the rest
                    // with the new mode.
                    foreach (MonitoredItemHandle monitoredItem in monitoredItems)
                    {
                        monitoredItem.Configure(options => options with { MonitoringMode = monitoringMode });
                    }

                    await ApplyChangesAsync(false);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SetFilterMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItemHandle[] monitoredItems = (MonitoredItemHandle[])GetSelectedItems(typeof(MonitoredItemHandle));

                if (monitoredItems.Length == 1)
                {
                    if (monitoredItems[0].NodeClass == NodeClass.Variable || monitoredItems[0].NodeClass == NodeClass.VariableType)
                    {
                        #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                        if (!new DataChangeFilterEditDlg().ShowDialog(m_subscription.Session, monitoredItems[0]))
                        #pragma warning restore CA2000
                        {
                            return;
                        }
                    }
                    else
                    {
                        #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                        EventFilter filter = new EventFilterDlg().ShowDialog(m_subscription.Session, Telemetry, monitoredItems[0].Settings.Filter as EventFilter, false);
                        #pragma warning restore CA2000

                        if (filter == null)
                        {
                            return;
                        }

                        monitoredItems[0].Configure(options => options with { Filter = filter });
                    }

                    await ApplyChangesAsync(false);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MonitorMI_Click(object sender, EventArgs e)
        {
            try
            {
                MonitoredItemHandle monitoredItem = SelectedTag as MonitoredItemHandle;

                if (monitoredItem == null)
                {
                    return;
                }

                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItemDlg dialog = null;

                if (!m_dialogs.TryGetValue(monitoredItem, out dialog))
                {
                    m_dialogs[monitoredItem] = dialog = new MonitoredItemDlg();
                    dialog.FormClosing += new FormClosingEventHandler(MonitoredItemDlg_FormClosing);
                }

                dialog.Show(m_subscription, monitoredItem);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        void MonitoredItemDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                foreach (KeyValuePair<MonitoredItemHandle, MonitoredItemDlg> current in m_dialogs)
                {
                    if (current.Value == sender)
                    {
                        m_dialogs.Remove(current.Key);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
