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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.RoleManagement.Server
{
    /// <summary>
    /// One demonstration account of the sample.
    /// </summary>
    /// <param name="UserName">The name the client signs in with.</param>
    /// <param name="Password">The password of the account. Sample accounts are their own password.</param>
    /// <param name="Role">
    /// The well known Role the server grants the account, or <c>null</c> for an account which
    /// authenticates but is granted nothing beyond the AuthenticatedUser Role every signed in
    /// Session holds.
    /// </param>
    public sealed record SampleUser(string UserName, string Password, Role Role);

    /// <summary>
    /// A Quickstart server which demonstrates OPC UA Part 18 role based security.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interesting part of this sample is not its address space, which is a single object
    /// with four variables and a method. It is who may do what with them, and how that is
    /// configured:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///   <see cref="CreateRoleManager"/> maps the demonstration accounts onto the well known
    ///   Roles of OPC UA Part 3 4.9.2 by adding identity mapping rules to the Role manager.
    ///   The stack does the rest: when a Session activates, the session manager asks
    ///   <see cref="IRoleManager.ResolveGrantedRoles"/> which Roles the identity earns and
    ///   wraps the identity in a <see cref="RoleBasedIdentity"/> carrying them.
    ///   </item>
    ///   <item>
    ///   The node manager puts RolePermissions on the nodes, so the master node manager
    ///   answers BadUserAccessDenied to a Session whose granted Roles do not carry the
    ///   permission a service needs, and AccessRestrictions on two of them, which the same
    ///   node manager checks against the channel rather than against the Session.
    ///   </item>
    ///   <item>
    ///   Not every Role is earned by signing in. The ConfigureAdmin Role of this sample is
    ///   granted for the certificate the client application presents and only on an
    ///   encrypted endpoint, which is the Part 18 4.4.3 X509Subject criteria and the
    ///   4.4.1 Endpoints filter respectively.
    ///   </item>
    ///   <item>
    ///   The RoleSet object below Server/ServerCapabilities is bound to the same Role manager
    ///   by the stack, so a client holding the SecurityAdmin Role can change all of the above
    ///   at runtime over an encrypted channel - and the stack re-evaluates the Roles of every
    ///   open Session as Part 18 4.4.1 requires.
    ///   </item>
    /// </list>
    /// <para>
    /// The sample deliberately keeps to the well known Roles for its node permissions. The
    /// nine well known Roles are part of the standard address space, so each of them has a
    /// RoleType node below the RoleSet that a client can browse and manage. A Role created on
    /// the Role manager during startup would be honoured for access control but would have no
    /// node, because the stack materializes a node only for a Role created through the
    /// RoleSet AddRole Method. Creating one that way is what the sample client demonstrates.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample server type name intentionally mirrors the namespace.")]
    public partial class RoleManagementServer : StandardServer
    {
        /// <summary>
        /// Creates the server. Its node manager is registered with the host by the hosting composition root of the sample.
        /// </summary>
        public RoleManagementServer(ITelemetryContext telemetry) : base(telemetry)
        {
        }

        #region Demonstration Accounts
        /// <summary>
        /// The accounts the sample accepts, and the well known Role each of them is granted.
        /// </summary>
        /// <remarks>
        /// A real server keeps its accounts in a user database and its Role mappings in an
        /// administered store. Both live in this one table here so that a reader can see in
        /// a single place which user ends up with which Role, and the sample client can offer
        /// the accounts in a drop down without having to guess them.
        /// </remarks>
        public static IReadOnlyList<SampleUser> Users { get; } = new SampleUser[]
        {
            new SampleUser("observer1", "observer1", Role.Observer),
            new SampleUser("operator1", "operator1", Role.Operator),
            new SampleUser("engineer1", "engineer1", Role.Engineer),
            new SampleUser("supervisor1", "supervisor1", Role.Supervisor),
            new SampleUser("secadmin", "secadmin", Role.SecurityAdmin),

            // authenticates, but earns no Role beyond AuthenticatedUser: the account which
            // shows what a Session sees when no RolePermission on a node names one of its Roles
            new SampleUser("guest", "guest", null),
        };
        #endregion

        #region The Maintenance Workstation
        /// <summary>
        /// The subject name of the application instance certificate the sample client
        /// creates for itself, in the Part 18 4.4.3 form the X509Subject criteria uses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The certificate the Role manager matches against is the <b>application instance
        /// certificate of the client</b> - the one the client sends in CreateSession - not a
        /// user certificate. That is worth saying out loud, because the criteria is named
        /// after X.509 and Part 18 4.4.3 allows either reading: a Role granted this way
        /// belongs to the software on that workstation, and every Session it opens holds it,
        /// signed in or not.
        /// </para>
        /// <para>
        /// The criteria is a normalised subject: <c>Name="Value"</c> pairs separated by
        /// slashes, in the order CN, O, OU, DC, L, S, C, whatever order the certificate
        /// itself carries them in. The DC is the host name because that is what the stack
        /// substitutes for <c>DC=localhost</c> when it creates a certificate from a
        /// configured subject name, so this only matches a client which runs on the same
        /// machine as the server - which is what a Quickstart does.
        /// </para>
        /// <para>
        /// A real server would not hard code this. It would hold the subject of every
        /// workstation it trusts in the same administered store as its Role mappings, or let
        /// a SecurityAdmin add one through the AddIdentity Method of the RoleSet, which is
        /// what the sample client's identity criteria drop down does.
        /// </para>
        /// </remarks>
        public static string WorkstationCertificateSubject { get; } =
            "CN=\"Quickstart RoleManagement Client\"" +
            "/O=\"OPC Foundation\"" +
            $"/DC=\"{Utils.GetHostName()}\"" +
            "/S=\"Arizona\"" +
            "/C=\"US\"";

        /// <summary>
        /// The Role the maintenance workstation earns, and the only Role of the sample which
        /// belongs to a machine rather than to a person.
        /// </summary>
        /// <remarks>
        /// The class is named in full because the source generator emits a
        /// <c>Quickstarts.RoleManagement.ObjectIds</c> for the model of this sample, and this
        /// namespace is a child of that one, so the generated class wins over the standard
        /// one for a bare <c>ObjectIds</c>.
        /// </remarks>
        public static NodeId WorkstationRoleId => Opc.Ua.ObjectIds.WellKnownRole_ConfigureAdmin;
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Configures the Role manager which decides the Roles a Session is granted.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The base implementation hands out the default <see cref="RoleManager"/>, already
        /// populated with the nine well known Roles of Part 3 4.9.2 and the default identity
        /// mapping rules Part 18 4.3 mandates - Anonymous for an anonymous Session and
        /// AuthenticatedUser for a signed in one. All this sample has to add is one rule per
        /// account, mapping its user name onto the Role it should hold.
        /// </para>
        /// <para>
        /// The rules added here are indistinguishable from rules a SecurityAdmin adds later
        /// through the AddIdentity Method of the RoleSet: both end up in the same Role manager,
        /// and both are visible in the Identities property of the Role node.
        /// </para>
        /// </remarks>
        protected override IRoleManager CreateRoleManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            IRoleManager roleManager = base.CreateRoleManager(server, configuration);

            foreach (SampleUser user in Users)
            {
                if (user.Role == null)
                {
                    continue;
                }

                NodeId roleId = ExpandedNodeId.ToNodeId(user.Role.RoleId, server.NamespaceUris);

                var rule = new IdentityMappingRuleType {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = user.UserName,
                };

                Check(
                    roleManager.AddIdentity(roleId, rule),
                    $"grant the {user.Role.Name} role to '{user.UserName}'");
            }

            // The identity criteria of the Role which belongs to the maintenance workstation
            // rather than to a user: it is granted for the certificate the client application
            // presented, not for a user name, so an anonymous Session from the sample client
            // holds it and a signed in Session from any other client does not.
            //
            // The Endpoints filter which goes with it is added in OnServerStarted, because
            // it cannot be written down until the server knows its own endpoints.
            Check(
                roleManager.AddIdentity(
                    WorkstationRoleId,
                    new IdentityMappingRuleType {
                        CriteriaType = IdentityCriteriaType.X509Subject,
                        Criteria = WorkstationCertificateSubject,
                    }),
                "map the certificate of the maintenance workstation onto the ConfigureAdmin role");

            return roleManager;
        }

        /// <summary>
        /// Restricts the Role of the maintenance workstation to the encrypted endpoints.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Part 18 4.4.1 lets a Role be qualified by two filters which are evaluated before
        /// its identity mapping rules are even looked at: the Applications it may be granted
        /// on and the Endpoints it may be granted on. With this one in place the ConfigureAdmin
        /// Role of the sample is refused to a Session which arrived on the unsecured endpoint
        /// however good its certificate is - and on an unsecured channel there is no client
        /// certificate to judge in the first place.
        /// </para>
        /// <para>
        /// Two things about how the filter has to be spelled. Part 18 4.4.2 says a field of
        /// an EndpointType which is left at its default value is ignored during the
        /// comparison, so <c>{ SecurityMode = SignAndEncrypt }</c> ought to be a compact way
        /// of saying "every encrypted endpoint" - but <see cref="IRoleManager.AddEndpoint"/>
        /// refuses an entry whose EndpointUrl is empty, so a rule which constrains the
        /// security mode alone cannot be stored. The endpoints the server actually
        /// advertises are copied instead, which is why this runs after the start rather than
        /// in <see cref="CreateRoleManager"/>: the URLs are not known before that, and the
        /// comparison is an exact string match, so guessing them from the base addresses of
        /// the configuration file would work only until a host name is spelled differently.
        /// </para>
        /// </remarks>
        private void ConfigureWorkstationEndpoints(IRoleManager roleManager)
        {
            NodeId roleId = WorkstationRoleId;
            int restricted = 0;

            foreach (EndpointDescription endpoint in GetEndpoints().ToArray())
            {
                if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                {
                    continue;
                }

                ServiceResult result = roleManager.AddEndpoint(
                    roleId,
                    new EndpointType {
                        EndpointUrl = endpoint.EndpointUrl,
                        SecurityMode = endpoint.SecurityMode,
                        SecurityPolicyUri = endpoint.SecurityPolicyUri,
                        TransportProfileUri = endpoint.TransportProfileUri,
                    });

                // the same endpoint url is advertised once per security policy, so the
                // second and later copies of an entry are the expected answer, not a failure
                if (result.StatusCode != StatusCodes.BadAlreadyExists)
                {
                    Check(result, $"restrict the ConfigureAdmin role to {endpoint.EndpointUrl}");
                }

                restricted++;
            }

            if (restricted == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The server offers no encrypted endpoint, so the ConfigureAdmin role of " +
                    "the sample would be granted on every endpoint instead of none.");
            }

            // false is the default for a well known Role, and saying so is the difference
            // between a list of endpoints the Role is granted on and a list it is refused on.
            // CustomConfiguration is left false on purpose: it is the flag which lets a Role
            // with an empty Identities list be granted at all, and setting it is what the
            // sample client's CustomConfiguration button demonstrates.
            Check(
                roleManager.SetEndpointsExclude(roleId, false),
                "make the endpoint list of the ConfigureAdmin role an inclusion list");
        }

        /// <summary>
        /// Turns a bad result of a Role manager call into a startup failure.
        /// </summary>
        /// <remarks>
        /// A misconfigured Role is not something to carry on from: the server would start and
        /// serve an address space which quietly grants the wrong things.
        /// </remarks>
        private static void Check(ServiceResult result, string what)
        {
            if (ServiceResult.IsBad(result))
            {
                throw ServiceResultException.Create(
                    result.StatusCode.Code,
                    "Could not {0}: {1}",
                    what,
                    result);
            }
        }

        /// <summary>
        /// Registers the authenticator which checks the passwords of the demonstration
        /// accounts, and finishes the Role configuration which needs the running server.
        /// </summary>
        /// <remarks>
        /// Authentication and authorization are separate concerns in Part 18, and they are
        /// separate here too: the authenticator decides whether the caller is who they claim
        /// to be, and nothing else. Which Roles that identity is worth is decided afterwards
        /// by the Role manager configured in <see cref="CreateRoleManager"/>.
        /// </remarks>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            server.IdentityRegistry.Register(new UserNamePasswordAuthenticator(AuthenticateUserNameAsync));

            ConfigureWorkstationEndpoints(server.RoleManager);
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        protected override ServerProperties LoadServerProperties()
        {
            var properties = new ServerProperties {
                ManufacturerName = "OPC Foundation",
                ProductName = "Quickstart RoleManagement Server",
                ProductUri = "http://opcfoundation.org/Quickstart/RoleManagementServer/v1.0",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp(),
            };

            return properties;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Accepts a user name token whose name and password match one of the sample accounts.
        /// </summary>
        /// <remarks>
        /// The password is taken from <see cref="UserNameIdentityTokenHandler.DecryptedPassword"/>
        /// rather than from the token: the password on the wire is encrypted with the
        /// server's certificate, and the handler is what holds the plain text after the
        /// stack decrypted it.
        /// </remarks>
        private ValueTask<IUserIdentity> AuthenticateUserNameAsync(
            UserNameIdentityTokenHandler handler,
            CancellationToken ct)
        {
            string password = handler.DecryptedPassword != null
                ? Encoding.UTF8.GetString(handler.DecryptedPassword)
                : null;

            foreach (SampleUser user in Users)
            {
                if (string.Equals(user.UserName, handler.UserName, StringComparison.Ordinal) &&
                    string.Equals(user.Password, password, StringComparison.Ordinal))
                {
                    return new ValueTask<IUserIdentity>(new UserIdentity(handler));
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadUserAccessDenied,
                "'{0}' is not one of the sample accounts, or the password is wrong.",
                handler.UserName);
        }
        #endregion
    }
}
