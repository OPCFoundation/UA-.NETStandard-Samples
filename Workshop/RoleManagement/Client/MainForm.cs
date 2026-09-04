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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Security.Certificates;

namespace Quickstarts.RoleManagement.Client
{
    // The source generator emits a Quickstarts.RoleManagement.BrowseNames and ObjectIds for
    // the model of the server. This namespace is a child of that one, so those would win over
    // the standard sets of the same name: both are named apart here.
    using ModelNames = Quickstarts.RoleManagement.BrowseNames;
    using BrowseNames = Opc.Ua.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;
    using ObjectTypeIds = Opc.Ua.ObjectTypeIds;
    using MethodIds = Opc.Ua.MethodIds;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// The main form of the OPC UA Part 18 role management Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The form is a three-panel demonstration of what a Role is worth. The upper list is the
    /// machine of the sample server as the current Session sees it: the nodes it may browse,
    /// the values it may read, the AccessRestrictions each of them carries, and what its
    /// UserRolePermissions say it may do with them. Signing in as a different account and
    /// connecting again changes all of it, without a line of client code knowing which
    /// account is which - and so does clearing the Use Security box, because two of the
    /// nodes are restricted to an encrypted channel and one Role is restricted to the
    /// encrypted endpoints.
    /// </para>
    /// <para>
    /// The middle list is the RoleSet the server publishes below
    /// Server/ServerCapabilities. Its Methods are the Part 18 4.2/4.4 role configuration
    /// API: a Session which holds the SecurityAdmin Role, over an encrypted channel, can
    /// create a Role, grant it to a user name or to the certificate of a client application,
    /// and set the CustomConfiguration flag, all while everybody else stays connected. The
    /// buttons are deliberately left enabled for every account, because seeing the server
    /// answer BadUserAccessDenied or BadSecurityModeInsufficient is the point.
    /// </para>
    /// <para>
    /// The lower list is the audit trail the server reports for those changes. It stays
    /// empty against 2.0.0-preview.4 - see <see cref="SubscribeToAuditEventsAsync"/>.
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

            // the three Part 18 4.4.3 identity criteria this sample can produce. UserName is
            // what the server maps its demonstration accounts with; the other two are matched
            // against the application instance certificate this client sends in CreateSession,
            // so the text box is filled with what this client would present.
            CriteriaCB.Items.AddRange(new object[] {
                IdentityCriteriaType.UserName,
                IdentityCriteriaType.Thumbprint,
                IdentityCriteriaType.X509Subject,
            });

            CriteriaCB.SelectedIndex = 0;
            CriteriaCB.SelectedIndexChanged += CriteriaCB_SelectedIndexChangedAsync;

            UpdateIdentityHint();
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The entry of the identity drop down which opens an anonymous Session.
        /// </summary>
        private const string kAnonymous = "Anonymous";

        /// <summary>
        /// The account the criteria box offers for a UserName rule.
        /// </summary>
        private const string kDefaultUserCriteria = "guest";

        /// <summary>
        /// The order Part 18 4.4.3 puts the parts of an X509Subject criteria in.
        /// </summary>
        private static readonly string[] kSubjectNameOrder =
            { "CN", "O", "OU", "DC", "L", "S", "C", "dnQualifier", "serialNumber" };

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

        /// <summary>
        /// The browse names of the event types the audit list has already shown.
        /// </summary>
        private readonly Dictionary<NodeId, string> m_typeNames = new Dictionary<NodeId, string>();

        /// <summary>
        /// The subscription which carries the audit events of the server, or null.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed asynchronously by DeleteSubscriptionAsync.")]
        private StreamingSubscription m_streaming;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed by DeleteSubscriptionAsync.")]
        private CancellationTokenSource m_cts;
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
                    await DeleteSubscriptionAsync().ConfigureAwait(true);

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

                await SubscribeToAuditEventsAsync().ConfigureAwait(true);

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
            ClientUtils.WaitForTeardown(DeleteSubscriptionAsync);

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
        /// Fills the criteria box with something the chosen criteria type accepts.
        /// </summary>
        /// <remarks>
        /// The two certificate criteria are matched against the application instance
        /// certificate of the <b>client</b>, so this client can fill them in from its own
        /// configuration. Granting a Role for one of them and reconnecting is how the sample
        /// shows a Role which belongs to a machine rather than to a person.
        /// </remarks>
        private async void CriteriaCB_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                RoleUserTB.Text = await CriteriaOfAsync(SelectedCriteriaType()).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Grants the selected Role to the identity in the text box.
        /// </summary>
        private async void AddIdentityBTN_ClickAsync(object sender, EventArgs e)
        {
            await ChangeIdentityAsync(BrowseNames.AddIdentity).ConfigureAwait(true);
        }

        /// <summary>
        /// Flips the CustomConfiguration flag of the selected Role.
        /// </summary>
        /// <remarks>
        /// Part 18 4.4.1: a Role whose Identities list is empty is granted to nobody unless
        /// CustomConfiguration is set, and with it set the rest of the configuration - the
        /// Applications and Endpoints filters - decides on its own. Revoke the X509Subject
        /// rule of the ConfigureAdmin Role of the sample, set the flag, and every Session on
        /// the encrypted endpoint holds that Role.
        /// </remarks>
        private async void CustomConfigBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || RolesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                var role = (RoleRow)RolesLV.SelectedItems[0].Tag;

                NodeId flagId = await ResolveAsync(role.NodeId, BrowseNames.CustomConfiguration)
                    .ConfigureAwait(true);

                if (flagId.IsNull)
                {
                    Report($"CustomConfiguration of {role.Name}", StatusCodes.BadUserAccessDenied);
                    return;
                }

                var valuesToWrite = new List<WriteValue> {
                    new WriteValue {
                        NodeId = flagId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(!role.CustomConfiguration)),
                    },
                };

                WriteResponse response = await m_session
                    .WriteAsync(null, valuesToWrite, default)
                    .ConfigureAwait(true);

                Report(
                    $"CustomConfiguration of {role.Name} := {!role.CustomConfiguration}",
                    response.Results.ToArray()[0]);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
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
        /// Both Methods take one IdentityMappingRuleType, whose criteria type the drop down
        /// picks: a UserName rule names an account, a Thumbprint or an X509Subject rule names
        /// the certificate a client application presents. The Methods live on the Role node
        /// itself, and the standard address space only lets a SecurityAdmin browse to them,
        /// so a Session which is refused the change is usually refused the browse as well.
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

                IdentityCriteriaType criteriaType = SelectedCriteriaType();

                var rule = new IdentityMappingRuleType {
                    CriteriaType = criteriaType,
                    Criteria = RoleUserTB.Text,
                };

                CallMethodResult result = await CallAsync(
                    role.NodeId,
                    methodId,
                    Variant.From(new ExtensionObject(rule))).ConfigureAwait(true);

                Report($"{methodName}({criteriaType}='{RoleUserTB.Text}') on {role.Name}", result.StatusCode);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// The identity criteria type the drop down is on.
        /// </summary>
        private IdentityCriteriaType SelectedCriteriaType()
        {
            return CriteriaCB.SelectedItem is IdentityCriteriaType selected
                ? selected
                : IdentityCriteriaType.UserName;
        }

        /// <summary>
        /// A criteria string of the given type which this client could be matched by.
        /// </summary>
        /// <remarks>
        /// A Thumbprint has to be upper case hexadecimal without separators, and an
        /// X509Subject has to be the normalised <c>Name="Value"</c> form of Part 18 4.4.3
        /// rather than the comma separated one a certificate reports. Both conversions are
        /// done here because the stack keeps its own normalisation internal.
        /// </remarks>
        private async Task<string> CriteriaOfAsync(IdentityCriteriaType criteriaType)
        {
            if (criteriaType == IdentityCriteriaType.UserName)
            {
                return kDefaultUserCriteria;
            }

            using Certificate certificate = await m_configuration.SecurityConfiguration
                .FindApplicationCertificateAsync(SecurityPolicies.Basic256Sha256, false, m_telemetry)
                .ConfigureAwait(true);

            if (certificate == null)
            {
                return string.Empty;
            }

            return criteriaType == IdentityCriteriaType.Thumbprint
                ? certificate.Thumbprint.ToUpperInvariant()
                : Part18Subject(certificate.Subject);
        }

        /// <summary>
        /// Turns the subject name of a certificate into the Part 18 4.4.3 X509Subject form.
        /// </summary>
        /// <remarks>
        /// The grammar is <c>Name="Value"</c> pairs separated by slashes, in the fixed order
        /// CN, O, OU, DC, L, S, C, dnQualifier, serialNumber, with names outside that set
        /// dropped. A certificate reports its subject comma separated and in its own order,
        /// so the two are only comparable after this.
        /// </remarks>
        private static string Part18Subject(string subject)
        {
            var pairs = new List<KeyValuePair<string, string>>();

            foreach (string part in (subject ?? string.Empty).Split(','))
            {
                int separator = part.IndexOf('=', StringComparison.Ordinal);

                if (separator <= 0)
                {
                    continue;
                }

                pairs.Add(new KeyValuePair<string, string>(
                    part.Substring(0, separator).Trim(),
                    part.Substring(separator + 1).Trim().Trim('"')));
            }

            var builder = new StringBuilder();

            foreach (string name in kSubjectNameOrder)
            {
                foreach (KeyValuePair<string, string> pair in pairs)
                {
                    if (!string.Equals(pair.Key, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append('/');
                    }

                    builder.Append(name).Append("=\"").Append(pair.Value).Append('"');
                }
            }

            return builder.ToString();
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
                item.SubItems.Add(await DescribeRestrictionsAsync(nodeId).ConfigureAwait(true));
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

                bool customConfiguration = await ReadFlagAsync(roleId, BrowseNames.CustomConfiguration)
                    .ConfigureAwait(true);

                var item = new ListViewItem(name) {
                    Tag = new RoleRow {
                        Name = name,
                        NodeId = roleId,
                        CustomConfiguration = customConfiguration,
                    },
                };

                item.SubItems.Add(grantedRoles.Contains(roleId) ? "yes" : string.Empty);
                item.SubItems.Add(await DescribeEndpointsAsync(roleId).ConfigureAwait(true));
                item.SubItems.Add(customConfiguration ? "yes" : string.Empty);
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
        /// The Endpoints filter of a Role, which is evaluated before its identity rules are.
        /// </summary>
        /// <remarks>
        /// A Role with an empty list is granted on every endpoint. The sample restricts one
        /// Role to the encrypted endpoints, which is why the same account can hold different
        /// Roles depending on the Use Security box of the connect bar.
        /// </remarks>
        private async Task<string> DescribeEndpointsAsync(NodeId roleId)
        {
            NodeId endpointsId = await ResolveAsync(roleId, BrowseNames.Endpoints).ConfigureAwait(true);

            if (endpointsId.IsNull)
            {
                return "(not visible)";
            }

            DataValue value = await ReadAsync(endpointsId, Attributes.Value).ConfigureAwait(true);

            if (!StatusCode.IsGood(value.StatusCode) ||
                !value.WrappedValue.TryGetStructure(out ArrayOf<EndpointType> endpoints))
            {
                return string.Empty;
            }

            EndpointType[] entries = endpoints.ToArray();

            if (entries.Length == 0)
            {
                return "any";
            }

            return string.Join(", ", entries.Select(endpoint => endpoint.SecurityMode.ToString()).Distinct());
        }

        /// <summary>
        /// Reads a boolean Property of a Role, false when the Session may not see it.
        /// </summary>
        private async Task<bool> ReadFlagAsync(NodeId roleId, string browseName)
        {
            NodeId flagId = await ResolveAsync(roleId, browseName).ConfigureAwait(true);

            if (flagId.IsNull)
            {
                return false;
            }

            DataValue value = await ReadAsync(flagId, Attributes.Value).ConfigureAwait(true);

            return StatusCode.IsGood(value.StatusCode) &&
                value.WrappedValue.TryGetValue(out bool flag) &&
                flag;
        }

        /// <summary>
        /// The AccessRestrictions of a node, which are about the channel rather than the user.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Part 3 5.2.11. A node which demands an encrypted channel answers
        /// BadSecurityModeInsufficient in the status column of an unencrypted Session however
        /// many Roles that Session holds, which is a different fix from BadUserAccessDenied:
        /// reconnect with Use Security rather than sign in as somebody else.
        /// </para>
        /// <para>
        /// The column stays empty for exactly the nodes a Session was refused. Reading any
        /// attribute other than the Value is checked against the restrictions as well, so a
        /// Session on an unencrypted channel cannot read the attribute which would tell it
        /// that the channel is the problem. The status code is what it has to go by.
        /// </para>
        /// </remarks>
        private async Task<string> DescribeRestrictionsAsync(NodeId nodeId)
        {
            DataValue value = await ReadAsync(nodeId, Attributes.AccessRestrictions).ConfigureAwait(true);

            if (!StatusCode.IsGood(value.StatusCode))
            {
                return string.Empty;
            }

            if (!value.WrappedValue.TryGetValue(out ushort restrictions) || restrictions == 0)
            {
                return string.Empty;
            }

            return ((AccessRestrictionType)restrictions).ToString();
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
        /// Subscribes to the audit events the server reports on its Server object.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Part 18 4.4 asks a server to audit every change to its role configuration, and the
        /// stack reports a RoleMappingRuleChangedAuditEventType from the RoleSet binding for
        /// each of the Methods. The filter here accepts AuditEventType and its subtypes, so
        /// the list shows every audited operation rather than only the role ones - a client
        /// which watches the role configuration usually wants the session events beside it.
        /// </para>
        /// <para>
        /// The list stays empty against 2.0.0-preview.4: the server reports Server.Auditing
        /// as true and the Methods answer Good, but no audit event reaches a subscriber. A
        /// GeneralModelChangeEvent from the same Server object does arrive, so this is the
        /// stack rather than the subscription. The subscription is here because it is what a
        /// client is supposed to do, and it starts showing rows the moment that is fixed.
        /// </para>
        /// </remarks>
        private async Task SubscribeToAuditEventsAsync()
        {
            await DeleteSubscriptionAsync().ConfigureAwait(true);

            if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                Report("Subscribing to the audit events", StatusCodes.BadNotSupported);
                return;
            }

            m_streaming = new StreamingSubscription(manager, ClientUtils.DefaultSubscriptionOptions);
            m_cts = new CancellationTokenSource();

            // nothing is awaited here on purpose: the enumeration runs for as long as the
            // client is connected
            _ = PumpAuditEventsAsync(m_cts.Token);
        }

        /// <summary>
        /// Adds a row to the audit list for every audit event the server reports.
        /// </summary>
        private async Task PumpAuditEventsAsync(CancellationToken ct)
        {
            IStreamingSubscription streaming = m_streaming;

            var options = new MonitoredItemOptions {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(ObjectIds.Server, AuditFilter(), options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    // without a window there is nothing to update, and the enumeration keeps
                    // running rather than ending for good
                    if (!IsHandleCreated)
                    {
                        continue;
                    }

                    Variant[] fields = notification.Fields.ToArray();

                    // the enumeration runs on a publish worker, so the display is updated on
                    // the UI thread
                    BeginInvoke(new Action(() => AddAuditRowAsync(fields)));
                }
            }
            catch (OperationCanceledException)
            {
                // the client disconnected.
            }
            catch (Exception exception)
            {
                // the pump runs on a publish worker, so the error is logged instead of shown
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to read the audit events.");
            }
        }

        /// <summary>
        /// A filter which accepts AuditEventType and its subtypes.
        /// </summary>
        /// <remarks>
        /// The select clauses decide what the fields of a notification are and in which
        /// order, so the four here line up with the four columns of the audit list.
        /// </remarks>
        private static EventFilter AuditFilter()
        {
            var filter = new EventFilter {
                SelectClauses = new[] {
                    Field(BrowseNames.Time),
                    Field(BrowseNames.EventType),
                    Field(BrowseNames.SourceName),
                    Field(BrowseNames.Message),
                }.ToArrayOf(),
            };

            filter.WhereClause = new ContentFilter();
            filter.WhereClause.Push(FilterOperator.OfType, Variant.From(ObjectTypeIds.AuditEventType));

            return filter;
        }

        /// <summary>
        /// One select clause of the audit filter, named on the base event type.
        /// </summary>
        private static SimpleAttributeOperand Field(string browseName)
        {
            return new SimpleAttributeOperand {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                AttributeId = Attributes.Value,
                BrowsePath = new[] { new QualifiedName(browseName) }.ToArrayOf(),
            };
        }

        /// <summary>
        /// Shows one audit event, newest first.
        /// </summary>
        private async void AddAuditRowAsync(Variant[] fields)
        {
            try
            {
                string time = fields.Length > 0 && fields[0].TryGetValue(out DateTimeUtc reported)
                    ? reported.ToDateTime().ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture)
                    : string.Empty;

                string eventType = fields.Length > 1 && fields[1].TryGetValue(out NodeId typeId)
                    ? await NameOfAsync(typeId).ConfigureAwait(true)
                    : string.Empty;

                string source = fields.Length > 2 && fields[2].TryGetValue(out string sourceName)
                    ? sourceName
                    : string.Empty;

                string message = fields.Length > 3 && fields[3].TryGetValue(out LocalizedText text)
                    ? text.Text
                    : string.Empty;

                var item = new ListViewItem(time);

                item.SubItems.Add(eventType);
                item.SubItems.Add(source);
                item.SubItems.Add(message);

                AuditLV.Items.Insert(0, item);
            }
            catch (Exception exception)
            {
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to show an audit event.");
            }
        }

        /// <summary>
        /// The browse name of a node, cached, so the audit list can name an event type.
        /// </summary>
        private async Task<string> NameOfAsync(NodeId nodeId)
        {
            if (m_typeNames.TryGetValue(nodeId, out string known))
            {
                return known;
            }

            DataValue value = await ReadAsync(nodeId, Attributes.BrowseName).ConfigureAwait(true);

            string name = StatusCode.IsGood(value.StatusCode) &&
                value.WrappedValue.TryGetValue(out QualifiedName browseName)
                ? browseName.Name
                : nodeId.ToString();

            m_typeNames[nodeId] = name;

            return name;
        }

        /// <summary>
        /// Stops the stream and deletes the subscription on the server.
        /// </summary>
        private async Task DeleteSubscriptionAsync()
        {
            StreamingSubscription streaming = m_streaming;
            CancellationTokenSource cts = m_cts;

            m_streaming = null;
            m_cts = null;

            if (cts != null)
            {
                await cts.CancelAsync().ConfigureAwait(true);
                cts.Dispose();
            }

            if (streaming == null)
            {
                return;
            }

            try
            {
                await streaming.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                // this also runs when the session has already gone away, and then the
                // subscription cannot be deleted on the server any more
                m_telemetry?.CreateLogger<MainForm>().LogError(exception, "Failed to delete the subscription.");
            }
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
            CustomConfigBTN.Enabled = enabled;
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
                "engineer1" => "Engineer: the only account which sees the calibration.",
                "supervisor1" => "Supervisor: writes the maintenance note, over an encrypted channel.",
                "secadmin" => "SecurityAdmin: manages the RoleSet, over an encrypted channel.",
                "guest" => "No Role beyond AuthenticatedUser: sees the machine, may change nothing.",
                _ => "Anonymous: browses the machine - and with Use Security on, this workstation " +
                     "still earns ConfigureAdmin from its certificate.",
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

            /// <summary>
            /// What the CustomConfiguration Property of the Role reads, so the button which
            /// flips it knows what to write.
            /// </summary>
            public bool CustomConfiguration { get; init; }
        }
        #endregion
    }
}
