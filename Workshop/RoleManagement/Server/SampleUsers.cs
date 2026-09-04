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
        /// Grants every account its Role, as an identity mapping rule on the well-known
        /// Role of the stack - the configuration of the role manager the stack installs
        /// on the server.
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
