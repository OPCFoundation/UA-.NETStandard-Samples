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
    /// Part 18 4.4.2 says a field of an EndpointType which is left at its default value is
    /// ignored during the comparison, so <c>{ SecurityMode = SignAndEncrypt }</c> is the
    /// whole filter: one entry which says "every encrypted endpoint, whatever its URL,
    /// security policy or transport". Naming the endpoints the server advertises instead
    /// would say the same thing while the host name is spelled the way it was on the day
    /// the rule was written - the comparison of a URL is an exact string match.
    /// </para>
    /// <para>
    /// This runs as a startup task rather than as part of
    /// <see cref="SampleUsers.ConfigureRoles"/> because a Role which does not exist yet
    /// cannot be given a filter, and because the server refusing the entry has to be a
    /// startup failure: a Role whose Endpoints filter was silently dropped is granted
    /// everywhere, which is the opposite of what this asks for.
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

            Check(
                roleManager.AddEndpoint(
                    roleId,
                    new EndpointType { SecurityMode = MessageSecurityMode.SignAndEncrypt }),
                "restrict the ConfigureAdmin role to the encrypted endpoints");

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
