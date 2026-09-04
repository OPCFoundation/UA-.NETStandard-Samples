/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Quickstarts.RoleManagement.Server
{
    /// <summary>
    /// Restricts the Role of the maintenance workstation to the encrypted endpoints of the
    /// server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 18 4.4.1 lets a Role be qualified by two filters which are evaluated before its
    /// identity mapping rules are even looked at: the Applications it may be granted on and
    /// the Endpoints it may be granted on. With this one in place the ConfigureAdmin Role of
    /// the sample is refused to a Session which arrived on the unsecured endpoint however
    /// good its certificate is - and on an unsecured channel there is no client certificate
    /// to judge in the first place.
    /// </para>
    /// <para>
    /// Two things about how the filter has to be spelled, which are also why this runs as a
    /// startup task rather than as part of <see cref="SampleUsers.ConfigureRoles"/>.
    /// Part 18 4.4.2 says a field of an EndpointType which is left at its default value is
    /// ignored during the comparison, so <c>{ SecurityMode = SignAndEncrypt }</c> ought to be
    /// a compact way of saying "every encrypted endpoint" - but
    /// <see cref="IRoleManager.AddEndpoint"/> refuses an entry whose EndpointUrl is empty, so
    /// a rule which constrains the security mode alone cannot be stored
    /// (<see href="https://github.com/OPCFoundation/UA-.NETStandard/issues/4412"/>). The
    /// endpoints the server actually advertises are copied instead, and those are only known
    /// once it has started; the comparison is an exact string match, so guessing them from
    /// the base addresses of the configuration file would work only until a host name is
    /// spelled differently.
    /// </para>
    /// <para>
    /// The role configuration of the stack applies its endpoint entries without looking at
    /// what <c>AddEndpoint</c> answered, so declaring the wildcard there would be worse than
    /// this: it would fail silently and the Role would be granted everywhere.
    /// </para>
    /// </remarks>
    public sealed class WorkstationEndpoints : IServerStartupTask
    {
        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken)
        {
            IRoleManager roleManager = ((IServerInternal)server).RoleManager;
            NodeId roleId = SampleUsers.WorkstationRoleId;

            ArrayOf<EndpointDescription> endpoints =
                (server as IServerEndpointRegistryProvider)?.ServerEndpoints ?? default;

            int restricted = 0;

            foreach (EndpointDescription endpoint in endpoints.ToArray())
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
            // between a list of endpoints the Role is granted on and a list it is refused on
            Check(
                roleManager.SetEndpointsExclude(roleId, false),
                "make the endpoint list of the ConfigureAdmin role an inclusion list");

            return ValueTask.CompletedTask;
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
    }
}
