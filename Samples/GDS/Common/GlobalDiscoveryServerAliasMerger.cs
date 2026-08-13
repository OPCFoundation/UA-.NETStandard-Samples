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
using Opc.Ua.Client;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;

// Justification: sample diagnostics log exception type/message directly; the
// extra evaluation when logging is disabled is negligible for a sample tool.
#pragma warning disable CA1873

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Maintains the GDS master AliasNames list defined by OPC 10000-12
    /// (Part 12) and OPC 10000-17 (Part 17).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This implements the alias-name merge feature tracked in issue #274.
    /// The spec states that when a Server registers with the GDS, the GDS
    /// shall merge the AliasNames of the registering Server into a master
    /// AliasNames list on the GDS.
    /// </para>
    /// <para>
    /// The master list is a Part 17 <see cref="InMemoryAliasNameStore"/>
    /// that this class registers with the GDS server's
    /// <see cref="IAliasNameStoreRegistry"/> so the standard well-known
    /// <c>Aliases (i=23470)</c> / <c>TagVariables (i=23479)</c> /
    /// <c>Topics (i=23488)</c> nodes on the GDS serve the aggregated view.
    /// </para>
    /// <para>
    /// To collect a registered server's aliases the GDS acts as a UA client
    /// and calls Part 17 <c>FindAlias</c> against that server. Because the
    /// GDS never captures user credentials during registration, the pull is
    /// performed <b>anonymously over a secured (Sign / SignAndEncrypt)
    /// channel</b> authenticated with the GDS application instance
    /// certificate. Servers that require a named user or a user certificate
    /// to read their aliases are simply skipped (the failure is logged).
    /// </para>
    /// </remarks>
    public sealed class GlobalDiscoveryServerAliasMerger : IDisposable
    {
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly InMemoryAliasNameStore m_masterStore;

        // Tracks the alias entries contributed by each registered server so a
        // re-registration (or periodic refresh) can replace them cleanly.
        private readonly Dictionary<string, List<AliasDeleteRequest>> m_contributions =
            new Dictionary<string, List<AliasDeleteRequest>>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim m_mergeLock = new SemaphoreSlim(1, 1);

        private ApplicationConfiguration m_configuration;
        private IApplicationsDatabase m_database;
        private CancellationTokenSource m_workerCts;
        private Task m_workerTask;
        private bool m_disposed;

        /// <summary>
        /// The session timeout (ms) used for the short-lived pull sessions.
        /// </summary>
        private const uint kSessionTimeout = 60000;

        /// <summary>
        /// The discovery timeout (ms) used when selecting an endpoint.
        /// </summary>
        private const int kDiscoverTimeout = 15000;

        /// <summary>
        /// Creates the merger and its master AliasNames store.
        /// </summary>
        public GlobalDiscoveryServerAliasMerger(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<GlobalDiscoveryServerAliasMerger>();
            m_masterStore = CreateMasterStore();
        }

        /// <summary>
        /// The master AliasNames store served by the GDS. Exposed for tests
        /// and advanced hosts; most callers only need
        /// <see cref="RegisterWithServer"/>.
        /// </summary>
        public InMemoryAliasNameStore MasterStore => m_masterStore;

        /// <summary>
        /// The category the merged aliases are written to — the well-known
        /// <c>Aliases (i=23470)</c> root, whose <c>FindAlias</c> aggregates
        /// the <c>TagVariables</c> / <c>Topics</c> sub-categories per Part 17
        /// §6.3.2.
        /// </summary>
        public static NodeId MasterCategoryId => Opc.Ua.ObjectIds.Aliases;

        /// <summary>
        /// Registers the master AliasNames store with the GDS server so the
        /// standard well-known alias nodes dispatch through it. Call from the
        /// server's <c>OnServerStarted</c> override.
        /// </summary>
        public void RegisterWithServer(IServerInternal server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (server is not IAliasNameStoreRegistryProvider provider)
            {
                m_logger.LogWarning(
                    "GDS alias merge: server does not expose an IAliasNameStoreRegistry - " +
                    "the master AliasNames list will not be served.");
                return;
            }

            provider.AliasNameStoreRegistry.Register(m_masterStore);
            m_logger.LogInformation("GDS alias merge: master AliasNames store registered.");
        }

        /// <summary>
        /// Starts the background worker that periodically re-scans every
        /// registered server and refreshes the master AliasNames list. The
        /// first sweep runs immediately.
        /// </summary>
        /// <param name="configuration">The GDS application configuration used
        /// to open client sessions to registered servers.</param>
        /// <param name="database">The GDS applications database used to
        /// enumerate registered servers.</param>
        /// <param name="refreshInterval">How often to refresh. Defaults to
        /// five minutes when <c>default</c>.</param>
        public void Start(
            ApplicationConfiguration configuration,
            IApplicationsDatabase database,
            TimeSpan refreshInterval = default)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_database = database ?? throw new ArgumentNullException(nameof(database));

            if (m_workerTask != null)
            {
                return;
            }

            if (refreshInterval <= TimeSpan.Zero)
            {
                refreshInterval = TimeSpan.FromMinutes(5);
            }

            m_workerCts = new CancellationTokenSource();
            m_workerTask = Task.Run(() => RunWorkerAsync(refreshInterval, m_workerCts.Token));
            m_logger.LogInformation(
                "GDS alias merge: background refresh started (interval {Interval}).",
                refreshInterval);
        }

        /// <summary>
        /// Requests an immediate, fire-and-forget merge of a single server's
        /// aliases. Intended to be called from an applications-database
        /// registration hook so a newly registered server's aliases are
        /// merged without waiting for the next periodic sweep.
        /// </summary>
        public void QueueMerge(string applicationUri, IList<string> discoveryUrls)
        {
            if (m_configuration == null)
            {
                // Not started yet - the first periodic sweep will pick it up.
                return;
            }

            CancellationToken ct = m_workerCts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () =>
            {
                try
                {
                    await MergeServerAsync(applicationUri, discoveryUrls, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        "GDS alias merge: background merge for {ApplicationUri} failed. Error=({ExceptionType}){Message}",
                        applicationUri, ex.GetType().Name, ex.Message);
                }
            }, ct);
        }

        /// <summary>
        /// Enumerates every registered server in the GDS database and merges
        /// its aliases into the master list.
        /// </summary>
        public async Task MergeAllRegisteredAsync(CancellationToken ct = default)
        {
            if (m_configuration == null || m_database == null)
            {
                throw new InvalidOperationException(
                    "The merger must be started with a configuration and database before merging.");
            }

            ApplicationDescription[] applications;
            try
            {
                applications = m_database.QueryApplications(
                    startingRecordId: 0,
                    maxRecordsToReturn: 0,
                    applicationName: string.Empty,
                    applicationUri: string.Empty,
                    applicationType: 0,
                    productUri: string.Empty,
                    serverCapabilities: ArrayOf<string>.Empty,
                    lastCounterResetTime: out _,
                    nextRecordId: out _);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    "GDS alias merge: QueryApplications failed. Error=({ExceptionType}){Message}",
                    ex.GetType().Name, ex.Message);
                return;
            }

            if (applications == null || applications.Length == 0)
            {
                return;
            }

            foreach (ApplicationDescription application in applications)
            {
                ct.ThrowIfCancellationRequested();

                // Only Servers publish AliasNames; skip pure clients and the
                // discovery servers (LDS / GDS directories).
                if (application.ApplicationType == ApplicationType.Client ||
                    application.ApplicationType == ApplicationType.DiscoveryServer)
                {
                    continue;
                }

                IList<string> discoveryUrls = application.DiscoveryUrls.IsNull
                    ? new List<string>()
                    : application.DiscoveryUrls.ToArray();

                await MergeServerAsync(application.ApplicationUri, discoveryUrls, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Connects to a single registered server, reads its AliasNames and
        /// merges them into the master list, replacing any aliases previously
        /// contributed by the same server.
        /// </summary>
        /// <param name="applicationUri">The registering server's application
        /// URI; used as the origin (<c>TargetServer</c>) of the merged
        /// aliases and as the replace key.</param>
        /// <param name="discoveryUrls">The server's discovery URLs.</param>
        /// <param name="ct">A cancellation token.</param>
        public async Task MergeServerAsync(
            string applicationUri,
            IList<string> discoveryUrls,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(applicationUri) || discoveryUrls == null || discoveryUrls.Count == 0)
            {
                return;
            }

            IReadOnlyList<AliasNameDataType> aliases = null;

            foreach (string discoveryUrl in discoveryUrls)
            {
                if (string.IsNullOrWhiteSpace(discoveryUrl))
                {
                    continue;
                }

                try
                {
                    aliases = await PullAliasesAsync(discoveryUrl, ct).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        "GDS alias merge: could not pull aliases from {DiscoveryUrl} ({ApplicationUri}). Error=({ExceptionType}){Message}",
                        discoveryUrl, applicationUri, ex.GetType().Name, ex.Message);
                }
            }

            if (aliases == null)
            {
                return;
            }

            await ApplyMergeAsync(applicationUri, aliases, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a short-lived anonymous, secured session to the server and
        /// reads every alias under the well-known <c>Aliases</c> category.
        /// </summary>
        private async Task<IReadOnlyList<AliasNameDataType>> PullAliasesAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                m_configuration,
                discoveryUrl,
                useSecurity: true,
                kDiscoverTimeout,
                m_telemetry,
                ct).ConfigureAwait(false);

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            Opc.Ua.Client.ISession session = await new DefaultSessionFactory(m_telemetry).CreateAsync(
                m_configuration,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: m_configuration.ApplicationName + " AliasMerge",
                sessionTimeout: kSessionTimeout,
                identity: new UserIdentity(),
                preferredLocales: ArrayOf<string>.Empty,
                ct).ConfigureAwait(false);

            try
            {
                AliasNameClient client = AliasNameClient.OpenStandardAliases(session);

                // "%" is the Part 17 wildcard that matches every alias name.
                return await client.FindAliasAsync("%", referenceTypeFilter: null, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await session.CloseAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        "GDS alias merge: closing pull session failed. Error=({ExceptionType}){Message}",
                        ex.GetType().Name, ex.Message);
                }

                session.Dispose();
            }
        }

        /// <summary>
        /// Replaces the named server's previous contribution with the newly
        /// pulled aliases in the master store.
        /// </summary>
        private async Task ApplyMergeAsync(
            string applicationUri,
            IReadOnlyList<AliasNameDataType> aliases,
            CancellationToken ct)
        {
            var additions = new List<AliasAddRequest>();
            var newContribution = new List<AliasDeleteRequest>();

            foreach (AliasNameDataType alias in aliases)
            {
                string name = alias.AliasName.Name;
                if (string.IsNullOrEmpty(name) || alias.ReferencedNodes.IsNull)
                {
                    continue;
                }

                foreach (ExpandedNodeId target in alias.ReferencedNodes.ToArray())
                {
                    if (target.IsNull)
                    {
                        continue;
                    }

                    additions.Add(new AliasAddRequest(
                        name,
                        target,
                        applicationUri,
                        ReferenceTypeIds.AliasFor));
                    newContribution.Add(new AliasDeleteRequest(name, target));
                }
            }

            await m_mergeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Remove what this server contributed previously so stale
                // aliases do not linger after the server changes them.
                if (m_contributions.TryGetValue(applicationUri, out List<AliasDeleteRequest> previous) &&
                    previous.Count > 0)
                {
                    await m_masterStore.DeleteAliasesAsync(MasterCategoryId, previous, ct)
                        .ConfigureAwait(false);
                }

                if (additions.Count > 0)
                {
                    await m_masterStore.AddAliasesAsync(MasterCategoryId, additions, ct)
                        .ConfigureAwait(false);
                    m_contributions[applicationUri] = newContribution;
                }
                else
                {
                    m_contributions.Remove(applicationUri);
                }
            }
            finally
            {
                m_mergeLock.Release();
            }

            m_logger.LogInformation(
                "GDS alias merge: merged {Count} alias target(s) from {ApplicationUri}.",
                additions.Count, applicationUri);
        }

        private async Task RunWorkerAsync(TimeSpan refreshInterval, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await MergeAllRegisteredAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        "GDS alias merge: periodic refresh failed. Error=({ExceptionType}){Message}",
                        ex.GetType().Name, ex.Message);
                }

                try
                {
                    await Task.Delay(refreshInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Builds the master store: the well-known <c>Aliases</c> root with
        /// the <c>TagVariables</c> and <c>Topics</c> sub-categories, matching
        /// the standard Part 17 layout so a client browsing the GDS sees the
        /// familiar hierarchy.
        /// </summary>
        private static InMemoryAliasNameStore CreateMasterStore()
        {
            var tagVariables = new AliasNameCategoryDescriptor(
                Opc.Ua.ObjectIds.TagVariables,
                QualifiedName.From(Opc.Ua.BrowseNames.TagVariables),
                AliasNameCapabilities.FindAliasVerbose);

            var topics = new AliasNameCategoryDescriptor(
                Opc.Ua.ObjectIds.Topics,
                QualifiedName.From(Opc.Ua.BrowseNames.Topics),
                AliasNameCapabilities.FindAliasVerbose);

            var aliases = new AliasNameCategoryDescriptor(
                Opc.Ua.ObjectIds.Aliases,
                QualifiedName.From(Opc.Ua.BrowseNames.Aliases),
                AliasNameCapabilities.FindAliasVerbose |
                AliasNameCapabilities.LastChange |
                AliasNameCapabilities.AddAliasesToCategory |
                AliasNameCapabilities.DeleteAliasesFromCategory,
                subCategories: new[] { tagVariables, topics });

            return new InMemoryAliasNameStore(new[] { aliases });
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;

            try
            {
                m_workerCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                m_workerTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(
                    "GDS alias merge: worker shutdown reported {ExceptionType}: {Message}",
                    ex.GetType().Name, ex.Message);
            }

            m_workerCts?.Dispose();
            m_mergeLock.Dispose();
            m_masterStore.Dispose();
        }
    }
}
#pragma warning restore CA1873
