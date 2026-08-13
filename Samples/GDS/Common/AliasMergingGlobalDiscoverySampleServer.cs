/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// A <see cref="GlobalDiscoverySampleServer"/> that also serves the GDS
    /// master AliasNames list (issue #274 / OPC 10000-12).
    /// </summary>
    /// <remarks>
    /// The only behavioural addition over the base GDS server is that, once
    /// the server has started, the supplied
    /// <see cref="GlobalDiscoveryServerAliasMerger"/>'s master
    /// <c>InMemoryAliasNameStore</c> is registered with the server-wide
    /// alias-name store registry. From then on the standard well-known
    /// <c>Aliases</c> / <c>TagVariables</c> / <c>Topics</c> nodes on the GDS
    /// dispatch <c>FindAlias</c> through the merged master list.
    /// </remarks>
    public class AliasMergingGlobalDiscoverySampleServer : GlobalDiscoverySampleServer
    {
        private readonly GlobalDiscoveryServerAliasMerger m_merger;

        /// <summary>
        /// Creates the alias-merging GDS server.
        /// </summary>
        public AliasMergingGlobalDiscoverySampleServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            ITelemetryContext telemetry,
            GlobalDiscoveryServerAliasMerger merger,
            bool autoApprove = true)
            : base(database, request, certificateGroup, userDatabase, telemetry, autoApprove)
        {
            m_merger = merger ?? throw new ArgumentNullException(nameof(merger));
        }

        /// <inheritdoc/>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
            m_merger.RegisterWithServer(server);
        }
    }
}
