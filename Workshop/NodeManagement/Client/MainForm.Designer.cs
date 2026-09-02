/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.NodeManagement.Client
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
            this.ActionStatusLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.NodesGB = new System.Windows.Forms.GroupBox();
            this.NodesLV = new System.Windows.Forms.ListView();
            this.NodeCH = new System.Windows.Forms.ColumnHeader();
            this.ClassCH = new System.Windows.Forms.ColumnHeader();
            this.NodeIdCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.NodesButtonsPN = new System.Windows.Forms.Panel();
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.NewNameTB = new System.Windows.Forms.TextBox();
            this.AddObjectBTN = new System.Windows.Forms.Button();
            this.AddVariableBTN = new System.Windows.Forms.Button();
            this.DeleteBTN = new System.Windows.Forms.Button();
            this.GroupGB = new System.Windows.Forms.GroupBox();
            this.GroupLV = new System.Windows.Forms.ListView();
            this.GroupNodeCH = new System.Windows.Forms.ColumnHeader();
            this.GroupNodeIdCH = new System.Windows.Forms.ColumnHeader();
            this.GroupButtonsPN = new System.Windows.Forms.Panel();
            this.AddReferenceBTN = new System.Windows.Forms.Button();
            this.DeleteReferenceBTN = new System.Windows.Forms.Button();
            this.RefusedBTN = new System.Windows.Forms.Button();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.NodesGB.SuspendLayout();
            this.NodesButtonsPN.SuspendLayout();
            this.GroupGB.SuspendLayout();
            this.GroupButtonsPN.SuspendLayout();
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
            this.MainPN.Controls.Add(this.NodesGB);
            this.MainPN.Controls.Add(this.GroupGB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 122);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 472);
            this.MainPN.TabIndex = 3;
            //
            // NodesGB
            //
            this.NodesGB.Controls.Add(this.NodesLV);
            this.NodesGB.Controls.Add(this.NodesButtonsPN);
            this.NodesGB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodesGB.Location = new System.Drawing.Point(2, 2);
            this.NodesGB.Name = "NodesGB";
            this.NodesGB.Size = new System.Drawing.Size(880, 282);
            this.NodesGB.TabIndex = 0;
            this.NodesGB.TabStop = false;
            this.NodesGB.Text = "The plant. Everything below Devices was created by a client.";
            //
            // NodesLV
            //
            this.NodesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.NodeCH,
            this.ClassCH,
            this.NodeIdCH,
            this.ValueCH});
            this.NodesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodesLV.FullRowSelect = true;
            this.NodesLV.HideSelection = false;
            this.NodesLV.Location = new System.Drawing.Point(3, 16);
            this.NodesLV.MultiSelect = false;
            this.NodesLV.Name = "NodesLV";
            this.NodesLV.Size = new System.Drawing.Size(874, 231);
            this.NodesLV.TabIndex = 0;
            this.NodesLV.UseCompatibleStateImageBehavior = false;
            this.NodesLV.View = System.Windows.Forms.View.Details;
            //
            // NodeCH
            //
            this.NodeCH.Text = "Node";
            this.NodeCH.Width = 260;
            //
            // ClassCH
            //
            this.ClassCH.Text = "Node class";
            this.ClassCH.Width = 90;
            //
            // NodeIdCH
            //
            this.NodeIdCH.Text = "Node id";
            this.NodeIdCH.Width = 260;
            //
            // ValueCH
            //
            this.ValueCH.Text = "Value";
            this.ValueCH.Width = 240;
            //
            // NodesButtonsPN
            //
            this.NodesButtonsPN.Controls.Add(this.RefreshBTN);
            this.NodesButtonsPN.Controls.Add(this.NewNameTB);
            this.NodesButtonsPN.Controls.Add(this.AddObjectBTN);
            this.NodesButtonsPN.Controls.Add(this.AddVariableBTN);
            this.NodesButtonsPN.Controls.Add(this.DeleteBTN);
            this.NodesButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.NodesButtonsPN.Location = new System.Drawing.Point(3, 247);
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
            // NewNameTB
            //
            this.NewNameTB.Location = new System.Drawing.Point(99, 6);
            this.NewNameTB.Name = "NewNameTB";
            this.NewNameTB.Size = new System.Drawing.Size(140, 20);
            this.NewNameTB.TabIndex = 1;
            this.NewNameTB.Text = "Pump1";
            //
            // AddObjectBTN
            //
            this.AddObjectBTN.Enabled = false;
            this.AddObjectBTN.Location = new System.Drawing.Point(245, 4);
            this.AddObjectBTN.Name = "AddObjectBTN";
            this.AddObjectBTN.Size = new System.Drawing.Size(130, 23);
            this.AddObjectBTN.TabIndex = 2;
            this.AddObjectBTN.Text = "Add object";
            this.AddObjectBTN.UseVisualStyleBackColor = true;
            this.AddObjectBTN.Click += new System.EventHandler(this.AddObjectBTN_ClickAsync);
            //
            // AddVariableBTN
            //
            this.AddVariableBTN.Enabled = false;
            this.AddVariableBTN.Location = new System.Drawing.Point(381, 4);
            this.AddVariableBTN.Name = "AddVariableBTN";
            this.AddVariableBTN.Size = new System.Drawing.Size(130, 23);
            this.AddVariableBTN.TabIndex = 3;
            this.AddVariableBTN.Text = "Add variable";
            this.AddVariableBTN.UseVisualStyleBackColor = true;
            this.AddVariableBTN.Click += new System.EventHandler(this.AddVariableBTN_ClickAsync);
            //
            // DeleteBTN
            //
            this.DeleteBTN.Enabled = false;
            this.DeleteBTN.Location = new System.Drawing.Point(517, 4);
            this.DeleteBTN.Name = "DeleteBTN";
            this.DeleteBTN.Size = new System.Drawing.Size(140, 23);
            this.DeleteBTN.TabIndex = 4;
            this.DeleteBTN.Text = "Delete selected";
            this.DeleteBTN.UseVisualStyleBackColor = true;
            this.DeleteBTN.Click += new System.EventHandler(this.DeleteBTN_ClickAsync);
            //
            // GroupGB
            //
            this.GroupGB.Controls.Add(this.GroupLV);
            this.GroupGB.Controls.Add(this.GroupButtonsPN);
            this.GroupGB.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.GroupGB.Location = new System.Drawing.Point(2, 284);
            this.GroupGB.Name = "GroupGB";
            this.GroupGB.Size = new System.Drawing.Size(880, 188);
            this.GroupGB.TabIndex = 1;
            this.GroupGB.TabStop = false;
            this.GroupGB.Text = "The Commissioned group. Filled with references, not with nodes.";
            //
            // GroupLV
            //
            this.GroupLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.GroupNodeCH,
            this.GroupNodeIdCH});
            this.GroupLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.GroupLV.FullRowSelect = true;
            this.GroupLV.HideSelection = false;
            this.GroupLV.Location = new System.Drawing.Point(3, 16);
            this.GroupLV.MultiSelect = false;
            this.GroupLV.Name = "GroupLV";
            this.GroupLV.Size = new System.Drawing.Size(874, 137);
            this.GroupLV.TabIndex = 0;
            this.GroupLV.UseCompatibleStateImageBehavior = false;
            this.GroupLV.View = System.Windows.Forms.View.Details;
            //
            // GroupNodeCH
            //
            this.GroupNodeCH.Text = "Node";
            this.GroupNodeCH.Width = 260;
            //
            // GroupNodeIdCH
            //
            this.GroupNodeIdCH.Text = "Node id";
            this.GroupNodeIdCH.Width = 400;
            //
            // GroupButtonsPN
            //
            this.GroupButtonsPN.Controls.Add(this.AddReferenceBTN);
            this.GroupButtonsPN.Controls.Add(this.DeleteReferenceBTN);
            this.GroupButtonsPN.Controls.Add(this.RefusedBTN);
            this.GroupButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.GroupButtonsPN.Location = new System.Drawing.Point(3, 153);
            this.GroupButtonsPN.Name = "GroupButtonsPN";
            this.GroupButtonsPN.Size = new System.Drawing.Size(874, 32);
            this.GroupButtonsPN.TabIndex = 1;
            //
            // AddReferenceBTN
            //
            this.AddReferenceBTN.Enabled = false;
            this.AddReferenceBTN.Location = new System.Drawing.Point(3, 4);
            this.AddReferenceBTN.Name = "AddReferenceBTN";
            this.AddReferenceBTN.Size = new System.Drawing.Size(190, 23);
            this.AddReferenceBTN.TabIndex = 0;
            this.AddReferenceBTN.Text = "Reference the selected node";
            this.AddReferenceBTN.UseVisualStyleBackColor = true;
            this.AddReferenceBTN.Click += new System.EventHandler(this.AddReferenceBTN_ClickAsync);
            //
            // DeleteReferenceBTN
            //
            this.DeleteReferenceBTN.Enabled = false;
            this.DeleteReferenceBTN.Location = new System.Drawing.Point(199, 4);
            this.DeleteReferenceBTN.Name = "DeleteReferenceBTN";
            this.DeleteReferenceBTN.Size = new System.Drawing.Size(190, 23);
            this.DeleteReferenceBTN.TabIndex = 1;
            this.DeleteReferenceBTN.Text = "Drop the reference again";
            this.DeleteReferenceBTN.UseVisualStyleBackColor = true;
            this.DeleteReferenceBTN.Click += new System.EventHandler(this.DeleteReferenceBTN_ClickAsync);
            //
            // RefusedBTN
            //
            this.RefusedBTN.Enabled = false;
            this.RefusedBTN.Location = new System.Drawing.Point(415, 4);
            this.RefusedBTN.Name = "RefusedBTN";
            this.RefusedBTN.Size = new System.Drawing.Size(230, 23);
            this.RefusedBTN.TabIndex = 2;
            this.RefusedBTN.Text = "Try it on a standard node";
            this.RefusedBTN.UseVisualStyleBackColor = true;
            this.RefusedBTN.Click += new System.EventHandler(this.RefusedBTN_ClickAsync);
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
            this.ClientSize = new System.Drawing.Size(884, 616);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart NodeManagement Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.NodesGB.ResumeLayout(false);
            this.NodesButtonsPN.ResumeLayout(false);
            this.NodesButtonsPN.PerformLayout();
            this.GroupGB.ResumeLayout(false);
            this.GroupButtonsPN.ResumeLayout(false);
            this.GroupButtonsPN.PerformLayout();
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
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.GroupBox NodesGB;
        private System.Windows.Forms.ListView NodesLV;
        private System.Windows.Forms.ColumnHeader NodeCH;
        private System.Windows.Forms.ColumnHeader ClassCH;
        private System.Windows.Forms.ColumnHeader NodeIdCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.Panel NodesButtonsPN;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.TextBox NewNameTB;
        private System.Windows.Forms.Button AddObjectBTN;
        private System.Windows.Forms.Button AddVariableBTN;
        private System.Windows.Forms.Button DeleteBTN;
        private System.Windows.Forms.GroupBox GroupGB;
        private System.Windows.Forms.ListView GroupLV;
        private System.Windows.Forms.ColumnHeader GroupNodeCH;
        private System.Windows.Forms.ColumnHeader GroupNodeIdCH;
        private System.Windows.Forms.Panel GroupButtonsPN;
        private System.Windows.Forms.Button AddReferenceBTN;
        private System.Windows.Forms.Button DeleteReferenceBTN;
        private System.Windows.Forms.Button RefusedBTN;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
