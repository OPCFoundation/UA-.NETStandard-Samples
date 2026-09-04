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
    /// A demonstration account of the sample: a user name, its password and the
    /// well-known Part 18 Role it holds, or <c>null</c> for an account which holds
    /// none beyond what every authenticated user gets.
    /// </summary>
    public sealed record SampleUser(string UserName, string Password, Role Role);

    /// <summary>
    /// The demonstration accounts of the sample, and the two things the server has to
    /// know about them: which Role each holds, which goes to the role manager of the
    /// stack, and how they log in, which goes to a user name authenticator of the
    /// stack. Both are registered on the server builder in the composition root.
    /// </summary>
    public static class SampleUsers
    {
        /// <summary>
        /// The accounts. The password of each is its user name.
        /// </summary>
        public static IReadOnlyList<SampleUser> All { get; } =
        [
            new SampleUser("observer1", "observer1", Role.Observer),
            new SampleUser("operator1", "operator1", Role.Operator),
            new SampleUser("engineer1", "engineer1", Role.Engineer),
            new SampleUser("supervisor1", "supervisor1", Role.Supervisor),
            new SampleUser("secadmin", "secadmin", Role.SecurityAdmin),
            new SampleUser("guest", "guest", null),
        ];

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

        /// <summary>
        /// Grants every account its Role, as an identity mapping rule on the well-known
        /// Role of the stack - the configuration of the role manager the stack installs
        /// on the server - and grants one Role for a certificate rather than an account.
        /// </summary>
        /// <param name="roles">The role configuration of the server.</param>
        public static void ConfigureRoles(RoleConfigurationOptions roles)
        {
            ArgumentNullException.ThrowIfNull(roles);

            foreach (SampleUser user in All)
            {
                if (user.Role == null)
                {
                    continue;
                }

                roles.Roles.Add(new RoleDefinitionOptions {
                    Name = user.Role.Name,
                    Identities = {
                        new RoleIdentityMappingOptions {
                            CriteriaType = IdentityCriteriaType.UserName,
                            Criteria = user.UserName,
                        },
                    },
                });
            }

            // The Role which belongs to the maintenance workstation rather than to a user:
            // it is granted for the certificate the client application presented, not for a
            // user name, so an anonymous Session from the sample client holds it and a
            // signed in Session from any other client does not.
            //
            // Two things this cannot say here. The Endpoints filter which goes with it is
            // applied by WorkstationEndpoints once the server knows its own endpoints. And
            // CustomConfiguration stays false on purpose: it is the flag which lets a Role
            // with an empty Identities list be granted at all, and setting it is what the
            // sample client's CustomConfiguration button demonstrates.
            roles.Roles.Add(new RoleDefinitionOptions {
                Name = Role.ConfigureAdmin.Name,
                Identities = {
                    new RoleIdentityMappingOptions {
                        CriteriaType = IdentityCriteriaType.X509Subject,
                        Criteria = WorkstationCertificateSubject,
                    },
                },
            });
        }

        /// <summary>
        /// Accepts a user name token of one of the accounts, with its password.
        /// </summary>
        /// <param name="handler">The token being validated.</param>
        /// <param name="ct">The cancellation token.</param>
        public static ValueTask<IUserIdentity> AuthenticateAsync(
            UserNameIdentityTokenHandler handler,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(handler);

            string password = handler.DecryptedPassword != null
                ? Encoding.UTF8.GetString(handler.DecryptedPassword)
                : null;

            foreach (SampleUser user in All)
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
    }
}
