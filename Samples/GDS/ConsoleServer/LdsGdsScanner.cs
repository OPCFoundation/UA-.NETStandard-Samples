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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;

// Justification: sample diagnostics log exception type/message directly; the
// extra evaluation when logging is disabled is negligible for a sample tool.
#pragma warning disable CA1873

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// A discovered server that is a candidate for registration in the GDS.
    /// </summary>
    public sealed class LdsScanCandidate
    {
        /// <summary>The LDS discovery URL the candidate was found through.</summary>
        #pragma warning disable CA1056 // Justification: sample DTO keeps the discovery URL as a string, matching the surrounding GDS sample code.
        public string SourceLdsUrl { get; set; }
        #pragma warning restore CA1056

        /// <summary>The <see cref="ServerOnNetwork"/> record returned by FindServersOnNetwork.</summary>
        public ServerOnNetwork Network { get; set; }

        /// <summary>The resolved application description (from FindServers), when available.</summary>
        public ApplicationDescription Application { get; set; }

        /// <summary>
        /// Maps the candidate onto an <see cref="ApplicationRecordDataType"/>
        /// suitable for <see cref="IApplicationsDatabase.RegisterApplication"/>.
        /// </summary>
        public ApplicationRecordDataType ToApplicationRecord()
        {
            ApplicationDescription app = Application;

            var discoveryUrls = new List<string>();
            if (app != null && !app.DiscoveryUrls.IsNull)
            {
                discoveryUrls.AddRange(app.DiscoveryUrls.ToArray());
            }
            else if (!string.IsNullOrEmpty(Network?.DiscoveryUrl))
            {
                discoveryUrls.Add(Network.DiscoveryUrl);
            }

            var serverCapabilities = new List<string>();
            if (Network != null && !Network.ServerCapabilities.IsNull)
            {
                serverCapabilities.AddRange(Network.ServerCapabilities.ToArray());
            }

            return new ApplicationRecordDataType
            {
                ApplicationType = app?.ApplicationType ?? ApplicationType.Server,
                ApplicationUri = app?.ApplicationUri,
                ProductUri = app?.ProductUri,
                ApplicationNames = new LocalizedText[]
                {
                    app?.ApplicationName ?? new LocalizedText(Network?.ServerName ?? string.Empty)
                },
                DiscoveryUrls = discoveryUrls,
                ServerCapabilities = serverCapabilities
            };
        }
    }

    /// <summary>
    /// Scans one or more Local Discovery Servers (LDS / LDS-ME) for servers on
    /// the network and stages them for registration in the GDS.
    /// </summary>
    /// <remarks>
    /// This implements the LDS-to-GDS auto-populate feature described in the GDS
    /// README and tracked in issue #329. From the GDS side an LDS is just another
    /// discovery endpoint, so the scan is driven with the standard UA client
    /// discovery services: <c>FindServersOnNetwork</c> to enumerate what the LDS
    /// has observed (including its own mDNS/LDS-ME view) and <c>FindServers</c> to
    /// resolve each candidate's full <see cref="ApplicationDescription"/>.
    /// Discovered servers are never registered automatically; the host must gate
    /// registration behind a <c>DiscoveryAdmin</c> approval before calling
    /// <see cref="RegisterApproved"/>.
    /// </remarks>
    public sealed class LdsGdsScanner
    {
        private readonly ApplicationConfiguration m_configuration;
        private readonly IApplicationsDatabase m_database;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;

        /// <summary>
        /// Creates a scanner bound to the GDS application configuration and
        /// applications database.
        /// </summary>
        public LdsGdsScanner(
            ApplicationConfiguration configuration,
            IApplicationsDatabase database,
            ITelemetryContext telemetry)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_database = database ?? throw new ArgumentNullException(nameof(database));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<LdsGdsScanner>();
        }

        /// <summary>
        /// Scans every configured LDS discovery URL and returns the servers that
        /// are not yet registered in the GDS database.
        /// </summary>
        /// <param name="ldsDiscoveryUrls">The LDS discovery URLs to scan.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The candidates awaiting approval.</returns>
        public async Task<IList<LdsScanCandidate>> ScanAsync(
            IEnumerable<string> ldsDiscoveryUrls,
            CancellationToken ct = default)
        {
            if (ldsDiscoveryUrls == null)
            {
                throw new ArgumentNullException(nameof(ldsDiscoveryUrls));
            }

            var candidates = new List<LdsScanCandidate>();
            var seenApplicationUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string ldsUrl in ldsDiscoveryUrls)
            {
                if (string.IsNullOrWhiteSpace(ldsUrl))
                {
                    continue;
                }

                IList<ServerOnNetwork> onNetwork;
                try
                {
                    onNetwork = await ListServersOnNetworkAsync(ldsUrl, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        "LDS scan: FindServersOnNetwork failed for {LdsUrl}. Error=({ExceptionType}){Message}",
                        ldsUrl, ex.GetType().Name, ex.Message);
                    continue;
                }

                foreach (ServerOnNetwork server in onNetwork)
                {
                    if (string.IsNullOrEmpty(server?.DiscoveryUrl))
                    {
                        continue;
                    }

                    ApplicationDescription application =
                        await ResolveApplicationAsync(server.DiscoveryUrl, ct).ConfigureAwait(false);

                    // Skip discovery servers (LDS/GDS) - they are directories, not
                    // registerable applications.
                    if (application != null &&
                        (application.ApplicationType == ApplicationType.DiscoveryServer))
                    {
                        continue;
                    }

                    string applicationUri = application?.ApplicationUri;

                    // De-duplicate within this scan and against the GDS database.
                    if (!string.IsNullOrEmpty(applicationUri))
                    {
                        if (!seenApplicationUris.Add(applicationUri))
                        {
                            continue;
                        }

                        if (IsAlreadyRegistered(applicationUri))
                        {
                            m_logger.LogInformation(
                                "LDS scan: {ApplicationUri} is already registered in the GDS - skipping.",
                                applicationUri);
                            continue;
                        }
                    }

                    candidates.Add(new LdsScanCandidate
                    {
                        SourceLdsUrl = ldsUrl,
                        Network = server,
                        Application = application
                    });
                }
            }

            m_logger.LogInformation("LDS scan complete: {Count} new candidate(s).", candidates.Count);
            return candidates;
        }

        /// <summary>
        /// Registers the approved candidates in the GDS applications database.
        /// </summary>
        /// <param name="approved">The candidates a DiscoveryAdmin has approved.</param>
        /// <returns>The application node ids assigned by the database.</returns>
        public IList<NodeId> RegisterApproved(IEnumerable<LdsScanCandidate> approved)
        {
            if (approved == null)
            {
                throw new ArgumentNullException(nameof(approved));
            }

            var registered = new List<NodeId>();

            foreach (LdsScanCandidate candidate in approved)
            {
                ApplicationRecordDataType record = candidate.ToApplicationRecord();
                try
                {
                    NodeId applicationId = m_database.RegisterApplication(record);
                    registered.Add(applicationId);
                    m_logger.LogInformation(
                        "LDS scan: registered {ApplicationUri} as {ApplicationId}.",
                        record.ApplicationUri, applicationId);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        "LDS scan: failed to register {ApplicationUri}. Error=({ExceptionType}){Message}",
                        record.ApplicationUri, ex.GetType().Name, ex.Message);
                }
            }

            return registered;
        }

        private bool IsAlreadyRegistered(string applicationUri)
        {
            try
            {
                ApplicationRecordDataType[] existing = m_database.FindApplications(applicationUri);
                return existing != null && existing.Length > 0;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    "LDS scan: FindApplications failed for {ApplicationUri}. Error=({ExceptionType}){Message}",
                    applicationUri, ex.GetType().Name, ex.Message);
                return false;
            }
        }

        private async Task<IList<ServerOnNetwork>> ListServersOnNetworkAsync(
            string ldsUrl,
            CancellationToken ct)
        {
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);

            var results = new List<ServerOnNetwork>();

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                new Uri(ldsUrl),
                endpointConfiguration,
                m_telemetry,
                DiagnosticsMasks.None,
                ct).ConfigureAwait(false);

            uint startingRecordId = 0;

            while (true)
            {
                (ArrayOf<ServerOnNetwork> servers, _) = await client.FindServersOnNetworkAsync(
                    startingRecordId,
                    maxRecordsToReturn: 100,
                    serverCapabilityFilter: new List<string>(),
                    ct).ConfigureAwait(false);

                if (servers.IsNull || servers.Count == 0)
                {
                    break;
                }

                results.AddRange(servers.ToArray());
                startingRecordId = servers.ToArray().Max(s => s.RecordId) + 1;
            }

            return results;
        }

        private async Task<ApplicationDescription> ResolveApplicationAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            try
            {
                EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);

                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    new Uri(discoveryUrl),
                    endpointConfiguration,
                    m_telemetry,
                    DiagnosticsMasks.None,
                    ct).ConfigureAwait(false);

                ArrayOf<ApplicationDescription> applications =
                    await client.FindServersAsync(ArrayOf<string>.Empty, ct).ConfigureAwait(false);

                if (applications.IsNull || applications.Count == 0)
                {
                    return null;
                }

                // Prefer a non-discovery-server description that advertises the
                // discovery URL we connected to.
                ApplicationDescription[] array = applications.ToArray();

                ApplicationDescription match = array.FirstOrDefault(a =>
                    a.ApplicationType != ApplicationType.DiscoveryServer &&
                    !a.DiscoveryUrls.IsNull &&
                    a.DiscoveryUrls.ToArray().Any(u => string.Equals(u, discoveryUrl, StringComparison.OrdinalIgnoreCase)));

                return match
                    ?? array.FirstOrDefault(a => a.ApplicationType != ApplicationType.DiscoveryServer)
                    ?? array[0];
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    "LDS scan: FindServers failed for {DiscoveryUrl}. Error=({ExceptionType}){Message}",
                    discoveryUrl, ex.GetType().Name, ex.Message);
                return null;
            }
        }
    }
}
#pragma warning restore CA1873
