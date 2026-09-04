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
using System.Windows.Forms;
using System.Reflection;
using System.IO;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using System.Threading.Tasks;
using System.Threading;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

    public partial class SessionTreeCtrl : Opc.Ua.Client.Controls.BaseTreeCtrl
    {
        #region Contructors
        public SessionTreeCtrl()
        {
            InitializeComponent();

            m_eventRegistrations = new Dictionary<object, TreeNode>();
            m_endpointUrls = new List<string>();
            m_subscriptions = new List<SubscriptionHandle>();
            m_dialogs = new Dictionary<SubscriptionHandle, SubscriptionDlg>();

            m_SubscriptionStateChanged = new Action<ISubscription, SubscriptionState, PublishState>(Subscription_StateChanged);
        }
        #endregion

        #region Private Fields
        private BrowseTreeCtrl m_AddressSpaceCtrl;
        private NotificationMessageListCtrl m_NotificationMessagesCtrl;
        private ToolStripStatusLabel m_ServerStatusCtrl;
        private Action<ISubscription, SubscriptionState, PublishState> m_SubscriptionStateChanged;
        private Dictionary<object, TreeNode> m_eventRegistrations;
        private List<string> m_endpointUrls;
        private List<SubscriptionHandle> m_subscriptions;
        private Dictionary<SubscriptionHandle, SubscriptionDlg> m_dialogs;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_messageContext;
        private ConfiguredEndpoint m_endpoint;
        private string m_filePath;
        #endregion

        #region Public Interface
        /// <summary>
        /// The configuration to use when creating sessions.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ApplicationConfiguration Configuration
        {
            get { return m_configuration; }
            set { m_configuration = value; }
        }

        /// <summary>
        /// The message context to use with the sessions.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ServiceMessageContext MessageContext
        {
            get { return m_messageContext; }
            set { m_messageContext = value; }
        }

        /// <summary>
        /// The locales to use when creating the session.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string[] PreferredLocales { get; set; }

        /// <summary>
        /// Closes all open sessions within the control.
        /// </summary>
        public async Task CloseAsync(CancellationToken ct = default)
        {
            // close any dialogs.
            foreach (SubscriptionDlg dialog in new List<SubscriptionDlg>(m_dialogs.Values))
            {
                dialog.Close();
            }

            await ClearAsync(ct);
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public async Task ClearAsync(CancellationToken ct = default)
        {
            // close all active sessions.
            foreach (TreeNode root in NodesTV.Nodes)
            {
                ISession session = root.Tag as ISession;

                if (session != null)
                {
                    await ClientUtils.CloseAndDisposeAsync(session, ct);
                }
            }

            m_subscriptions.Clear();
            Clear(NodesTV.Nodes);
        }

        /// <summary>
        /// The control used to display the address space for a session.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public BrowseTreeCtrl AddressSpaceCtrl
        {
            get { return m_AddressSpaceCtrl; }
            set { m_AddressSpaceCtrl = value; }
        }

        /// <summary>
        /// The control used to display the notification messages returned for a session..
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public NotificationMessageListCtrl NotificationMessagesCtrl
        {
            get { return m_NotificationMessagesCtrl; }
            set { m_NotificationMessagesCtrl = value; }
        }

        /// <summary>
        /// The control use to display the selected server's status.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ToolStripStatusLabel ServerStatusCtrl
        {
            get { return m_ServerStatusCtrl; }
            set { m_ServerStatusCtrl = value; }
        }

        /// <summary>
        /// Creates a session with the endpoint.
        /// </summary>
        /// <remarks>
        /// The session is created with a <see cref="ManagedSessionFactory"/> by the open
        /// dialog, so it reconnects on its own and runs the V2 subscription engine.
        /// </remarks>
        public async Task<ISession> ConnectAsync(ConfiguredEndpoint endpoint, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            Telemetry = telemetry;

            // check if the endpoint needs to be updated.
            if (endpoint.UpdateBeforeConnect)
            {
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                ConfiguredServerDlg configurationDialog = new ConfiguredServerDlg();
                #pragma warning restore CA2000
                #pragma warning disable CA1849 // Justification: modal dialogs pump their own message loop.
                endpoint = configurationDialog.ShowDialog(endpoint, m_configuration);
                #pragma warning restore CA1849

                if (endpoint == null)
                {
                    return null;
                }
            }

            m_endpoint = endpoint;

            // copy the message context.
            m_messageContext = m_configuration.CreateMessageContext();

            // create and open the session.
            #pragma warning disable CA2000, CA1849 // Justification: modal dialogs pump their own message loop; sample code retains existing ownership/lifetime and behavior.
            ISession session = new SessionOpenDlg().ShowDialog(m_configuration, endpoint, PreferredLocales, telemetry);
            #pragma warning restore CA2000, CA1849

            if (session == null)
            {
                return null;
            }

            // delete the existing session.
            await CloseAsync(ct);

            // add session to tree.
            AddNode(session);

            // return the new session.
            return session;
        }

        /// <summary>
        /// Deletes a session.
        /// </summary>
        public async Task DeleteAsync(ISession session, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            TreeNode node = FindNode(NodesTV.Nodes, session);

            if (node != null)
            {
                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Clear(node.Nodes);
                #pragma warning restore CA1849
                node.Remove();
            }

            // close any dialogs.
            foreach (SubscriptionDlg dialog in new List<SubscriptionDlg>(m_dialogs.Values))
            {
                dialog.Close();
            }

            m_subscriptions.RemoveAll(handle => Object.ReferenceEquals(handle.Session, session));

            await ClientUtils.CloseAndDisposeAsync(session, ct);
            NodesTV.SelectedNode = null;
            SelectNode();
        }

        /// <summary>
        /// Deletes a subscription.
        /// </summary>
        public async Task DeleteAsync(SubscriptionHandle subscription, CancellationToken ct = default)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            // close any dialog.
            SubscriptionDlg dialog = null;

            if (m_dialogs.TryGetValue(subscription, out dialog))
            {
                dialog.Close();
            }

            // disposing the subscription deletes it on the server and drops it from the
            // subscription manager of the session.
            await subscription.DeleteAsync();

            m_subscriptions.Remove(subscription);

            TreeNode node = FindNode(NodesTV.Nodes, subscription);

            if (node != null)
            {
                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Clear(node.Nodes);
                #pragma warning restore CA1849
                node.Remove();
            }

            NodesTV.SelectedNode = FindNode(NodesTV.Nodes, subscription.Session);
        }

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        public async Task DeleteAsync(SubscriptionHandle subscription, MonitoredItemHandle monitoredItem, CancellationToken ct = default)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

            TreeNode node = FindNode(NodesTV.Nodes, monitoredItem);

            if (node != null)
            {
                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Clear(node.Nodes);
                #pragma warning restore CA1849
                node.Remove();
            }

            // removing the item is the request, the engine deletes it on its own worker.
            subscription.RemoveItem(monitoredItem);
            await subscription.WaitForPendingChangesAsync(TimeSpan.FromSeconds(10), ct);

            NodesTV.SelectedNode = FindNode(NodesTV.Nodes, subscription);
        }

        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        public async Task<SubscriptionHandle> CreateSubscriptionAsync(ISession session, CancellationToken ct = default)
        {
            // create form.
            #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
            SubscriptionDlg dialog = new SubscriptionDlg();
            #pragma warning restore CA2000
            dialog.FormClosing += new FormClosingEventHandler(Subscription_FormClosing);

            // create subscription.
            SubscriptionHandle subscription = dialog.New(session, Telemetry);

            if (subscription != null)
            {
                m_dialogs.Add(subscription, dialog);
                AddSubscription(subscription);
                return subscription;
            }

            return null;
        }

        /// <summary>
        /// The subscriptions of the session which the control keeps track of.
        /// </summary>
        public IList<SubscriptionHandle> GetSubscriptions(ISession session)
        {
            List<SubscriptionHandle> subscriptions = new List<SubscriptionHandle>();

            foreach (SubscriptionHandle handle in m_subscriptions)
            {
                if (Object.ReferenceEquals(handle.Session, session))
                {
                    subscriptions.Add(handle);
                }
            }

            return subscriptions;
        }

        /// <summary>
        /// Rebuilds the tree for the session, e.g. after a reconnect.
        /// </summary>
        /// <remarks>
        /// The managed session keeps the same session instance and its V2 subscriptions
        /// across a reconnect, so this only refreshes the display.
        /// </remarks>
        public void Reload(ISession session)
        {
            // update any dialogs.
            foreach (KeyValuePair<SubscriptionHandle, SubscriptionDlg> current in new List<KeyValuePair<SubscriptionHandle, SubscriptionDlg>>(m_dialogs))
            {
                current.Value.Show(current.Key);
            }

            // clear all nodes.
            Clear(NodesTV.Nodes);

            // add session to tree.
            AddNode(session);
            SelectNode();
        }
        #endregion

        #region Overridden Members
        /// <see cref="BaseTreeCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(TreeNode clickedNode)
        {
            NewSessionMI.Enabled = true;
            NewWindowMI.Visible = this.FindForm() is ClientForm;
            NewWindowMI.Enabled = true;

            ISession session = clickedNode.Tag as ISession;

            if (session != null)
            {
                SessionSaveMI.Enabled = true;
                SessionLoadMI.Enabled = true;
                SetLocaleMI.Enabled = true;
                DeleteMI.Enabled = true;
                ReadMI.Enabled = true;
                WriteMI.Enabled = true;
                BrowseMI.Enabled = session.Connected;
                BrowseAllMI.Enabled = BrowseMI.Enabled;
                BrowseObjectsMI.Enabled = BrowseMI.Enabled;
                BrowseObjectTypesMI.Enabled = BrowseMI.Enabled;
                BrowseEventTypesMI.Enabled = BrowseMI.Enabled;
                BrowseServerViewsMI.Enabled = BrowseMI.Enabled;
                BrowseVariableTypesMI.Enabled = BrowseMI.Enabled;
                BrowseDataTypesMI.Enabled = BrowseMI.Enabled;
                BrowseReferenceTypesMI.Enabled = BrowseMI.Enabled;
                SubscriptionMI.Enabled = session.Connected;
                SubscriptionCreateMI.Enabled = SubscriptionMI.Enabled;
            }

            SubscriptionHandle subscription = clickedNode.Tag as SubscriptionHandle;

            if (subscription != null)
            {
                DeleteMI.Enabled = true;
                ReadMI.Enabled = true;
                WriteMI.Enabled = true;
                SubscriptionMI.Enabled = subscription.Session.Connected;
                SubscriptionMonitorMI.Enabled = SubscriptionMI.Enabled;
                SubscriptionEnabledPublishingMI.Enabled = SubscriptionMI.Enabled;
                SubscriptionEnabledPublishingMI.Checked = (subscription.Subscription != null) ? subscription.Subscription.CurrentPublishingEnabled : subscription.Settings.PublishingEnabled;
            }

            MonitoredItemHandle monitoredItem = clickedNode.Tag as MonitoredItemHandle;

            if (monitoredItem != null)
            {
                DeleteMI.Enabled = true;
                ReadMI.Enabled = true;
                WriteMI.Enabled = true;
            }
        }

        /// <summary>
        /// Finds the first tag in the tree above the node that matches the type argument.
        /// </summary>
        private T Get<T>(TreeNode node)
        {
            if (node == null)
            {
                return default(T);
            }

            if (node.Tag is T)
            {
                return (T)node.Tag;
            }

            return Get<T>(node.Parent);
        }

        /// <see cref="BaseTreeCtrl.SelectNode" />
        protected override void SelectNode()
        {
            base.SelectNode();

            TreeNode selectedNode = NodesTV.SelectedNode;

            ISession session = Get<ISession>(selectedNode);
            SubscriptionHandle subscription = Get<SubscriptionHandle>(selectedNode);

            // update address space control.
            if (m_AddressSpaceCtrl != null)
            {
                m_AddressSpaceCtrl.SetViewAsync(session, BrowseViewType.Objects, NodeId.Null, Telemetry);
            }

            // update notification messages control.
            if (m_NotificationMessagesCtrl != null)
            {
                m_NotificationMessagesCtrl.Initialize(session, GetSubscriptions(session), subscription);
            }
        }
        #endregion

        /// <summary>
        /// Recursively clears a subtree.
        /// </summary>
        private void Clear(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag != null && m_eventRegistrations.Remove(node.Tag))
                {
                    if (node.Tag is SubscriptionHandle subscription)
                    {
                        subscription.Callbacks.StateChangedCallback -= m_SubscriptionStateChanged;
                    }
                }

                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.

                Clear(node.Nodes);

                #pragma warning restore CA1849
            }

            nodes.Clear();
        }

        #region Private Members
        /// <summary>
        /// Called by the V2 subscription engine when the state of a subscription changes,
        /// which includes the engine finishing to apply monitored item changes.
        /// </summary>
        private void Subscription_StateChanged(ISubscription subscription, SubscriptionState state, PublishState publishStateMask)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_SubscriptionStateChanged, subscription, state, publishStateMask);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            SubscriptionHandle handle = FindSubscription(subscription);

            if (handle == null)
            {
                return;
            }

            TreeNode node = FindNode(NodesTV.Nodes, handle);

            if (node == null)
            {
                return;
            }

            UpdateNode(node, handle);
        }

        /// <summary>
        /// Finds the handle for a subscription of the V2 engine.
        /// </summary>
        private SubscriptionHandle FindSubscription(ISubscription subscription)
        {
            foreach (SubscriptionHandle handle in m_subscriptions)
            {
                if (Object.ReferenceEquals(handle.Subscription, subscription))
                {
                    return handle;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively finds the node with the specified tag.
        /// </summary>
        private TreeNode FindNode(TreeNodeCollection collection, object tag)
        {
            foreach (TreeNode node in collection)
            {
                if (Object.ReferenceEquals(node.Tag, tag))
                {
                    return node;
                }

                TreeNode child = FindNode(node.Nodes, tag);

                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Registers a subscription created on behalf of the control and adds it to the tree.
        /// </summary>
        private void AddSubscription(SubscriptionHandle subscription)
        {
            m_subscriptions.Add(subscription);

            TreeNode parent = FindNode(NodesTV.Nodes, subscription.Session);

            if (parent != null)
            {
                AddNode(parent, subscription);
                parent.EnsureVisible();
                parent.Expand();
            }
        }

        /// <summary>
        /// Adds a session to the tree.
        /// </summary>
        private void AddNode(ISession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            TreeNode node = AddNode(null, session, session.SessionName, "Server");
            UpdateNode(node, session);

            NodesTV.SelectedNode = node;
        }

        /// <summary>
        /// Updates a session node in the tree.
        /// </summary>
        private void UpdateNode(TreeNode parent, ISession session)
        {
            UpdateNode(parent, session, session.SessionName, (session.Connected) ? "Server" : "ServerStopped");
            Clear(parent.Nodes);

            if (Object.ReferenceEquals(parent.Tag, session))
            {
                foreach (SubscriptionHandle subscription in GetSubscriptions(session))
                {
                    AddNode(parent, subscription);
                }
            }
        }

        /// <summary>
        /// Adds a subscription to the tree.
        /// </summary>
        private void AddNode(TreeNode parent, SubscriptionHandle subscription)
        {
            TreeNode node = AddNode(parent, subscription, subscription.DisplayName, "Object");
            UpdateNode(node, subscription);

            if (!m_eventRegistrations.ContainsKey(subscription))
            {
                subscription.Callbacks.StateChangedCallback += m_SubscriptionStateChanged;
                m_eventRegistrations.Add(subscription, node);
            }
        }

        /// <summary>
        /// Updates a subscription node in the tree.
        /// </summary>
        private void UpdateNode(TreeNode parent, SubscriptionHandle subscription)
        {
            Clear(parent.Nodes);
            parent.Text = subscription.DisplayName;

            foreach (MonitoredItemHandle monitoredItem in subscription.Items)
            {
                AddNode(parent, monitoredItem, monitoredItem.DisplayName, "Property");
            }
        }
        #endregion

        private void BrowseAllMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    new AddressSpaceDlg().Show(session, BrowseViewType.All, NodeId.Null, Telemetry);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseObjectsMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    new AddressSpaceDlg().Show(session, BrowseViewType.Objects, NodeId.Null, Telemetry);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseObjectTypesMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    await new BrowseTypesDlg().ShowAsync(session, ObjectTypeIds.BaseObjectType);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseVariableTypesMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    await new BrowseTypesDlg().ShowAsync(session, VariableTypeIds.BaseDataVariableType);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseDataTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    new AddressSpaceDlg().Show(session, BrowseViewType.DataTypes, NodeId.Null, Telemetry);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseReferenceTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    new AddressSpaceDlg().Show(session, BrowseViewType.ReferenceTypes, NodeId.Null, Telemetry);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseEventTypesMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    await new BrowseTypesDlg().ShowAsync(session, ObjectTypeIds.BaseEventType);
                    #pragma warning restore CA2000
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseServerViewsMI_DropDownOpeningAsync(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    BrowseServerViewsMI.DropDown.Items.Clear();

                    Browser browser = new Browser(session);

                    browser.BrowseDirection = BrowseDirection.Forward;
                    browser.IncludeSubtypes = true;
                    browser.ReferenceTypeId = NodeId.Null;
                    browser.NodeClassMask = (int)NodeClass.View;
                    browser.ContinueUntilDone = true;

                    var references = await browser.BrowseAsync(new NodeId(Objects.ViewsFolder));

                    foreach (ReferenceDescription reference in references)
                    {
                        ToolStripItem item = BrowseServerViewsMI.DropDown.Items.Add(reference.ToString());
                        item.Click += new EventHandler(BrowseServerViewsMI_Click);
                        item.Tag = reference;
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        void BrowseServerViewsMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeNode selectedNode = NodesTV.SelectedNode;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    ToolStripMenuItem menuitem = sender as ToolStripMenuItem;

                    if (menuitem != null)
                    {
                        ReferenceDescription reference = menuitem.Tag as ReferenceDescription;

                        #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                        new AddressSpaceDlg().Show(
                        #pragma warning restore CA2000
                            session,
                            BrowseViewType.ServerDefinedView,
                            (NodeId)reference.NodeId,
                            Telemetry);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SubscriptionCreateMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get current selection.
                TreeNode selectedNode = NodesTV.SelectedNode;

                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                ISession session = selectedNode.Tag as ISession;

                if (session == null)
                {
                    return;
                }

                // create the subscription.
                await CreateSubscriptionAsync(session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void Subscription_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (KeyValuePair<SubscriptionHandle, SubscriptionDlg> current in m_dialogs)
            {
                if (current.Value == sender)
                {
                    m_dialogs.Remove(current.Key);
                    return;
                }
            }
        }

        private async void NewSessionMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectAsync(m_endpoint, Telemetry);
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
                // get current selection.
                TreeNode selectedNode = NodesTV.SelectedNode;

                if (selectedNode == null)
                {
                    return;
                }

                // delete session.
                ISession session = selectedNode.Tag as ISession;

                if (session != null)
                {
                    await DeleteAsync(session);
                }

                // delete subscription
                SubscriptionHandle subscription = selectedNode.Tag as SubscriptionHandle;

                if (subscription != null)
                {
                    await DeleteAsync(subscription);
                }

                // delete monitored item
                MonitoredItemHandle monitoredItem = selectedNode.Tag as MonitoredItemHandle;

                if (monitoredItem != null)
                {
                    subscription = Get<SubscriptionHandle>(selectedNode);

                    if (subscription != null)
                    {
                        await DeleteAsync(subscription, monitoredItem);
                    }
                }
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
                // get the current session.
                ISession session = Get<ISession>(NodesTV.SelectedNode);

                if (session == null || !session.Connected)
                {
                    return;
                }

                // build list of nodes to read.
                List<ReadValueId> valueIds = new List<ReadValueId>();

                MonitoredItemHandle monitoredItem = Get<MonitoredItemHandle>(NodesTV.SelectedNode);

                if (monitoredItem != null)
                {
                    valueIds.Add(ToReadValueId(monitoredItem));
                }
                else
                {
                    SubscriptionHandle subscription = Get<SubscriptionHandle>(NodesTV.SelectedNode);

                    if (subscription != null)
                    {
                        foreach (MonitoredItemHandle item in subscription.Items)
                        {
                            valueIds.Add(ToReadValueId(item));
                        }
                    }
                }

                // show form.
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                await new ReadDlg().ShowAsync(session, valueIds, Telemetry);
                #pragma warning restore CA2000
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Builds the read request for the node a monitored item watches.
        /// </summary>
        private static ReadValueId ToReadValueId(MonitoredItemHandle monitoredItem)
        {
            return new ReadValueId {
                NodeId = monitoredItem.Settings.StartNodeId,
                AttributeId = monitoredItem.Settings.AttributeId,
                IndexRange = monitoredItem.Settings.IndexRange,
                DataEncoding = monitoredItem.Settings.Encoding ?? QualifiedName.Null,
            };
        }

        private async void WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get the current session.
                ISession session = Get<ISession>(NodesTV.SelectedNode);

                if (session == null || !session.Connected)
                {
                    return;
                }

                // build list of nodes to write.
                List<WriteValue> values = new List<WriteValue>();

                MonitoredItemHandle monitoredItem = Get<MonitoredItemHandle>(NodesTV.SelectedNode);

                if (monitoredItem != null)
                {
                    values.Add(ToWriteValue(monitoredItem));
                }
                else
                {
                    SubscriptionHandle subscription = Get<SubscriptionHandle>(NodesTV.SelectedNode);

                    if (subscription != null)
                    {
                        foreach (MonitoredItemHandle item in subscription.Items)
                        {
                            values.Add(ToWriteValue(item));
                        }
                    }
                }

                // show form.
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                await new WriteDlg().ShowAsync(session, values, Telemetry);
                #pragma warning restore CA2000
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Builds the write request for the node a monitored item watches.
        /// </summary>
        private static WriteValue ToWriteValue(MonitoredItemHandle monitoredItem)
        {
            return new WriteValue {
                NodeId = monitoredItem.Settings.StartNodeId,
                AttributeId = monitoredItem.Settings.AttributeId,
                IndexRange = monitoredItem.Settings.IndexRange,
            };
        }

        private async void SubscriptionEnabledPublishingMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get current selection.
                TreeNode selectedNode = NodesTV.SelectedNode;

                if (selectedNode == null)
                {
                    return;
                }

                SubscriptionHandle subscription = selectedNode.Tag as SubscriptionHandle;

                if (subscription != null)
                {
                    // reconfiguring the options is the request, the engine applies it on its
                    // own worker.
                    bool enabled = SubscriptionEnabledPublishingMI.Checked;
                    subscription.Configure(options => options with { PublishingEnabled = enabled });
                    await subscription.WaitForPendingChangesAsync(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionMonitorMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get selected subscription.
                SubscriptionHandle subscription = SelectedTag as SubscriptionHandle;

                if (subscription == null)
                {
                    return;
                }

                // show form
                SubscriptionDlg dialog = null;

                if (!m_dialogs.TryGetValue(subscription, out dialog))
                {
                    dialog = new SubscriptionDlg();
                    dialog.FormClosing += new FormClosingEventHandler(Subscription_FormClosing);
                    m_dialogs.Add(subscription, dialog);
                }

                dialog.Show(subscription);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SessionSaveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                ISession session = SelectedTag as ISession;

                if (session is not ManagedSession managedSession)
                {
                    return;
                }

                // create a default file.
                if (String.IsNullOrEmpty(m_filePath))
                {
                    FileInfo defaultInfo = new FileInfo(Application.ExecutablePath);

                    m_filePath = defaultInfo.DirectoryName;
                    m_filePath += Path.DirectorySeparatorChar;
                    m_filePath += session.SessionName;
                    m_filePath += ".uasubs";
                }

                // prompt user to select file.
                FileInfo fileInfo = new FileInfo(m_filePath);

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                SaveFileDialog dialog = new SaveFileDialog();
                #pragma warning restore CA2000

                dialog.CheckFileExists = false;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".uasubs";
                dialog.Filter = "Subscription Files (*.uasubs)|*.uasubs|All Files (*.*)|*.*";
                dialog.ValidateNames = true;
                dialog.Title = "Save Subscriptions";
                dialog.FileName = m_filePath;
                dialog.InitialDirectory = fileInfo.DirectoryName;
                dialog.RestoreDirectory = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                // save the subscriptions of the V2 engine.
                using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create))
                {
                    await managedSession.SaveSubscriptionsAsync(stream, null);
                }

                // remember file path.
                m_filePath = dialog.FileName;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SessionLoadMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                ISession session = SelectedTag as ISession;

                if (session is not ManagedSession managedSession)
                {
                    return;
                }

                // create a default file.
                if (String.IsNullOrEmpty(m_filePath))
                {
                    FileInfo defaultInfo = new FileInfo(Application.ExecutablePath);

                    m_filePath = defaultInfo.DirectoryName;
                    m_filePath += Path.DirectorySeparatorChar;
                    m_filePath += session.SessionName;
                    m_filePath += ".uasubs";
                }

                FileInfo fileInfo = new FileInfo(m_filePath);

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                OpenFileDialog dialog = new OpenFileDialog();
                #pragma warning restore CA2000

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".uasubs";
                dialog.Filter = "Subscription Files (*.uasubs)|*.uasubs|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Load Subscriptions";
                dialog.FileName = m_filePath;
                dialog.InitialDirectory = fileInfo.DirectoryName;
                dialog.RestoreDirectory = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                // remember file path.
                m_filePath = dialog.FileName;

                // restore the subscriptions into the V2 engine. Each restored subscription
                // gets its own handle, whose callbacks the engine takes as the notification
                // handler.
                List<SubscriptionHandle> loaded = new List<SubscriptionHandle>();

                using (FileStream stream = new FileStream(dialog.FileName, FileMode.Open))
                {
                    await managedSession.LoadSubscriptionsAsync(
                        stream,
                        name => {
                            SubscriptionHandle handle = new SubscriptionHandle(session, name, ClientUtils.DefaultSubscriptionOptions);
                            loaded.Add(handle);
                            return handle.Callbacks;
                        },
                        false);
                }

                // the engine holds the restored subscriptions, so pair each new one with the
                // handle whose callbacks it was created with.
                if (loaded.Count > 0 && session.TryGetSubscriptionManager(out ISubscriptionManager manager))
                {
                    int index = 0;

                    foreach (ISubscription subscription in manager.Items)
                    {
                        if (FindSubscription(subscription) == null && index < loaded.Count)
                        {
                            loaded[index].Attach(subscription);
                            AddSubscription(loaded[index]);
                            index++;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void NewWindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                ClientForm form = this.FindForm() as ClientForm;

                if (form != null)
                {
                    form.OpenForm();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void SetLocaleMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                ISession session = SelectedTag as ISession;

                if (session == null)
                {
                    return;
                }

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                string locale = await new SelectLocaleDlg().ShowDialogAsync(session);
                #pragma warning restore CA2000

                if (locale == null)
                {
                    return;
                }

                PreferredLocales = new string[] { locale };
                await session.ChangePreferredLocalesAsync(new List<string>(PreferredLocales), CancellationToken.None);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
