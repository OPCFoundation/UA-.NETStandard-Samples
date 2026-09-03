/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;

namespace Quickstarts.FileTransferServer
{
    /// <summary>
    /// Creates the file system node manager of the SDK for the directory the
    /// configuration of the sample names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample writes no node manager of its own. The SDK ships one - the
    /// <see cref="FileSystemNodeManager"/> of OPC UA Part 5 Annex C and Part 20 - which
    /// turns any <see cref="IFileSystemProvider"/> into the <c>FileType</c> /
    /// <c>FileDirectoryType</c> address space a file transfer client expects, and mounts
    /// it below the standard <c>Server/FileSystem</c> object. What is left to do is to
    /// pick a provider: the <see cref="PhysicalFileSystemProvider"/> of the SDK, which
    /// maps the file system onto a single directory of the host and refuses every path
    /// which would leave it. A server for a database, a blob store or an in-memory tree
    /// differs from this sample in exactly one line: the provider it constructs.
    /// </para>
    /// <para>
    /// The directory, the mount name and whether it is writable come from the
    /// <see cref="FileTransferServerConfiguration"/> extension of the configuration,
    /// which is not loaded when the container creates the factory. The factory is
    /// therefore registered with the container like any other, and reads the
    /// configuration the first time the server asks it for its namespace or for the
    /// node manager - both happen once the configuration is loaded.
    /// </para>
    /// </remarks>
    public sealed class FileTransferNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IServiceProvider m_provider;
        private readonly ILogger m_logger;
        private FileSystemNodeManagerFactory m_inner;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="provider">The container, for the loaded configuration.</param>
        /// <param name="telemetry">The telemetry context of the host.</param>
        public FileTransferNodeManagerFactory(IServiceProvider provider, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(telemetry);

            m_provider = provider;
            m_logger = telemetry.CreateLogger<FileTransferNodeManagerFactory>();
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => Inner(m_provider.GetRequiredService<ApplicationConfiguration>()).NamespacesUris;

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            return Inner(configuration).CreateAsync(server, configuration, cancellationToken);
        }

        /// <summary>
        /// The factory of the SDK for the directory the configuration names, created on
        /// first use.
        /// </summary>
        private FileSystemNodeManagerFactory Inner(ApplicationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (m_inner != null)
            {
                return m_inner;
            }

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

            AddSampleContent(rootDirectory, m_logger);

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation(
                    "Publishing '{RootDirectory}' as {MountName}, {Access}.",
                    rootDirectory,
                    mountName,
                    settings.Writable ? "writable" : "read only");
            }

            m_inner = new FileSystemNodeManagerFactory(
                new PhysicalFileSystemProvider(rootDirectory, mountName, settings.Writable));

            return m_inner;
        }

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

        private const string kReadMeName = "ReadMe.txt";
        private const string kReportsName = "Reports";
        private const string kReportName = "Trend.csv";
        private const string kUploadsName = "Uploads";
    }
}
