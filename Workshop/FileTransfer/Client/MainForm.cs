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
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.FileTransferClient.Model;

namespace Quickstarts.FileTransferClient
{
    /// <summary>
    /// The main form of the file transfer Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="FileTransferClientModel"/>, which browses, reads, writes, creates and
    /// deletes through the file system client of the SDK. The window only keeps the tree
    /// of directories the user opened, lists the entries of the selected one, and moves the
    /// progress bar while a transfer runs.
    /// </para>
    /// <para>
    /// The tree is the place the user browsed to, and it is kept across a reconnect: a
    /// managed session keeps its <see cref="ISession"/>, so the paths the model handed out
    /// stay valid and nothing has to be rebuilt.
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

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62569/Quickstarts/FileTransferServer";
            this.Text = configuration.ApplicationName;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new FileTransferClientModel(telemetry);
            m_model.Error += Model_Error;

            // Progress<T> reports on the thread it was created on, so the bar is moved
            // from the transfer without the transfer knowing about the window
            m_progress = new Progress<TransferProgress>(progress =>
                ReportTransfer(progress.Copied, progress.Total, progress.What));
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The text of the node which stands for the file system of the server.
        /// </summary>
        private const string kRootText = "FileSystem";

        private readonly ITelemetryContext m_telemetry;
        private readonly FileTransferClientModel m_model;
        private readonly IProgress<TransferProgress> m_progress;

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
        /// <remarks>
        /// The model is detached first: it stops the transfers before the control closes
        /// the session.
        /// </remarks>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.DetachAsync();
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
                ISession session = ConnectServerCTRL.Session;

                if (session == null)
                {
                    await m_model.DetachAsync();
                    Clear();
                    return;
                }

                Clear();

                // the model opens the file system of the server while it attaches
                await m_model.AttachAsync(session);

                await ShowRootAsync();
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
                m_model.NotifyReconnectStarting();
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
        /// file system client of the model and the tree which was browsed with it are still
        /// valid and the user keeps their place. Rebuilding the tree here would throw it
        /// away and drop the selection back to the root every time the connection hiccups.
        /// </remarks>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                await m_model.NotifyReconnectCompletedAsync();
                EnableCommands(m_model.IsConnected);
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
            FileSystemEntry entry = SelectedEntry();

            DownloadBTN.Enabled = entry != null && !entry.IsDirectory;
            DeleteBTN.Enabled = entry != null;
        }

        /// <summary>
        /// Opens a directory, or downloads a file.
        /// </summary>
        private async void EntriesLV_DoubleClickAsync(object sender, EventArgs e)
        {
            try
            {
                FileSystemEntry entry = SelectedEntry();

                if (entry == null)
                {
                    return;
                }

                if (entry.IsDirectory)
                {
                    await OpenSubdirectoryAsync(entry);
                }
                else
                {
                    await DownloadAsync(entry);
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
                await RefreshSelectedDirectoryAsync();
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
                FileSystemEntry entry = SelectedEntry();

                if (entry != null && !entry.IsDirectory)
                {
                    await DownloadAsync(entry);
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
                FileSystemEntry entry = SelectedEntry();

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

                await m_model.DeleteAsync(entry.FullPath, entry.IsDirectory);

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
                string directory = CurrentDirectory();

                if (name.Length == 0 || directory == null)
                {
                    return;
                }

                string created = await m_model.CreateDirectoryAsync(directory, name);

                Status($"Created {created}.");

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
        /// <remarks>
        /// FormClosing cannot await, so the model is detached on a thread pool thread and
        /// waited for; a transfer which is half way through is cancelled by that. Only then
        /// does the control close the session.
        /// </remarks>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClientUtils.WaitForTeardown(m_model.DetachAsync);
            ConnectServerCTRL.Disconnect();
        }

        /// <summary>
        /// Reports a failure on a background path of the model.
        /// </summary>
        private void Model_Error(object sender, ModelErrorEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ClientUtils.HandleException(m_telemetry, this.Text, e.Exception);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Shows the root of the file system of the server.
        /// </summary>
        private async Task ShowRootAsync()
        {
            var root = new TreeNode(kRootText) {
                Tag = new DirectoryTag { Path = FileTransferClientModel.RootPath },
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
                foreach (DirectoryEntry directory in await m_model.ListDirectoriesAsync(tag.Path))
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

            foreach (FileSystemEntry entry in await m_model.ListEntriesAsync(tag.Path))
            {
                rows.Add(Describe(entry));
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
        private static ListViewItem Describe(FileSystemEntry entry)
        {
            var row = new ListViewItem(entry.Name) { Tag = entry };

            if (entry.IsDirectory)
            {
                row.SubItems.Add("Folder");
                row.SubItems.Add(string.Empty);
                row.SubItems.Add(string.Empty);
            }
            else
            {
                row.SubItems.Add("File");
                row.SubItems.Add(entry.Size.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(
                    entry.LastModified?.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
            }

            row.SubItems.Add(entry.FullPath);

            return row;
        }

        /// <summary>
        /// Selects the tree node of a subdirectory of the current one.
        /// </summary>
        private async Task OpenSubdirectoryAsync(FileSystemEntry directory)
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
        private async Task DownloadAsync(FileSystemEntry file)
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

            using var target = new FileStream(
                dialog.FileName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            BeginTransfer();

            try
            {
                long copied = await m_model.DownloadAsync(file.FullPath, target, m_progress);

                Status($"Downloaded {copied} bytes from {file.FullPath}.");
            }
            finally
            {
                EndTransfer();
            }
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

            using var source = new FileStream(
                local.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            BeginTransfer();

            try
            {
                string created = await m_model.UploadAsync(directory, local.Name, source, local.Length, m_progress);

                Status($"Uploaded {local.Length} bytes to {created}.");
            }
            finally
            {
                EndTransfer();
            }

            await RefreshSelectedDirectoryAsync();
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
        private FileSystemEntry SelectedEntry()
        {
            return EntriesLV.SelectedItems.Count == 0
                ? null
                : EntriesLV.SelectedItems[0].Tag as FileSystemEntry;
        }

        /// <summary>
        /// Drops everything which belonged to a session.
        /// </summary>
        private void Clear()
        {
            DirectoriesTV.Nodes.Clear();
            EntriesLV.Items.Clear();

            EnableCommands(false);
            Status(string.Empty);
        }

        private void EnableCommands(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            UploadBTN.Enabled = enabled;
            NewFolderBTN.Enabled = enabled;
            DownloadBTN.Enabled = enabled && SelectedEntry() is { IsDirectory: false };
            DeleteBTN.Enabled = enabled && SelectedEntry() != null;
        }

        private void BeginTransfer()
        {
            TransferPB.Minimum = 0;
            TransferPB.Maximum = 100;
            TransferPB.Value = 0;
        }

        private void ReportTransfer(long copied, long total, string what)
        {
            if (IsDisposed)
            {
                return;
            }

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
