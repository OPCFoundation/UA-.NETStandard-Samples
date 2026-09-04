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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Samples.Client;

namespace Quickstarts.RoleManagement.Client.Model
{
    // The source generator emits a Quickstarts.RoleManagement.BrowseNames and ObjectIds for
    // the model of the server. This namespace is a child of that one, so those would win over
    // the standard sets of the same name: both are named apart here.
    using ModelNames = Quickstarts.RoleManagement.BrowseNames;
    using BrowseNames = Opc.Ua.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;
    using MethodIds = Opc.Ua.MethodIds;

    /// <summary>
    /// One child of the machine, as the current Session sees it.
    /// </summary>
    /// <param name="Name">The browse name of the node.</param>
    /// <param name="NodeId">The node, with whatever namespace index the server assigned.</param>
    /// <param name="IsMethod">True for the Reset method, false for a variable.</param>
    /// <param name="IsText">True when the value of the variable is a string.</param>
    /// <param name="Value">The value, or empty when the Session may not read it.</param>
    /// <param name="Status">What the read answered, or empty for a method.</param>
    /// <param name="Permissions">The UserRolePermissions of the node, rendered as "Role: permissions".</param>
    public sealed record MachineNodeEntry(
        string Name,
        NodeId NodeId,
        bool IsMethod,
        bool IsText,
        string Value,
        string Status,
        string Permissions);

    /// <summary>
    /// One Role of the RoleSet of the server.
    /// </summary>
    /// <param name="Name">The browse name of the Role.</param>
    /// <param name="NodeId">The Role node.</param>
    /// <param name="Granted">True when the UserRolePermissions of the machine name this Role for the Session.</param>
    /// <param name="Identities">The identity mapping rules of the Role, or why they could not be read.</param>
    public sealed record RoleEntry(string Name, NodeId NodeId, bool Granted, string Identities);

    /// <summary>
    /// What one refresh found: the machine and the RoleSet, both as the Session sees them.
    /// </summary>
    /// <param name="Nodes">The children of the machine the Session may browse.</param>
    /// <param name="Roles">The Roles of the RoleSet.</param>
    public sealed record RoleManagementSnapshot(
        IReadOnlyList<MachineNodeEntry> Nodes,
        IReadOnlyList<RoleEntry> Roles)
    {
        /// <summary>
        /// A snapshot with nothing in it, for a model which is detached.
        /// </summary>
        public static RoleManagementSnapshot Empty { get; } = new RoleManagementSnapshot(
            Array.Empty<MachineNodeEntry>(),
            Array.Empty<RoleEntry>());
    }

    /// <summary>
    /// The client model of the OPC UA Part 18 role management Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model is a two-part demonstration of what a Role is worth. The first part is the
    /// machine of the sample server as the current Session sees it: the nodes it may browse,
    /// the values it may read, and what its UserRolePermissions say it may do with each of
    /// them. Signing in as a different account and connecting again changes all three,
    /// without a line of client code knowing which account is which.
    /// </para>
    /// <para>
    /// The second part is the RoleSet the server publishes below
    /// Server/ServerCapabilities. Its Methods are the Part 18 4.2/4.4 role configuration
    /// API: a Session which holds the SecurityAdmin Role, over an encrypted channel, can
    /// create a Role and grant it to a user while everybody else stays connected. Every
    /// operation is offered to every account, because seeing the server answer
    /// BadUserAccessDenied or BadSecurityModeInsufficient is the point - which is why the
    /// operations answer an <see cref="OperationResult"/> instead of throwing on a bad status.
    /// </para>
    /// </remarks>
    public sealed class RoleManagementClientModel : SampleClientModel
    {
        /// <summary>
        /// The account which opens an anonymous Session.
        /// </summary>
        public const string Anonymous = "Anonymous";

        /// <summary>
        /// The accounts the sample server knows, and which the window offers in its drop
        /// down. The client has no idea what any of them means - it only picks the identity
        /// token and lets the server decide what the Session is worth.
        /// </summary>
        public static IReadOnlyList<string> Accounts { get; } = new[] {
            Anonymous,
            "observer1",
            "operator1",
            "engineer1",
            "supervisor1",
            "secadmin",
            "guest",
        };

        private RoleManagementSnapshot m_snapshot = RoleManagementSnapshot.Empty;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public RoleManagementClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The machine of the sample server, or null when the server does not serve it.
        /// </summary>
        public NodeId MachineId { get; private set; } = NodeId.Null;

        /// <summary>
        /// The Reset method of the machine, as this Session browsed it, or null when the
        /// Session may not see it.
        /// </summary>
        public NodeId ResetId { get; private set; } = NodeId.Null;

        /// <summary>
        /// What the last <see cref="RefreshAsync"/> found.
        /// </summary>
        public RoleManagementSnapshot Snapshot => m_snapshot;

        /// <summary>
        /// The identity token which signs in as one of the <see cref="Accounts"/>.
        /// </summary>
        /// <remarks>
        /// The whole of the identity handling of this client is this one method. Everything
        /// the rest of the sample shows follows from which token was sent, because the server
        /// resolves the Roles of the Session from it. The password of every sample account
        /// is its user name.
        /// </remarks>
        /// <param name="account">One of the <see cref="Accounts"/>.</param>
        /// <returns>The identity, or null for an anonymous Session.</returns>
        public static IUserIdentity IdentityFor(string account)
        {
            return string.IsNullOrEmpty(account) || string.Equals(account, Anonymous, StringComparison.Ordinal)
                ? null
                : new UserIdentity(account, Encoding.UTF8.GetBytes(account));
        }

        /// <summary>
        /// Explains what an account is expected to be able to do.
        /// </summary>
        /// <param name="account">One of the <see cref="Accounts"/>.</param>
        public static string HintFor(string account)
        {
            return account switch {
                "observer1" => "Observer: reads the temperature and the set point.",
                "operator1" => "Operator: writes the set point and calls Reset.",
                "engineer1" => "Engineer: the only Role which sees the calibration.",
                "supervisor1" => "Supervisor: writes the maintenance note.",
                "secadmin" => "SecurityAdmin: manages the RoleSet, over an encrypted channel.",
                "guest" => "No Role beyond AuthenticatedUser: sees the machine, may change nothing.",
                _ => "Anonymous: browses the machine, and is refused every value.",
            };
        }

        /// <summary>
        /// Reads the machine and the RoleSet again.
        /// </summary>
        /// <remarks>
        /// The RoleSet is read before the children of the machine, so that the permissions
        /// of each node are described with the names the Roles have right now rather than
        /// with the ones of the previous refresh.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>What was found, which is also kept in <see cref="Snapshot"/>.</returns>
        public async Task<RoleManagementSnapshot> RefreshAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            ResetId = NodeId.Null;

            IReadOnlyList<NodeId> granted = MachineId.IsNull
                ? Array.Empty<NodeId>()
                : (await ReadPermissionsAsync(session, MachineId, ct).ConfigureAwait(false))
                    .Select(permission => permission.RoleId)
                    .ToList();

            IReadOnlyList<RoleEntry> roles = await LoadRolesAsync(session, granted, ct).ConfigureAwait(false);

            var roleNames = new Dictionary<NodeId, string>();

            foreach (RoleEntry role in roles)
            {
                roleNames[role.NodeId] = role.Name;
            }

            IReadOnlyList<MachineNodeEntry> nodes = await LoadMachineAsync(session, roleNames, ct).ConfigureAwait(false);

            m_snapshot = new RoleManagementSnapshot(nodes, roles);

            return m_snapshot;
        }

        /// <summary>
        /// Writes a value to one of the variables of the machine.
        /// </summary>
        /// <param name="node">The variable, from the last <see cref="Snapshot"/>.</param>
        /// <param name="text">The value as the user typed it: a number, or a text for a string variable.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> WriteAsync(MachineNodeEntry node, string text, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(node);

            ISession session = RequireSession();
            string what = $"Writing {node.Name}";

            if (node.IsMethod)
            {
                return new OperationResult(what, StatusCodes.BadAttributeIdInvalid);
            }

            double number = 0;

            if (!node.IsText &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
            {
                return new OperationResult(what, StatusCodes.BadTypeMismatch);
            }

            Variant value = node.IsText
                ? Variant.From(text)
                : Variant.From(number);

            var valuesToWrite = new List<WriteValue> {
                new WriteValue {
                    NodeId = node.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value),
                },
            };

            WriteResponse response = await session
                .WriteAsync(null, valuesToWrite, ct)
                .ConfigureAwait(false);

            return new OperationResult(what, response.Results.ToArray()[0]);
        }

        /// <summary>
        /// Calls the Reset method of the machine.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> ResetAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();
            const string what = "Calling Reset";

            if (MachineId.IsNull)
            {
                return new OperationResult(what, StatusCodes.BadNodeIdUnknown);
            }

            if (ResetId.IsNull)
            {
                // the method is not in the address space this Session can see, so it was
                // not granted Browse on it - which is a refusal in its own right
                return new OperationResult(what, StatusCodes.BadUserAccessDenied);
            }

            CallMethodResult result = await CallAsync(session, MachineId, ResetId, ct).ConfigureAwait(false);

            return new OperationResult(what, result.StatusCode);
        }

        /// <summary>
        /// Grants a Role to a user.
        /// </summary>
        /// <param name="role">The Role, from the last <see cref="Snapshot"/>.</param>
        /// <param name="userName">The user name the identity mapping rule names.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> AddIdentityAsync(RoleEntry role, string userName, CancellationToken ct = default)
        {
            return ChangeIdentityAsync(role, BrowseNames.AddIdentity, userName, ct);
        }

        /// <summary>
        /// Revokes a Role from a user.
        /// </summary>
        /// <param name="role">The Role, from the last <see cref="Snapshot"/>.</param>
        /// <param name="userName">The user name the identity mapping rule names.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> RemoveIdentityAsync(RoleEntry role, string userName, CancellationToken ct = default)
        {
            return ChangeIdentityAsync(role, BrowseNames.RemoveIdentity, userName, ct);
        }

        /// <summary>
        /// Adds a Role of the server's own to the RoleSet.
        /// </summary>
        /// <param name="roleName">The name of the new Role.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> AddRoleAsync(string roleName, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            CallMethodResult result = await CallAsync(
                session,
                ObjectIds.Server_ServerCapabilities_RoleSet,
                MethodIds.Server_ServerCapabilities_RoleSet_AddRole,
                ct,
                Variant.From(roleName),

                // no namespace uri: the server puts the Role in the namespace it
                // allocates dynamic Roles in, rather than into one which holds a model,
                // where it would take the node id of an existing node
                // (UA-.NETStandard#4361)
                Variant.From(string.Empty)).ConfigureAwait(false);

            return new OperationResult($"Adding the role '{roleName}'", result.StatusCode);
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // this client has built-in knowledge of the information model of the server
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.RoleManagement);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                "1:" + ModelNames.Machine).ConfigureAwait(false);

            MachineId = nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            // nothing was created on the server: this client only reads, writes and calls
            MachineId = NodeId.Null;
            ResetId = NodeId.Null;
            m_snapshot = RoleManagementSnapshot.Empty;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Calls AddIdentity or RemoveIdentity on a Role.
        /// </summary>
        /// <remarks>
        /// Both Methods take one IdentityMappingRuleType, and a UserName rule is the one
        /// this sample server is configured with. The Methods live on the Role node itself,
        /// and the standard address space only lets a SecurityAdmin browse to them, so a
        /// Session which is refused the change is usually refused the browse as well.
        /// </remarks>
        private async Task<OperationResult> ChangeIdentityAsync(
            RoleEntry role,
            string methodName,
            string userName,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(role);

            ISession session = RequireSession();

            NodeId methodId = await ResolveAsync(session, role.NodeId, methodName, ct).ConfigureAwait(false);

            if (methodId.IsNull)
            {
                return new OperationResult($"{methodName} on {role.Name}", StatusCodes.BadUserAccessDenied);
            }

            var rule = new IdentityMappingRuleType {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = userName,
            };

            CallMethodResult result = await CallAsync(
                session,
                role.NodeId,
                methodId,
                ct,
                Variant.From(new ExtensionObject(rule))).ConfigureAwait(false);

            return new OperationResult($"{methodName}('{userName}') on {role.Name}", result.StatusCode);
        }

        /// <summary>
        /// Lists the children of the machine this Session may browse.
        /// </summary>
        /// <remarks>
        /// The list is built from a Browse rather than from a hard coded set of nodes, which
        /// is what makes the effect of the Browse permission visible: two Sessions on the
        /// same server come back with a different number of entries.
        /// </remarks>
        private async Task<IReadOnlyList<MachineNodeEntry>> LoadMachineAsync(
            ISession session,
            IReadOnlyDictionary<NodeId, string> roleNames,
            CancellationToken ct)
        {
            var nodes = new List<MachineNodeEntry>();

            if (MachineId.IsNull)
            {
                return nodes;
            }

            var browse = new BrowseDescription {
                NodeId = MachineId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Variable | NodeClass.Method),
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, ct)
                .ConfigureAwait(false);

            foreach (ReferenceDescription reference in response.Results.ToArray()[0].References.ToArray())
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                string name = reference.BrowseName.Name;
                bool isMethod = reference.NodeClass == NodeClass.Method;

                // The node ids of the model come from this browse rather than from a browse
                // path, so the namespace index is whatever the server assigned and the client
                // never has to guess it. Comparing the browse name is safe here because the
                // browse is scoped to the children of the machine.
                if (isMethod && string.Equals(name, ModelNames.Reset, StringComparison.Ordinal))
                {
                    ResetId = nodeId;
                }

                bool isText = false;
                string value = string.Empty;
                string status = string.Empty;

                if (!isMethod)
                {
                    DataValue read = await ReadAsync(session, nodeId, Attributes.Value, ct).ConfigureAwait(false);

                    // a refused read carries no value, so there is no type to look at
                    isText = StatusCode.IsGood(read.StatusCode)
                        && read.WrappedValue.TypeInfo.BuiltInType == BuiltInType.String;
                    value = StatusCode.IsGood(read.StatusCode) ? read.WrappedValue.ToString() : string.Empty;
                    status = read.StatusCode.ToString();
                }

                IReadOnlyList<RolePermissionType> permissions =
                    await ReadPermissionsAsync(session, nodeId, ct).ConfigureAwait(false);

                nodes.Add(new MachineNodeEntry(
                    name,
                    nodeId,
                    isMethod,
                    isText,
                    value,
                    status,
                    DescribePermissions(permissions, roleNames)));
            }

            return nodes;
        }

        /// <summary>
        /// Lists the Roles of the RoleSet and their identity rules.
        /// </summary>
        private static async Task<IReadOnlyList<RoleEntry>> LoadRolesAsync(
            ISession session,
            IReadOnlyList<NodeId> grantedRoles,
            CancellationToken ct)
        {
            var roles = new List<RoleEntry>();

            var browse = new BrowseDescription {
                NodeId = ObjectIds.Server_ServerCapabilities_RoleSet,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, ct)
                .ConfigureAwait(false);

            foreach (ReferenceDescription reference in response.Results.ToArray()[0].References.ToArray())
            {
                NodeId roleId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);

                roles.Add(new RoleEntry(
                    reference.BrowseName.Name,
                    roleId,
                    grantedRoles.Contains(roleId),
                    await DescribeIdentitiesAsync(session, roleId, ct).ConfigureAwait(false)));
            }

            return roles;
        }

        /// <summary>
        /// The identity mapping rules of a Role, or why they could not be read.
        /// </summary>
        private static async Task<string> DescribeIdentitiesAsync(ISession session, NodeId roleId, CancellationToken ct)
        {
            NodeId identitiesId = await ResolveAsync(session, roleId, BrowseNames.Identities, ct).ConfigureAwait(false);

            if (identitiesId.IsNull)
            {
                // the standard address space reserves the Role nodes for the SecurityAdmin
                // Role, so an ordinary Session cannot even browse to the property
                return "(not visible to this session)";
            }

            DataValue value = await ReadAsync(session, identitiesId, Attributes.Value, ct).ConfigureAwait(false);

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
        private static async Task<IReadOnlyList<RolePermissionType>> ReadPermissionsAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct)
        {
            DataValue value = await ReadAsync(session, nodeId, Attributes.UserRolePermissions, ct).ConfigureAwait(false);

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
        private static string DescribePermissions(
            IReadOnlyList<RolePermissionType> permissions,
            IReadOnlyDictionary<NodeId, string> roleNames)
        {
            return string.Join(
                "; ",
                permissions.Select(permission => {
                    string name = roleNames.TryGetValue(permission.RoleId, out string known)
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
        /// <param name="session">The session to ask.</param>
        /// <param name="startingNode">The node to start at.</param>
        /// <param name="browseName">A browse name in namespace zero.</param>
        /// <param name="ct">The cancellation token.</param>
        private static async Task<NodeId> ResolveAsync(
            ISession session,
            NodeId startingNode,
            string browseName,
            CancellationToken ct)
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

            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, new List<BrowsePath> { browsePath }, ct)
                .ConfigureAwait(false);

            BrowsePathResult result = response.Results.ToArray()[0];

            if (StatusCode.IsBad(result.StatusCode) || result.Targets.Count == 0)
            {
                return NodeId.Null;
            }

            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
        }

        /// <summary>
        /// Reads one attribute of a node without throwing on a bad status code.
        /// </summary>
        /// <remarks>
        /// The bad status codes are what this client is here to show, so they have to arrive
        /// as values it can put in a column rather than as exceptions.
        /// </remarks>
        private static async Task<DataValue> ReadAsync(
            ISession session,
            NodeId nodeId,
            uint attributeId,
            CancellationToken ct)
        {
            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = attributeId },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Calls a method without throwing on a bad status code.
        /// </summary>
        private static async Task<CallMethodResult> CallAsync(
            ISession session,
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct,
            params Variant[] inputArguments)
        {
            var request = new CallMethodRequest {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = (inputArguments ?? Array.Empty<Variant>()).ToArrayOf(),
            };

            CallResponse response = await session
                .CallAsync(null, new List<CallMethodRequest> { request }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }
    }
}
