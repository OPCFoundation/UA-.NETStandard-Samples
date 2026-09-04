/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.RuntimeNodeSets.Client
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
            this.components = new System.ComponentModel.Container();
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.ActionStatusLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ModelGB = new System.Windows.Forms.GroupBox();
            this.NodesLV = new System.Windows.Forms.ListView();
            this.NodeCH = new System.Windows.Forms.ColumnHeader();
            this.NodeIdCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.ControlPN = new System.Windows.Forms.Panel();
            this.RevisionLB = new System.Windows.Forms.Label();
            this.RevisionCB = new System.Windows.Forms.ComboBox();
            this.ModeLB = new System.Windows.Forms.Label();
            this.ModeCB = new System.Windows.Forms.ComboBox();
            this.LoadBTN = new System.Windows.Forms.Button();
            this.ReloadBTN = new System.Windows.Forms.Button();
            this.RemoveBTN = new System.Windows.Forms.Button();
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.WatchGB = new System.Windows.Forms.GroupBox();
            this.WatchLV = new System.Windows.Forms.ListView();
            this.WatchTimeCH = new System.Windows.Forms.ColumnHeader();
            this.WatchValueCH = new System.Windows.Forms.ColumnHeader();
            this.WatchStatusCH = new System.Windows.Forms.ColumnHeader();
            this.WatchPN = new System.Windows.Forms.Panel();
            this.WatchBTN = new System.Windows.Forms.Button();
            this.StopWatchingBTN = new System.Windows.Forms.Button();
            this.StateLB = new System.Windows.Forms.Label();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.ModelGB.SuspendLayout();
            this.ControlPN.SuspendLayout();
            this.WatchGB.SuspendLayout();
            this.WatchPN.SuspendLayout();
            this.SuspendLayout();
            //
            // MenuBar
            //
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerMI});
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
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_ClickAsync);
            //
            // StatusBar
            //
            this.StatusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ActionStatusLB});
            this.StatusBar.Location = new System.Drawing.Point(0, 594);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            //
            // ActionStatusLB
            //
            this.ActionStatusLB.Name = "ActionStatusLB";
            this.ActionStatusLB.Size = new System.Drawing.Size(0, 17);
            //
            // MainPN
            //
            this.MainPN.Controls.Add(this.ModelGB);
            this.MainPN.Controls.Add(this.WatchGB);
            this.MainPN.Controls.Add(this.ControlPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 148);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(4);
            this.MainPN.Size = new System.Drawing.Size(884, 446);
            this.MainPN.TabIndex = 4;
            //
            // ControlPN
            //
            this.ControlPN.Controls.Add(this.RevisionLB);
            this.ControlPN.Controls.Add(this.RevisionCB);
            this.ControlPN.Controls.Add(this.ModeLB);
            this.ControlPN.Controls.Add(this.ModeCB);
            this.ControlPN.Controls.Add(this.LoadBTN);
            this.ControlPN.Controls.Add(this.ReloadBTN);
            this.ControlPN.Controls.Add(this.RemoveBTN);
            this.ControlPN.Controls.Add(this.RefreshBTN);
            this.ControlPN.Controls.Add(this.StateLB);
            this.ControlPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.ControlPN.Location = new System.Drawing.Point(4, 4);
            this.ControlPN.Name = "ControlPN";
            this.ControlPN.Size = new System.Drawing.Size(876, 62);
            this.ControlPN.TabIndex = 0;
            //
            // RevisionLB
            //
            this.RevisionLB.AutoSize = true;
            this.RevisionLB.Location = new System.Drawing.Point(4, 11);
            this.RevisionLB.Name = "RevisionLB";
            this.RevisionLB.Size = new System.Drawing.Size(50, 13);
            this.RevisionLB.TabIndex = 0;
            this.RevisionLB.Text = "Revision";
            //
            // RevisionCB
            //
            this.RevisionCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.RevisionCB.Location = new System.Drawing.Point(60, 8);
            this.RevisionCB.Name = "RevisionCB";
            this.RevisionCB.Size = new System.Drawing.Size(100, 21);
            this.RevisionCB.TabIndex = 1;
            //
            // ModeLB
            //
            this.ModeLB.AutoSize = true;
            this.ModeLB.Location = new System.Drawing.Point(176, 11);
            this.ModeLB.Name = "ModeLB";
            this.ModeLB.Size = new System.Drawing.Size(70, 13);
            this.ModeLB.TabIndex = 2;
            this.ModeLB.Text = "Reload mode";
            //
            // ModeCB
            //
            this.ModeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ModeCB.Location = new System.Drawing.Point(252, 8);
            this.ModeCB.Name = "ModeCB";
            this.ModeCB.Size = new System.Drawing.Size(140, 21);
            this.ModeCB.TabIndex = 3;
            //
            // LoadBTN
            //
            this.LoadBTN.Location = new System.Drawing.Point(408, 6);
            this.LoadBTN.Name = "LoadBTN";
            this.LoadBTN.Size = new System.Drawing.Size(80, 25);
            this.LoadBTN.TabIndex = 4;
            this.LoadBTN.Text = "Load";
            this.LoadBTN.UseVisualStyleBackColor = true;
            this.LoadBTN.Click += new System.EventHandler(this.LoadBTN_ClickAsync);
            //
            // ReloadBTN
            //
            this.ReloadBTN.Location = new System.Drawing.Point(494, 6);
            this.ReloadBTN.Name = "ReloadBTN";
            this.ReloadBTN.Size = new System.Drawing.Size(80, 25);
            this.ReloadBTN.TabIndex = 5;
            this.ReloadBTN.Text = "Reload";
            this.ReloadBTN.UseVisualStyleBackColor = true;
            this.ReloadBTN.Click += new System.EventHandler(this.ReloadBTN_ClickAsync);
            //
            // RemoveBTN
            //
            this.RemoveBTN.Location = new System.Drawing.Point(580, 6);
            this.RemoveBTN.Name = "RemoveBTN";
            this.RemoveBTN.Size = new System.Drawing.Size(80, 25);
            this.RemoveBTN.TabIndex = 6;
            this.RemoveBTN.Text = "Remove";
            this.RemoveBTN.UseVisualStyleBackColor = true;
            this.RemoveBTN.Click += new System.EventHandler(this.RemoveBTN_ClickAsync);
            //
            // RefreshBTN
            //
            this.RefreshBTN.Location = new System.Drawing.Point(666, 6);
            this.RefreshBTN.Name = "RefreshBTN";
            this.RefreshBTN.Size = new System.Drawing.Size(80, 25);
            this.RefreshBTN.TabIndex = 7;
            this.RefreshBTN.Text = "Browse again";
            this.RefreshBTN.UseVisualStyleBackColor = true;
            this.RefreshBTN.Click += new System.EventHandler(this.RefreshBTN_ClickAsync);
            //
            // StateLB
            //
            this.StateLB.AutoSize = true;
            this.StateLB.Location = new System.Drawing.Point(4, 40);
            this.StateLB.Name = "StateLB";
            this.StateLB.Size = new System.Drawing.Size(0, 13);
            this.StateLB.TabIndex = 8;
            //
            // ModelGB
            //
            this.ModelGB.Controls.Add(this.NodesLV);
            this.ModelGB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ModelGB.Location = new System.Drawing.Point(4, 66);
            this.ModelGB.Name = "ModelGB";
            this.ModelGB.Size = new System.Drawing.Size(876, 230);
            this.ModelGB.TabIndex = 1;
            this.ModelGB.TabStop = false;
            this.ModelGB.Text = "The vendor model, as it is published right now";
            //
            // NodesLV
            //
            this.NodesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.NodeCH,
            this.NodeIdCH,
            this.ValueCH});
            this.NodesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodesLV.FullRowSelect = true;
            this.NodesLV.Location = new System.Drawing.Point(3, 16);
            this.NodesLV.MultiSelect = false;
            this.NodesLV.Name = "NodesLV";
            this.NodesLV.Size = new System.Drawing.Size(870, 211);
            this.NodesLV.TabIndex = 0;
            this.NodesLV.UseCompatibleStateImageBehavior = false;
            this.NodesLV.View = System.Windows.Forms.View.Details;
            //
            // NodeCH
            //
            this.NodeCH.Text = "Node";
            this.NodeCH.Width = 320;
            //
            // NodeIdCH
            //
            this.NodeIdCH.Text = "NodeId";
            this.NodeIdCH.Width = 260;
            //
            // ValueCH
            //
            this.ValueCH.Text = "Value";
            this.ValueCH.Width = 240;
            //
            // WatchGB
            //
            this.WatchGB.Controls.Add(this.WatchLV);
            this.WatchGB.Controls.Add(this.WatchPN);
            this.WatchGB.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.WatchGB.Location = new System.Drawing.Point(4, 296);
            this.WatchGB.Name = "WatchGB";
            this.WatchGB.Size = new System.Drawing.Size(876, 146);
            this.WatchGB.TabIndex = 2;
            this.WatchGB.TabStop = false;
            this.WatchGB.Text = "A MonitoredItem on Conveyor1/Speed, across the reload";
            //
            // WatchLV
            //
            this.WatchLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.WatchTimeCH,
            this.WatchValueCH,
            this.WatchStatusCH});
            this.WatchLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.WatchLV.FullRowSelect = true;
            this.WatchLV.Location = new System.Drawing.Point(3, 16);
            this.WatchLV.Name = "WatchLV";
            this.WatchLV.Size = new System.Drawing.Size(870, 92);
            this.WatchLV.TabIndex = 0;
            this.WatchLV.UseCompatibleStateImageBehavior = false;
            this.WatchLV.View = System.Windows.Forms.View.Details;
            //
            // WatchTimeCH
            //
            this.WatchTimeCH.Text = "Received";
            this.WatchTimeCH.Width = 160;
            //
            // WatchValueCH
            //
            this.WatchValueCH.Text = "Value";
            this.WatchValueCH.Width = 200;
            //
            // WatchStatusCH
            //
            this.WatchStatusCH.Text = "Status";
            this.WatchStatusCH.Width = 460;
            //
            // WatchPN
            //
            this.WatchPN.Controls.Add(this.WatchBTN);
            this.WatchPN.Controls.Add(this.StopWatchingBTN);
            this.WatchPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.WatchPN.Location = new System.Drawing.Point(3, 108);
            this.WatchPN.Name = "WatchPN";
            this.WatchPN.Size = new System.Drawing.Size(870, 35);
            this.WatchPN.TabIndex = 1;
            //
            // WatchBTN
            //
            this.WatchBTN.Location = new System.Drawing.Point(1, 4);
            this.WatchBTN.Name = "WatchBTN";
            this.WatchBTN.Size = new System.Drawing.Size(120, 25);
            this.WatchBTN.TabIndex = 0;
            this.WatchBTN.Text = "Watch the speed";
            this.WatchBTN.UseVisualStyleBackColor = true;
            this.WatchBTN.Click += new System.EventHandler(this.WatchBTN_ClickAsync);
            //
            // StopWatchingBTN
            //
            this.StopWatchingBTN.Location = new System.Drawing.Point(127, 4);
            this.StopWatchingBTN.Name = "StopWatchingBTN";
            this.StopWatchingBTN.Size = new System.Drawing.Size(120, 25);
            this.StopWatchingBTN.TabIndex = 1;
            this.StopWatchingBTN.Text = "Stop watching";
            this.StopWatchingBTN.UseVisualStyleBackColor = true;
            this.StopWatchingBTN.Click += new System.EventHandler(this.StopWatchingBTN_ClickAsync);
            //
            // ConnectServerCTRL
            //
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 99);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 49);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 49);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(884, 49);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 3;
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
            this.ClientSize = new System.Drawing.Size(884, 616);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart RuntimeNodeSets Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.ModelGB.ResumeLayout(false);
            this.ControlPN.ResumeLayout(false);
            this.ControlPN.PerformLayout();
            this.WatchGB.ResumeLayout(false);
            this.WatchPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripStatusLabel ActionStatusLB;
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Panel ControlPN;
        private System.Windows.Forms.Label RevisionLB;
        private System.Windows.Forms.ComboBox RevisionCB;
        private System.Windows.Forms.Label ModeLB;
        private System.Windows.Forms.ComboBox ModeCB;
        private System.Windows.Forms.Button LoadBTN;
        private System.Windows.Forms.Button ReloadBTN;
        private System.Windows.Forms.Button RemoveBTN;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.Label StateLB;
        private System.Windows.Forms.GroupBox ModelGB;
        private System.Windows.Forms.ListView NodesLV;
        private System.Windows.Forms.ColumnHeader NodeCH;
        private System.Windows.Forms.ColumnHeader NodeIdCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.GroupBox WatchGB;
        private System.Windows.Forms.ListView WatchLV;
        private System.Windows.Forms.ColumnHeader WatchTimeCH;
        private System.Windows.Forms.ColumnHeader WatchValueCH;
        private System.Windows.Forms.ColumnHeader WatchStatusCH;
        private System.Windows.Forms.Panel WatchPN;
        private System.Windows.Forms.Button WatchBTN;
        private System.Windows.Forms.Button StopWatchingBTN;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
