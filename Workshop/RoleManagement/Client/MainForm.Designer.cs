/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.RoleManagement.Client
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
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.IdentityPN = new System.Windows.Forms.Panel();
            this.IdentityHintLB = new System.Windows.Forms.Label();
            this.IdentityCB = new System.Windows.Forms.ComboBox();
            this.IdentityLB = new System.Windows.Forms.Label();
            this.MainPN = new System.Windows.Forms.Panel();
            this.NodesGB = new System.Windows.Forms.GroupBox();
            this.NodesLV = new System.Windows.Forms.ListView();
            this.NodeCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.StatusCH = new System.Windows.Forms.ColumnHeader();
            this.PermissionsCH = new System.Windows.Forms.ColumnHeader();
            this.NodesButtonsPN = new System.Windows.Forms.Panel();
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.WriteValueTB = new System.Windows.Forms.TextBox();
            this.WriteBTN = new System.Windows.Forms.Button();
            this.ResetBTN = new System.Windows.Forms.Button();
            this.RolesGB = new System.Windows.Forms.GroupBox();
            this.RolesLV = new System.Windows.Forms.ListView();
            this.RoleCH = new System.Windows.Forms.ColumnHeader();
            this.GrantedCH = new System.Windows.Forms.ColumnHeader();
            this.IdentitiesCH = new System.Windows.Forms.ColumnHeader();
            this.RolesButtonsPN = new System.Windows.Forms.Panel();
            this.RoleUserTB = new System.Windows.Forms.TextBox();
            this.AddIdentityBTN = new System.Windows.Forms.Button();
            this.RemoveIdentityBTN = new System.Windows.Forms.Button();
            this.NewRoleTB = new System.Windows.Forms.TextBox();
            this.AddRoleBTN = new System.Windows.Forms.Button();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.IdentityPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.NodesGB.SuspendLayout();
            this.NodesButtonsPN.SuspendLayout();
            this.RolesGB.SuspendLayout();
            this.RolesButtonsPN.SuspendLayout();
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
            this.Help_ContentsMI.Size = new System.Drawing.Size(118, 22);
            this.Help_ContentsMI.Text = "Contents";
            //
            // StatusBar
            //
            this.StatusBar.Location = new System.Drawing.Point(0, 594);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            //
            // IdentityPN
            //
            this.IdentityPN.Controls.Add(this.IdentityHintLB);
            this.IdentityPN.Controls.Add(this.IdentityCB);
            this.IdentityPN.Controls.Add(this.IdentityLB);
            this.IdentityPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.IdentityPN.Location = new System.Drawing.Point(0, 99);
            this.IdentityPN.Name = "IdentityPN";
            this.IdentityPN.Size = new System.Drawing.Size(884, 30);
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
            this.MainPN.Controls.Add(this.NodesGB);
            this.MainPN.Controls.Add(this.RolesGB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 152);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 442);
            this.MainPN.TabIndex = 3;
            //
            // NodesGB
            //
            this.NodesGB.Controls.Add(this.NodesLV);
            this.NodesGB.Controls.Add(this.NodesButtonsPN);
            this.NodesGB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodesGB.Location = new System.Drawing.Point(2, 2);
            this.NodesGB.Name = "NodesGB";
            this.NodesGB.Size = new System.Drawing.Size(880, 232);
            this.NodesGB.TabIndex = 0;
            this.NodesGB.TabStop = false;
            this.NodesGB.Text = "The machine, as this session sees it";
            //
            // NodesLV
            //
            this.NodesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.NodeCH,
            this.ValueCH,
            this.StatusCH,
            this.PermissionsCH});
            this.NodesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodesLV.FullRowSelect = true;
            this.NodesLV.HideSelection = false;
            this.NodesLV.Location = new System.Drawing.Point(3, 16);
            this.NodesLV.MultiSelect = false;
            this.NodesLV.Name = "NodesLV";
            this.NodesLV.Size = new System.Drawing.Size(874, 181);
            this.NodesLV.TabIndex = 0;
            this.NodesLV.UseCompatibleStateImageBehavior = false;
            this.NodesLV.View = System.Windows.Forms.View.Details;
            //
            // NodeCH
            //
            this.NodeCH.Text = "Node";
            this.NodeCH.Width = 140;
            //
            // ValueCH
            //
            this.ValueCH.Text = "Value";
            this.ValueCH.Width = 200;
            //
            // StatusCH
            //
            this.StatusCH.Text = "Status";
            this.StatusCH.Width = 180;
            //
            // PermissionsCH
            //
            this.PermissionsCH.Text = "This session may";
            this.PermissionsCH.Width = 330;
            //
            // NodesButtonsPN
            //
            this.NodesButtonsPN.Controls.Add(this.RefreshBTN);
            this.NodesButtonsPN.Controls.Add(this.WriteValueTB);
            this.NodesButtonsPN.Controls.Add(this.WriteBTN);
            this.NodesButtonsPN.Controls.Add(this.ResetBTN);
            this.NodesButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.NodesButtonsPN.Location = new System.Drawing.Point(3, 197);
            this.NodesButtonsPN.Name = "NodesButtonsPN";
            this.NodesButtonsPN.Size = new System.Drawing.Size(874, 32);
            this.NodesButtonsPN.TabIndex = 1;
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
            // WriteValueTB
            //
            this.WriteValueTB.Location = new System.Drawing.Point(99, 6);
            this.WriteValueTB.Name = "WriteValueTB";
            this.WriteValueTB.Size = new System.Drawing.Size(120, 20);
            this.WriteValueTB.TabIndex = 1;
            //
            // WriteBTN
            //
            this.WriteBTN.Enabled = false;
            this.WriteBTN.Location = new System.Drawing.Point(225, 4);
            this.WriteBTN.Name = "WriteBTN";
            this.WriteBTN.Size = new System.Drawing.Size(130, 23);
            this.WriteBTN.TabIndex = 2;
            this.WriteBTN.Text = "Write to selected";
            this.WriteBTN.UseVisualStyleBackColor = true;
            this.WriteBTN.Click += new System.EventHandler(this.WriteBTN_ClickAsync);
            //
            // ResetBTN
            //
            this.ResetBTN.Enabled = false;
            this.ResetBTN.Location = new System.Drawing.Point(361, 4);
            this.ResetBTN.Name = "ResetBTN";
            this.ResetBTN.Size = new System.Drawing.Size(130, 23);
            this.ResetBTN.TabIndex = 3;
            this.ResetBTN.Text = "Call Reset";
            this.ResetBTN.UseVisualStyleBackColor = true;
            this.ResetBTN.Click += new System.EventHandler(this.ResetBTN_ClickAsync);
            //
            // RolesGB
            //
            this.RolesGB.Controls.Add(this.RolesLV);
            this.RolesGB.Controls.Add(this.RolesButtonsPN);
            this.RolesGB.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RolesGB.Location = new System.Drawing.Point(2, 234);
            this.RolesGB.Name = "RolesGB";
            this.RolesGB.Size = new System.Drawing.Size(880, 208);
            this.RolesGB.TabIndex = 1;
            this.RolesGB.TabStop = false;
            this.RolesGB.Text = "The RoleSet of the server (Part 18)";
            //
            // RolesLV
            //
            this.RolesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.RoleCH,
            this.GrantedCH,
            this.IdentitiesCH});
            this.RolesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RolesLV.FullRowSelect = true;
            this.RolesLV.HideSelection = false;
            this.RolesLV.Location = new System.Drawing.Point(3, 16);
            this.RolesLV.MultiSelect = false;
            this.RolesLV.Name = "RolesLV";
            this.RolesLV.Size = new System.Drawing.Size(874, 157);
            this.RolesLV.TabIndex = 0;
            this.RolesLV.UseCompatibleStateImageBehavior = false;
            this.RolesLV.View = System.Windows.Forms.View.Details;
            //
            // RoleCH
            //
            this.RoleCH.Text = "Role";
            this.RoleCH.Width = 180;
            //
            // GrantedCH
            //
            this.GrantedCH.Text = "Granted to this session";
            this.GrantedCH.Width = 150;
            //
            // IdentitiesCH
            //
            this.IdentitiesCH.Text = "Identities";
            this.IdentitiesCH.Width = 520;
            //
            // RolesButtonsPN
            //
            this.RolesButtonsPN.Controls.Add(this.RoleUserTB);
            this.RolesButtonsPN.Controls.Add(this.AddIdentityBTN);
            this.RolesButtonsPN.Controls.Add(this.RemoveIdentityBTN);
            this.RolesButtonsPN.Controls.Add(this.NewRoleTB);
            this.RolesButtonsPN.Controls.Add(this.AddRoleBTN);
            this.RolesButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RolesButtonsPN.Location = new System.Drawing.Point(3, 173);
            this.RolesButtonsPN.Name = "RolesButtonsPN";
            this.RolesButtonsPN.Size = new System.Drawing.Size(874, 32);
            this.RolesButtonsPN.TabIndex = 1;
            //
            // RoleUserTB
            //
            this.RoleUserTB.Location = new System.Drawing.Point(3, 6);
            this.RoleUserTB.Name = "RoleUserTB";
            this.RoleUserTB.Size = new System.Drawing.Size(120, 20);
            this.RoleUserTB.TabIndex = 0;
            this.RoleUserTB.Text = "guest";
            //
            // AddIdentityBTN
            //
            this.AddIdentityBTN.Enabled = false;
            this.AddIdentityBTN.Location = new System.Drawing.Point(129, 4);
            this.AddIdentityBTN.Name = "AddIdentityBTN";
            this.AddIdentityBTN.Size = new System.Drawing.Size(150, 23);
            this.AddIdentityBTN.TabIndex = 1;
            this.AddIdentityBTN.Text = "Grant selected role";
            this.AddIdentityBTN.UseVisualStyleBackColor = true;
            this.AddIdentityBTN.Click += new System.EventHandler(this.AddIdentityBTN_ClickAsync);
            //
            // RemoveIdentityBTN
            //
            this.RemoveIdentityBTN.Enabled = false;
            this.RemoveIdentityBTN.Location = new System.Drawing.Point(285, 4);
            this.RemoveIdentityBTN.Name = "RemoveIdentityBTN";
            this.RemoveIdentityBTN.Size = new System.Drawing.Size(150, 23);
            this.RemoveIdentityBTN.TabIndex = 2;
            this.RemoveIdentityBTN.Text = "Revoke selected role";
            this.RemoveIdentityBTN.UseVisualStyleBackColor = true;
            this.RemoveIdentityBTN.Click += new System.EventHandler(this.RemoveIdentityBTN_ClickAsync);
            //
            // NewRoleTB
            //
            this.NewRoleTB.Location = new System.Drawing.Point(465, 6);
            this.NewRoleTB.Name = "NewRoleTB";
            this.NewRoleTB.Size = new System.Drawing.Size(120, 20);
            this.NewRoleTB.TabIndex = 3;
            this.NewRoleTB.Text = "Maintenance";
            //
            // AddRoleBTN
            //
            this.AddRoleBTN.Enabled = false;
            this.AddRoleBTN.Location = new System.Drawing.Point(591, 4);
            this.AddRoleBTN.Name = "AddRoleBTN";
            this.AddRoleBTN.Size = new System.Drawing.Size(130, 23);
            this.AddRoleBTN.TabIndex = 4;
            this.AddRoleBTN.Text = "Add role";
            this.AddRoleBTN.UseVisualStyleBackColor = true;
            this.AddRoleBTN.Click += new System.EventHandler(this.AddRoleBTN_ClickAsync);
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
            this.ClientSize = new System.Drawing.Size(884, 616);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.IdentityPN);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart RoleManagement Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.IdentityPN.ResumeLayout(false);
            this.IdentityPN.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.NodesGB.ResumeLayout(false);
            this.NodesButtonsPN.ResumeLayout(false);
            this.NodesButtonsPN.PerformLayout();
            this.RolesGB.ResumeLayout(false);
            this.RolesButtonsPN.ResumeLayout(false);
            this.RolesButtonsPN.PerformLayout();
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
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.Panel IdentityPN;
        private System.Windows.Forms.Label IdentityLB;
        private System.Windows.Forms.ComboBox IdentityCB;
        private System.Windows.Forms.Label IdentityHintLB;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.GroupBox NodesGB;
        private System.Windows.Forms.ListView NodesLV;
        private System.Windows.Forms.ColumnHeader NodeCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.ColumnHeader StatusCH;
        private System.Windows.Forms.ColumnHeader PermissionsCH;
        private System.Windows.Forms.Panel NodesButtonsPN;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.TextBox WriteValueTB;
        private System.Windows.Forms.Button WriteBTN;
        private System.Windows.Forms.Button ResetBTN;
        private System.Windows.Forms.GroupBox RolesGB;
        private System.Windows.Forms.ListView RolesLV;
        private System.Windows.Forms.ColumnHeader RoleCH;
        private System.Windows.Forms.ColumnHeader GrantedCH;
        private System.Windows.Forms.ColumnHeader IdentitiesCH;
        private System.Windows.Forms.Panel RolesButtonsPN;
        private System.Windows.Forms.TextBox RoleUserTB;
        private System.Windows.Forms.Button AddIdentityBTN;
        private System.Windows.Forms.Button RemoveIdentityBTN;
        private System.Windows.Forms.TextBox NewRoleTB;
        private System.Windows.Forms.Button AddRoleBTN;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
