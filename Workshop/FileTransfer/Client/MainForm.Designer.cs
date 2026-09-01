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

namespace Quickstarts.FileTransferClient
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.MainPN = new System.Windows.Forms.Panel();
            this.EntriesLV = new System.Windows.Forms.ListView();
            this.NameCH = new System.Windows.Forms.ColumnHeader();
            this.KindCH = new System.Windows.Forms.ColumnHeader();
            this.SizeCH = new System.Windows.Forms.ColumnHeader();
            this.ModifiedCH = new System.Windows.Forms.ColumnHeader();
            this.PathCH = new System.Windows.Forms.ColumnHeader();
            this.DirectoriesTV = new System.Windows.Forms.TreeView();
            this.CommandsPN = new System.Windows.Forms.Panel();
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.DownloadBTN = new System.Windows.Forms.Button();
            this.UploadBTN = new System.Windows.Forms.Button();
            this.DeleteBTN = new System.Windows.Forms.Button();
            this.NewFolderBTN = new System.Windows.Forms.Button();
            this.NewFolderTB = new System.Windows.Forms.TextBox();
            this.NewFolderLB = new System.Windows.Forms.Label();
            this.TransferPB = new System.Windows.Forms.ProgressBar();
            this.TransferLB = new System.Windows.Forms.Label();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.CommandsPN.SuspendLayout();
            this.SuspendLayout();
            //
            // MenuBar
            //
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(884, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            //
            // ServerMI
            //
            this.ServerMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Server_DiscoverMI,
            this.Server_ConnectMI,
            this.Server_DisconnectMI});
            this.ServerMI.Name = "ServerMI";
            this.ServerMI.Size = new System.Drawing.Size(51, 20);
            this.ServerMI.Text = "Server";
            //
            // Server_DiscoverMI
            //
            this.Server_DiscoverMI.Name = "Server_DiscoverMI";
            this.Server_DiscoverMI.Size = new System.Drawing.Size(127, 22);
            this.Server_DiscoverMI.Text = "Discover...";
            this.Server_DiscoverMI.Click += new System.EventHandler(this.Server_DiscoverMI_Click);
            //
            // Server_ConnectMI
            //
            this.Server_ConnectMI.Name = "Server_ConnectMI";
            this.Server_ConnectMI.Size = new System.Drawing.Size(127, 22);
            this.Server_ConnectMI.Text = "Connect";
            this.Server_ConnectMI.Click += new System.EventHandler(this.Server_ConnectMI_ClickAsync);
            //
            // Server_DisconnectMI
            //
            this.Server_DisconnectMI.Name = "Server_DisconnectMI";
            this.Server_DisconnectMI.Size = new System.Drawing.Size(127, 22);
            this.Server_DisconnectMI.Text = "Disconnect";
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_Click);
            //
            // HelpMI
            //
            this.HelpMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Help_ContentsMI});
            this.HelpMI.Name = "HelpMI";
            this.HelpMI.Size = new System.Drawing.Size(40, 20);
            this.HelpMI.Text = "Help";
            //
            // Help_ContentsMI
            //
            this.Help_ContentsMI.Name = "Help_ContentsMI";
            this.Help_ContentsMI.Size = new System.Drawing.Size(152, 22);
            this.Help_ContentsMI.Text = "Contents";
            //
            // StatusBar
            //
            this.StatusBar.Location = new System.Drawing.Point(0, 524);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            //
            // MainPN
            //
            this.MainPN.Controls.Add(this.EntriesLV);
            this.MainPN.Controls.Add(this.DirectoriesTV);
            this.MainPN.Controls.Add(this.CommandsPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 122);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 402);
            this.MainPN.TabIndex = 3;
            //
            // EntriesLV
            //
            this.EntriesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.NameCH,
            this.KindCH,
            this.SizeCH,
            this.ModifiedCH,
            this.PathCH});
            this.EntriesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EntriesLV.FullRowSelect = true;
            this.EntriesLV.HideSelection = false;
            this.EntriesLV.Location = new System.Drawing.Point(282, 2);
            this.EntriesLV.MultiSelect = false;
            this.EntriesLV.Name = "EntriesLV";
            this.EntriesLV.Size = new System.Drawing.Size(600, 332);
            this.EntriesLV.TabIndex = 2;
            this.EntriesLV.UseCompatibleStateImageBehavior = false;
            this.EntriesLV.View = System.Windows.Forms.View.Details;
            this.EntriesLV.DoubleClick += new System.EventHandler(this.EntriesLV_DoubleClickAsync);
            this.EntriesLV.SelectedIndexChanged += new System.EventHandler(this.EntriesLV_SelectedIndexChanged);
            //
            // NameCH
            //
            this.NameCH.Text = "Name";
            this.NameCH.Width = 200;
            //
            // KindCH
            //
            this.KindCH.Text = "Type";
            this.KindCH.Width = 80;
            //
            // SizeCH
            //
            this.SizeCH.Text = "Size";
            this.SizeCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.SizeCH.Width = 90;
            //
            // ModifiedCH
            //
            this.ModifiedCH.Text = "Last Modified";
            this.ModifiedCH.Width = 140;
            //
            // PathCH
            //
            this.PathCH.Text = "Path";
            this.PathCH.Width = 260;
            //
            // DirectoriesTV
            //
            this.DirectoriesTV.Dock = System.Windows.Forms.DockStyle.Left;
            this.DirectoriesTV.HideSelection = false;
            this.DirectoriesTV.Location = new System.Drawing.Point(2, 2);
            this.DirectoriesTV.Name = "DirectoriesTV";
            this.DirectoriesTV.Size = new System.Drawing.Size(280, 332);
            this.DirectoriesTV.TabIndex = 1;
            this.DirectoriesTV.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.DirectoriesTV_BeforeExpandAsync);
            this.DirectoriesTV.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.DirectoriesTV_AfterSelectAsync);
            //
            // CommandsPN
            //
            this.CommandsPN.Controls.Add(this.RefreshBTN);
            this.CommandsPN.Controls.Add(this.DownloadBTN);
            this.CommandsPN.Controls.Add(this.UploadBTN);
            this.CommandsPN.Controls.Add(this.DeleteBTN);
            this.CommandsPN.Controls.Add(this.NewFolderLB);
            this.CommandsPN.Controls.Add(this.NewFolderTB);
            this.CommandsPN.Controls.Add(this.NewFolderBTN);
            this.CommandsPN.Controls.Add(this.TransferPB);
            this.CommandsPN.Controls.Add(this.TransferLB);
            this.CommandsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.CommandsPN.Location = new System.Drawing.Point(2, 334);
            this.CommandsPN.Name = "CommandsPN";
            this.CommandsPN.Size = new System.Drawing.Size(880, 68);
            this.CommandsPN.TabIndex = 3;
            //
            // RefreshBTN
            //
            this.RefreshBTN.Enabled = false;
            this.RefreshBTN.Location = new System.Drawing.Point(3, 6);
            this.RefreshBTN.Name = "RefreshBTN";
            this.RefreshBTN.Size = new System.Drawing.Size(90, 23);
            this.RefreshBTN.TabIndex = 0;
            this.RefreshBTN.Text = "Refresh";
            this.RefreshBTN.UseVisualStyleBackColor = true;
            this.RefreshBTN.Click += new System.EventHandler(this.RefreshBTN_ClickAsync);
            //
            // DownloadBTN
            //
            this.DownloadBTN.Enabled = false;
            this.DownloadBTN.Location = new System.Drawing.Point(99, 6);
            this.DownloadBTN.Name = "DownloadBTN";
            this.DownloadBTN.Size = new System.Drawing.Size(90, 23);
            this.DownloadBTN.TabIndex = 1;
            this.DownloadBTN.Text = "Download...";
            this.DownloadBTN.UseVisualStyleBackColor = true;
            this.DownloadBTN.Click += new System.EventHandler(this.DownloadBTN_ClickAsync);
            //
            // UploadBTN
            //
            this.UploadBTN.Enabled = false;
            this.UploadBTN.Location = new System.Drawing.Point(195, 6);
            this.UploadBTN.Name = "UploadBTN";
            this.UploadBTN.Size = new System.Drawing.Size(90, 23);
            this.UploadBTN.TabIndex = 2;
            this.UploadBTN.Text = "Upload...";
            this.UploadBTN.UseVisualStyleBackColor = true;
            this.UploadBTN.Click += new System.EventHandler(this.UploadBTN_ClickAsync);
            //
            // DeleteBTN
            //
            this.DeleteBTN.Enabled = false;
            this.DeleteBTN.Location = new System.Drawing.Point(291, 6);
            this.DeleteBTN.Name = "DeleteBTN";
            this.DeleteBTN.Size = new System.Drawing.Size(90, 23);
            this.DeleteBTN.TabIndex = 3;
            this.DeleteBTN.Text = "Delete";
            this.DeleteBTN.UseVisualStyleBackColor = true;
            this.DeleteBTN.Click += new System.EventHandler(this.DeleteBTN_ClickAsync);
            //
            // NewFolderLB
            //
            this.NewFolderLB.AutoSize = true;
            this.NewFolderLB.Location = new System.Drawing.Point(400, 11);
            this.NewFolderLB.Name = "NewFolderLB";
            this.NewFolderLB.Size = new System.Drawing.Size(66, 13);
            this.NewFolderLB.TabIndex = 4;
            this.NewFolderLB.Text = "Folder name";
            //
            // NewFolderTB
            //
            this.NewFolderTB.Location = new System.Drawing.Point(472, 8);
            this.NewFolderTB.Name = "NewFolderTB";
            this.NewFolderTB.Size = new System.Drawing.Size(160, 20);
            this.NewFolderTB.TabIndex = 5;
            //
            // NewFolderBTN
            //
            this.NewFolderBTN.Enabled = false;
            this.NewFolderBTN.Location = new System.Drawing.Point(638, 6);
            this.NewFolderBTN.Name = "NewFolderBTN";
            this.NewFolderBTN.Size = new System.Drawing.Size(100, 23);
            this.NewFolderBTN.TabIndex = 6;
            this.NewFolderBTN.Text = "Create Folder";
            this.NewFolderBTN.UseVisualStyleBackColor = true;
            this.NewFolderBTN.Click += new System.EventHandler(this.NewFolderBTN_ClickAsync);
            //
            // TransferPB
            //
            this.TransferPB.Location = new System.Drawing.Point(3, 38);
            this.TransferPB.Name = "TransferPB";
            this.TransferPB.Size = new System.Drawing.Size(378, 18);
            this.TransferPB.TabIndex = 7;
            //
            // TransferLB
            //
            this.TransferLB.AutoSize = true;
            this.TransferLB.Location = new System.Drawing.Point(400, 40);
            this.TransferLB.Name = "TransferLB";
            this.TransferLB.Size = new System.Drawing.Size(0, 13);
            this.TransferLB.TabIndex = 8;
            //
            // ConnectServerCTRL
            //
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 99);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(884, 23);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 4;
            this.ConnectServerCTRL.UserIdentity = null;
            this.ConnectServerCTRL.UseSecurity = true;
            this.ConnectServerCTRL.ConnectComplete += new System.EventHandler(this.Server_ConnectCompleteAsync);
            this.ConnectServerCTRL.ReconnectStarting += new System.EventHandler(this.Server_ReconnectStarting);
            this.ConnectServerCTRL.ReconnectComplete += new System.EventHandler(this.Server_ReconnectCompleteAsync);
            //
            // clientHeaderBranding1
            //
            this.clientHeaderBranding1.Dock = System.Windows.Forms.DockStyle.Top;
            this.clientHeaderBranding1.Location = new System.Drawing.Point(0, 24);
            this.clientHeaderBranding1.MaximumSize = new System.Drawing.Size(0, 75);
            this.clientHeaderBranding1.MinimumSize = new System.Drawing.Size(500, 75);
            this.clientHeaderBranding1.Name = "clientHeaderBranding1";
            this.clientHeaderBranding1.Size = new System.Drawing.Size(884, 75);
            this.clientHeaderBranding1.TabIndex = 5;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 546);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart File Transfer Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.CommandsPN.ResumeLayout(false);
            this.CommandsPN.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.ListView EntriesLV;
        private System.Windows.Forms.ColumnHeader NameCH;
        private System.Windows.Forms.ColumnHeader KindCH;
        private System.Windows.Forms.ColumnHeader SizeCH;
        private System.Windows.Forms.ColumnHeader ModifiedCH;
        private System.Windows.Forms.ColumnHeader PathCH;
        private System.Windows.Forms.TreeView DirectoriesTV;
        private System.Windows.Forms.Panel CommandsPN;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.Button DownloadBTN;
        private System.Windows.Forms.Button UploadBTN;
        private System.Windows.Forms.Button DeleteBTN;
        private System.Windows.Forms.Button NewFolderBTN;
        private System.Windows.Forms.TextBox NewFolderTB;
        private System.Windows.Forms.Label NewFolderLB;
        private System.Windows.Forms.ProgressBar TransferPB;
        private System.Windows.Forms.Label TransferLB;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
