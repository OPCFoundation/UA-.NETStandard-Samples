/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.AliasNames.Client
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.ActionStatusLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.IdentityPN = new System.Windows.Forms.Panel();
            this.IdentityHintLB = new System.Windows.Forms.Label();
            this.IdentityCB = new System.Windows.Forms.ComboBox();
            this.IdentityLB = new System.Windows.Forms.Label();
            this.MainPN = new System.Windows.Forms.Panel();
            this.PlantGB = new System.Windows.Forms.GroupBox();
            this.PlantLV = new System.Windows.Forms.ListView();
            this.PathCH = new System.Windows.Forms.ColumnHeader();
            this.NodeIdCH = new System.Windows.Forms.ColumnHeader();
            this.PlantValueCH = new System.Windows.Forms.ColumnHeader();
            this.TagNameCH = new System.Windows.Forms.ColumnHeader();
            this.PlantButtonsPN = new System.Windows.Forms.Panel();
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.NewAliasLB = new System.Windows.Forms.Label();
            this.NewAliasTB = new System.Windows.Forms.TextBox();
            this.AddAliasBTN = new System.Windows.Forms.Button();
            this.AliasGB = new System.Windows.Forms.GroupBox();
            this.AliasLV = new System.Windows.Forms.ListView();
            this.AliasCH = new System.Windows.Forms.ColumnHeader();
            this.ResolvesToCH = new System.Windows.Forms.ColumnHeader();
            this.AliasValueCH = new System.Windows.Forms.ColumnHeader();
            this.AliasCategoryCH = new System.Windows.Forms.ColumnHeader();
            this.AliasServerCH = new System.Windows.Forms.ColumnHeader();
            this.SearchPN = new System.Windows.Forms.Panel();
            this.CategoryLB = new System.Windows.Forms.Label();
            this.CategoryCB = new System.Windows.Forms.ComboBox();
            this.PatternLB = new System.Windows.Forms.Label();
            this.PatternTB = new System.Windows.Forms.TextBox();
            this.FindBTN = new System.Windows.Forms.Button();
            this.FindVerboseBTN = new System.Windows.Forms.Button();
            this.LastChangeLB = new System.Windows.Forms.Label();
            this.AliasButtonsPN = new System.Windows.Forms.Panel();
            this.DeleteAliasBTN = new System.Windows.Forms.Button();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.IdentityPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.PlantGB.SuspendLayout();
            this.PlantButtonsPN.SuspendLayout();
            this.AliasGB.SuspendLayout();
            this.SearchPN.SuspendLayout();
            this.AliasButtonsPN.SuspendLayout();
            this.SuspendLayout();
            //
            // MenuBar
            //
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(944, 24);
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
            this.Help_ContentsMI.Size = new System.Drawing.Size(118, 22);
            this.Help_ContentsMI.Text = "Contents";
            //
            // StatusBar
            //
            this.StatusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ActionStatusLB});
            this.StatusBar.Location = new System.Drawing.Point(0, 654);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(944, 22);
            this.StatusBar.TabIndex = 2;
            //
            // ActionStatusLB
            //
            this.ActionStatusLB.Name = "ActionStatusLB";
            this.ActionStatusLB.Size = new System.Drawing.Size(0, 17);
            //
            // IdentityPN
            //
            this.IdentityPN.Controls.Add(this.IdentityHintLB);
            this.IdentityPN.Controls.Add(this.IdentityCB);
            this.IdentityPN.Controls.Add(this.IdentityLB);
            this.IdentityPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.IdentityPN.Location = new System.Drawing.Point(0, 99);
            this.IdentityPN.Name = "IdentityPN";
            this.IdentityPN.Size = new System.Drawing.Size(944, 30);
            this.IdentityPN.TabIndex = 6;
            //
            // IdentityLB
            //
            this.IdentityLB.AutoSize = true;
            this.IdentityLB.Location = new System.Drawing.Point(6, 8);
            this.IdentityLB.Name = "IdentityLB";
            this.IdentityLB.Size = new System.Drawing.Size(66, 13);
            this.IdentityLB.TabIndex = 0;
            this.IdentityLB.Text = "Sign in as";
            //
            // IdentityCB
            //
            this.IdentityCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.IdentityCB.FormattingEnabled = true;
            this.IdentityCB.Location = new System.Drawing.Point(78, 4);
            this.IdentityCB.Name = "IdentityCB";
            this.IdentityCB.Size = new System.Drawing.Size(160, 21);
            this.IdentityCB.TabIndex = 1;
            //
            // IdentityHintLB
            //
            this.IdentityHintLB.AutoSize = true;
            this.IdentityHintLB.Location = new System.Drawing.Point(250, 8);
            this.IdentityHintLB.Name = "IdentityHintLB";
            this.IdentityHintLB.Size = new System.Drawing.Size(0, 13);
            this.IdentityHintLB.TabIndex = 2;
            //
            // MainPN
            //
            this.MainPN.Controls.Add(this.PlantGB);
            this.MainPN.Controls.Add(this.AliasGB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 152);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(944, 502);
            this.MainPN.TabIndex = 3;
            //
            // PlantGB
            //
            this.PlantGB.Controls.Add(this.PlantLV);
            this.PlantGB.Controls.Add(this.PlantButtonsPN);
            this.PlantGB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PlantGB.Location = new System.Drawing.Point(2, 2);
            this.PlantGB.Name = "PlantGB";
            this.PlantGB.Size = new System.Drawing.Size(940, 232);
            this.PlantGB.TabIndex = 0;
            this.PlantGB.TabStop = false;
            this.PlantGB.Text = "The plant, as a client browses it - the last column is the name the alias inventory knows each node by";
            //
            // PlantLV
            //
            this.PlantLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.PathCH,
            this.NodeIdCH,
            this.PlantValueCH,
            this.TagNameCH});
            this.PlantLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PlantLV.FullRowSelect = true;
            this.PlantLV.HideSelection = false;
            this.PlantLV.Location = new System.Drawing.Point(3, 16);
            this.PlantLV.MultiSelect = false;
            this.PlantLV.Name = "PlantLV";
            this.PlantLV.Size = new System.Drawing.Size(934, 181);
            this.PlantLV.TabIndex = 0;
            this.PlantLV.UseCompatibleStateImageBehavior = false;
            this.PlantLV.View = System.Windows.Forms.View.Details;
            //
            // PathCH
            //
            this.PathCH.Text = "Browse path";
            this.PathCH.Width = 280;
            //
            // NodeIdCH
            //
            this.NodeIdCH.Text = "NodeId";
            this.NodeIdCH.Width = 140;
            //
            // PlantValueCH
            //
            this.PlantValueCH.Text = "Value";
            this.PlantValueCH.Width = 160;
            //
            // TagNameCH
            //
            this.TagNameCH.Text = "Alias name (Part 17)";
            this.TagNameCH.Width = 320;
            //
            // PlantButtonsPN
            //
            this.PlantButtonsPN.Controls.Add(this.RefreshBTN);
            this.PlantButtonsPN.Controls.Add(this.NewAliasLB);
            this.PlantButtonsPN.Controls.Add(this.NewAliasTB);
            this.PlantButtonsPN.Controls.Add(this.AddAliasBTN);
            this.PlantButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.PlantButtonsPN.Location = new System.Drawing.Point(3, 197);
            this.PlantButtonsPN.Name = "PlantButtonsPN";
            this.PlantButtonsPN.Size = new System.Drawing.Size(934, 32);
            this.PlantButtonsPN.TabIndex = 1;
            //
            // RefreshBTN
            //
            this.RefreshBTN.Enabled = false;
            this.RefreshBTN.Location = new System.Drawing.Point(3, 4);
            this.RefreshBTN.Name = "RefreshBTN";
            this.RefreshBTN.Size = new System.Drawing.Size(90, 23);
            this.RefreshBTN.TabIndex = 0;
            this.RefreshBTN.Text = "Refresh";
            this.RefreshBTN.UseVisualStyleBackColor = true;
            this.RefreshBTN.Click += new System.EventHandler(this.RefreshBTN_ClickAsync);
            //
            // NewAliasLB
            //
            this.NewAliasLB.AutoSize = true;
            this.NewAliasLB.Location = new System.Drawing.Point(110, 9);
            this.NewAliasLB.Name = "NewAliasLB";
            this.NewAliasLB.Size = new System.Drawing.Size(120, 13);
            this.NewAliasLB.TabIndex = 1;
            this.NewAliasLB.Text = "Name the selected node";
            //
            // NewAliasTB
            //
            this.NewAliasTB.Location = new System.Drawing.Point(240, 6);
            this.NewAliasTB.Name = "NewAliasTB";
            this.NewAliasTB.Size = new System.Drawing.Size(140, 20);
            this.NewAliasTB.TabIndex = 2;
            this.NewAliasTB.Text = "TIC101_ALT";
            //
            // AddAliasBTN
            //
            this.AddAliasBTN.Enabled = false;
            this.AddAliasBTN.Location = new System.Drawing.Point(386, 4);
            this.AddAliasBTN.Name = "AddAliasBTN";
            this.AddAliasBTN.Size = new System.Drawing.Size(210, 23);
            this.AddAliasBTN.TabIndex = 3;
            this.AddAliasBTN.Text = "Add to the selected category";
            this.AddAliasBTN.UseVisualStyleBackColor = true;
            this.AddAliasBTN.Click += new System.EventHandler(this.AddAliasBTN_ClickAsync);
            //
            // AliasGB
            //
            this.AliasGB.Controls.Add(this.AliasLV);
            this.AliasGB.Controls.Add(this.AliasButtonsPN);
            this.AliasGB.Controls.Add(this.SearchPN);
            this.AliasGB.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.AliasGB.Location = new System.Drawing.Point(2, 234);
            this.AliasGB.Name = "AliasGB";
            this.AliasGB.Size = new System.Drawing.Size(940, 268);
            this.AliasGB.TabIndex = 1;
            this.AliasGB.TabStop = false;
            this.AliasGB.Text = "Alias name search (Part 17) - a name, or a pattern over names, answered from an index beside the address space";
            //
            // SearchPN
            //
            this.SearchPN.Controls.Add(this.CategoryLB);
            this.SearchPN.Controls.Add(this.CategoryCB);
            this.SearchPN.Controls.Add(this.PatternLB);
            this.SearchPN.Controls.Add(this.PatternTB);
            this.SearchPN.Controls.Add(this.FindBTN);
            this.SearchPN.Controls.Add(this.FindVerboseBTN);
            this.SearchPN.Controls.Add(this.LastChangeLB);
            this.SearchPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.SearchPN.Location = new System.Drawing.Point(3, 16);
            this.SearchPN.Name = "SearchPN";
            this.SearchPN.Size = new System.Drawing.Size(934, 32);
            this.SearchPN.TabIndex = 0;
            //
            // CategoryLB
            //
            this.CategoryLB.AutoSize = true;
            this.CategoryLB.Location = new System.Drawing.Point(3, 9);
            this.CategoryLB.Name = "CategoryLB";
            this.CategoryLB.Size = new System.Drawing.Size(50, 13);
            this.CategoryLB.TabIndex = 0;
            this.CategoryLB.Text = "Category";
            //
            // CategoryCB
            //
            this.CategoryCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.CategoryCB.FormattingEnabled = true;
            this.CategoryCB.Location = new System.Drawing.Point(59, 5);
            this.CategoryCB.Name = "CategoryCB";
            this.CategoryCB.Size = new System.Drawing.Size(230, 21);
            this.CategoryCB.TabIndex = 1;
            this.CategoryCB.SelectedIndexChanged += new System.EventHandler(this.CategoryCB_SelectedIndexChangedAsync);
            //
            // PatternLB
            //
            this.PatternLB.AutoSize = true;
            this.PatternLB.Location = new System.Drawing.Point(300, 9);
            this.PatternLB.Name = "PatternLB";
            this.PatternLB.Size = new System.Drawing.Size(42, 13);
            this.PatternLB.TabIndex = 2;
            this.PatternLB.Text = "Pattern";
            //
            // PatternTB
            //
            this.PatternTB.Location = new System.Drawing.Point(348, 5);
            this.PatternTB.Name = "PatternTB";
            this.PatternTB.Size = new System.Drawing.Size(120, 20);
            this.PatternTB.TabIndex = 3;
            this.PatternTB.Text = "%";
            //
            // FindBTN
            //
            this.FindBTN.Enabled = false;
            this.FindBTN.Location = new System.Drawing.Point(474, 3);
            this.FindBTN.Name = "FindBTN";
            this.FindBTN.Size = new System.Drawing.Size(90, 23);
            this.FindBTN.TabIndex = 4;
            this.FindBTN.Text = "Find";
            this.FindBTN.UseVisualStyleBackColor = true;
            this.FindBTN.Click += new System.EventHandler(this.FindBTN_ClickAsync);
            //
            // FindVerboseBTN
            //
            this.FindVerboseBTN.Enabled = false;
            this.FindVerboseBTN.Location = new System.Drawing.Point(570, 3);
            this.FindVerboseBTN.Name = "FindVerboseBTN";
            this.FindVerboseBTN.Size = new System.Drawing.Size(120, 23);
            this.FindVerboseBTN.TabIndex = 5;
            this.FindVerboseBTN.Text = "Find verbose";
            this.FindVerboseBTN.UseVisualStyleBackColor = true;
            this.FindVerboseBTN.Click += new System.EventHandler(this.FindVerboseBTN_ClickAsync);
            //
            // LastChangeLB
            //
            this.LastChangeLB.AutoSize = true;
            this.LastChangeLB.Location = new System.Drawing.Point(700, 9);
            this.LastChangeLB.Name = "LastChangeLB";
            this.LastChangeLB.Size = new System.Drawing.Size(0, 13);
            this.LastChangeLB.TabIndex = 6;
            //
            // AliasLV
            //
            this.AliasLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.AliasCH,
            this.ResolvesToCH,
            this.AliasValueCH,
            this.AliasCategoryCH,
            this.AliasServerCH});
            this.AliasLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AliasLV.FullRowSelect = true;
            this.AliasLV.HideSelection = false;
            this.AliasLV.Location = new System.Drawing.Point(3, 48);
            this.AliasLV.MultiSelect = false;
            this.AliasLV.Name = "AliasLV";
            this.AliasLV.Size = new System.Drawing.Size(934, 185);
            this.AliasLV.TabIndex = 1;
            this.AliasLV.UseCompatibleStateImageBehavior = false;
            this.AliasLV.View = System.Windows.Forms.View.Details;
            //
            // AliasCH
            //
            this.AliasCH.Text = "Alias name";
            this.AliasCH.Width = 180;
            //
            // ResolvesToCH
            //
            this.ResolvesToCH.Text = "Resolves to";
            this.ResolvesToCH.Width = 140;
            //
            // AliasValueCH
            //
            this.AliasValueCH.Text = "Value of that node";
            this.AliasValueCH.Width = 160;
            //
            // AliasCategoryCH
            //
            this.AliasCategoryCH.Text = "Category (verbose only)";
            this.AliasCategoryCH.Width = 180;
            //
            // AliasServerCH
            //
            this.AliasServerCH.Text = "Remote server (verbose only)";
            this.AliasServerCH.Width = 200;
            //
            // AliasButtonsPN
            //
            this.AliasButtonsPN.Controls.Add(this.DeleteAliasBTN);
            this.AliasButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.AliasButtonsPN.Location = new System.Drawing.Point(3, 233);
            this.AliasButtonsPN.Name = "AliasButtonsPN";
            this.AliasButtonsPN.Size = new System.Drawing.Size(934, 32);
            this.AliasButtonsPN.TabIndex = 2;
            //
            // DeleteAliasBTN
            //
            this.DeleteAliasBTN.Enabled = false;
            this.DeleteAliasBTN.Location = new System.Drawing.Point(3, 4);
            this.DeleteAliasBTN.Name = "DeleteAliasBTN";
            this.DeleteAliasBTN.Size = new System.Drawing.Size(210, 23);
            this.DeleteAliasBTN.TabIndex = 0;
            this.DeleteAliasBTN.Text = "Delete the selected alias";
            this.DeleteAliasBTN.UseVisualStyleBackColor = true;
            this.DeleteAliasBTN.Click += new System.EventHandler(this.DeleteAliasBTN_ClickAsync);
            //
            // ConnectServerCTRL
            //
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 129);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(944, 23);
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
            this.clientHeaderBranding1.Size = new System.Drawing.Size(944, 75);
            this.clientHeaderBranding1.TabIndex = 5;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(944, 676);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.IdentityPN);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart AliasNames Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.IdentityPN.ResumeLayout(false);
            this.IdentityPN.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.PlantGB.ResumeLayout(false);
            this.PlantButtonsPN.ResumeLayout(false);
            this.PlantButtonsPN.PerformLayout();
            this.AliasGB.ResumeLayout(false);
            this.SearchPN.ResumeLayout(false);
            this.SearchPN.PerformLayout();
            this.AliasButtonsPN.ResumeLayout(false);
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
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.Panel IdentityPN;
        private System.Windows.Forms.Label IdentityLB;
        private System.Windows.Forms.ComboBox IdentityCB;
        private System.Windows.Forms.Label IdentityHintLB;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.GroupBox PlantGB;
        private System.Windows.Forms.ListView PlantLV;
        private System.Windows.Forms.ColumnHeader PathCH;
        private System.Windows.Forms.ColumnHeader NodeIdCH;
        private System.Windows.Forms.ColumnHeader PlantValueCH;
        private System.Windows.Forms.ColumnHeader TagNameCH;
        private System.Windows.Forms.Panel PlantButtonsPN;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.Label NewAliasLB;
        private System.Windows.Forms.TextBox NewAliasTB;
        private System.Windows.Forms.Button AddAliasBTN;
        private System.Windows.Forms.GroupBox AliasGB;
        private System.Windows.Forms.ListView AliasLV;
        private System.Windows.Forms.ColumnHeader AliasCH;
        private System.Windows.Forms.ColumnHeader ResolvesToCH;
        private System.Windows.Forms.ColumnHeader AliasValueCH;
        private System.Windows.Forms.ColumnHeader AliasCategoryCH;
        private System.Windows.Forms.ColumnHeader AliasServerCH;
        private System.Windows.Forms.Panel SearchPN;
        private System.Windows.Forms.Label CategoryLB;
        private System.Windows.Forms.ComboBox CategoryCB;
        private System.Windows.Forms.Label PatternLB;
        private System.Windows.Forms.TextBox PatternTB;
        private System.Windows.Forms.Button FindBTN;
        private System.Windows.Forms.Button FindVerboseBTN;
        private System.Windows.Forms.Label LastChangeLB;
        private System.Windows.Forms.Panel AliasButtonsPN;
        private System.Windows.Forms.Button DeleteAliasBTN;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
