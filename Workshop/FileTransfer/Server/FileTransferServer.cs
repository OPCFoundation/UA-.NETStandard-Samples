/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;

namespace Quickstarts.FileTransferServer
{
    /// <summary>
    /// A Quickstart server which publishes a directory of the host as an OPC UA file system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample writes no node manager of its own. The SDK ships one - the
    /// <see cref="FileSystemNodeManager"/> of OPC UA Part 5 Annex C and Part 20 - which turns
    /// any <see cref="IFileSystemProvider"/> into the <c>FileType</c> / <c>FileDirectoryType</c>
    /// address space a file transfer client expects, and mounts it below the standard
    /// <c>Server/FileSystem</c> object. What the sample shows is how little is left to do:
    /// pick a provider, register the factory, and the file services are served.
    /// </para>
    /// <para>
    /// The provider used here is the <see cref="PhysicalFileSystemProvider"/> of the SDK,
    /// which maps the file system onto a single directory of the host and refuses every path
    /// which would leave it. A server for a database, a blob store or an in-memory tree
    /// differs from this sample in exactly one line: the provider it constructs.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample server type name intentionally mirrors the namespace.")]
    public partial class FileTransferServer : StandardServer
    {
        public FileTransferServer(ITelemetryContext telemetry) : base(telemetry)
        {
        }

        #region Overridden Methods
        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// The directory to publish comes from the configuration, which is not available
        /// before startup, so the node manager factory is registered here rather than in the
        /// constructor. The base implementation then creates the node manager from it.
        /// </remarks>
        protected override async ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(configuration);

            ILogger logger = server.Telemetry.CreateLogger<FileTransferServer>();

            FileTransferServerConfiguration settings =
                configuration.ParseExtension<FileTransferServerConfiguration>()
                ?? new FileTransferServerConfiguration();

            string rootDirectory = Utils.GetAbsoluteDirectoryPath(
                string.IsNullOrWhiteSpace(settings.RootDirectory)
                    ? FileTransferServerConfiguration.kDefaultRootDirectory
                    : settings.RootDirectory,
                checkCurrentDirectory: true,
                createAlways: true,
                throwOnError: true);

            string mountName = string.IsNullOrWhiteSpace(settings.MountName)
                ? FileTransferServerConfiguration.kDefaultMountName
                : settings.MountName;

            AddSampleContent(rootDirectory, logger);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Publishing '{RootDirectory}' as {MountName}, {Access}.",
                    rootDirectory,
                    mountName,
                    settings.Writable ? "writable" : "read only");
            }

            // a restarted server registers a fresh factory for the current configuration.
            if (m_factory != null)
            {
                RemoveNodeManager(m_factory);
            }

            m_factory = new FileSystemNodeManagerFactory(
                new PhysicalFileSystemProvider(rootDirectory, mountName, settings.Writable));

            AddNodeManager(m_factory);

            return await base.CreateMasterNodeManagerAsync(server, configuration, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            var properties = new ServerProperties {
                ManufacturerName = "OPC Foundation",
                ProductName = "Quickstart File Transfer Server",
                ProductUri = "http://opcfoundation.org/Quickstart/FileTransferServer/v1.0",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp(),
            };

            return properties;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Puts something in the published directory the first time the server runs.
        /// </summary>
        /// <remarks>
        /// A file transfer sample whose file system is empty demonstrates nothing, so an
        /// untouched directory is seeded with a file to read and a directory to write into.
        /// Anything a user put there is left alone.
        /// </remarks>
        private static void AddSampleContent(string rootDirectory, ILogger logger)
        {
            try
            {
                var root = new DirectoryInfo(rootDirectory);

                if (root.EnumerateFileSystemInfos().Any())
                {
                    return;
                }

                File.WriteAllText(
                    Path.Combine(rootDirectory, kReadMeName),
                    "This directory is published by the OPC UA Quickstart File Transfer Server." +
                    Environment.NewLine +
                    "Browse it with the File Transfer Client, or with any client which supports " +
                    "the FileDirectoryType of OPC UA Part 20." + Environment.NewLine);

                string reports = Path.Combine(rootDirectory, kReportsName);
                Directory.CreateDirectory(reports);

                File.WriteAllLines(
                    Path.Combine(reports, kReportName),
                    [
                        "Timestamp,Temperature,Pressure",
                        "2026-01-02T08:00:00Z,21.4,1013",
                        "2026-01-02T09:00:00Z,22.1,1012",
                        "2026-01-02T10:00:00Z,22.9,1011",
                    ]);

                Directory.CreateDirectory(Path.Combine(rootDirectory, kUploadsName));
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not seed the published directory.");
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "Could not seed the published directory.");
            }
        }
        #endregion

        #region Private Fields
        private const string kReadMeName = "ReadMe.txt";
        private const string kReportsName = "Reports";
        private const string kReportName = "Trend.csv";
        private const string kUploadsName = "Uploads";

        private IAsyncNodeManagerFactory m_factory;
        #endregion
    }
}
