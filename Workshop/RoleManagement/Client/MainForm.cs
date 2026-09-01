/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.RoleManagement.Client
{
    // The source generator emits a Quickstarts.RoleManagement.BrowseNames and ObjectIds for
    // the model of the server. This namespace is a child of that one, so those would win over
    // the standard sets of the same name: both are named apart here.
    using ModelNames = Quickstarts.RoleManagement.BrowseNames;
    using BrowseNames = Opc.Ua.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;
    using MethodIds = Opc.Ua.MethodIds;

    /// <summary>
    /// The main form of the OPC UA Part 18 role management Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The form is a two-panel demonstration of what a Role is worth. The upper list is the
    /// machine of the sample server as the current Session sees it: the nodes it may browse,
    /// the values it may read, and what its UserRolePermissions say it may do with each of
    /// them. Signing in as a different account and connecting again changes all three,
    /// without a line of client code knowing which account is which.
    /// </para>
    /// <para>
    /// The lower list is the RoleSet the server publishes below
    /// Server/ServerCapabilities. Its Methods are the Part 18 4.2/4.4 role configuration
    /// API: a Session which holds the SecurityAdmin Role, over an encrypted channel, can
    /// create a Role and grant it to a user while everybody else stays connected. The
    /// buttons are deliberately left enabled for every account, because seeing the server
    /// answer BadUserAccessDenied or BadSecurityModeInsufficient is the point.
    /// </para>
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the application.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62573/Quickstarts/RoleManagementServer";
            this.Text = m_configuration.ApplicationName;

            // the accounts the sample server knows, and the Role each of them earns. The
            // client has no idea what any of that means - it only picks the identity token
            // and lets the server decide what the Session is worth.
            IdentityCB.Items.AddRange(new object[] {
                kAnonymous,
                "observer1",
                "operator1",
                "engineer1",
                "supervisor1",
                "secadmin",
                "guest",
            });

            IdentityCB.SelectedIndex = 0;
            IdentityCB.SelectedIndexChanged += IdentityCB_SelectedIndexChanged;

            UpdateIdentityHint();
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The entry of the identity drop down which opens an anonymous Session.
        /// </summary>
        private const string kAnonymous = "Anonymous";

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private ISession m_session;
        private NodeId m_machineId;

        /// <summary>
        /// The Reset method of the machine, as this Session browsed it, or null when the
        /// Session may not see it.
        /// </summary>
        private NodeId m_resetId;
        private readonly Dictionary<NodeId, string> m_roleNames = new Dictionary<NodeId, string>();
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Picks the identity token the next connect uses.
        /// </summary>
        /// <remarks>
        /// The whole of the identity handling of this client is these few lines. Everything
        /// the rest of the form shows follows from which token was sent, because the server
        /// resolves the Roles of the Session from it.
        /// </remarks>
        private void IdentityCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                string account = IdentityCB.SelectedItem as string;

                ConnectServerCTRL.UserIdentity = string.Equals(account, kAnonymous, StringComparison.Ordinal)
                    ? null
                    : new UserIdentity(account, Encoding.UTF8.GetBytes(account));

                UpdateIdentityHint();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    m_machineId = NodeId.Null;
                    m_resetId = NodeId.Null;
                    NodesLV.Items.Clear();
                    RolesLV.Items.Clear();
                    SetButtonsEnabled(false);
                    return;
                }

                // this client has built-in knowledge of the information model of the server
                var wellKnownNamespaceUris = new NamespaceTable();
                wellKnownNamespaceUris.Append(Namespaces.RoleManagement);

                List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                    m_session,
                    ObjectIds.ObjectsFolder,
                    wellKnownNamespaceUris,
                    default,
                    "1:" + ModelNames.Machine);

                m_machineId = nodes.Count > 0 ? nodes[0] : NodeId.Null;

                SetButtonsEnabled(true);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                SetButtonsEnabled(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                SetButtonsEnabled(m_session != null);

                if (m_session != null)
                {
                    await RefreshAsync().ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }

        /// <summary>
        /// Reads the machine and the RoleSet again.
        /// </summary>
        private async void RefreshBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Writes the value in the text box to the selected node.
        /// </summary>
        private async void WriteBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || NodesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                var node = (NodeRow)NodesLV.SelectedItems[0].Tag;

                if (node.IsMethod)
                {
                    Report($"Writing {node.Name}", StatusCodes.BadAttributeIdInvalid);
                    return;
                }

                if (!node.IsText &&
                    !double.TryParse(WriteValueTB.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double _))
                {
                    Report($"Writing {node.Name}", StatusCodes.BadTypeMismatch);
                    return;
                }

                Variant value = node.IsText
                    ? Variant.From(WriteValueTB.Text)
                    : Variant.From(double.Parse(WriteValueTB.Text, CultureInfo.CurrentCulture));

                var valuesToWrite = new List<WriteValue> {
                    new WriteValue {
                        NodeId = node.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value),
                    },
                };

                WriteResponse response = await m_session
                    .WriteAsync(null, valuesToWrite, default)
                    .ConfigureAwait(true);

                Report($"Writing {node.Name}", response.Results.ToArray()[0]);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Calls the Reset method of the machine.
        /// </summary>
        private async void ResetBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || m_machineId.IsNull)
                {
                    return;
                }

                if (m_resetId.IsNull)
                {
                    // the method is not in the address space this Session can see, so it was
                    // not granted Browse on it - which is a refusal in its own right
                    Report("Calling Reset", StatusCodes.BadUserAccessDenied);
                    return;
                }

                CallMethodResult result = await CallAsync(m_machineId, m_resetId).ConfigureAwait(true);

                Report("Calling Reset", result.StatusCode);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Grants the selected Role to the user in the text box.
        /// </summary>
        private async void AddIdentityBTN_ClickAsync(object sender, EventArgs e)
        {
            await ChangeIdentityAsync(BrowseNames.AddIdentity).ConfigureAwait(true);
        }

        /// <summary>
        /// Revokes the selected Role from the user in the text box.
        /// </summary>
        private async void RemoveIdentityBTN_ClickAsync(object sender, EventArgs e)
        {
            await ChangeIdentityAsync(BrowseNames.RemoveIdentity).ConfigureAwait(true);
        }

        /// <summary>
        /// Adds a Role of the server's own to the RoleSet.
        /// </summary>
        private async void AddRoleBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                CallMethodResult result = await CallAsync(
                    ObjectIds.Server_ServerCapabilities_RoleSet,
                    MethodIds.Server_ServerCapabilities_RoleSet_AddRole,
                    Variant.From(NewRoleTB.Text),

                    // no namespace uri: the server puts the Role in the namespace it
                    // allocates dynamic Roles in, rather than into one which holds a model,
                    // where it would take the node id of an existing node
                    // (UA-.NETStandard#4361)
                    Variant.From(string.Empty)).ConfigureAwait(true);

                Report($"Adding the role '{NewRoleTB.Text}'", result.StatusCode);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Calls AddIdentity or RemoveIdentity on the selected Role.
        /// </summary>
        /// <remarks>
        /// Both Methods take one IdentityMappingRuleType, and a UserName rule is the one
        /// this sample server is configured with. The Methods live on the Role node itself,
        /// and the standard address space only lets a SecurityAdmin browse to them, so a
        /// Session which is refused the change is usually refused the browse as well.
        /// </remarks>
        private async Task ChangeIdentityAsync(string methodName)
        {
            try
            {
                if (m_session == null || RolesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                var role = (RoleRow)RolesLV.SelectedItems[0].Tag;

                NodeId methodId = await ResolveAsync(role.NodeId, methodName).ConfigureAwait(true);

                if (methodId.IsNull)
                {
                    Report($"{methodName} on {role.Name}", StatusCodes.BadUserAccessDenied);
                    return;
                }

                var rule = new IdentityMappingRuleType {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = RoleUserTB.Text,
                };

                CallMethodResult result = await CallAsync(
                    role.NodeId,
                    methodId,
                    Variant.From(new ExtensionObject(rule))).ConfigureAwait(true);

                Report($"{methodName}('{RoleUserTB.Text}') on {role.Name}", result.StatusCode);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Reads the machine and the RoleSet and fills both lists.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (m_session == null)
            {
                return;
            }

            ArrayOf<NodeId> grantedRoles = await LoadMachineAsync().ConfigureAwait(true);

            await LoadRolesAsync(grantedRoles).ConfigureAwait(true);
        }

        /// <summary>
        /// Fills the node list with the children of the machine this Session may browse.
        /// </summary>
        /// <remarks>
        /// The list is built from a Browse rather than from a hard coded set of nodes, which
        /// is what makes the effect of the Browse permission visible: two Sessions on the
        /// same server come back with a different number of rows.
        /// </remarks>
        /// <returns>The Roles the UserRolePermissions of the machine name for this Session.</returns>
        private async Task<ArrayOf<NodeId>> LoadMachineAsync()
        {
            NodesLV.Items.Clear();
            m_resetId = NodeId.Null;

            if (m_machineId.IsNull)
            {
                return default;
            }

            var granted = new List<NodeId>();

            foreach (RolePermissionType permission in await ReadPermissionsAsync(m_machineId).ConfigureAwait(true))
            {
                granted.Add(permission.RoleId);
            }

            var browse = new BrowseDescription {
                NodeId = m_machineId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Variable | NodeClass.Method),
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await m_session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, default)
                .ConfigureAwait(true);

            foreach (ReferenceDescription reference in response.Results.ToArray()[0].References.ToArray())
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);

                var row = new NodeRow {
                    Name = reference.BrowseName.Name,
                    NodeId = nodeId,
                    IsMethod = reference.NodeClass == NodeClass.Method,
                };

                // The node ids of the model come from this browse rather than from a browse
                // path, so the namespace index is whatever the server assigned and the client
                // never has to guess it. Comparing the browse name is safe here because the
                // browse is scoped to the children of the machine.
                if (row.IsMethod && string.Equals(row.Name, ModelNames.Reset, StringComparison.Ordinal))
                {
                    m_resetId = nodeId;
                }

                string value = string.Empty;
                string status = string.Empty;

                if (!row.IsMethod)
                {
                    DataValue read = await ReadAsync(nodeId, Attributes.Value).ConfigureAwait(true);

                    row.IsText = read.WrappedValue.TypeInfo.BuiltInType == BuiltInType.String;
                    value = StatusCode.IsGood(read.StatusCode) ? read.WrappedValue.ToString() : string.Empty;
                    status = read.StatusCode.ToString();
                }

                IReadOnlyList<RolePermissionType> permissions =
                    await ReadPermissionsAsync(nodeId).ConfigureAwait(true);

                var item = new ListViewItem(row.Name) { Tag = row };

                item.SubItems.Add(value);
                item.SubItems.Add(status);
                item.SubItems.Add(DescribePermissions(permissions));

                NodesLV.Items.Add(item);
            }

            return granted.ToArrayOf();
        }

        /// <summary>
        /// Fills the role list with the Roles of the RoleSet and their identity rules.
        /// </summary>
        private async Task LoadRolesAsync(ArrayOf<NodeId> grantedRoles)
        {
            RolesLV.Items.Clear();
            m_roleNames.Clear();

            var browse = new BrowseDescription {
                NodeId = ObjectIds.Server_ServerCapabilities_RoleSet,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await m_session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, default)
                .ConfigureAwait(true);

            foreach (ReferenceDescription reference in response.Results.ToArray()[0].References.ToArray())
            {
                NodeId roleId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                string name = reference.BrowseName.Name;

                m_roleNames[roleId] = name;

                var item = new ListViewItem(name) {
                    Tag = new RoleRow { Name = name, NodeId = roleId },
                };

                item.SubItems.Add(grantedRoles.Contains(roleId) ? "yes" : string.Empty);
                item.SubItems.Add(await DescribeIdentitiesAsync(roleId).ConfigureAwait(true));

                RolesLV.Items.Add(item);
            }
        }

        /// <summary>
        /// The identity mapping rules of a Role, or why they could not be read.
        /// </summary>
        private async Task<string> DescribeIdentitiesAsync(NodeId roleId)
        {
            NodeId identitiesId = await ResolveAsync(roleId, BrowseNames.Identities).ConfigureAwait(true);

            if (identitiesId.IsNull)
            {
                // the standard address space reserves the Role nodes for the SecurityAdmin
                // Role, so an ordinary Session cannot even browse to the property
                return "(not visible to this session)";
            }

            DataValue value = await ReadAsync(identitiesId, Attributes.Value).ConfigureAwait(true);

            if (!StatusCode.IsGood(value.StatusCode))
            {
                return value.StatusCode.ToString();
            }

            if (!value.WrappedValue.TryGetStructure(out ArrayOf<IdentityMappingRuleType> rules))
            {
                return string.Empty;
            }

            return string.Join(
                ", ",
                rules.ToArray().Select(rule => string.IsNullOrEmpty(rule.Criteria)
                    ? rule.CriteriaType.ToString()
                    : $"{rule.CriteriaType}={rule.Criteria}"));
        }

        /// <summary>
        /// The UserRolePermissions of a node: what the Roles of this Session earn on it.
        /// </summary>
        private async Task<IReadOnlyList<RolePermissionType>> ReadPermissionsAsync(NodeId nodeId)
        {
            DataValue value = await ReadAsync(nodeId, Attributes.UserRolePermissions).ConfigureAwait(true);

            if (!StatusCode.IsGood(value.StatusCode) ||
                !value.WrappedValue.TryGetStructure(out ArrayOf<RolePermissionType> permissions))
            {
                return Array.Empty<RolePermissionType>();
            }

            return permissions.ToArray();
        }

        /// <summary>
        /// Renders a UserRolePermissions table as "Role: permission, permission".
        /// </summary>
        private string DescribePermissions(IReadOnlyList<RolePermissionType> permissions)
        {
            return string.Join(
                "; ",
                permissions.Select(permission => {
                    string name = m_roleNames.TryGetValue(permission.RoleId, out string known)
                        ? known
                        : permission.RoleId.ToString();

                    return $"{name}: {(PermissionType)permission.Permissions}";
                }));
        }

        /// <summary>
        /// Follows one hierarchical browse name from a node.
        /// </summary>
        /// <remarks>
        /// Only for browse names of the STANDARD address space, which are in namespace zero -
        /// the Methods and Properties of a Role, for instance. A browse name of the sample's
        /// own model is in the model's namespace, and a <see cref="QualifiedName"/> built from
        /// a bare string is in namespace zero, so passing one here silently resolves to
        /// nothing. The nodes of the model are taken from the browse in
        /// <see cref="LoadMachineAsync"/> instead, which carries whatever namespace index the
        /// server assigned.
        /// </remarks>
        /// <param name="startingNode">The node to start at.</param>
        /// <param name="browseName">A browse name in namespace zero.</param>
        private async Task<NodeId> ResolveAsync(NodeId startingNode, string browseName)
        {
            var browsePath = new BrowsePath {
                StartingNode = startingNode,
                RelativePath = new RelativePath {
                    Elements = new[] {
                        new RelativePathElement {
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName),
                        },
                    }.ToArrayOf(),
                },
            };

            TranslateBrowsePathsToNodeIdsResponse response = await m_session
                .TranslateBrowsePathsToNodeIdsAsync(null, new List<BrowsePath> { browsePath }, default)
                .ConfigureAwait(true);

            BrowsePathResult result = response.Results.ToArray()[0];

            if (StatusCode.IsBad(result.StatusCode) || result.Targets.Count == 0)
            {
                return NodeId.Null;
            }

            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, m_session.NamespaceUris);
        }

        /// <summary>
        /// Reads one attribute of a node without throwing on a bad status code.
        /// </summary>
        /// <remarks>
        /// The bad status codes are what this client is here to show, so they have to arrive
        /// as values it can put in a column rather than as exceptions.
        /// </remarks>
        private async Task<DataValue> ReadAsync(NodeId nodeId, uint attributeId)
        {
            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = attributeId },
            };

            ReadResponse response = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, default)
                .ConfigureAwait(true);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Calls a method without throwing on a bad status code.
        /// </summary>
        private async Task<CallMethodResult> CallAsync(
            NodeId objectId,
            NodeId methodId,
            params Variant[] inputArguments)
        {
            var request = new CallMethodRequest {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = (inputArguments ?? Array.Empty<Variant>()).ToArrayOf(),
            };

            CallResponse response = await m_session
                .CallAsync(null, new List<CallMethodRequest> { request }, default)
                .ConfigureAwait(true);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Reports what the server answered to an operation the user asked for.
        /// </summary>
        /// <remarks>
        /// The status bar rather than a message box: half the point of this sample is to try
        /// an operation as one account after another and compare the refusals, and a modal
        /// dialog between every click makes that tedious. It also keeps the buttons drivable
        /// from a test, which a modal dialog does not.
        /// </remarks>
        private void Report(string what, StatusCode status)
        {
            ActionStatusLB.Text = $"{what} answered {status}";
            ActionStatusLB.ForeColor = StatusCode.IsGood(status) ? Color.Empty : Color.Red;
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            WriteBTN.Enabled = enabled;
            ResetBTN.Enabled = enabled;
            AddIdentityBTN.Enabled = enabled;
            RemoveIdentityBTN.Enabled = enabled;
            AddRoleBTN.Enabled = enabled;
        }

        /// <summary>
        /// Explains what the selected account is expected to be able to do.
        /// </summary>
        private void UpdateIdentityHint()
        {
            string account = IdentityCB.SelectedItem as string;

            IdentityHintLB.Text = account switch {
                "observer1" => "Observer: reads the temperature and the set point.",
                "operator1" => "Operator: writes the set point and calls Reset.",
                "engineer1" => "Engineer: the only Role which sees the calibration.",
                "supervisor1" => "Supervisor: writes the maintenance note.",
                "secadmin" => "SecurityAdmin: manages the RoleSet, over an encrypted channel.",
                "guest" => "No Role beyond AuthenticatedUser: sees the machine, may change nothing.",
                _ => "Anonymous: browses the machine, and is refused every value.",
            };
        }
        #endregion

        #region Private Types
        /// <summary>
        /// One row of the node list.
        /// </summary>
        private sealed class NodeRow
        {
            public string Name { get; init; }
            public NodeId NodeId { get; init; }
            public bool IsMethod { get; init; }
            public bool IsText { get; set; }
        }

        /// <summary>
        /// One row of the role list.
        /// </summary>
        private sealed class RoleRow
        {
            public string Name { get; init; }
            public NodeId NodeId { get; init; }
        }
        #endregion
    }
}
