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
    ///   permission a service needs.
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
        /// Creates the server and registers its node manager factory.
        /// </summary>
        public RoleManagementServer(ITelemetryContext telemetry) : base(telemetry)
        {
            // register the source generated node manager factory. the server creates the
            // node manager from it while it builds the master node manager on startup.
            AddNodeManager(new RoleManagementNodeManagerFactory());
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

                ServiceResult result = roleManager.AddIdentity(roleId, rule);

                if (ServiceResult.IsBad(result))
                {
                    throw ServiceResultException.Create(
                        result.StatusCode.Code,
                        "Could not grant the {0} role to '{1}'.",
                        user.Role.Name,
                        user.UserName);
                }
            }

            return roleManager;
        }

        /// <summary>
        /// Registers the authenticator which checks the passwords of the demonstration accounts.
        /// </summary>
        /// <remarks>
        /// Authentication and authorization are separate concerns in Part 18, and they are
        /// separate here too: this decides whether the caller is who they claim to be, and
        /// nothing else. Which Roles that identity is worth is decided afterwards by the Role
        /// manager configured in <see cref="CreateRoleManager"/>.
        /// </remarks>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            server.IdentityRegistry.Register(new UserNamePasswordAuthenticator(AuthenticateUserNameAsync));
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
