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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.FileSystem;

namespace Quickstarts.FileTransferClient
{
    /// <summary>
    /// The main form of the file transfer Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client speaks to the server through <see cref="FileSystemClient"/>, the
    /// <c>System.IO</c> shaped wrapper the SDK puts around the <c>FileType</c> /
    /// <c>FileDirectoryType</c> model of OPC UA Part 5 Annex C and Part 20. Browsing,
    /// reading, writing, creating and deleting all go through it - the sample never issues
    /// a Call or a Read of its own.
    /// </para>
    /// <para>
    /// Paths are never spelled out. The node manager of a server puts its file system in a
    /// namespace of its own, so the path of an entry carries a namespace prefix whose index
    /// only that server knows - <c>/2:SampleFiles/2:Reports</c> and not
    /// <c>/SampleFiles/Reports</c>. The client therefore browses for what it wants and
    /// keeps the <see cref="UaFileSystemInfo.FullPath"/> the server handed it.
    /// </para>
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the application.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62569/Quickstarts/FileTransferServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The size of a single read or write while a file is transferred.
        /// </summary>
        /// <remarks>
        /// The client chunks a transfer anyway - it never asks for more than the server
        /// allows in one ByteString - so this only decides how often the progress bar moves.
        /// </remarks>
        private const int kTransferBufferSize = 32 * 1024;

        /// <summary>
        /// The text of the node which stands for the file system of the server.
        /// </summary>
        private const string kRootText = "FileSystem";

        private readonly ITelemetryContext m_telemetry;
        private ApplicationConfiguration m_configuration;
        private ISession m_session;
        private FileSystemClient m_fileSystem;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed by CancelTransfers, which the form calls when it closes.")]
        private CancellationTokenSource m_cancellation;

        /// <summary>
        /// Counts the directory listings which were started, so a listing can tell whether
        /// it is still the one the user is waiting for.
        /// </summary>
        private int m_browse;
        #endregion

        #region Private Types
        /// <summary>
        /// What a node of the directory tree stands for.
        /// </summary>
        private sealed class DirectoryTag
        {
            /// <summary>
            /// The path the server gave the directory.
            /// </summary>
            public string Path { get; init; }

            /// <summary>
            /// Whether the subdirectories of this directory have been browsed already.
            /// </summary>
            public bool Loaded { get; set; }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    Clear();
                    return;
                }

                await OpenFileSystemAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                EnableCommands(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        /// <remarks>
        /// A managed session keeps its <see cref="ISession"/> across a reconnect, so the
        /// file system client and the tree which was browsed with it are still valid and
        /// the user keeps their place. Rebuilding them here would throw the tree away and
        /// drop the selection back to the root every time the connection hiccups.
        /// Only a session which really was replaced is worth starting over from.
        /// </remarks>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                ISession session = ConnectServerCTRL.Session;

                if (ReferenceEquals(session, m_session) && m_fileSystem != null)
                {
                    EnableCommands(true);
                    return;
                }

                m_session = session;

                await OpenFileSystemAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Browses the subdirectories of a node the first time it is opened.
        /// </summary>
        private async void DirectoriesTV_BeforeExpandAsync(object sender, TreeViewCancelEventArgs e)
        {
            try
            {
                await LoadSubdirectoriesAsync(e.Node);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Lists the content of the selected directory.
        /// </summary>
        private async void DirectoriesTV_AfterSelectAsync(object sender, TreeViewEventArgs e)
        {
            try
            {
                await ShowDirectoryAsync(e.Node);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Enables the commands which need a selected entry.
        /// </summary>
        private void EntriesLV_SelectedIndexChanged(object sender, EventArgs e)
        {
            UaFileSystemInfo entry = SelectedEntry();

            DownloadBTN.Enabled = entry is UaFileInfo;
            DeleteBTN.Enabled = entry != null;
        }

        /// <summary>
        /// Opens a directory, or downloads a file.
        /// </summary>
        private async void EntriesLV_DoubleClickAsync(object sender, EventArgs e)
        {
            try
            {
                UaFileSystemInfo entry = SelectedEntry();

                if (entry is UaDirectoryInfo directory)
                {
                    await OpenSubdirectoryAsync(directory);
                }
                else if (entry is UaFileInfo file)
                {
                    await DownloadAsync(file);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Lists the selected directory again.
        /// </summary>
        private async void RefreshBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                TreeNode node = DirectoriesTV.SelectedNode;

                if (node != null)
                {
                    ((DirectoryTag)node.Tag).Loaded = false;
                    node.Nodes.Clear();

                    await LoadSubdirectoriesAsync(node);
                    await ShowDirectoryAsync(node);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Copies the selected file of the server to a file on this machine.
        /// </summary>
        private async void DownloadBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (SelectedEntry() is UaFileInfo file)
                {
                    await DownloadAsync(file);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Copies a file of this machine into the selected directory of the server.
        /// </summary>
        private async void UploadBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await UploadAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Removes the selected file or directory from the server.
        /// </summary>
        private async void DeleteBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                UaFileSystemInfo entry = SelectedEntry();

                if (entry == null)
                {
                    return;
                }

                DialogResult confirmed = MessageBox.Show(
                    this,
                    $"Delete '{entry.FullPath}' on the server?",
                    this.Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirmed != DialogResult.Yes)
                {
                    return;
                }

                // a directory the user picked may well have content, and the server removes
                // it in one operation rather than the client walking the tree
                await m_fileSystem.DeleteAsync(entry.FullPath, entry.IsDirectory, Token());

                await RefreshSelectedDirectoryAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Creates a directory below the selected one.
        /// </summary>
        private async void NewFolderBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                string name = NewFolderTB.Text.Trim();

                if (name.Length == 0 || CurrentDirectory() == null)
                {
                    return;
                }

                // the server picks the namespace of the browse name, so the leaf of the path
                // is handed over as a plain name
                UaDirectoryInfo created = await m_fileSystem.CreateDirectoryAsync(
                    UaPath.Combine(CurrentDirectory(), name),
                    createIntermediate: false,
                    Token());

                Status($"Created {created.FullPath}.");

                NewFolderTB.Text = string.Empty;

                await RefreshSelectedDirectoryAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            CancelTransfers();
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Opens the file system of the server and shows its root.
        /// </summary>
        private async Task OpenFileSystemAsync()
        {
            Clear();

            // the standard Server/FileSystem object, which is where a server publishes the
            // directories it offers
            m_fileSystem = FileSystemClient.OpenServerFileSystem(m_session);
            m_cancellation = new CancellationTokenSource();

            var root = new TreeNode(kRootText) {
                Tag = new DirectoryTag { Path = UaPath.Root },
            };

            DirectoriesTV.Nodes.Add(root);

            await LoadSubdirectoriesAsync(root);

            root.Expand();

            DirectoriesTV.SelectedNode = root;

            EnableCommands(true);
        }

        /// <summary>
        /// Browses the subdirectories of a node, once.
        /// </summary>
        /// <remarks>
        /// The node is claimed before the browse rather than after it. Expanding a node
        /// raises BeforeExpand again while the first browse is still running, and a flag
        /// which is only set at the end lets the second one through - which browses the
        /// same directory twice and appends its children a second time.
        /// </remarks>
        private async Task LoadSubdirectoriesAsync(TreeNode node)
        {
            var tag = (DirectoryTag)node.Tag;

            if (tag.Loaded)
            {
                return;
            }

            tag.Loaded = true;

            var children = new List<TreeNode>();

            try
            {
                await foreach (UaDirectoryInfo directory in
                    m_fileSystem.EnumerateDirectoriesAsync(tag.Path, Token()))
                {
                    var child = new TreeNode(directory.Name) {
                        Tag = new DirectoryTag { Path = directory.FullPath },
                    };

                    // a node which was never browsed gets a placeholder, so that it shows
                    // the expander a user has to click to have it browsed
                    child.Nodes.Add(new TreeNode());

                    children.Add(child);
                }
            }
            catch
            {
                // a browse which did not finish leaves nothing behind, so the next attempt
                // to open the node tries again instead of showing it as empty
                tag.Loaded = false;
                throw;
            }

            // the placeholder is replaced only once the real children are known, so the
            // node never flickers through an empty state
            node.Nodes.Clear();
            node.Nodes.AddRange(children.ToArray());
        }

        /// <summary>
        /// Lists the files and directories of a node.
        /// </summary>
        /// <remarks>
        /// Reading the metadata of every entry takes a round trip each, so a browse is slow
        /// enough for the user to have selected something else by the time it finishes. The
        /// rows are therefore collected first and only shown if this browse is still the
        /// one the tree is waiting for - otherwise two directories would interleave their
        /// entries in the list.
        /// </remarks>
        private async Task ShowDirectoryAsync(TreeNode node)
        {
            int browse = ++m_browse;

            if (node == null)
            {
                EntriesLV.Items.Clear();
                return;
            }

            var tag = (DirectoryTag)node.Tag;
            var rows = new List<ListViewItem>();

            await foreach (UaFileSystemInfo entry in m_fileSystem.EnumerateAsync(tag.Path, Token()))
            {
                rows.Add(await DescribeAsync(entry));
            }

            if (browse != m_browse)
            {
                return;
            }

            EntriesLV.BeginUpdate();

            try
            {
                EntriesLV.Items.Clear();
                EntriesLV.Items.AddRange(rows.ToArray());
            }
            finally
            {
                EntriesLV.EndUpdate();
            }

            EntriesLV_SelectedIndexChanged(this, EventArgs.Empty);

            Status($"{tag.Path} - {EntriesLV.Items.Count} entries.");
        }

        /// <summary>
        /// Turns an entry into the row which describes it.
        /// </summary>
        /// <remarks>
        /// The metadata of a file - its size, when it was last written, whether it may be
        /// written at all - are properties of the FileType instance, and reading them is a
        /// round trip of its own, which is what RefreshAsync does.
        /// </remarks>
        private static async Task<ListViewItem> DescribeAsync(UaFileSystemInfo entry)
        {
            var row = new ListViewItem(entry.Name) { Tag = entry };

            if (entry is UaFileInfo file)
            {
                await file.RefreshAsync();

                row.SubItems.Add("File");
                row.SubItems.Add(file.Size.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(
                    file.LastModifiedTime?.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
            }
            else
            {
                row.SubItems.Add("Folder");
                row.SubItems.Add(string.Empty);
                row.SubItems.Add(string.Empty);
            }

            row.SubItems.Add(entry.FullPath);

            return row;
        }

        /// <summary>
        /// Selects the tree node of a subdirectory of the current one.
        /// </summary>
        private async Task OpenSubdirectoryAsync(UaDirectoryInfo directory)
        {
            TreeNode parent = DirectoriesTV.SelectedNode;

            if (parent == null)
            {
                return;
            }

            await LoadSubdirectoriesAsync(parent);

            foreach (TreeNode child in parent.Nodes)
            {
                if (((DirectoryTag)child.Tag).Path == directory.FullPath)
                {
                    DirectoriesTV.SelectedNode = child;
                    child.EnsureVisible();
                    return;
                }
            }
        }

        /// <summary>
        /// Copies a file of the server into a file the user picks.
        /// </summary>
        private async Task DownloadAsync(UaFileInfo file)
        {
            using var dialog = new SaveFileDialog {
                Title = "Download from the server",
                FileName = file.Name,
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await file.RefreshAsync(Token());

            // the file is opened for reading on the server and read in chunks; a large file
            // never has to fit into memory on either side
            await using UaFileStream source = await file.OpenReadAsync(Token());

            using var target = new FileStream(
                dialog.FileName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            long copied = await CopyAsync(source, target, (long)file.Size, "Downloading");

            Status($"Downloaded {copied} bytes from {file.FullPath}.");
        }

        /// <summary>
        /// Copies a file the user picks into the selected directory of the server.
        /// </summary>
        private async Task UploadAsync()
        {
            string directory = CurrentDirectory();

            if (directory == null)
            {
                return;
            }

            using var dialog = new OpenFileDialog {
                Title = "Upload to the server",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var local = new FileInfo(dialog.FileName);

            // creating the file and writing it are two steps: the server names the new node,
            // and the path it answers with is the one the transfer then writes to
            UaFileInfo file = await m_fileSystem.CreateFileAsync(
                UaPath.Combine(directory, local.Name),
                createIntermediate: false,
                Token());

            await using (UaFileStream target = await file.OpenWriteAsync(Token()))
            {
                using var source = new FileStream(
                    local.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                long copied = await CopyAsync(source, target, local.Length, "Uploading");

                Status($"Uploaded {copied} bytes to {file.FullPath}.");
            }

            await RefreshSelectedDirectoryAsync();
        }

        /// <summary>
        /// Copies one stream into the other and moves the progress bar while it does.
        /// </summary>
        /// <remarks>
        /// Only the asynchronous members of <see cref="UaFileStream"/> are used: the
        /// synchronous ones block on the asynchronous ones, which on the thread of a
        /// Windows Forms application is a deadlock waiting to happen.
        /// </remarks>
        private async Task<long> CopyAsync(Stream source, Stream target, long total, string what)
        {
            byte[] buffer = new byte[kTransferBufferSize];
            long copied = 0;

            BeginTransfer(total, what);

            try
            {
                while (true)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(), Token());

                    if (read == 0)
                    {
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), Token());

                    copied += read;

                    ReportTransfer(copied, total, what);
                }

                await target.FlushAsync(Token());
            }
            finally
            {
                EndTransfer();
            }

            return copied;
        }

        /// <summary>
        /// Lists the selected directory again, after it was changed.
        /// </summary>
        private async Task RefreshSelectedDirectoryAsync()
        {
            TreeNode node = DirectoriesTV.SelectedNode;

            if (node == null)
            {
                return;
            }

            ((DirectoryTag)node.Tag).Loaded = false;
            node.Nodes.Clear();

            await LoadSubdirectoriesAsync(node);
            await ShowDirectoryAsync(node);
        }

        /// <summary>
        /// The path of the directory which is currently selected in the tree.
        /// </summary>
        private string CurrentDirectory()
        {
            TreeNode node = DirectoriesTV.SelectedNode;

            return node == null ? null : ((DirectoryTag)node.Tag).Path;
        }

        /// <summary>
        /// The entry which is currently selected in the list.
        /// </summary>
        private UaFileSystemInfo SelectedEntry()
        {
            return EntriesLV.SelectedItems.Count == 0
                ? null
                : EntriesLV.SelectedItems[0].Tag as UaFileSystemInfo;
        }

        /// <summary>
        /// Drops everything which belonged to a session.
        /// </summary>
        private void Clear()
        {
            CancelTransfers();

            m_fileSystem = null;

            DirectoriesTV.Nodes.Clear();
            EntriesLV.Items.Clear();

            EnableCommands(false);
            Status(string.Empty);
        }

        /// <summary>
        /// Stops a transfer which is still running and forgets its token source.
        /// </summary>
        private void CancelTransfers()
        {
            CancellationTokenSource cancellation = m_cancellation;

            m_cancellation = null;

            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// The token which stops the operations of this form when it closes.
        /// </summary>
        private CancellationToken Token()
        {
            return m_cancellation?.Token ?? CancellationToken.None;
        }

        private void EnableCommands(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            UploadBTN.Enabled = enabled;
            NewFolderBTN.Enabled = enabled;
            DownloadBTN.Enabled = enabled && SelectedEntry() is UaFileInfo;
            DeleteBTN.Enabled = enabled && SelectedEntry() != null;
        }

        private void BeginTransfer(long total, string what)
        {
            TransferPB.Minimum = 0;
            TransferPB.Maximum = 100;
            TransferPB.Value = 0;

            ReportTransfer(0, total, what);
        }

        private void ReportTransfer(long copied, long total, string what)
        {
            TransferPB.Value = total <= 0
                ? 100
                : (int)Math.Min(100, copied * 100 / total);

            Status($"{what} - {copied} of {total} bytes.");
        }

        private void EndTransfer()
        {
            TransferPB.Value = TransferPB.Maximum;
        }

        private void Status(string text)
        {
            TransferLB.Text = text;
        }
        #endregion
    }
}
