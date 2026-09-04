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
using Quickstarts.AliasNames.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the AliasNames server sample: its configuration file,
    /// the node managers the server is made of - the plant and the alias categories
    /// laid over it - the SecurityAdmin Role of the one account which may change the
    /// alias inventory, and how that account logs in.
    /// </summary>
    /// <remarks>
    /// All of that is registered with the server builder of the stack. The one thing
    /// which is not is the store behind the standard <c>TagVariables</c> category,
    /// which <see cref="AliasNamesServer"/> explains. The entry point of the sample and
    /// the tests which host it share this one registration.
    /// </remarks>
    public static class AliasNamesServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample.
        /// </summary>
        public const string ConfigurationFile = "Quickstarts.AliasNamesServer.Config.xml";

        /// <summary>
        /// Registers the AliasNames server as the hosted OPC UA server of the stack,
        /// together with the node managers it serves and the account it knows.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddAliasNamesServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            return services.AddSampleServer<AliasNamesServer>(
                configurationFile ?? ConfigurationFile,
                server => server
                    .AddNodeManager<AliasNamesNodeManagerFactory>()
                    .AddNodeManager<AliasNameCategoryNodeManagerFactory>()

                    // Part 17 §6.3.4/§6.3.5 leave the authorization of the mutation
                    // Methods to the server, and the stack takes the strict reading:
                    // AddAliasesToCategory and DeleteAliasesFromCategory are refused unless
                    // the caller holds SecurityAdmin. The well-known Roles of Part 3 §4.9.2
                    // and their default mapping rules are already there; this adds the one
                    // rule which makes the sample account hold that Role.
                    .ConfigureRoles(roles => roles.Roles.Add(new RoleDefinitionOptions {
                        Name = Role.SecurityAdmin.Name,
                        Identities = {
                            new RoleIdentityMappingOptions {
                                CriteriaType = IdentityCriteriaType.UserName,
                                Criteria = AliasNamesServer.SecurityAdminUser,
                            },
                        },
                    }))
                    .AddIdentityAuthenticator(
                        (_, _) => new UserNamePasswordAuthenticator(AuthenticateSecurityAdminAsync)),
                configure);
        }

        /// <summary>
        /// Accepts the one demonstration account, whose password is its user name.
        /// </summary>
        /// <remarks>
        /// The password arrives encrypted with the server's certificate, and
        /// <see cref="UserNameIdentityTokenHandler.DecryptedPassword"/> is what holds the
        /// plain text after the stack decrypted it.
        /// </remarks>
        private static ValueTask<IUserIdentity> AuthenticateSecurityAdminAsync(
            UserNameIdentityTokenHandler handler,
            CancellationToken ct)
        {
            string password = handler.DecryptedPassword != null
                ? Encoding.UTF8.GetString(handler.DecryptedPassword)
                : null;

            if (string.Equals(handler.UserName, AliasNamesServer.SecurityAdminUser, StringComparison.Ordinal) &&
                string.Equals(password, AliasNamesServer.SecurityAdminUser, StringComparison.Ordinal))
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
