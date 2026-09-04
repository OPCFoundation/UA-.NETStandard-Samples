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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Samples.Client;
using Quickstarts.FileTransferClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the FileTransfer client exists to show, asked of its model without the window:
    /// the file system of the server is browsed rather than spelled out, a file goes up and
    /// comes back unchanged, directories come and go, and a reconnect leaves the file system
    /// client alone.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class FileTransferClientModelTests : ClientModelFixtureBase<FileTransferClientModel>
    {
        /// <summary>
        /// The mount the shipped configuration of the sample server publishes.
        /// </summary>
        private const string kMount = "SampleFiles";

        /// <summary>
        /// The directory below the mount the sample offers for uploads.
        /// </summary>
        private const string kUploads = "Uploads";

        /// <summary>
        /// Large enough for several chunks of the transfer, so the progress moves.
        /// </summary>
        private const int kPayloadSize = 200 * 1024;

        protected override string SampleName => "FileTransfer";

        protected override FileTransferClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new FileTransferClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task RootListsTheMountOfTheServer(CancellationToken ct)
        {
            // before an attach there is no file system to ask
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await Model.ListDirectoriesAsync(FileTransferClientModel.RootPath, ct).ConfigureAwait(false));

            await AttachAsync(ct).ConfigureAwait(false);

            IReadOnlyList<DirectoryEntry> root = await Model
                .ListDirectoriesAsync(FileTransferClientModel.RootPath, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync("The root holds: " + string.Join(", ", root.Select(directory => $"{directory.Name} ({directory.FullPath})")))
                .ConfigureAwait(false);

            DirectoryEntry mount = root.FirstOrDefault(directory => directory.Name == kMount);

            Assert.That(mount, Is.Not.Null, $"The sample does not mount '{kMount}' below the Server/FileSystem object.");

            // the path carries the namespace prefix the server chose, which is why the
            // client browses for it rather than spelling it out
            Assert.That(mount.FullPath, Does.EndWith(kMount));
            Assert.That(mount.FullPath, Is.Not.EqualTo("/" + kMount), "The path the server hands out carries a namespace prefix.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task UploadComesBackUnchangedAndIsDeleted(CancellationToken ct)
        {
            const string name = "model-round-trip.bin";

            await AttachAsync(ct).ConfigureAwait(false);

            string uploads = await UploadsPathAsync(ct).ConfigureAwait(false);

            await DeleteIfPresentAsync(uploads, name, ct).ConfigureAwait(false);

            // a pattern rather than zeros, so a transfer which drops or reorders a chunk
            // does not come back looking equal
            byte[] payload = new byte[kPayloadSize];

            for (int ii = 0; ii < payload.Length; ii++)
            {
                payload[ii] = (byte)(ii * 31 + ii / 256);
            }

            var progress = new ProgressRecorder();

            string uploaded;

            using (var source = new MemoryStream(payload))
            {
                uploaded = await Model
                    .UploadAsync(uploads, name, source, payload.Length, progress, ct)
                    .ConfigureAwait(false);
            }

            Assert.That(uploaded, Does.EndWith(name), "The server named the file differently.");
            Assert.That(progress.Last.What, Is.EqualTo("Uploading"));
            Assert.That(progress.Last.Copied, Is.EqualTo(payload.Length), "The upload did not report its end.");
            Assert.That(progress.Reports, Is.GreaterThan(2), "A transfer of several chunks reports after each.");

            IReadOnlyList<FileSystemEntry> entries = await Model.ListEntriesAsync(uploads, ct).ConfigureAwait(false);

            FileSystemEntry file = entries.FirstOrDefault(entry => entry.Name == name);

            Assert.That(file, Is.Not.Null, "The uploaded file is not listed.");
            Assert.That(file.IsDirectory, Is.False);
            Assert.That(file.FullPath, Is.EqualTo(uploaded));
            Assert.That(file.Size, Is.EqualTo(payload.Length), "The server reports another size than was written.");

            progress = new ProgressRecorder();

            using (var target = new MemoryStream())
            {
                long copied = await Model.DownloadAsync(file.FullPath, target, progress, ct).ConfigureAwait(false);

                Assert.That(copied, Is.EqualTo(payload.Length));
                Assert.That(target.ToArray(), Is.EqualTo(payload), "The file did not come back as it went up.");
            }

            Assert.That(progress.Last.What, Is.EqualTo("Downloading"));
            Assert.That(progress.Last.Copied, Is.EqualTo(progress.Last.Total), "The download did not report its end.");
            Assert.That(progress.Last.Total, Is.EqualTo(payload.Length));

            await Model.DeleteAsync(file.FullPath, isDirectory: false, ct).ConfigureAwait(false);

            entries = await Model.ListEntriesAsync(uploads, ct).ConfigureAwait(false);

            Assert.That(entries.Select(entry => entry.Name), Does.Not.Contain(name), "The deleted file is still listed.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DirectoriesAreCreatedListedAndDeleted(CancellationToken ct)
        {
            const string name = "model-test-directory";

            await AttachAsync(ct).ConfigureAwait(false);

            string uploads = await UploadsPathAsync(ct).ConfigureAwait(false);

            await DeleteIfPresentAsync(uploads, name, ct).ConfigureAwait(false);

            string created = await Model.CreateDirectoryAsync(uploads, name, ct).ConfigureAwait(false);

            Assert.That(created, Does.EndWith(name));

            IReadOnlyList<DirectoryEntry> directories = await Model.ListDirectoriesAsync(uploads, ct).ConfigureAwait(false);

            Assert.That(
                directories.Select(directory => directory.FullPath),
                Does.Contain(created),
                "The new directory is not among the subdirectories.");

            IReadOnlyList<FileSystemEntry> entries = await Model.ListEntriesAsync(uploads, ct).ConfigureAwait(false);

            FileSystemEntry entry = entries.FirstOrDefault(candidate => candidate.FullPath == created);

            Assert.That(entry, Is.Not.Null, "The new directory is not among the entries.");
            Assert.That(entry.IsDirectory, Is.True);

            await Model.DeleteAsync(created, isDirectory: true, ct).ConfigureAwait(false);

            directories = await Model.ListDirectoriesAsync(uploads, ct).ConfigureAwait(false);

            Assert.That(directories.Select(directory => directory.FullPath), Does.Not.Contain(created), "The deleted directory is still listed.");
        }

        /// <summary>
        /// A managed session keeps its <see cref="ISession"/> across a reconnect, so the
        /// model has nothing to rebuild - and must not, because the window keeps the paths
        /// the model handed out in its tree.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReconnectLeavesTheFileSystemAlone(CancellationToken ct)
        {
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.ConnectionChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            string mount = await MountPathAsync(ct).ConfigureAwait(false);

            IReadOnlyList<FileSystemEntry> before = await Model.ListEntriesAsync(mount, ct).ConfigureAwait(false);

            Assume.That(before, Is.Not.Empty, "The mount has to have content to compare.");

            // the connection hiccups and comes back on the same session
            Model.NotifyReconnectStarting();

            Assert.That(Model.IsReconnecting, Is.True);

            await Model.NotifyReconnectCompletedAsync(ct).ConfigureAwait(false);

            Assert.That(Model.IsReconnecting, Is.False);
            Assert.That(Model.Session, Is.SameAs(Session), "The model swapped its session on a reconnect.");

            // the paths browsed before are still good, without an attach in between
            IReadOnlyList<FileSystemEntry> after = await Model.ListEntriesAsync(mount, ct).ConfigureAwait(false);

            Assert.That(
                after.Select(entry => entry.FullPath),
                Is.EquivalentTo(before.Select(entry => entry.FullPath)),
                "The listing changed across the reconnect.");

            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] {
                    ConnectionChange.Attached,
                    ConnectionChange.ReconnectStarting,
                    ConnectionChange.ReconnectCompleted,
                }));
        }

        /// <summary>
        /// The path of the mount, as the model browsed it.
        /// </summary>
        private async Task<string> MountPathAsync(CancellationToken ct)
        {
            return await ChildPathAsync(FileTransferClientModel.RootPath, kMount, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The path of the directory the sample offers for uploads.
        /// </summary>
        private async Task<string> UploadsPathAsync(CancellationToken ct)
        {
            string mount = await MountPathAsync(ct).ConfigureAwait(false);

            return await ChildPathAsync(mount, kUploads, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Browses a directory for a subdirectory and returns the path the server gave it.
        /// </summary>
        private async Task<string> ChildPathAsync(string directory, string name, CancellationToken ct)
        {
            IReadOnlyList<DirectoryEntry> directories = await Model.ListDirectoriesAsync(directory, ct).ConfigureAwait(false);

            DirectoryEntry child = directories.FirstOrDefault(candidate => candidate.Name == name);

            Assert.That(
                child,
                Is.Not.Null,
                $"'{directory}' has no subdirectory '{name}'. It has: " +
                string.Join(", ", directories.Select(candidate => candidate.Name)));

            return child.FullPath;
        }

        /// <summary>
        /// Removes what a previous run may have left behind.
        /// </summary>
        private async Task DeleteIfPresentAsync(string directory, string name, CancellationToken ct)
        {
            IReadOnlyList<FileSystemEntry> entries = await Model.ListEntriesAsync(directory, ct).ConfigureAwait(false);

            foreach (FileSystemEntry leftover in entries.Where(entry => entry.Name == name))
            {
                await Model.DeleteAsync(leftover.FullPath, leftover.IsDirectory, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Records the progress a transfer reports, synchronously.
        /// </summary>
        /// <remarks>
        /// <see cref="Progress{T}"/> posts to the thread pool when there is no
        /// synchronization context, so a test which asserts on the last report right after
        /// the transfer returns needs a recorder which does not go through it.
        /// </remarks>
        private sealed class ProgressRecorder : IProgress<TransferProgress>
        {
            private readonly object m_lock = new object();
            private TransferProgress m_last;
            private int m_reports;

            public TransferProgress Last
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_last;
                    }
                }
            }

            public int Reports
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_reports;
                    }
                }
            }

            public void Report(TransferProgress value)
            {
                lock (m_lock)
                {
                    m_last = value;
                    m_reports++;
                }
            }
        }
    }
}
