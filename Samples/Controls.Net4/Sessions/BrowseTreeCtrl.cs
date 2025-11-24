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
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseTreeCtrl : Opc.Ua.Client.Controls.BaseTreeCtrl
    {
        #region Contructors
        public BrowseTreeCtrl()
        {
            InitializeComponent();
            m_references = new ReferenceDescriptionCollection();
            m_BrowserMoreReferences = new BrowserEventHandler(Browser_MoreReferencesAsync);
        }
        #endregion

        #region Private Fields
        private Browser m_browser;
        private ISession m_session;
        private ILogger m_logger;
        private NodeId m_rootId;
        private AttributeListCtrl m_AttributesCtrl;
        private bool m_allowPick;
        private bool m_showReferences;
        private TreeNode m_nodeToBrowse;
        private ReferenceDescription m_parent;
        private ReferenceDescriptionCollection m_references;
        private event NodesSelectedEventHandler m_ItemsSelected;
        private event MethodCalledEventHandler m_MethodCalled;
        private BrowserEventHandler m_BrowserMoreReferences;
        private SessionTreeCtrl m_SessionTreeCtrl;
        #endregion

        #region Public Interface
        /// <summary>
        /// The control used to display the address space for a session.
        /// </summary>
        public SessionTreeCtrl SessionTreeCtrl
        {
            get { return m_SessionTreeCtrl; }
            set { m_SessionTreeCtrl = value; }
        }

        /// <summary>
        /// Whether items can be picked in the control.
        /// </summary>
        [DefaultValue(false)]
        public bool AllowPick
        {
            get { return m_allowPick; }
            set { m_allowPick = value; }
        }

        /// <summary>
        /// Whether references should be displayed in the control.
        /// </summary>
        [DefaultValue(false)]
        public bool ShowReferences
        {
            get { return m_showReferences; }
            set { m_showReferences = value; }
        }

        /// <summary>
        /// Whether references should be displayed in the control.
        /// </summary>
        public ReferenceDescriptionCollection SelectedReferences
        {
            get
            {
                return m_references;
            }

            set
            {
                m_references = value;

                if (m_references == null)
                {
                    m_references = new ReferenceDescriptionCollection();
                }
            }
        }

        /// <summary>
        /// The control used to display the attributes for the currently selected node.
        /// </summary>
        public AttributeListCtrl AttributesCtrl
        {
            get { return m_AttributesCtrl; }
            set { m_AttributesCtrl = value; }
        }

        /// <summary>
        /// Raised when nodes are selected in the control.
        /// </summary>
        public event NodesSelectedEventHandler ItemsSelected
        {
            add { m_ItemsSelected += value; }
            remove { m_ItemsSelected -= value; }
        }

        /// <summary>
        /// Raised when a method is called.
        /// </summary>
        public event MethodCalledEventHandler MethodCalled
        {
            add { m_MethodCalled += value; }
            remove { m_MethodCalled -= value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            if (m_browser != null)
            {
                m_browser.MoreReferences -= m_BrowserMoreReferences;
            }

            NodesTV.Nodes.Clear();
        }

        /// <summary>
        /// Sets the root node for the control.
        /// </summary>
        public async Task SetRootAsync(Browser browser, NodeId rootId, ISession session, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            Clear();

            ShowReferencesMI.Checked = m_showReferences;

            m_rootId = rootId;
            m_browser = browser;
            m_session = session;
            Telemetry = telemetry;
            m_logger = telemetry.CreateLogger<BrowseTreeCtrl>();

            if (m_browser != null)
            {
                m_browser.MoreReferences += m_BrowserMoreReferences;
            }

            // check if session is connected.
            if (m_browser == null || !m_browser.Session.Connected)
            {
                return;
            }

            if (NodeId.IsNull(rootId))
            {
                m_rootId = Objects.RootFolder;
            }

            if (m_browser != null)
            {
                INode node = await m_session.NodeCache.FindAsync(m_rootId, ct);

                if (node == null)
                {
                    return;
                }

                ReferenceDescription reference = new ReferenceDescription();

                reference.ReferenceTypeId = ReferenceTypeIds.References;
                reference.IsForward = true;
                reference.NodeId = node.NodeId;
                reference.NodeClass = (NodeClass)node.NodeClass;
                reference.BrowseName = node.BrowseName;
                reference.DisplayName = node.DisplayName;
                reference.TypeDefinition = null;

                string text = GetTargetText(reference);
                string icon = await GuiUtils.GetTargetIconAsync(m_browser.Session as Session, reference, ct);

                TreeNode root = AddNode(null, reference, text, icon);
                root.Nodes.Add(new TreeNode());
                root.Expand();
            }
        }

        /// <summary>
        /// Sets the root node for the control.
        /// </summary>
        public Task SetRootAsync(Session session, NodeId rootId, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            return SetRootAsync(new Browser(session), rootId, session, telemetry, ct);
        }

        /// <summary>
        /// Sets the view for the control.
        /// </summary>
        public Task SetViewAsync(Session session, BrowseViewType viewType, NodeId viewId, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            Clear();

            // check if session is connected.
            if (session == null || !session.Connected)
            {
                return Task.CompletedTask;
            }

            Browser browser = new Browser(session);

            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ReferenceTypeId = null;
            browser.IncludeSubtypes = true;
            browser.NodeClassMask = 0;
            browser.ContinueUntilDone = false;

            NodeId rootId = Objects.RootFolder;
            ShowReferences = false;

            switch (viewType)
            {
                case BrowseViewType.All:
                {
                    ShowReferences = true;
                    break;
                }

                case BrowseViewType.Objects:
                {
                    rootId = Objects.ObjectsFolder;
                    browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                    break;
                }

                case BrowseViewType.Types:
                {
                    rootId = Objects.TypesFolder;
                    browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                    break;
                }

                case BrowseViewType.ObjectTypes:
                {
                    rootId = ObjectTypes.BaseObjectType;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.EventTypes:
                {
                    rootId = ObjectTypes.BaseEventType;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.DataTypes:
                {
                    rootId = DataTypeIds.BaseDataType;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.ReferenceTypes:
                {
                    rootId = ReferenceTypeIds.References;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.ServerDefinedView:
                {
                    rootId = viewId;
                    browser.View = new ViewDescription();
                    browser.View.ViewId = viewId;
                    ShowReferences = true;
                    break;
                }
            }

            return SetRootAsync(browser, rootId, session, telemetry, ct);
        }

        /// <summary>
        /// The root node where browsing should start.
        /// </summary>
        public NodeId RootId
        {
            get { return m_rootId; }
        }
        #endregion

        #region Overridden Members
        /// <see cref="BaseTreeCtrl.BeforeExpandAsync" />
        protected override Task<bool> BeforeExpandAsync(TreeNode clickedNode, CancellationToken ct = default)
        {
            // check if a placeholder child is present.
            if (clickedNode.Nodes.Count == 1 && clickedNode.Nodes[0].Text == String.Empty)
            {
                // clear dummy children.
                clickedNode.Nodes.Clear();

                // do nothing if an error is detected.
                if (m_session.KeepAliveStopped)
                {
                    return Task.FromResult(false);
                }

                // browse.
                return BrowseAsync(clickedNode, ct);
            }

            // do not cancel expand.
            return Task.FromResult(false);
        }

        /// <see cref="BaseTreeCtrl.EnableMenuItems" />
        protected override async void EnableMenuItems(TreeNode clickedNode)
        {
            try
            {
                BrowseOptionsMI.Enabled = true;
                ShowReferencesMI.Enabled = true;
                SelectMI.Visible = m_allowPick;
                SelectSeparatorMI.Visible = m_allowPick;

                if (clickedNode != null)
                {
                    // do nothing if an error is detected.
                    if (m_session.KeepAliveStopped)
                    {
                        return;
                    }

                    SelectMI.Enabled = true;
                    SelectItemMI.Enabled = true;
                    SelectChildrenMI.Enabled = clickedNode.Nodes.Count > 0;
                    BrowseRefreshMI.Enabled = true;

                    ReferenceDescription reference = clickedNode.Tag as ReferenceDescription;

                    if (reference != null)
                    {
                        BrowseMI.Enabled = (reference.NodeId != null && !reference.NodeId.IsAbsolute);
                        ViewAttributesMI.Enabled = true;

                        NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);

                        INode node = await m_browser.Session.ReadNodeAsync(nodeId);

                        byte accessLevel = 0;
                        byte eventNotifier = 0;
                        bool executable = false;

                        VariableNode variableNode = node as VariableNode;

                        if (variableNode != null)
                        {
                            accessLevel = variableNode.UserAccessLevel;
                        }

                        ObjectNode objectNode = node as ObjectNode;

                        if (objectNode != null)
                        {
                            eventNotifier = objectNode.EventNotifier;
                        }

                        ViewNode viewNode = node as ViewNode;

                        if (viewNode != null)
                        {
                            eventNotifier = viewNode.EventNotifier;
                        }

                        MethodNode methodNode = node as MethodNode;

                        if (methodNode != null)
                        {
                            executable = methodNode.UserExecutable;
                        }

                        ReadMI.Visible = false;
                        HistoryReadMI.Visible = false;
                        WriteMI.Visible = false;
                        HistoryUpdateMI.Visible = false;
                        SubscribeMI.Visible = false;
                        CallMI.Visible = false;

                        if (accessLevel != 0)
                        {
                            ReadMI.Visible = true;
                            HistoryReadMI.Visible = true;
                            WriteMI.Visible = true;
                            HistoryUpdateMI.Visible = true;
                            SubscribeMI.Visible = m_SessionTreeCtrl != null;

                            if ((accessLevel & (byte)AccessLevels.CurrentRead) != 0)
                            {
                                ReadMI.Enabled = true;
                                SubscribeMI.Enabled = true;
                                SubscribeNewMI.Enabled = true;
                            }

                            if ((accessLevel & (byte)AccessLevels.CurrentWrite) != 0)
                            {
                                WriteMI.Enabled = true;
                            }

                            if ((accessLevel & (byte)AccessLevels.HistoryRead) != 0)
                            {
                                HistoryReadMI.Enabled = true;
                            }

                            if ((accessLevel & (byte)AccessLevels.HistoryWrite) != 0)
                            {
                                HistoryUpdateMI.Enabled = true;
                            }
                        }

                        if (eventNotifier != 0)
                        {
                            HistoryReadMI.Visible = true;
                            HistoryUpdateMI.Visible = true;
                            SubscribeMI.Visible = true;

                            if ((eventNotifier & (byte)EventNotifiers.HistoryRead) != 0)
                            {
                                HistoryReadMI.Enabled = true;
                            }

                            if ((eventNotifier & (byte)EventNotifiers.HistoryWrite) != 0)
                            {
                                HistoryUpdateMI.Enabled = true;
                            }

                            SubscribeMI.Enabled = (eventNotifier & (byte)EventNotifiers.SubscribeToEvents) != 0;
                            SubscribeNewMI.Enabled = SubscribeMI.Enabled;
                        }

                        if (methodNode != null)
                        {
                            CallMI.Visible = true;
                            CallMI.Enabled = executable;
                        }

                        if (SubscribeMI.Enabled)
                        {
                            while (SubscribeMI.DropDown.Items.Count > 1)
                            {
                                SubscribeMI.DropDown.Items.RemoveAt(SubscribeMI.DropDown.Items.Count - 1);
                            }

                            foreach (Subscription subscription in m_session.Subscriptions)
                            {
                                if (subscription.Created)
                                {
                                    ToolStripItem item = SubscribeMI.DropDown.Items.Add(subscription.DisplayName);
                                    item.Click += new EventHandler(Subscription_ClickAsync);
                                    item.Tag = subscription;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <see cref="BaseTreeCtrl.SelectNode" />
        protected override async void SelectNode()
        {
            try
            {
                base.SelectNode();

                // check if node is selected.
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                m_parent = GetParentOfSelected();

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                // update the attributes control.
                if (m_AttributesCtrl != null)
                {
                    if (reference != null)
                    {
                        await m_AttributesCtrl.InitializeAsync(m_browser.Session as Session, reference.NodeId, Telemetry);
                    }
                    else
                    {
                        m_AttributesCtrl.Clear();
                    }
                }

                // check for single reference.
                if (reference != null)
                {
                    m_references = new ReferenceDescription[] { reference };
                    return;
                }

                // check if reference type folder is selected.
                NodeId referenceTypeId = NodesTV.SelectedNode.Tag as NodeId;

                if (referenceTypeId != null)
                {
                    m_references = new ReferenceDescriptionCollection();

                    foreach (TreeNode child in NodesTV.SelectedNode.Nodes)
                    {
                        reference = child.Tag as ReferenceDescription;

                        if (reference != null)
                        {
                            m_references.Add(reference);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        #region Private Members
        /// <summary>
        /// Returns the parent node of the selected reference.
        /// </summary>
        private ReferenceDescription GetParentOfSelected()
        {
            if (NodesTV.SelectedNode.Parent != null)
            {
                if (NodesTV.SelectedNode.Parent.Tag is NodeId)
                {
                    if (NodesTV.SelectedNode.Parent.Parent != null)
                    {
                        return NodesTV.SelectedNode.Parent.Tag as ReferenceDescription;
                    }
                }
                else
                {
                    return NodesTV.SelectedNode.Parent.Tag as ReferenceDescription;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a item to a subscription.
        /// </summary>
        private async Task SubscribeAsync(Subscription subscription, ReferenceDescription reference, CancellationToken ct = default)
        {
            MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);

            monitoredItem.DisplayName = await subscription.Session.NodeCache.GetDisplayTextAsync(reference, ct);
            monitoredItem.StartNodeId = (NodeId)reference.NodeId;
            monitoredItem.NodeClass = (NodeClass)reference.NodeClass;
            monitoredItem.AttributeId = Attributes.Value;
            monitoredItem.SamplingInterval = 0;
            monitoredItem.QueueSize = 1;

            // add condition fields to any event filter.
            EventFilter filter = monitoredItem.Filter as EventFilter;

            if (filter != null)
            {
                monitoredItem.AttributeId = Attributes.EventNotifier;
                monitoredItem.QueueSize = 0;
            }

            subscription.AddItem(monitoredItem);
            await subscription.ApplyChangesAsync(ct);
        }

        /// <summary>
        /// Browses the server address space and adds the targets to the tree.
        /// </summary>
        private async Task<bool> BrowseAsync(TreeNode node, CancellationToken ct = default)
        {
            // save node being browsed.
            m_nodeToBrowse = node;

            // find node to browse.
            ReferenceDescription reference = node.Tag as ReferenceDescription;

            if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
            {
                return false;
            }

            // fetch references.
            ReferenceDescriptionCollection references = null;

            if (reference != null)
            {
                references = await m_browser.BrowseAsync((NodeId)reference.NodeId, ct);
            }
            else
            {
                references = await m_browser.BrowseAsync(m_rootId, ct);
            }

            // add nodes to tree.
            await AddReferencesAsync(m_nodeToBrowse, references, ct);

            return false;
        }

        /// <summary>
        /// Adds a target to the tree control.
        /// </summary>
        private async Task AddReferencesAsync(TreeNode parent, ReferenceDescriptionCollection references, CancellationToken ct = default)
        {
            foreach (ReferenceDescription reference in references)
            {
                if (reference.ReferenceTypeId.IsNullNodeId)
                {
                    m_logger.LogDebug("Reference {0} has null reference type id", reference.DisplayName);
                    continue;
                }

                ReferenceTypeNode typeNode = await m_session.NodeCache.FindAsync(reference.ReferenceTypeId, ct) as ReferenceTypeNode;
                if (typeNode == null)
                {
                    m_logger.LogDebug("Reference {0} has invalid reference type id.", reference.DisplayName);
                    continue;
                }

                if (m_browser.BrowseDirection == BrowseDirection.Forward && !reference.IsForward
                    || m_browser.BrowseDirection == BrowseDirection.Inverse && reference.IsForward)
                {
                    m_logger.LogDebug("Reference's IsForward value is: {0}, but the browse direction is: {1}; for reference {2}", reference.IsForward, m_browser.BrowseDirection, reference.DisplayName);
                    continue;
                }

                if (reference.NodeId == null || reference.NodeId.IsNull)
                {
                    m_logger.LogDebug("The node id of the reference {0} is NULL.", reference.DisplayName);
                    continue;
                }

                if (reference.BrowseName == null || reference.BrowseName.Name == null)
                {
                    m_logger.LogDebug("Browse name is empty for reference {0}", reference.DisplayName);
                    continue;
                }

                if (!Enum.IsDefined(typeof(Opc.Ua.NodeClass), reference.NodeClass) || reference.NodeClass == NodeClass.Unspecified)
                {
                    m_logger.LogDebug("Node class is an unknown or unspecified value, for reference {0}", reference.DisplayName);
                    continue;
                }

                if (m_browser.NodeClassMask != 0 && m_browser.NodeClassMask != 255)
                {
                    if (reference.TypeDefinition == null || reference.TypeDefinition.IsNull)
                    {
                        m_logger.LogDebug("Type definition is null for reference {0}", reference.DisplayName);
                        continue;
                    }
                }

                // suppress duplicate references.
                if (!m_showReferences)
                {
                    bool exists = false;

                    foreach (TreeNode existingChild in parent.Nodes)
                    {
                        ReferenceDescription existingReference = existingChild.Tag as ReferenceDescription;

                        if (existingReference != null)
                        {
                            if (existingReference.NodeId == reference.NodeId)
                            {
                                exists = true;
                                break;
                            }
                        }
                    }

                    if (exists)
                    {
                        continue;
                    }
                }

                string text = GetTargetText(reference);
                string icon = await GuiUtils.GetTargetIconAsync(m_browser.Session as Session, reference, ct);

                TreeNode container = parent;

                if (m_showReferences)
                {
                    container = await FindReferenceTypeContainerAsync(parent, reference, ct);
                }

                if (container != null)
                {
                    TreeNode child = AddNode(container, reference, text, icon);
                    child.Nodes.Add(new TreeNode());
                }
            }
        }

        /// <summary>
        /// Adds a container for the reference type to the tree control.
        /// </summary>
        private async Task<TreeNode> FindReferenceTypeContainerAsync(TreeNode parent, ReferenceDescription reference, CancellationToken ct = default)
        {
            if (parent == null)
            {
                return null;
            }

            if (reference.ReferenceTypeId.IsNullNodeId)
            {
                m_logger.LogDebug("NULL reference type id, for reference: {0}", reference.DisplayName);
                return null;
            }

            ReferenceTypeNode typeNode = await m_session.NodeCache.FindAsync(reference.ReferenceTypeId, ct) as ReferenceTypeNode;

            foreach (TreeNode child in parent.Nodes)
            {
                NodeId referenceTypeId = child.Tag as NodeId;

                if (typeNode.NodeId == referenceTypeId)
                {
                    if (typeNode.InverseName == null)
                    {
                        return child;
                    }

                    if (reference.IsForward)
                    {
                        if (child.Text == typeNode.DisplayName.Text)
                        {
                            return child;
                        }
                    }
                    else
                    {
                        if (child.Text == typeNode.InverseName.Text)
                        {
                            return child;
                        }
                    }
                }
            }

            if (typeNode != null)
            {
                string text = typeNode.DisplayName.Text;
                string icon = "ReferenceType";

                if (!reference.IsForward && typeNode.InverseName != null)
                {
                    text = typeNode.InverseName.Text;
                }

                return AddNode(parent, typeNode.NodeId, text, icon);
            }

            m_logger.LogDebug("Reference type id not found for: {0}", reference.ReferenceTypeId);

            return null;
        }

        /// <summary>
        /// Returns to display text for the target of a reference.
        /// </summary>
        public string GetTargetText(ReferenceDescription reference)
        {
            if (reference != null)
            {
                if (reference.DisplayName != null && !String.IsNullOrEmpty(reference.DisplayName.Text))
                {
                    return reference.DisplayName.Text;
                }

                if (reference.BrowseName != null)
                {
                    return reference.BrowseName.Name;
                }
            }

            return null;
        }
        #endregion

        private async void BrowseOptionsMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (await new BrowseOptionsDlg().ShowDialogAsync(m_browser, m_session, Telemetry))
                {
                    if (NodesTV.SelectedNode != null)
                    {
                        NodesTV.SelectedNode.Nodes.Clear();
                        await BrowseAsync(NodesTV.SelectedNode);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseRefreshMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode != null)
                {
                    NodesTV.SelectedNode.Nodes.Clear();
                    await BrowseAsync(NodesTV.SelectedNode);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void Browser_MoreReferencesAsync(Browser sender, BrowserEventArgs e)
        {
            try
            {
                await AddReferencesAsync(m_nodeToBrowse, e.References);
                e.References.Clear();

                if (MessageBox.Show("More references exist. Continue?", "Browse", MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SelectItemMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_ItemsSelected == null || NodesTV.SelectedNode == null)
                {
                    return;
                }

                if (m_references.Count > 0)
                {
                    m_ItemsSelected(this, new NodesSelectedEventArgs(m_parent.NodeId, m_references));
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SelectChildrenMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_ItemsSelected == null || NodesTV.SelectedNode == null)
                {
                    return;
                }

                m_parent = GetParentOfSelected();
                m_references = new ReferenceDescriptionCollection();

                foreach (TreeNode child in NodesTV.SelectedNode.Nodes)
                {
                    ReferenceDescription reference = child.Tag as ReferenceDescription;

                    if (reference != null)
                    {
                        m_references.Add(reference);
                    }
                }

                if (m_references.Count > 0)
                {
                    m_ItemsSelected(this, new NodesSelectedEventArgs(m_parent.NodeId, m_references));
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ShowReferencesMI_CheckedChangedAsync(object sender, EventArgs e)
        {
            m_showReferences = ShowReferencesMI.Checked;
            try
            {
                await SetRootAsync(m_browser, m_rootId, m_session, Telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ViewAttributesMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }

                await new NodeAttributesDlg().ShowDialogAsync(m_browser.Session as Session, reference.NodeId, Telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void CallMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                Session session = m_browser.Session as Session;

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || reference.NodeClass != NodeClass.Method)
                {
                    return;
                }

                NodeId methodId = (NodeId)reference.NodeId;

                reference = NodesTV.SelectedNode.Parent.Tag as ReferenceDescription;

                if (reference == null)
                {
                    reference = NodesTV.SelectedNode.Parent.Parent.Tag as ReferenceDescription;

                    if (reference == null)
                    {
                        return;
                    }
                }

                NodeId objectId = (NodeId)reference.NodeId;

                if (m_MethodCalled != null)
                {
                    MethodCalledEventArgs args = new MethodCalledEventArgs(m_browser.Session as Session, objectId, methodId);
                    m_MethodCalled(this, args);

                    if (args.Handled)
                    {
                        return;
                    }
                }

                await new CallMethodDlg().ShowAsync(m_browser.Session as Session, objectId, methodId, Telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ReadMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || (reference.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) == 0)
                {
                    return;
                }

                Session session = m_browser.Session as Session;

                // build list of nodes to read.
                ReadValueIdCollection valueIds = new ReadValueIdCollection();

                ReadValueId valueId = new ReadValueId();

                valueId.NodeId = (NodeId)reference.NodeId;
                valueId.AttributeId = Attributes.Value;
                valueId.IndexRange = null;
                valueId.DataEncoding = null;

                valueIds.Add(valueId);

                // show form.
                await new ReadDlg().ShowAsync(session, valueIds, Telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || (reference.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) == 0)
                {
                    return;
                }

                Session session = m_browser.Session as Session;

                // build list of nodes to read.
                WriteValueCollection values = new WriteValueCollection();

                WriteValue value = new WriteValue();

                value.NodeId = (NodeId)reference.NodeId;
                value.AttributeId = Attributes.Value;
                value.IndexRange = null;
                value.Value = null;

                values.Add(value);

                // show form.
                await new WriteDlg().ShowAsync(session, values, Telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SubscribeNewMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }

                if (m_SessionTreeCtrl != null)
                {
                    Subscription subscription = await m_SessionTreeCtrl.CreateSubscriptionAsync(m_browser.Session as Session);

                    if (subscription != null)
                    {
                        await SubscribeAsync(subscription, reference);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void Subscription_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }

                Subscription subscription = ((ToolStripItem)sender).Tag as Subscription;

                if (subscription != null)
                {
                    await SubscribeAsync(subscription, reference);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void HistoryReadMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                await new ReadHistoryDlg().ShowDialogAsync(m_browser.Session as Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                await new BrowseDlg().ShowAsync(m_browser.Session as Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }

    #region NodesSelectedEventArgs Class
    /// <summary>
    /// The event arguments provided nodes are picked in the dialog.
    /// </summary>
    public class NodesSelectedEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal NodesSelectedEventArgs(ExpandedNodeId sourceId, ReferenceDescriptionCollection references)
        {
            m_sourceId = sourceId;
            m_references = references;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The source of the references that were picked.
        /// </summary>
        public ExpandedNodeId SourceId
        {
            get { return m_sourceId; }
        }

        /// <summary>
        /// The references that were picked in the dialog.
        /// </summary>
        public IEnumerable<ReferenceDescription> References
        {
            get { return m_references; }
        }
        #endregion

        #region Private Fields
        private ExpandedNodeId m_sourceId;
        private ReferenceDescriptionCollection m_references;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive notifications when nodes are picked in the dialog.
    /// </summary>
    public delegate void NodesSelectedEventHandler(object sender, NodesSelectedEventArgs e);
    #endregion

    #region BrowseViewType Enumeration
    /// <summary>
    /// The type views that can be used when browsing the address space.
    /// </summary>
    public enum BrowseViewType
    {
        /// <summary>
        /// All nodes and references in the address space.
        /// </summary>
        All,

        /// <summary>
        /// The object instance hierarchy.
        /// </summary>
        Objects,

        /// <summary>
        /// The type hierarchies.
        /// </summary>
        Types,

        /// <summary>
        /// The object type hierarchies.
        /// </summary>
        ObjectTypes,

        /// <summary>
        /// The event type hierarchies.
        /// </summary>
        EventTypes,

        /// <summary>
        /// The data type hierarchies.
        /// </summary>
        DataTypes,

        /// <summary>
        /// The reference type hierarchies.
        /// </summary>
        ReferenceTypes,

        /// <summary>
        /// A server defined view.
        /// </summary>
        ServerDefinedView
    }
    #endregion


    #region MethodCalledEventArgs Class
    /// <summary>
    /// The event arguments provided nodes are picked in the dialog.
    /// </summary>
    public class MethodCalledEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal MethodCalledEventArgs(Session session, NodeId objectId, NodeId methodId)
        {
            m_session = session;
            m_objectId = objectId;
            m_methodId = methodId;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The session
        /// </summary>
        public Session Session
        {
            get { return m_session; }
        }

        /// <summary>
        /// The NodeId of the Object with a method.
        /// </summary>
        public NodeId ObjectId
        {
            get { return m_objectId; }
        }

        /// <summary>
        /// The NodeId of the Method to call.
        /// </summary>
        public NodeId MethodId
        {
            get { return m_methodId; }
        }

        /// <summary>
        /// Whether the method call was handled.
        /// </summary>
        public bool Handled
        {
            get { return m_handled; }
            set { m_handled = value; }
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_objectId;
        private NodeId m_methodId;
        private bool m_handled;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive notifications when nodes are picked in the dialog.
    /// </summary>
    public delegate void MethodCalledEventHandler(object sender, MethodCalledEventArgs e);
    #endregion
}
