/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Samples.Client;

namespace Quickstarts.FileTransferClient.Model
{
    /// <summary>
    /// One directory of the file system of the server.
    /// </summary>
    /// <param name="Name">The name of the directory.</param>
    /// <param name="FullPath">The path the server gave it, namespace prefixes included.</param>
    public sealed record DirectoryEntry(string Name, string FullPath);

    /// <summary>
    /// One entry of a directory: a file or a subdirectory.
    /// </summary>
    /// <param name="Name">The name of the entry.</param>
    /// <param name="FullPath">The path the server gave it.</param>
    /// <param name="IsDirectory">True for a subdirectory, false for a file.</param>
    /// <param name="Size">The size of a file in bytes; zero for a directory.</param>
    /// <param name="LastModified">When a file was last written, if the server says.</param>
    public sealed record FileSystemEntry(
        string Name,
        string FullPath,
        bool IsDirectory,
        long Size,
        DateTime? LastModified);

    /// <summary>
    /// How far a transfer has come.
    /// </summary>
    /// <param name="Copied">The bytes copied so far.</param>
    /// <param name="Total">The bytes to copy in all.</param>
    /// <param name="What">"Downloading" or "Uploading".</param>
    public readonly record struct TransferProgress(long Copied, long Total, string What);

    /// <summary>
    /// The client model of the file transfer Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model speaks to the server through <see cref="FileSystemClient"/>, the
    /// <c>System.IO</c> shaped wrapper the SDK puts around the <c>FileType</c> /
    /// <c>FileDirectoryType</c> model of OPC UA Part 5 Annex C and Part 20. Browsing,
    /// reading, writing, creating and deleting all go through it - the sample never issues
    /// a Call or a Read of its own.
    /// </para>
    /// <para>
    /// Paths are never spelled out. The node manager of a server puts its file system in a
    /// namespace of its own, so the path of an entry carries a namespace prefix whose index
    /// only that server knows - <c>/2:SampleFiles/2:Reports</c> and not
    /// <c>/SampleFiles/Reports</c>. The model therefore browses for what it wants and keeps
    /// the <see cref="UaFileSystemInfo.FullPath"/> the server handed it.
    /// </para>
    /// <para>
    /// A managed session keeps its <see cref="ISession"/> across a reconnect, so the file
    /// system client survives one: the reconnect hooks of the base class are not
    /// overridden, and whatever the window browsed stays valid.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The handles below are taken, cleared and released by OnDetachingAsync, which the detach of the base class runs - on a detach as well as on a dispose. The analyzer does not follow an asynchronous release through a virtual hook.")]
    public sealed class FileTransferClientModel : SampleClientModel
    {
        /// <summary>
        /// The path of the file system of the server, where the directories it offers are.
        /// </summary>
        public const string RootPath = UaPath.Root;

        /// <summary>
        /// The size of a single read or write while a file is transferred.
        /// </summary>
        /// <remarks>
        /// The client chunks a transfer anyway - it never asks for more than the server
        /// allows in one ByteString - so this only decides how often progress is reported.
        /// </remarks>
        private const int kTransferBufferSize = 32 * 1024;

        private FileSystemClient m_fileSystem;
        private CancellationTokenSource m_transfers;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public FileTransferClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The subdirectories of a directory.
        /// </summary>
        /// <param name="path">The path of the directory, as the server gave it; <see cref="RootPath"/> for the file system itself.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<DirectoryEntry>> ListDirectoriesAsync(string path, CancellationToken ct = default)
        {
            FileSystemClient fileSystem = RequireFileSystem();

            using CancellationTokenSource linked = LinkTransfers(ct);

            var directories = new List<DirectoryEntry>();

            await foreach (UaDirectoryInfo directory in
                fileSystem.EnumerateDirectoriesAsync(path, linked.Token).ConfigureAwait(false))
            {
                directories.Add(new DirectoryEntry(directory.Name, directory.FullPath));
            }

            return directories;
        }

        /// <summary>
        /// The files and directories of a directory, with the metadata of each file.
        /// </summary>
        /// <remarks>
        /// The metadata of a file - its size, when it was last written - are properties of
        /// the FileType instance, and reading them is a round trip of its own per file,
        /// which is what makes a listing slow enough to be worth cancelling.
        /// </remarks>
        /// <param name="path">The path of the directory, as the server gave it.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string path, CancellationToken ct = default)
        {
            FileSystemClient fileSystem = RequireFileSystem();

            using CancellationTokenSource linked = LinkTransfers(ct);

            var entries = new List<FileSystemEntry>();

            await foreach (UaFileSystemInfo entry in fileSystem.EnumerateAsync(path, linked.Token).ConfigureAwait(false))
            {
                if (entry is UaFileInfo file)
                {
                    await file.RefreshAsync(linked.Token).ConfigureAwait(false);

                    entries.Add(new FileSystemEntry(
                        file.Name,
                        file.FullPath,
                        false,
                        (long)file.Size,
                        file.LastModifiedTime));
                }
                else
                {
                    entries.Add(new FileSystemEntry(entry.Name, entry.FullPath, true, 0, null));
                }
            }

            return entries;
        }

        /// <summary>
        /// Copies a file of the server into a stream.
        /// </summary>
        /// <remarks>
        /// The file is opened for reading on the server and read in chunks; a large file
        /// never has to fit into memory on either side.
        /// </remarks>
        /// <param name="filePath">The path of the file, as the server gave it.</param>
        /// <param name="target">Where the content goes.</param>
        /// <param name="progress">Told after every chunk, or null.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The bytes copied.</returns>
        public async Task<long> DownloadAsync(
            string filePath,
            Stream target,
            IProgress<TransferProgress> progress,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(target);

            FileSystemClient fileSystem = RequireFileSystem();

            using CancellationTokenSource linked = LinkTransfers(ct);

            UaFileInfo file = await fileSystem.GetFileAsync(filePath, linked.Token).ConfigureAwait(false);

            await file.RefreshAsync(linked.Token).ConfigureAwait(false);

            await using UaFileStream source = await file.OpenReadAsync(linked.Token).ConfigureAwait(false);

            return await CopyAsync(source, target, (long)file.Size, "Downloading", progress, linked.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a stream into a new file of a directory of the server.
        /// </summary>
        /// <remarks>
        /// Creating the file and writing it are two steps: the server names the new node,
        /// and the path it answers with is the one the transfer then writes to.
        /// </remarks>
        /// <param name="directory">The path of the directory, as the server gave it.</param>
        /// <param name="name">The name of the new file; the server picks the namespace of its browse name.</param>
        /// <param name="source">The content.</param>
        /// <param name="length">How many bytes the content has, for the progress.</param>
        /// <param name="progress">Told after every chunk, or null.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The path the server gave the new file.</returns>
        public async Task<string> UploadAsync(
            string directory,
            string name,
            Stream source,
            long length,
            IProgress<TransferProgress> progress,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            FileSystemClient fileSystem = RequireFileSystem();

            using CancellationTokenSource linked = LinkTransfers(ct);

            UaFileInfo file = await fileSystem.CreateFileAsync(
                UaPath.Combine(directory, name),
                createIntermediate: false,
                linked.Token).ConfigureAwait(false);

            await using (UaFileStream target = await file.OpenWriteAsync(linked.Token).ConfigureAwait(false))
            {
                await CopyAsync(source, target, length, "Uploading", progress, linked.Token).ConfigureAwait(false);
            }

            return file.FullPath;
        }

        /// <summary>
        /// Removes a file or a directory from the server.
        /// </summary>
        /// <remarks>
        /// A directory may well have content, and the server removes it in one operation
        /// rather than the client walking the tree.
        /// </remarks>
        /// <param name="path">The path of the entry, as the server gave it.</param>
        /// <param name="isDirectory">True for a directory, whose content goes with it.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task DeleteAsync(string path, bool isDirectory, CancellationToken ct = default)
        {
            FileSystemClient fileSystem = RequireFileSystem();

            using CancellationTokenSource linked = LinkTransfers(ct);

            await fileSystem.DeleteAsync(path, isDirectory, linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a directory below another.
        /// </summary>
        /// <param name="directory">The path of the parent, as the server gave it.</param>
        /// <param name="name">The name of the new directory; the server picks the namespace of its browse name.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The path the server gave the new directory.</returns>
        public async Task<string> CreateDirectoryAsync(string directory, string name, CancellationToken ct = default)
        {
            FileSystemClient fileSystem = RequireFileSystem();

            using CancellationTokenSource linked = LinkTransfers(ct);

            UaDirectoryInfo created = await fileSystem.CreateDirectoryAsync(
                UaPath.Combine(directory, name),
                createIntermediate: false,
                linked.Token).ConfigureAwait(false);

            return created.FullPath;
        }

        /// <summary>
        /// Stops every operation which is still running on the file system.
        /// </summary>
        /// <remarks>
        /// The operations of the model share one token source, which is what lets the
        /// window stop a transfer that is half way through when it closes. A new source
        /// takes its place, so the model stays usable.
        /// </remarks>
        public void CancelTransfers()
        {
            CancellationTokenSource transfers = m_transfers;

            m_transfers = IsConnected ? new CancellationTokenSource() : null;

            if (transfers != null)
            {
                transfers.Cancel();
                transfers.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override Task OnAttachedAsync(CancellationToken ct)
        {
            // the standard Server/FileSystem object, which is where a server publishes the
            // directories it offers
            m_fileSystem = FileSystemClient.OpenServerFileSystem(RequireSession());
            m_transfers = new CancellationTokenSource();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            m_fileSystem = null;

            CancellationTokenSource transfers = m_transfers;

            m_transfers = null;

            if (transfers != null)
            {
                // a transfer which is half way through is stopped here, before the window
                // closes the session it runs on
                await transfers.CancelAsync().ConfigureAwait(false);
                transfers.Dispose();
            }
        }

        // a managed session keeps its ISession across a reconnect, so the file system
        // client and the paths which were browsed with it are still valid: the reconnect
        // hooks of the base class are not overridden. Rebuilding here would throw away
        // what the user browsed every time the connection hiccups.

        /// <summary>
        /// Copies one stream into the other and reports after every chunk.
        /// </summary>
        /// <remarks>
        /// Only the asynchronous members of <see cref="UaFileStream"/> are used: the
        /// synchronous ones block on the asynchronous ones, which on the thread of a
        /// Windows Forms application is a deadlock waiting to happen.
        /// </remarks>
        private static async Task<long> CopyAsync(
            Stream source,
            Stream target,
            long total,
            string what,
            IProgress<TransferProgress> progress,
            CancellationToken ct)
        {
            byte[] buffer = new byte[kTransferBufferSize];
            long copied = 0;

            progress?.Report(new TransferProgress(0, total, what));

            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                copied += read;

                progress?.Report(new TransferProgress(copied, total, what));
            }

            await target.FlushAsync(ct).ConfigureAwait(false);

            return copied;
        }

        /// <summary>
        /// The file system client, or an exception for a caller which needs one while the
        /// model is detached.
        /// </summary>
        private FileSystemClient RequireFileSystem()
        {
            RequireSession();

            return m_fileSystem ?? throw new InvalidOperationException(
                $"{nameof(FileTransferClientModel)} has no file system client.");
        }

        /// <summary>
        /// A token which stops when the caller's does, or when the transfers are cancelled.
        /// </summary>
        private CancellationTokenSource LinkTransfers(CancellationToken ct)
        {
            CancellationToken transfers = m_transfers?.Token ?? CancellationToken.None;

            return CancellationTokenSource.CreateLinkedTokenSource(ct, transfers);
        }
    }
}
