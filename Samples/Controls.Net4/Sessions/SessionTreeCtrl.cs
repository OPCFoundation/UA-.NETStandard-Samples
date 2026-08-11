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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Sample.Controls
{
    public partial class SessionTreeCtrl : Opc.Ua.Client.Controls.BaseTreeCtrl
    {
        #region Contructors
        public SessionTreeCtrl()
        {
            InitializeComponent();

            m_eventRegistrations = new Dictionary<object, TreeNode>();
            m_endpointUrls = new List<string>();
            m_dialogs = new Dictionary<Subscription, SubscriptionDlg>();

            m_SessionSubscriptionsChanged = new EventHandler(Session_SubscriptionsChanged);
            m_SubscriptionStateChanged = new SubscriptionStateChangedEventHandler(Subscription_StateChanged);
        }
        #endregion

        #region Private Fields
        private BrowseTreeCtrl m_AddressSpaceCtrl;
        private NotificationMessageListCtrl m_NotificationMessagesCtrl;
        private ToolStripStatusLabel m_ServerStatusCtrl;
        private EventHandler m_SessionSubscriptionsChanged;
        private SubscriptionStateChangedEventHandler m_SubscriptionStateChanged;
        private Dictionary<object, TreeNode> m_eventRegistrations;
        private List<string> m_endpointUrls;
        private Dictionary<Subscription, SubscriptionDlg> m_dialogs;
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

            // close all active sessions.
            foreach (TreeNode root in NodesTV.Nodes)
            {
                Session session = root.Tag as Session;

                if (session != null)
                {
                    await session.CloseAsync(ct);
                }
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
                Session session = root.Tag as Session;

                if (session != null)
                {
                    await session.CloseAsync(ct);
                }
            }

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
        public async Task<Session> ConnectAsync(ConfiguredEndpoint endpoint, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            Telemetry = telemetry;

            List<EndpointDescription> availableEndpoints = null;

            // check if the endpoint needs to be updated.
            if (endpoint.UpdateBeforeConnect)
            {
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                ConfiguredServerDlg configurationDialog = new ConfiguredServerDlg();
                #pragma warning restore CA2000
                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.
                endpoint = configurationDialog.ShowDialog(endpoint, m_configuration);
                #pragma warning restore CA1849

                if (endpoint == null)
                {
                    return null;
                }
                availableEndpoints = configurationDialog.AvailableEnpoints;
            }

            m_endpoint = endpoint;

            // copy the message context.
            m_messageContext = m_configuration.CreateMessageContext();

            Opc.Ua.Security.Certificates.Certificate clientCertificate = null;

            if (endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
            {
                if (m_configuration.SecurityConfiguration.ApplicationCertificate == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
                }

                clientCertificate = await m_configuration.CertificateManager.CertificateProvider.GetPrivateKeyCertificateAsync(
                    m_configuration.SecurityConfiguration.ApplicationCertificate,
                    null,
                    null,
                    ct);

                if (clientCertificate == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate cannot be found.");
                }

            }

            // create the channel.
            ITransportChannel channel = await UaChannelBase.CreateUaBinaryChannelAsync(
                m_configuration,
                endpoint.Description,
                endpoint.Configuration,
                clientCertificate,
                m_messageContext,
                ct);

            // create the session.
            return await ConnectAsync(endpoint, channel, availableEndpoints, telemetry, ct);
        }

        /// <summary>
        /// Opens a new session.
        /// </summary>
        public async Task<Session> ConnectAsync(ConfiguredEndpoint endpoint, ITransportChannel channel, List<EndpointDescription> availableEndpoints, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));

            Telemetry = telemetry;

            try
            {
                // create the session.
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Session session = new Session(channel, m_configuration, endpoint, null);
                #pragma warning restore CA2000
                session.ReturnDiagnostics = DiagnosticsMasks.All;

                #pragma warning disable CA1849, CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                if (!new SessionOpenDlg().ShowDialog(session, PreferredLocales))
                #pragma warning restore CA1849, CA2000
                {
                    return null;
                }

                // session now owns the channel.
                channel = null;

                // delete the existing session.
                await CloseAsync(ct);

                // add session to tree.
                AddNode(session);

                // return the new session.
                return session;
            }
            finally
            {
                // ensure the channel is closed on error.
                if (channel != null)
                {
                    await channel.CloseAsync(ct);
                }
            }
        }

        /// <summary>
        /// Deletes a session.
        /// </summary>
        public async Task DeleteAsync(Session session, CancellationToken ct = default)
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

            await session.CloseAsync(ct);
            NodesTV.SelectedNode = null;
            SelectNode();
        }

        /// <summary>
        /// Deletes a subscription.
        /// </summary>
        public async Task DeleteAsync(Subscription subscription, CancellationToken ct = default)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            // close any dialog.
            SubscriptionDlg dialog = null;

            if (m_dialogs.TryGetValue(subscription, out dialog))
            {
                dialog.Close();
            }

            Session session = subscription.Session as Session;
            await session.RemoveSubscriptionAsync(subscription, ct);

            TreeNode node = FindNode(NodesTV.Nodes, subscription);

            if (node != null)
            {
                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Clear(node.Nodes);
                #pragma warning restore CA1849
                node.Remove();
            }

            NodesTV.SelectedNode = FindNode(NodesTV.Nodes, session);
        }

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        public async Task DeleteAsync(MonitoredItem monitoredItem, CancellationToken ct = default)
        {
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

            TreeNode node = FindNode(NodesTV.Nodes, monitoredItem);

            if (node != null)
            {
                #pragma warning disable CA1849 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Clear(node.Nodes);
                #pragma warning restore CA1849
                node.Remove();
            }

            Subscription subscription = monitoredItem.Subscription;
            subscription.RemoveItem(monitoredItem);
            await subscription.ApplyChangesAsync(ct);
            NodesTV.SelectedNode = FindNode(NodesTV.Nodes, subscription);
        }

        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        public async Task<Subscription> CreateSubscriptionAsync(Session session, CancellationToken ct = default)
        {
            // create form.
            #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
            SubscriptionDlg dialog = new SubscriptionDlg();
            #pragma warning restore CA2000
            dialog.FormClosing += new FormClosingEventHandler(Subscription_FormClosing);

            // create subscription.
            Subscription subscription = await dialog.NewAsync(session, Telemetry, ct);

            if (subscription != null)
            {
                m_dialogs.Add(subscription, dialog);
                subscription.Handle = dialog;
                return subscription;
            }

            return null;
        }

        public void Reload(Session session)
        {
            // update any dialogs.
            foreach (SubscriptionDlg dialog in new List<SubscriptionDlg>(m_dialogs.Values))
            {
                foreach (Subscription subscription in session.Subscriptions)
                {
                    if (subscription.Handle == dialog)
                    {
                        dialog.Show(subscription);
                    }
                }
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

            Session session = clickedNode.Tag as Session;

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

            Subscription subscription = clickedNode.Tag as Subscription;

            if (subscription != null)
            {
                DeleteMI.Enabled = true;
                ReadMI.Enabled = true;
                WriteMI.Enabled = true;
                SubscriptionMI.Enabled = subscription.Session.Connected;
                SubscriptionMonitorMI.Enabled = SubscriptionMI.Enabled;
                SubscriptionEnabledPublishingMI.Enabled = SubscriptionMI.Enabled;
                SubscriptionEnabledPublishingMI.Checked = subscription.CurrentPublishingEnabled;
            }

            MonitoredItem monitoredItem = clickedNode.Tag as MonitoredItem;

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

            Session session = Get<Session>(selectedNode);
            Subscription subscription = Get<Subscription>(selectedNode);

            // update address space control.
            if (m_AddressSpaceCtrl != null)
            {
                m_AddressSpaceCtrl.SetViewAsync(session, BrowseViewType.Objects, NodeId.Null, Telemetry);
            }

            // update notification messages control.
            if (m_NotificationMessagesCtrl != null)
            {
                m_NotificationMessagesCtrl.Initialize(session, subscription);
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
                if (m_eventRegistrations.Remove(node.Tag))
                {
                    if (node.Tag is Session)
                    {
                        ((Session)node.Tag).SubscriptionsChanged -= m_SessionSubscriptionsChanged;
                    }

                    else if (node.Tag is Subscription)
                    {
                        ((Subscription)node.Tag).StateChanged -= m_SubscriptionStateChanged;
                    }
                }

                Clear(node.Nodes);
            }

            nodes.Clear();
        }

        #region Private Members
        /// <summary>
        /// Called when the set of items for a subscription changes.
        /// </summary>
        private void Subscription_StateChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(Subscription_StateChanged), sender, e);
                return;
            }

            TreeNode node = FindNode(NodesTV.Nodes, sender);

            if (node == null)
            {
                return;
            }

            UpdateNode(node, sender as Subscription);
        }

        /// <summary>
        /// Called when the set of subscriptions for a session changes.
        /// </summary>
        private void Session_SubscriptionsChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(Session_SubscriptionsChanged), sender, e);
                return;
            }

            TreeNode node = FindNode(NodesTV.Nodes, sender);

            if (node == null)
            {
                return;
            }

            UpdateNode(node, sender as Session);
            node.EnsureVisible();
            node.Expand();
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
        /// Recursively finds the node with the specified tag and returns the immediate child with the specified child tag.
        /// </summary>
        private TreeNode FindChild(TreeNodeCollection collection, object tag, object childTag)
        {
            TreeNode parent = FindNode(collection, tag);

            if (parent == null)
            {
                return null;
            }

            foreach (TreeNode child in parent.Nodes)
            {
                if (Object.ReferenceEquals(child.Tag, childTag))
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a session to the tree.
        /// </summary>
        private void AddNode(Session session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            TreeNode node = AddNode(null, session, session.SessionName, "Server");
            UpdateNode(node, session);

            if (!m_eventRegistrations.ContainsKey(session))
            {
                session.SubscriptionsChanged += m_SessionSubscriptionsChanged;
                m_eventRegistrations.Add(session, node);
            }

            NodesTV.SelectedNode = node;
        }

        /// <summary>
        /// Updates a session node in the tree.
        /// </summary>
        private void UpdateNode(TreeNode parent, Session session)
        {
            UpdateNode(parent, session, session.SessionName, (session.Connected) ? "Server" : "ServerStopped");
            Clear(parent.Nodes);

            if (Object.ReferenceEquals(parent.Tag, session))
            {
                foreach (Subscription subscription in session.Subscriptions)
                {
                    AddNode(parent, subscription);
                }
            }
        }

        /// <summary>
        /// Adds a subscription to the tree.
        /// </summary>
        private void AddNode(TreeNode parent, Subscription subscription)
        {
            TreeNode node = AddNode(parent, subscription, subscription.DisplayName, "Object");
            UpdateNode(node, subscription);

            if (!m_eventRegistrations.ContainsKey(subscription))
            {
                subscription.StateChanged += m_SubscriptionStateChanged;
                m_eventRegistrations.Add(subscription, node);
            }
        }

        /// <summary>
        /// Updates a subscription node in the tree.
        /// </summary>
        private void UpdateNode(TreeNode parent, Subscription subscription)
        {
            Clear(parent.Nodes);
            parent.Text = subscription.DisplayName;

            foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
                Session session = selectedNode.Tag as Session;

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
            foreach (KeyValuePair<Subscription, SubscriptionDlg> current in m_dialogs)
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
                Session session = selectedNode.Tag as Session;

                if (session != null)
                {
                    await DeleteAsync(session);
                }

                // delete subscription
                Subscription subscription = selectedNode.Tag as Subscription;

                if (subscription != null)
                {
                    await DeleteAsync(subscription);
                }

                // delete monitored item
                MonitoredItem monitoredItem = selectedNode.Tag as MonitoredItem;

                if (monitoredItem != null)
                {
                    await DeleteAsync(monitoredItem);
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
                Session session = Get<Session>(NodesTV.SelectedNode);

                if (session == null || !session.Connected)
                {
                    return;
                }

                // build list of nodes to read.
                List<ReadValueId> valueIds = new List<ReadValueId>();

                MonitoredItem monitoredItem = Get<MonitoredItem>(NodesTV.SelectedNode);

                if (monitoredItem != null)
                {
                    ReadValueId valueId = new ReadValueId();

                    valueId.NodeId = monitoredItem.ResolvedNodeId;
                    valueId.AttributeId = monitoredItem.AttributeId;
                    valueId.IndexRange = monitoredItem.IndexRange;
                    valueId.DataEncoding = monitoredItem.Encoding;

                    valueIds.Add(valueId);
                }
                else
                {
                    Subscription subscription = Get<Subscription>(NodesTV.SelectedNode);

                    if (subscription != null)
                    {
                        foreach (MonitoredItem item in subscription.MonitoredItems)
                        {
                            ReadValueId valueId = new ReadValueId();

                            valueId.NodeId = item.ResolvedNodeId;
                            valueId.AttributeId = item.AttributeId;
                            valueId.IndexRange = item.IndexRange;
                            valueId.DataEncoding = item.Encoding;

                            valueIds.Add(valueId);
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

        private async void WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // get the current session.
                Session session = Get<Session>(NodesTV.SelectedNode);

                if (session == null || !session.Connected)
                {
                    return;
                }

                // build list of nodes to read.
                List<WriteValue> values = new List<WriteValue>();

                MonitoredItem monitoredItem = Get<MonitoredItem>(NodesTV.SelectedNode);

                if (monitoredItem != null)
                {
                    WriteValue value = new WriteValue();

                    value.NodeId = monitoredItem.ResolvedNodeId;
                    value.AttributeId = monitoredItem.AttributeId;
                    value.IndexRange = monitoredItem.IndexRange;

                    MonitoredItemNotification datachange = monitoredItem.LastValue as MonitoredItemNotification;

                    if (datachange != null)
                    {
                        value.Value = datachange.Value;
                    }

                    values.Add(value);
                }
                else
                {
                    Subscription subscription = Get<Subscription>(NodesTV.SelectedNode);

                    if (subscription != null)
                    {
                        foreach (MonitoredItem item in subscription.MonitoredItems)
                        {
                            WriteValue value = new WriteValue();

                            value.NodeId = item.ResolvedNodeId;
                            value.AttributeId = item.AttributeId;
                            value.IndexRange = item.IndexRange;

                            MonitoredItemNotification datachange = item.LastValue as MonitoredItemNotification;

                            if (datachange != null)
                            {
                                value.Value = datachange.Value;
                            }

                            values.Add(value);
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

                // delete session.
                Subscription subscription = selectedNode.Tag as Subscription;

                if (subscription != null)
                {
                    await subscription.SetPublishingModeAsync(SubscriptionEnabledPublishingMI.Checked);
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
                // get selected session.
                Subscription subscription = SelectedTag as Subscription;

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
                    subscription.Handle = dialog;
                }

                dialog.Show(subscription);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SessionSaveMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                Session session = SelectedTag as Session;

                if (session == null)
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
                    m_filePath += ".xml";
                }

                // prompt user to select file.
                FileInfo fileInfo = new FileInfo(m_filePath);

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                SaveFileDialog dialog = new SaveFileDialog();
                #pragma warning restore CA2000

                dialog.CheckFileExists = false;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".xml";
                dialog.Filter = "Result Files (*.xml)|*.xml|All Files (*.*)|*.*";
                dialog.ValidateNames = true;
                dialog.Title = "Save Subscriptions";
                dialog.FileName = m_filePath;
                dialog.InitialDirectory = fileInfo.DirectoryName;
                dialog.RestoreDirectory = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                // save file.
                session.Save(dialog.FileName);

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
                Session session = SelectedTag as Session;

                if (session == null)
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
                    m_filePath += ".xml";
                }

                FileInfo fileInfo = new FileInfo(m_filePath);

                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                OpenFileDialog dialog = new OpenFileDialog();
                #pragma warning restore CA2000

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".xml";
                dialog.Filter = "Result Files (*.xml)|*.xml|All Files (*.*)|*.*";
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

                // load file.
                IEnumerable<Subscription> subscriptions = session.Load(dialog.FileName);

                // create the subscriptions automatically if the session is connected.
                if (session.Connected)
                {
                    foreach (Subscription subscription in subscriptions)
                    {
                        await subscription.CreateAsync();
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
                Session session = SelectedTag as Session;

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
