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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// An <see cref="IConfigurationDataStore"/> that projects the applications already
    /// registered in the GDS directory onto the OPC 10000-12 §7.10.16
    /// <c>ManagedApplications</c> folder, and persists each application's configuration
    /// blob as a file under the GDS database store path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §7.10.14 - §7.10.16 let a GDS expose the configuration of the *other* applications it
    /// manages, each as an <c>ApplicationConfigurationType</c> instance carrying a
    /// <c>ConfigurationFile</c> with the <c>CloseAndUpdate</c> / <c>ConfirmUpdate</c>
    /// lifecycle. <c>DefaultManagedApplicationsNodeManager</c> builds those nodes; this store
    /// answers the two questions it cannot answer on its own - which applications to expose,
    /// and where their configuration lives.
    /// </para>
    /// <para>
    /// The sample answers the first from the GDS applications database itself, so every
    /// application that registered with this GDS shows up under <c>ManagedApplications</c>.
    /// The node manager enumerates the store once, while the address space is built, so
    /// applications registered after start-up appear on the next restart - which is the
    /// behaviour of the SDK node manager, not a limitation of this store.
    /// </para>
    /// <para>
    /// <see cref="WriteConfigurationAsync"/> is guarded by an optimistic-concurrency check:
    /// a write whose <c>currentVersion</c> does not match the stored version is rejected
    /// with <see cref="StatusCodes.BadInvalidState"/>, so two administrators editing the
    /// same application cannot silently overwrite each other. The written blob only becomes
    /// the active configuration once <see cref="ConfirmUpdateAsync"/> reports that the
    /// managed application applied it.
    /// </para>
    /// </remarks>
    public sealed class GdsManagedApplicationsDataStore : IConfigurationDataStore
    {
        private const string kPendingSuffix = ".pending";
        private const string kActiveSuffix = ".active";

        private readonly IApplicationsDatabase m_database;
        private readonly string m_storePath;
        private readonly ILogger m_logger;
        private readonly ConcurrentDictionary<string, uint> m_versions =
            new ConcurrentDictionary<string, uint>(StringComparer.Ordinal);

        /// <summary>
        /// Creates the store over the supplied GDS applications database.
        /// </summary>
        /// <param name="database">The GDS applications database to enumerate.</param>
        /// <param name="storePath">
        /// Directory the per-application configuration blobs are written to. Special folder
        /// names are expanded. Created on first use.
        /// </param>
        /// <param name="telemetry">Telemetry context used for logging.</param>
        /// <exception cref="ArgumentNullException"><paramref name="database"/> is <c>null</c>.</exception>
        public GdsManagedApplicationsDataStore(
            IApplicationsDatabase database,
            string storePath,
            ITelemetryContext telemetry)
        {
            m_database = database ?? throw new ArgumentNullException(nameof(database));
            m_storePath = Utils.ReplaceSpecialFolderNames(storePath) ?? storePath;
            m_logger = telemetry.CreateLogger<GdsManagedApplicationsDataStore>();
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<ManagedApplicationInfo>> GetManagedApplicationsAsync(
            CancellationToken ct = default)
        {
            var result = new List<ManagedApplicationInfo>();

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
                    "ManagedApplications: QueryApplications failed. Error=({ExceptionType}){Message}",
                    ex.GetType().Name, ex.Message);

                return new ValueTask<IReadOnlyList<ManagedApplicationInfo>>(result);
            }

            if (applications == null)
            {
                return new ValueTask<IReadOnlyList<ManagedApplicationInfo>>(result);
            }

            foreach (ApplicationDescription application in applications)
            {
                ct.ThrowIfCancellationRequested();

                if (String.IsNullOrEmpty(application?.ApplicationUri))
                {
                    continue;
                }

                result.Add(new ManagedApplicationInfo {
                    ApplicationUri = application.ApplicationUri,
                    ProductUri = application.ProductUri,
                    ApplicationType = application.ApplicationType,
                    Enabled = true,
                    IsNonUaApplication = false
                });
            }

            #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
            m_logger.LogInformation(
                "ManagedApplications: exposing {Count} registered application(s).", result.Count);
            #pragma warning restore CA1873

            return new ValueTask<IReadOnlyList<ManagedApplicationInfo>>(result);
        }

        /// <inheritdoc/>
        public async ValueTask<byte[]> ReadConfigurationAsync(
            string applicationUri,
            CancellationToken ct = default)
        {
            string path = GetFilePath(applicationUri, kActiveSuffix);

            if (!File.Exists(path))
            {
                return Array.Empty<byte>();
            }

            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when <paramref name="currentVersion"/>
        /// does not match the version the store holds.
        /// </exception>
        public async ValueTask<uint> WriteConfigurationAsync(
            string applicationUri,
            byte[] data,
            uint currentVersion,
            CancellationToken ct = default)
        {
            uint stored = GetVersion(applicationUri);

            if (currentVersion != stored)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    Utils.Format(
                        "Configuration of '{0}' changed since it was read (expected version {1}, found {2}).",
                        applicationUri, currentVersion, stored));
            }

            Directory.CreateDirectory(m_storePath);
            await File.WriteAllBytesAsync(
                GetFilePath(applicationUri, kPendingSuffix),
                data ?? Array.Empty<byte>(),
                ct).ConfigureAwait(false);

            uint newVersion = unchecked(stored + 1);
            m_versions[applicationUri] = newVersion;

            #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
            m_logger.LogInformation(
                "ManagedApplications: staged configuration version {Version} for {ApplicationUri}.",
                newVersion, applicationUri);
            #pragma warning restore CA1873

            return newVersion;
        }

        /// <inheritdoc/>
        public ValueTask ConfirmUpdateAsync(
            string applicationUri,
            uint configVersion,
            CancellationToken ct = default)
        {
            string pending = GetFilePath(applicationUri, kPendingSuffix);

            if (File.Exists(pending))
            {
                File.Copy(pending, GetFilePath(applicationUri, kActiveSuffix), overwrite: true);
                File.Delete(pending);
            }

            m_versions[applicationUri] = configVersion;

            #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
            m_logger.LogInformation(
                "ManagedApplications: confirmed configuration version {Version} for {ApplicationUri}.",
                configVersion, applicationUri);
            #pragma warning restore CA1873

            return default;
        }

        private uint GetVersion(string applicationUri)
        {
            return m_versions.TryGetValue(applicationUri ?? string.Empty, out uint version) ? version : 0;
        }

        /// <summary>
        /// Maps an ApplicationUri onto a file name. The URI is hashed rather than escaped so
        /// the name stays inside the file-name length limit and cannot escape the store
        /// directory.
        /// </summary>
        private string GetFilePath(string applicationUri, string suffix)
        {
            uint hash = 2166136261;

            foreach (char c in applicationUri ?? string.Empty)
            {
                hash = unchecked((hash ^ c) * 16777619);
            }

            return Path.Combine(
                m_storePath,
                hash.ToString("x8", CultureInfo.InvariantCulture) + suffix + ".bin");
        }
    }
}
