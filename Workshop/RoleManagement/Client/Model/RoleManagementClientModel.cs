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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Samples.Client;
using Opc.Ua.Security.Certificates;

namespace Quickstarts.RoleManagement.Client.Model
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
    /// One child of the machine, as the current Session sees it.
    /// </summary>
    /// <param name="Name">The browse name of the node.</param>
    /// <param name="NodeId">The node, with whatever namespace index the server assigned.</param>
    /// <param name="IsMethod">True for the Reset method, false for a variable.</param>
    /// <param name="IsText">True when the value of the variable is a string.</param>
    /// <param name="Value">The value, or empty when the Session may not read it.</param>
    /// <param name="Status">What the read answered, or empty for a method.</param>
    /// <param name="Restrictions">The AccessRestrictions of the node, or empty when there are none or the Session may not read them.</param>
    /// <param name="Permissions">The UserRolePermissions of the node, rendered as "Role: permissions".</param>
    public sealed record MachineNodeEntry(
        string Name,
        NodeId NodeId,
        bool IsMethod,
        bool IsText,
        string Value,
        string Status,
        string Restrictions,
        string Permissions);

    /// <summary>
    /// One Role of the RoleSet of the server.
    /// </summary>
    /// <param name="Name">The browse name of the Role.</param>
    /// <param name="NodeId">The Role node.</param>
    /// <param name="Granted">True when the UserRolePermissions of the machine name this Role for the Session.</param>
    /// <param name="Endpoints">The Endpoints filter of the Role: "any", the security modes it names, or "(not visible)".</param>
    /// <param name="CustomConfiguration">What the CustomConfiguration Property of the Role reads, so the button which flips it knows what to write.</param>
    /// <param name="Identities">The identity mapping rules of the Role, or why they could not be read.</param>
    public sealed record RoleEntry(
        string Name,
        NodeId NodeId,
        bool Granted,
        string Endpoints,
        bool CustomConfiguration,
        string Identities);

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
    /// One audit event the server reported: one field per column of the audit list.
    /// </summary>
    /// <param name="TimeUtc">When the event occurred, or null when the server did not say.</param>
    /// <param name="EventTypeName">The browse name of the event type, or its node id when it could not be read.</param>
    /// <param name="SourceName">The name of the source of the event.</param>
    /// <param name="Message">The message of the event.</param>
    public sealed record AuditEventEntry(
        DateTime? TimeUtc,
        string EventTypeName,
        string SourceName,
        string Message);

    /// <summary>
    /// The payload of <see cref="RoleManagementClientModel.AuditEventReceived"/>.
    /// </summary>
    public sealed class AuditEventReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public AuditEventReceivedEventArgs(AuditEventEntry entry)
        {
            Event = entry;
        }

        /// <summary>
        /// The audit event.
        /// </summary>
        public AuditEventEntry Event { get; }
    }

    /// <summary>
    /// The client model of the OPC UA Part 18 role management Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model is a three-part demonstration of what a Role is worth. The first part is
    /// the machine of the sample server as the current Session sees it: the nodes it may
    /// browse, the values it may read, the AccessRestrictions each of them carries, and what
    /// its UserRolePermissions say it may do with them. Signing in as a different account and
    /// connecting again changes all of it, without a line of client code knowing which
    /// account is which - and so does clearing the Use Security box, because two of the
    /// nodes are restricted to an encrypted channel and one Role is restricted to the
    /// encrypted endpoints.
    /// </para>
    /// <para>
    /// The second part is the RoleSet the server publishes below
    /// Server/ServerCapabilities. Its Methods are the Part 18 4.2/4.4 role configuration
    /// API: a Session which holds the SecurityAdmin Role, over an encrypted channel, can
    /// create a Role, grant it to a user name or to the certificate of a client application,
    /// and set the CustomConfiguration flag, all while everybody else stays connected. Every
    /// operation is offered to every account, because seeing the server answer
    /// BadUserAccessDenied or BadSecurityModeInsufficient is the point - which is why the
    /// operations answer an <see cref="OperationResult"/> instead of throwing on a bad status.
    /// </para>
    /// <para>
    /// The third part is the audit trail the server reports for those changes, streamed
    /// through <see cref="AuditEventReceived"/> for as long as the model is attached. It
    /// stays empty against 2.0.0-preview.4 - see <see cref="PumpAuditEventsAsync"/>.
    /// </para>
    /// </remarks>
    public sealed class RoleManagementClientModel : SampleClientModel
    {
        /// <summary>
        /// The account which opens an anonymous Session.
        /// </summary>
        public const string Anonymous = "Anonymous";

        /// <summary>
        /// The account <see cref="CriteriaOfAsync"/> offers for a UserName rule.
        /// </summary>
        public const string DefaultUserCriteria = "guest";

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

        /// <summary>
        /// The three Part 18 4.4.3 identity criteria this sample can produce.
        /// </summary>
        /// <remarks>
        /// UserName is what the server maps its demonstration accounts with; the other two
        /// are matched against the application instance certificate this client sends in
        /// CreateSession, which is why <see cref="CriteriaOfAsync"/> can fill them in from
        /// the client's own configuration.
        /// </remarks>
        public static IReadOnlyList<IdentityCriteriaType> CriteriaTypes { get; } = new[] {
            IdentityCriteriaType.UserName,
            IdentityCriteriaType.Thumbprint,
            IdentityCriteriaType.X509Subject,
        };

        /// <summary>
        /// The order Part 18 4.4.3 puts the parts of an X509Subject criteria in.
        /// </summary>
        private static readonly string[] s_subjectNameOrder =
            { "CN", "O", "OU", "DC", "L", "S", "C", "dnQualifier", "serialNumber" };

        private RoleManagementSnapshot m_snapshot = RoleManagementSnapshot.Empty;
        private StreamingSubscription m_streaming;
        private SubscriptionPump m_pump;

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
        /// Raised for every audit event the server reports while the model is attached.
        /// </summary>
        public event EventHandler<AuditEventReceivedEventArgs> AuditEventReceived;

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
                "engineer1" => "Engineer: the only account which sees the calibration.",
                "supervisor1" => "Supervisor: writes the maintenance note, over an encrypted channel.",
                "secadmin" => "SecurityAdmin: manages the RoleSet, over an encrypted channel.",
                "guest" => "No Role beyond AuthenticatedUser: sees the machine, may change nothing.",
                _ => "Anonymous: browses the machine - and with Use Security on, this workstation " +
                     "still earns ConfigureAdmin from its certificate.",
            };
        }

        /// <summary>
        /// Turns the subject name of a certificate into the Part 18 4.4.3 X509Subject form.
        /// </summary>
        /// <remarks>
        /// The grammar is <c>Name="Value"</c> pairs separated by slashes, in the fixed order
        /// CN, O, OU, DC, L, S, C, dnQualifier, serialNumber, with names outside that set
        /// dropped. A certificate reports its subject comma separated and in its own order,
        /// so the two are only comparable after this. Done here because the stack keeps its
        /// own normalisation internal.
        /// </remarks>
        /// <param name="subject">The subject as the certificate reports it.</param>
        public static string Part18Subject(string subject)
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

            foreach (string name in s_subjectNameOrder)
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
        /// A criteria string of the given type which this client could be matched by.
        /// </summary>
        /// <remarks>
        /// The two certificate criteria are matched against the application instance
        /// certificate of the <b>client</b>, so this client can fill them in from its own
        /// configuration: a Thumbprint has to be upper case hexadecimal without separators,
        /// and an X509Subject the normalised form of <see cref="Part18Subject"/>. Granting a
        /// Role for one of them and reconnecting is how the sample shows a Role which belongs
        /// to a machine rather than to a person. Needs no session.
        /// </remarks>
        /// <param name="criteriaType">One of the <see cref="CriteriaTypes"/>.</param>
        /// <param name="securityConfiguration">The security configuration of the client, which holds its certificate.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The criteria, or empty when the client has no certificate.</returns>
        public async Task<string> CriteriaOfAsync(
            IdentityCriteriaType criteriaType,
            SecurityConfiguration securityConfiguration,
            CancellationToken ct = default)
        {
            if (criteriaType == IdentityCriteriaType.UserName)
            {
                return DefaultUserCriteria;
            }

            ArgumentNullException.ThrowIfNull(securityConfiguration);

            using Certificate certificate = await securityConfiguration
                .FindApplicationCertificateAsync(SecurityPolicies.Basic256Sha256, false, Telemetry, ct)
                .ConfigureAwait(false);

            if (certificate == null)
            {
                return string.Empty;
            }

            return criteriaType == IdentityCriteriaType.Thumbprint
                ? certificate.Thumbprint.ToUpperInvariant()
                : Part18Subject(certificate.Subject);
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

            StatusCode status = await WriteValueAsync(session, node.NodeId, value, ct).ConfigureAwait(false);

            return new OperationResult(what, status);
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
        /// Grants a Role to an identity.
        /// </summary>
        /// <param name="role">The Role, from the last <see cref="Snapshot"/>.</param>
        /// <param name="criteriaType">What kind of identity the rule names.</param>
        /// <param name="criteria">The account, the thumbprint or the normalised subject the rule names.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> AddIdentityAsync(
            RoleEntry role,
            IdentityCriteriaType criteriaType,
            string criteria,
            CancellationToken ct = default)
        {
            return ChangeIdentityAsync(role, BrowseNames.AddIdentity, criteriaType, criteria, ct);
        }

        /// <summary>
        /// Revokes a Role from an identity.
        /// </summary>
        /// <param name="role">The Role, from the last <see cref="Snapshot"/>.</param>
        /// <param name="criteriaType">What kind of identity the rule names.</param>
        /// <param name="criteria">The account, the thumbprint or the normalised subject the rule names.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> RemoveIdentityAsync(
            RoleEntry role,
            IdentityCriteriaType criteriaType,
            string criteria,
            CancellationToken ct = default)
        {
            return ChangeIdentityAsync(role, BrowseNames.RemoveIdentity, criteriaType, criteria, ct);
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

        /// <summary>
        /// Writes the CustomConfiguration flag of a Role.
        /// </summary>
        /// <remarks>
        /// Part 18 4.4.1: a Role whose Identities list is empty is granted to nobody unless
        /// CustomConfiguration is set, and with it set the rest of the configuration - the
        /// Applications and Endpoints filters - decides on its own. Revoke the X509Subject
        /// rule of the ConfigureAdmin Role of the sample, set the flag, and every Session on
        /// the encrypted endpoint holds that Role. The window flips whatever the
        /// <see cref="RoleEntry.CustomConfiguration"/> of the last refresh read.
        /// </remarks>
        /// <param name="role">The Role, from the last <see cref="Snapshot"/>.</param>
        /// <param name="enabled">What to write.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<OperationResult> SetCustomConfigurationAsync(
            RoleEntry role,
            bool enabled,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(role);

            ISession session = RequireSession();

            NodeId flagId = await ResolveAsync(session, role.NodeId, BrowseNames.CustomConfiguration, ct)
                .ConfigureAwait(false);

            if (flagId.IsNull)
            {
                // the Property is not in the address space this Session can see
                return new OperationResult($"CustomConfiguration of {role.Name}", StatusCodes.BadUserAccessDenied);
            }

            StatusCode status = await WriteValueAsync(session, flagId, Variant.From(enabled), ct).ConfigureAwait(false);

            return new OperationResult($"CustomConfiguration of {role.Name} := {enabled}", status);
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

            StartAuditTrail(session);
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            StreamingSubscription streaming = m_streaming;
            SubscriptionPump pump = m_pump;

            m_streaming = null;
            m_pump = null;

            // the enumeration first: cancelling it removes the monitored item, and waiting
            // for it guarantees that nothing is raised after the detach returns.
            if (pump != null)
            {
                await pump.DisposeAsync().ConfigureAwait(false);
            }

            // then the subscription on the server. Done before the session is closed:
            // closing a session which still carries a subscription waits for the publish
            // pipeline to drain.
            if (streaming != null)
            {
                await streaming.DisposeAsync().ConfigureAwait(false);
            }

            MachineId = NodeId.Null;
            ResetId = NodeId.Null;
            m_snapshot = RoleManagementSnapshot.Empty;
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so the audit trail keeps running
        // and the reconnect hooks of the base class are not overridden.

        /// <summary>
        /// Subscribes to the audit events the server reports on its Server object.
        /// </summary>
        /// <remarks>
        /// The streaming subscription lives as long as the model is attached: the underlying
        /// OPC UA subscription is created when the enumeration in
        /// <see cref="PumpAuditEventsAsync"/> starts. A session which does not run the V2
        /// engine gets no trail, which is logged rather than thrown - the audit list is the
        /// least of what this sample shows.
        /// </remarks>
        private void StartAuditTrail(ISession session)
        {
            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                Logger.LogWarning("The session does not use the V2 subscription engine; the audit trail stays empty.");
                return;
            }

            var streaming = new StreamingSubscription(manager, SampleSession.DefaultSubscriptionOptions);
            var pump = new SubscriptionPump();

            m_streaming = streaming;
            m_pump = pump;

            // the pump keeps the task: the enumeration runs for as long as the model is
            // attached, and detaching waits for it to end.
            pump.Run(token => PumpAuditEventsAsync(session, streaming, token));
        }

        /// <summary>
        /// Reports every audit event the server delivers until the model detaches.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Part 18 4.4 asks a server to audit every change to its role configuration, and the
        /// stack reports a RoleMappingRuleChangedAuditEventType from the RoleSet binding for
        /// each of the Methods. The filter here accepts AuditEventType and its subtypes, so
        /// the trail carries every audited operation rather than only the role ones - a
        /// client which watches the role configuration usually wants the session events
        /// beside it.
        /// </para>
        /// <para>
        /// The trail stays empty against 2.0.0-preview.4: the server reports Server.Auditing
        /// as true and the Methods answer Good, but no audit event reaches a subscriber. A
        /// GeneralModelChangeEvent from the same Server object does arrive, so this is the
        /// stack rather than the subscription. The subscription is here because it is what a
        /// client is supposed to do, and it starts reporting the moment that is fixed.
        /// </para>
        /// <para>
        /// The loop runs on a publish worker of the engine: the lookup of the event type
        /// name happens there, and only the finished entry is handed to the base class,
        /// which posts it to the thread the model was created on.
        /// </para>
        /// </remarks>
        private async Task PumpAuditEventsAsync(
            ISession session,
            IStreamingSubscription streaming,
            CancellationToken ct)
        {
            var options = new MonitoredItemOptions {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = 1000,
                DiscardOldest = true,
            };

            // the browse names of the event types the trail has already named
            var typeNames = new Dictionary<NodeId, string>();

            try
            {
                await foreach (EventNotification notification in streaming
                    .SubscribeEventsAsync(ObjectIds.Server, AuditFilter(), options, ct)
                    .ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    AuditEventEntry entry = await ToEntryAsync(
                        session,
                        notification.Fields.ToArray(),
                        typeNames,
                        ct).ConfigureAwait(false);

                    Raise(AuditEventReceived, new AuditEventReceivedEventArgs(entry));
                }
            }
            catch (OperationCanceledException)
            {
                // the model detached.
            }
            catch (Exception exception)
            {
                // the pump has no caller to throw to, so the failure goes through the
                // error channel of the model.
                ReportError("Reading the audit events", exception);
            }
        }

        /// <summary>
        /// A filter which accepts AuditEventType and its subtypes.
        /// </summary>
        /// <remarks>
        /// The select clauses decide what the fields of a notification are and in which
        /// order, so the four here line up with the four fields of an
        /// <see cref="AuditEventEntry"/>.
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
        /// Turns the fields of one audit event into the entry the client displays.
        /// </summary>
        private static async Task<AuditEventEntry> ToEntryAsync(
            ISession session,
            Variant[] fields,
            Dictionary<NodeId, string> typeNames,
            CancellationToken ct)
        {
            DateTime? time = fields.Length > 0 && fields[0].TryGetValue(out DateTimeUtc reported)
                ? reported.ToDateTime()
                : null;

            string eventType = fields.Length > 1 && fields[1].TryGetValue(out NodeId typeId)
                ? await NameOfAsync(session, typeId, typeNames, ct).ConfigureAwait(false)
                : string.Empty;

            string source = fields.Length > 2 && fields[2].TryGetValue(out string sourceName)
                ? sourceName
                : string.Empty;

            string message = fields.Length > 3 && fields[3].TryGetValue(out LocalizedText text)
                ? text.Text
                : string.Empty;

            return new AuditEventEntry(time, eventType, source, message);
        }

        /// <summary>
        /// The browse name of a node, cached, so the trail can name an event type.
        /// </summary>
        private static async Task<string> NameOfAsync(
            ISession session,
            NodeId nodeId,
            Dictionary<NodeId, string> typeNames,
            CancellationToken ct)
        {
            if (typeNames.TryGetValue(nodeId, out string known))
            {
                return known;
            }

            DataValue value = await ReadAsync(session, nodeId, Attributes.BrowseName, ct).ConfigureAwait(false);

            string name = StatusCode.IsGood(value.StatusCode) &&
                value.WrappedValue.TryGetValue(out QualifiedName browseName)
                ? browseName.Name
                : nodeId.ToString();

            typeNames[nodeId] = name;

            return name;
        }

        /// <summary>
        /// Calls AddIdentity or RemoveIdentity on a Role.
        /// </summary>
        /// <remarks>
        /// Both Methods take one IdentityMappingRuleType, whose criteria type the window's
        /// drop down picks: a UserName rule names an account, a Thumbprint or an X509Subject
        /// rule names the certificate a client application presents. The Methods live on the
        /// Role node itself, and the standard address space only lets a SecurityAdmin browse
        /// to them, so a Session which is refused the change is usually refused the browse as
        /// well.
        /// </remarks>
        private async Task<OperationResult> ChangeIdentityAsync(
            RoleEntry role,
            string methodName,
            IdentityCriteriaType criteriaType,
            string criteria,
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
                CriteriaType = criteriaType,
                Criteria = criteria,
            };

            CallMethodResult result = await CallAsync(
                session,
                role.NodeId,
                methodId,
                ct,
                Variant.From(new ExtensionObject(rule))).ConfigureAwait(false);

            return new OperationResult($"{methodName}({criteriaType}='{criteria}') on {role.Name}", result.StatusCode);
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
                    await DescribeRestrictionsAsync(session, nodeId, ct).ConfigureAwait(false),
                    DescribePermissions(permissions, roleNames)));
            }

            return nodes;
        }

        /// <summary>
        /// Lists the Roles of the RoleSet, their filters and their identity rules.
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
                    await DescribeEndpointsAsync(session, roleId, ct).ConfigureAwait(false),
                    await ReadFlagAsync(session, roleId, BrowseNames.CustomConfiguration, ct).ConfigureAwait(false),
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
        /// The Endpoints filter of a Role, which is evaluated before its identity rules are.
        /// </summary>
        /// <remarks>
        /// A Role with an empty list is granted on every endpoint. The sample restricts one
        /// Role to the encrypted endpoints, which is why the same account can hold different
        /// Roles depending on the Use Security box of the connect bar.
        /// </remarks>
        private static async Task<string> DescribeEndpointsAsync(ISession session, NodeId roleId, CancellationToken ct)
        {
            NodeId endpointsId = await ResolveAsync(session, roleId, BrowseNames.Endpoints, ct).ConfigureAwait(false);

            if (endpointsId.IsNull)
            {
                return "(not visible)";
            }

            DataValue value = await ReadAsync(session, endpointsId, Attributes.Value, ct).ConfigureAwait(false);

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
        private static async Task<bool> ReadFlagAsync(
            ISession session,
            NodeId roleId,
            string browseName,
            CancellationToken ct)
        {
            NodeId flagId = await ResolveAsync(session, roleId, browseName, ct).ConfigureAwait(false);

            if (flagId.IsNull)
            {
                return false;
            }

            DataValue value = await ReadAsync(session, flagId, Attributes.Value, ct).ConfigureAwait(false);

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
        /// BadSecurityModeInsufficient in the status of an unencrypted Session however many
        /// Roles that Session holds, which is a different fix from BadUserAccessDenied:
        /// reconnect with Use Security rather than sign in as somebody else.
        /// </para>
        /// <para>
        /// The text stays empty for exactly the nodes a Session was refused. Reading any
        /// attribute other than the Value is checked against the restrictions as well, so a
        /// Session on an unencrypted channel cannot read the attribute which would tell it
        /// that the channel is the problem. The status code is what it has to go by.
        /// </para>
        /// </remarks>
        private static async Task<string> DescribeRestrictionsAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            DataValue value = await ReadAsync(session, nodeId, Attributes.AccessRestrictions, ct).ConfigureAwait(false);

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
        /// Writes the Value of a node without throwing on a bad status code.
        /// </summary>
        private static async Task<StatusCode> WriteValueAsync(
            ISession session,
            NodeId nodeId,
            Variant value,
            CancellationToken ct)
        {
            var valuesToWrite = new List<WriteValue> {
                new WriteValue {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value),
                },
            };

            WriteResponse response = await session
                .WriteAsync(null, valuesToWrite, ct)
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
