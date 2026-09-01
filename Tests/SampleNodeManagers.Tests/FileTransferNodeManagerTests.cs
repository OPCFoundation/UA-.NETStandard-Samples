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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the file transfer sample does: it publishes a directory of the host through the
    /// FileType / FileDirectoryType model of OPC UA Part 5 Annex C and Part 20, below the
    /// standard <c>Server/FileSystem</c> object.
    /// </summary>
    /// <remarks>
    /// The node manager is the one the SDK ships, so what is under test here is the sample's
    /// side of the contract: that it mounts the configured directory where a client looks
    /// for it, that the mount carries the content the sample seeds, and that the mount is
    /// writable - browse, read, write, create, move and delete all have to work through a
    /// plain <see cref="FileSystemClient"/>, because that is the whole point of the sample.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class FileTransferNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "FileTransfer";

        /// <summary>
        /// The browse name the shipped configuration of the sample mounts under.
        /// </summary>
        private const string kMount = "SampleFiles";

        /// <summary>
        /// The directory the sample seeds for clients to write into.
        /// </summary>
        private const string kUploads = "Uploads";

        private string m_mountPath;

        private FileSystemClient FileSystem => FileSystemClient.OpenServerFileSystem(Session);

        /// <summary>
        /// The server publishes its directory as a child of the standard file system object,
        /// which is where <see cref="FileSystemClient.OpenServerFileSystem"/> looks for it.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ServerFileSystemCarriesTheConfiguredMount(CancellationToken ct)
        {
            FileSystemClient fileSystem = FileSystem;

            List<UaFileSystemInfo> entries = await CollectAsync(fileSystem.EnumerateAsync("/", ct))
                .ConfigureAwait(false);

            await ReportAsync("Server/FileSystem", entries.Select(entry => entry.FullPath))
                .ConfigureAwait(false);

            UaFileSystemInfo mount = entries.Find(entry => entry.Name == kMount);

            Assert.That(
                mount,
                Is.Not.Null,
                $"The sample does not mount '{kMount}' below the Server/FileSystem object.");

            Assert.That(mount.IsDirectory, Is.True, "The mount has to be a directory.");
        }

        /// <summary>
        /// The content the sample seeds is browsable and readable, and a file reports the
        /// metadata a FileType instance has to carry.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SeededContentIsBrowsableAndReadable(CancellationToken ct)
        {
            FileSystemClient fileSystem = FileSystem;
            string mount = await MountPathAsync(fileSystem, ct).ConfigureAwait(false);

            List<UaFileSystemInfo> entries = await CollectAsync(fileSystem.EnumerateAsync(mount, ct))
                .ConfigureAwait(false);

            await ReportAsync(mount, entries.Select(entry => entry.FullPath)).ConfigureAwait(false);

            Assert.That(
                entries.Select(entry => entry.Name),
                Is.SupersetOf(new[] { "ReadMe.txt", "Reports", kUploads }),
                "The sample seeds its directory so that a client has something to look at.");

            string readMePath = await ChildPathAsync(fileSystem, mount, "ReadMe.txt", ct)
                .ConfigureAwait(false);

            UaFileInfo readMe = await fileSystem.GetFileAsync(readMePath, ct).ConfigureAwait(false);

            await readMe.RefreshAsync(ct).ConfigureAwait(false);

            string content = await readMe.ReadAllTextAsync(Encoding.UTF8, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    content,
                    Does.Contain("Quickstart File Transfer Server"),
                    "The seeded file does not read back what the sample wrote.");

                Assert.That(
                    readMe.Size,
                    Is.EqualTo((ulong)Encoding.UTF8.GetByteCount(content)),
                    "The Size property has to match what the file returns.");

                Assert.That(readMe.Writable, Is.True, "The shipped configuration publishes the mount writable.");
            });
        }

        /// <summary>
        /// A client can upload a file, read it back and delete it again.
        /// </summary>
        /// <remarks>
        /// This is the round trip the sample exists for. It runs through the bulk helpers
        /// rather than through a stream, so a failure points at the file services and not
        /// at the chunking of the client.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task UploadDownloadAndDeleteRoundTrip(CancellationToken ct)
        {
            FileSystemClient fileSystem = FileSystem;
            string uploads = await UploadsPathAsync(fileSystem, ct).ConfigureAwait(false);

            const string name = "round-trip.txt";
            const string payload = "the quick brown fox jumps over the lazy dog";

            await DeleteIfPresentAsync(fileSystem, uploads, name, ct).ConfigureAwait(false);

            UaFileInfo file = await fileSystem
                .CreateFileAsync(UaPath.Combine(uploads, name), createIntermediate: false, ct)
                .ConfigureAwait(false);

            await file.WriteAllTextAsync(payload, Encoding.UTF8, ct).ConfigureAwait(false);

            Assert.That(
                await fileSystem.FileExistsAsync(file.FullPath, ct).ConfigureAwait(false),
                Is.True,
                "The uploaded file is not in the address space.");

            string readBack = await fileSystem
                .ReadAllTextAsync(file.FullPath, Encoding.UTF8, ct)
                .ConfigureAwait(false);

            Assert.That(readBack, Is.EqualTo(payload), "The file did not read back what was written.");

            await fileSystem.DeleteAsync(file.FullPath, recursive: false, ct).ConfigureAwait(false);

            Assert.That(
                await fileSystem.FileExistsAsync(file.FullPath, ct).ConfigureAwait(false),
                Is.False,
                "The deleted file is still in the address space.");
        }

        /// <summary>
        /// A file which is longer than one chunk is transferred through a stream, in both
        /// directions, and the bytes survive.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StreamedTransferPreservesLargeContent(CancellationToken ct)
        {
            FileSystemClient fileSystem = FileSystem;
            string uploads = await UploadsPathAsync(fileSystem, ct).ConfigureAwait(false);

            const string name = "streamed.bin";

            byte[] payload = new byte[128 * 1024];

            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)(index % 251);
            }

            await DeleteIfPresentAsync(fileSystem, uploads, name, ct).ConfigureAwait(false);

            UaFileInfo file = await fileSystem
                .CreateFileAsync(UaPath.Combine(uploads, name), createIntermediate: false, ct)
                .ConfigureAwait(false);

            await using (UaFileStream writing = await file.OpenWriteAsync(ct).ConfigureAwait(false))
            {
                await writing.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            }

            byte[] readBack;

            await using (UaFileStream reading = await file.OpenReadAsync(ct).ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();

                await reading.CopyToAsync(buffer, 16 * 1024, ct).ConfigureAwait(false);

                readBack = buffer.ToArray();
            }

            Assert.That(readBack, Is.EqualTo(payload), "The streamed file did not survive the round trip.");

            await fileSystem.DeleteAsync(file.FullPath, recursive: false, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Directories can be created, renamed and removed, which is the rest of the
        /// FileDirectoryType surface the sample publishes.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DirectoriesCanBeCreatedRenamedAndRemoved(CancellationToken ct)
        {
            FileSystemClient fileSystem = FileSystem;
            string uploads = await UploadsPathAsync(fileSystem, ct).ConfigureAwait(false);

            const string created = "batch";
            const string renamed = "archive";

            await DeleteIfPresentAsync(fileSystem, uploads, created, ct).ConfigureAwait(false);
            await DeleteIfPresentAsync(fileSystem, uploads, renamed, ct).ConfigureAwait(false);

            UaDirectoryInfo directory = await fileSystem
                .CreateDirectoryAsync(UaPath.Combine(uploads, created), createIntermediate: false, ct)
                .ConfigureAwait(false);

            await directory.CreateFileAsync("payload.txt", ct).ConfigureAwait(false);

            string before = directory.FullPath;

            // the info objects are immutable: a move answers with the moved object rather than
            // updating the one it was called on, so the answer is what the rest of the test uses
            UaFileSystemInfo moved = await directory
                .MoveToAsync(UaPath.Combine(uploads, renamed), ct)
                .ConfigureAwait(false);

            Assert.That(
                await fileSystem.DirectoryExistsAsync(before, ct).ConfigureAwait(false),
                Is.False,
                "The directory is still under its old path.");

            string after = moved.FullPath;

            Assert.That(
                after,
                Is.EqualTo(await ChildPathAsync(fileSystem, uploads, renamed, ct).ConfigureAwait(false)),
                "The path a move answers with has to be the path the directory is browsable under.");

            string payload = await ChildPathAsync(fileSystem, after, "payload.txt", ct).ConfigureAwait(false);

            Assert.That(
                await fileSystem.FileExistsAsync(payload, ct).ConfigureAwait(false),
                Is.True,
                "The renamed directory lost its content.");

            // a directory which still has content may only be removed recursively
            await Assert.ThatAsync(
                async () => await fileSystem.DeleteAsync(after, recursive: false, ct).ConfigureAwait(false),
                Throws.InstanceOf<IOException>(),
                "Deleting a directory which has content must not succeed without the recursive flag.")
                .ConfigureAwait(false);

            await fileSystem.DeleteAsync(after, recursive: true, ct).ConfigureAwait(false);

            Assert.That(
                await fileSystem.DirectoryExistsAsync(after, ct).ConfigureAwait(false),
                Is.False,
                "The directory survived a recursive delete.");
        }

        /// <summary>
        /// The provider of the sample is rooted at the published directory, so a path which
        /// tries to leave it resolves to nothing rather than to a file of the host.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task PathsCannotLeaveThePublishedDirectory(CancellationToken ct)
        {
            FileSystemClient fileSystem = FileSystem;
            string mount = await MountPathAsync(fileSystem, ct).ConfigureAwait(false);

            Assert.That(
                await fileSystem.ExistsAsync(mount + "/../../secrets.txt", ct).ConfigureAwait(false),
                Is.False,
                "A path which walks out of the mount must not resolve.");
        }

        /// <summary>
        /// The path of the mount, as a client which browsed for it sees it.
        /// </summary>
        /// <remarks>
        /// The node manager of the SDK puts the mount and everything below it in a namespace
        /// of its own, so the paths carry a namespace prefix - <c>/2:SampleFiles</c> rather
        /// than <c>/SampleFiles</c> - and the index depends on the order in which the server
        /// registered its namespaces. A client therefore browses for what it wants instead of
        /// spelling the path out, which is what the sample client does too.
        /// </remarks>
        private async Task<string> MountPathAsync(FileSystemClient fileSystem, CancellationToken ct)
        {
            m_mountPath ??= await ChildPathAsync(fileSystem, "/", kMount, ct).ConfigureAwait(false);

            return m_mountPath;
        }

        /// <summary>
        /// The path of the directory the sample offers for uploads.
        /// </summary>
        private async Task<string> UploadsPathAsync(FileSystemClient fileSystem, CancellationToken ct)
        {
            string mount = await MountPathAsync(fileSystem, ct).ConfigureAwait(false);

            return await ChildPathAsync(fileSystem, mount, kUploads, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Browses a directory for a child and returns the path the server gave it.
        /// </summary>
        private static async Task<string> ChildPathAsync(
            FileSystemClient fileSystem,
            string directory,
            string name,
            CancellationToken ct)
        {
            List<UaFileSystemInfo> entries = await CollectAsync(fileSystem.EnumerateAsync(directory, ct))
                .ConfigureAwait(false);

            UaFileSystemInfo child = entries.Find(entry => entry.Name == name);

            Assert.That(
                child,
                Is.Not.Null,
                $"'{directory}' has no child called '{name}'. It has: " +
                string.Join(", ", entries.Select(entry => entry.Name)));

            return child.FullPath;
        }

        /// <summary>
        /// Removes a leftover of an earlier run, so that a test starts from a known state.
        /// </summary>
        private static async Task DeleteIfPresentAsync(
            FileSystemClient fileSystem,
            string directory,
            string name,
            CancellationToken ct)
        {
            List<UaFileSystemInfo> entries = await CollectAsync(fileSystem.EnumerateAsync(directory, ct))
                .ConfigureAwait(false);

            UaFileSystemInfo leftover = entries.Find(entry => entry.Name == name);

            if (leftover != null)
            {
                await fileSystem.DeleteAsync(leftover.FullPath, recursive: true, ct).ConfigureAwait(false);
            }
        }

        private static async Task<List<UaFileSystemInfo>> CollectAsync(IAsyncEnumerable<UaFileSystemInfo> entries)
        {
            var collected = new List<UaFileSystemInfo>();

            await foreach (UaFileSystemInfo entry in entries.ConfigureAwait(false))
            {
                collected.Add(entry);
            }

            return collected;
        }
    }
}
