/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Gives a sample server the one account which may watch its audit events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC UA Part 3 §8.55 checks the ReceiveEvents permission on the EventType of an
    /// event, and the standard NodeSet grants it on <c>AuditEventType</c> and its
    /// subtypes to the SecurityAdmin Role only. Since 2.0 the stack enforces that, so an
    /// anonymous or a merely authenticated Session subscribes to audit events and never
    /// receives one. A sample which shows its audit events therefore needs an account
    /// which holds SecurityAdmin, and a client which signs in with it.
    /// </para>
    /// <para>
    /// The account is a demonstration account: its password is its user name. The
    /// stack also delivers audit events only over an encrypted channel, so the client
    /// connects to a SignAndEncrypt endpoint, which also protects the password.
    /// </para>
    /// </remarks>
    public static class SampleAuditorExtensions
    {
        /// <summary>
        /// The user name, and the password, of the demonstration account.
        /// </summary>
        public const string AuditorAccount = "auditor";

        /// <summary>
        /// Maps <see cref="AuditorAccount"/> to the SecurityAdmin Role and accepts it as
        /// a user name login.
        /// </summary>
        /// <param name="server">The builder of the server.</param>
        /// <returns>The builder, for chaining.</returns>
        public static IOpcUaServerBuilder AddSampleAuditor(this IOpcUaServerBuilder server)
        {
            ArgumentNullException.ThrowIfNull(server);

            return server
                // the well-known Roles of Part 3 §4.9.2 and their default mapping rules
                // are already there; this adds the one rule which makes the account
                // hold SecurityAdmin.
                .ConfigureRoles(roles => roles.Roles.Add(new RoleDefinitionOptions {
                    Name = Role.SecurityAdmin.Name,
                    Identities = {
                        new RoleIdentityMappingOptions {
                            CriteriaType = IdentityCriteriaType.UserName,
                            Criteria = AuditorAccount,
                        },
                    },
                }))
                .AddIdentityAuthenticator(
                    (_, _) => new UserNamePasswordAuthenticator(AuthenticateAuditorAsync));
        }

        /// <summary>
        /// Accepts the demonstration account, whose password is its user name.
        /// </summary>
        /// <remarks>
        /// The password arrives encrypted with the server's certificate, and
        /// <see cref="UserNameIdentityTokenHandler.DecryptedPassword"/> is what holds the
        /// plain text after the stack decrypted it.
        /// </remarks>
        private static ValueTask<IUserIdentity> AuthenticateAuditorAsync(
            UserNameIdentityTokenHandler handler,
            CancellationToken ct)
        {
            string password = handler.DecryptedPassword != null
                ? Encoding.UTF8.GetString(handler.DecryptedPassword)
                : null;

            if (string.Equals(handler.UserName, AuditorAccount, StringComparison.Ordinal) &&
                string.Equals(password, AuditorAccount, StringComparison.Ordinal))
            {
                return new ValueTask<IUserIdentity>(new UserIdentity(handler));
            }

            throw ServiceResultException.Create(
                StatusCodes.BadUserAccessDenied,
                "'{0}' is not the sample account, or the password is wrong.",
                handler.UserName);
        }
    }
}
