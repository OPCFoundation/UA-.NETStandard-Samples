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

namespace Quickstarts.StateMachines.Client
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
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.MainPN = new System.Windows.Forms.Panel();
            this.OperationGB = new System.Windows.Forms.GroupBox();
            this.OperationStateLB = new System.Windows.Forms.Label();
            this.OperationStateTB = new System.Windows.Forms.TextBox();
            this.OperationTransitionLB = new System.Windows.Forms.Label();
            this.OperationTransitionTB = new System.Windows.Forms.TextBox();
            this.ProductionStateLB = new System.Windows.Forms.Label();
            this.ProductionStateTB = new System.Windows.Forms.TextBox();
            this.StartBatchBTN = new System.Windows.Forms.Button();
            this.InterlockCB = new System.Windows.Forms.CheckBox();
            this.PowerOnBTN = new System.Windows.Forms.Button();
            this.PowerOffBTN = new System.Windows.Forms.Button();
            this.StartBTN = new System.Windows.Forms.Button();
            this.StopBTN = new System.Windows.Forms.Button();
            this.FaultBTN = new System.Windows.Forms.Button();
            this.ResetBTN = new System.Windows.Forms.Button();
            this.ProgramGB = new System.Windows.Forms.GroupBox();
            this.ProgramStateLB = new System.Windows.Forms.Label();
            this.ProgramStateTB = new System.Windows.Forms.TextBox();
            this.ProgramTransitionLB = new System.Windows.Forms.Label();
            this.ProgramTransitionTB = new System.Windows.Forms.TextBox();
            this.ProgramStartBTN = new System.Windows.Forms.Button();
            this.ProgramSuspendBTN = new System.Windows.Forms.Button();
            this.ProgramResumeBTN = new System.Windows.Forms.Button();
            this.ProgramHaltBTN = new System.Windows.Forms.Button();
            this.ProgramResetBTN = new System.Windows.Forms.Button();
            this.TransitionsLV = new System.Windows.Forms.ListView();
            this.TimeCH = new System.Windows.Forms.ColumnHeader();
            this.MachineCH = new System.Windows.Forms.ColumnHeader();
            this.StateCH = new System.Windows.Forms.ColumnHeader();
            this.TransitionCH = new System.Windows.Forms.ColumnHeader();
            this.ModelLV = new System.Windows.Forms.ListView();
            this.KindCH = new System.Windows.Forms.ColumnHeader();
            this.ElementCH = new System.Windows.Forms.ColumnHeader();
            this.NumberCH = new System.Windows.Forms.ColumnHeader();
            this.ElementNodeCH = new System.Windows.Forms.ColumnHeader();
            this.SubMachineCH = new System.Windows.Forms.ColumnHeader();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.OperationGB.SuspendLayout();
            this.ProgramGB.SuspendLayout();
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
            this.MainPN.Controls.Add(this.TransitionsLV);
            this.MainPN.Controls.Add(this.ModelLV);
            this.MainPN.Controls.Add(this.ProgramGB);
            this.MainPN.Controls.Add(this.OperationGB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 122);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 496);
            this.MainPN.TabIndex = 3;
            //
            // OperationGB
            //
            this.OperationGB.Controls.Add(this.ResetBTN);
            this.OperationGB.Controls.Add(this.FaultBTN);
            this.OperationGB.Controls.Add(this.StopBTN);
            this.OperationGB.Controls.Add(this.StartBTN);
            this.OperationGB.Controls.Add(this.PowerOffBTN);
            this.OperationGB.Controls.Add(this.PowerOnBTN);
            this.OperationGB.Controls.Add(this.InterlockCB);
            this.OperationGB.Controls.Add(this.StartBatchBTN);
            this.OperationGB.Controls.Add(this.ProductionStateTB);
            this.OperationGB.Controls.Add(this.ProductionStateLB);
            this.OperationGB.Controls.Add(this.OperationTransitionTB);
            this.OperationGB.Controls.Add(this.OperationTransitionLB);
            this.OperationGB.Controls.Add(this.OperationStateTB);
            this.OperationGB.Controls.Add(this.OperationStateLB);
            this.OperationGB.Location = new System.Drawing.Point(8, 8);
            this.OperationGB.Name = "OperationGB";
            this.OperationGB.Size = new System.Drawing.Size(430, 190);
            this.OperationGB.TabIndex = 1;
            this.OperationGB.TabStop = false;
            this.OperationGB.Text = "Operation - declared with the builder";
            //
            // OperationStateLB
            //
            this.OperationStateLB.AutoSize = true;
            this.OperationStateLB.Location = new System.Drawing.Point(12, 26);
            this.OperationStateLB.Name = "OperationStateLB";
            this.OperationStateLB.Size = new System.Drawing.Size(69, 13);
            this.OperationStateLB.TabIndex = 0;
            this.OperationStateLB.Text = "Current state";
            //
            // OperationStateTB
            //
            this.OperationStateTB.Location = new System.Drawing.Point(110, 23);
            this.OperationStateTB.Name = "OperationStateTB";
            this.OperationStateTB.ReadOnly = true;
            this.OperationStateTB.Size = new System.Drawing.Size(150, 20);
            this.OperationStateTB.TabIndex = 1;
            //
            // OperationTransitionLB
            //
            this.OperationTransitionLB.AutoSize = true;
            this.OperationTransitionLB.Location = new System.Drawing.Point(12, 52);
            this.OperationTransitionLB.Name = "OperationTransitionLB";
            this.OperationTransitionLB.Size = new System.Drawing.Size(75, 13);
            this.OperationTransitionLB.TabIndex = 2;
            this.OperationTransitionLB.Text = "Last transition";
            //
            // OperationTransitionTB
            //
            this.OperationTransitionTB.Location = new System.Drawing.Point(110, 49);
            this.OperationTransitionTB.Name = "OperationTransitionTB";
            this.OperationTransitionTB.ReadOnly = true;
            this.OperationTransitionTB.Size = new System.Drawing.Size(150, 20);
            this.OperationTransitionTB.TabIndex = 3;
            //
            // ProductionStateLB
            //
            this.ProductionStateLB.AutoSize = true;
            this.ProductionStateLB.Location = new System.Drawing.Point(12, 78);
            this.ProductionStateLB.Name = "ProductionStateLB";
            this.ProductionStateLB.Size = new System.Drawing.Size(85, 13);
            this.ProductionStateLB.TabIndex = 4;
            this.ProductionStateLB.Text = "Production state";
            //
            // ProductionStateTB
            //
            this.ProductionStateTB.Location = new System.Drawing.Point(110, 75);
            this.ProductionStateTB.Name = "ProductionStateTB";
            this.ProductionStateTB.ReadOnly = true;
            this.ProductionStateTB.Size = new System.Drawing.Size(150, 20);
            this.ProductionStateTB.TabIndex = 5;
            //
            // StartBatchBTN
            //
            this.StartBatchBTN.Enabled = false;
            this.StartBatchBTN.Location = new System.Drawing.Point(270, 73);
            this.StartBatchBTN.Name = "StartBatchBTN";
            this.StartBatchBTN.Size = new System.Drawing.Size(90, 23);
            this.StartBatchBTN.TabIndex = 6;
            this.StartBatchBTN.Text = "StartBatch";
            this.StartBatchBTN.UseVisualStyleBackColor = true;
            this.StartBatchBTN.Click += new System.EventHandler(this.StartBatchBTN_ClickAsync);
            //
            // InterlockCB
            //
            this.InterlockCB.AutoSize = true;
            this.InterlockCB.Checked = true;
            this.InterlockCB.CheckState = System.Windows.Forms.CheckState.Checked;
            this.InterlockCB.Enabled = false;
            this.InterlockCB.Location = new System.Drawing.Point(15, 104);
            this.InterlockCB.Name = "InterlockCB";
            this.InterlockCB.Size = new System.Drawing.Size(200, 17);
            this.InterlockCB.TabIndex = 7;
            this.InterlockCB.Text = "Safety interlock clear (guards Start)";
            this.InterlockCB.UseVisualStyleBackColor = true;
            this.InterlockCB.CheckedChanged += new System.EventHandler(this.InterlockCB_CheckedChangedAsync);
            //
            // PowerOnBTN
            //
            this.PowerOnBTN.Enabled = false;
            this.PowerOnBTN.Location = new System.Drawing.Point(15, 130);
            this.PowerOnBTN.Name = "PowerOnBTN";
            this.PowerOnBTN.Size = new System.Drawing.Size(75, 23);
            this.PowerOnBTN.TabIndex = 8;
            this.PowerOnBTN.Text = "PowerOn";
            this.PowerOnBTN.UseVisualStyleBackColor = true;
            this.PowerOnBTN.Click += new System.EventHandler(this.OperationCauseBTN_ClickAsync);
            //
            // PowerOffBTN
            //
            this.PowerOffBTN.Enabled = false;
            this.PowerOffBTN.Location = new System.Drawing.Point(96, 130);
            this.PowerOffBTN.Name = "PowerOffBTN";
            this.PowerOffBTN.Size = new System.Drawing.Size(75, 23);
            this.PowerOffBTN.TabIndex = 9;
            this.PowerOffBTN.Text = "PowerOff";
            this.PowerOffBTN.UseVisualStyleBackColor = true;
            this.PowerOffBTN.Click += new System.EventHandler(this.OperationCauseBTN_ClickAsync);
            //
            // StartBTN
            //
            this.StartBTN.Enabled = false;
            this.StartBTN.Location = new System.Drawing.Point(177, 130);
            this.StartBTN.Name = "StartBTN";
            this.StartBTN.Size = new System.Drawing.Size(75, 23);
            this.StartBTN.TabIndex = 10;
            this.StartBTN.Text = "Start";
            this.StartBTN.UseVisualStyleBackColor = true;
            this.StartBTN.Click += new System.EventHandler(this.OperationCauseBTN_ClickAsync);
            //
            // StopBTN
            //
            this.StopBTN.Enabled = false;
            this.StopBTN.Location = new System.Drawing.Point(15, 159);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(75, 23);
            this.StopBTN.TabIndex = 11;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            this.StopBTN.Click += new System.EventHandler(this.OperationCauseBTN_ClickAsync);
            //
            // FaultBTN
            //
            this.FaultBTN.Enabled = false;
            this.FaultBTN.Location = new System.Drawing.Point(96, 159);
            this.FaultBTN.Name = "FaultBTN";
            this.FaultBTN.Size = new System.Drawing.Size(75, 23);
            this.FaultBTN.TabIndex = 12;
            this.FaultBTN.Text = "Fault";
            this.FaultBTN.UseVisualStyleBackColor = true;
            this.FaultBTN.Click += new System.EventHandler(this.OperationCauseBTN_ClickAsync);
            //
            // ResetBTN
            //
            this.ResetBTN.Enabled = false;
            this.ResetBTN.Location = new System.Drawing.Point(177, 159);
            this.ResetBTN.Name = "ResetBTN";
            this.ResetBTN.Size = new System.Drawing.Size(75, 23);
            this.ResetBTN.TabIndex = 13;
            this.ResetBTN.Text = "Reset";
            this.ResetBTN.UseVisualStyleBackColor = true;
            this.ResetBTN.Click += new System.EventHandler(this.OperationCauseBTN_ClickAsync);
            //
            // ProgramGB
            //
            this.ProgramGB.Controls.Add(this.ProgramResetBTN);
            this.ProgramGB.Controls.Add(this.ProgramHaltBTN);
            this.ProgramGB.Controls.Add(this.ProgramResumeBTN);
            this.ProgramGB.Controls.Add(this.ProgramSuspendBTN);
            this.ProgramGB.Controls.Add(this.ProgramStartBTN);
            this.ProgramGB.Controls.Add(this.ProgramTransitionTB);
            this.ProgramGB.Controls.Add(this.ProgramTransitionLB);
            this.ProgramGB.Controls.Add(this.ProgramStateTB);
            this.ProgramGB.Controls.Add(this.ProgramStateLB);
            this.ProgramGB.Location = new System.Drawing.Point(446, 8);
            this.ProgramGB.Name = "ProgramGB";
            this.ProgramGB.Size = new System.Drawing.Size(430, 190);
            this.ProgramGB.TabIndex = 2;
            this.ProgramGB.TabStop = false;
            this.ProgramGB.Text = "Program - the state machine of OPC 10000-10";
            //
            // ProgramStateLB
            //
            this.ProgramStateLB.AutoSize = true;
            this.ProgramStateLB.Location = new System.Drawing.Point(12, 26);
            this.ProgramStateLB.Name = "ProgramStateLB";
            this.ProgramStateLB.Size = new System.Drawing.Size(69, 13);
            this.ProgramStateLB.TabIndex = 0;
            this.ProgramStateLB.Text = "Current state";
            //
            // ProgramStateTB
            //
            this.ProgramStateTB.Location = new System.Drawing.Point(110, 23);
            this.ProgramStateTB.Name = "ProgramStateTB";
            this.ProgramStateTB.ReadOnly = true;
            this.ProgramStateTB.Size = new System.Drawing.Size(150, 20);
            this.ProgramStateTB.TabIndex = 1;
            //
            // ProgramTransitionLB
            //
            this.ProgramTransitionLB.AutoSize = true;
            this.ProgramTransitionLB.Location = new System.Drawing.Point(12, 52);
            this.ProgramTransitionLB.Name = "ProgramTransitionLB";
            this.ProgramTransitionLB.Size = new System.Drawing.Size(75, 13);
            this.ProgramTransitionLB.TabIndex = 2;
            this.ProgramTransitionLB.Text = "Last transition";
            //
            // ProgramTransitionTB
            //
            this.ProgramTransitionTB.Location = new System.Drawing.Point(110, 49);
            this.ProgramTransitionTB.Name = "ProgramTransitionTB";
            this.ProgramTransitionTB.ReadOnly = true;
            this.ProgramTransitionTB.Size = new System.Drawing.Size(150, 20);
            this.ProgramTransitionTB.TabIndex = 3;
            //
            // ProgramStartBTN
            //
            this.ProgramStartBTN.Enabled = false;
            this.ProgramStartBTN.Location = new System.Drawing.Point(15, 106);
            this.ProgramStartBTN.Name = "ProgramStartBTN";
            this.ProgramStartBTN.Size = new System.Drawing.Size(75, 23);
            this.ProgramStartBTN.TabIndex = 4;
            this.ProgramStartBTN.Text = "Start";
            this.ProgramStartBTN.UseVisualStyleBackColor = true;
            this.ProgramStartBTN.Click += new System.EventHandler(this.ProgramStartBTN_ClickAsync);
            //
            // ProgramSuspendBTN
            //
            this.ProgramSuspendBTN.Enabled = false;
            this.ProgramSuspendBTN.Location = new System.Drawing.Point(96, 106);
            this.ProgramSuspendBTN.Name = "ProgramSuspendBTN";
            this.ProgramSuspendBTN.Size = new System.Drawing.Size(75, 23);
            this.ProgramSuspendBTN.TabIndex = 5;
            this.ProgramSuspendBTN.Text = "Suspend";
            this.ProgramSuspendBTN.UseVisualStyleBackColor = true;
            this.ProgramSuspendBTN.Click += new System.EventHandler(this.ProgramSuspendBTN_ClickAsync);
            //
            // ProgramResumeBTN
            //
            this.ProgramResumeBTN.Enabled = false;
            this.ProgramResumeBTN.Location = new System.Drawing.Point(177, 106);
            this.ProgramResumeBTN.Name = "ProgramResumeBTN";
            this.ProgramResumeBTN.Size = new System.Drawing.Size(75, 23);
            this.ProgramResumeBTN.TabIndex = 6;
            this.ProgramResumeBTN.Text = "Resume";
            this.ProgramResumeBTN.UseVisualStyleBackColor = true;
            this.ProgramResumeBTN.Click += new System.EventHandler(this.ProgramResumeBTN_ClickAsync);
            //
            // ProgramHaltBTN
            //
            this.ProgramHaltBTN.Enabled = false;
            this.ProgramHaltBTN.Location = new System.Drawing.Point(15, 135);
            this.ProgramHaltBTN.Name = "ProgramHaltBTN";
            this.ProgramHaltBTN.Size = new System.Drawing.Size(75, 23);
            this.ProgramHaltBTN.TabIndex = 7;
            this.ProgramHaltBTN.Text = "Halt";
            this.ProgramHaltBTN.UseVisualStyleBackColor = true;
            this.ProgramHaltBTN.Click += new System.EventHandler(this.ProgramHaltBTN_ClickAsync);
            //
            // ProgramResetBTN
            //
            this.ProgramResetBTN.Enabled = false;
            this.ProgramResetBTN.Location = new System.Drawing.Point(96, 135);
            this.ProgramResetBTN.Name = "ProgramResetBTN";
            this.ProgramResetBTN.Size = new System.Drawing.Size(75, 23);
            this.ProgramResetBTN.TabIndex = 8;
            this.ProgramResetBTN.Text = "Reset";
            this.ProgramResetBTN.UseVisualStyleBackColor = true;
            this.ProgramResetBTN.Click += new System.EventHandler(this.ProgramResetBTN_ClickAsync);
            //
            // TransitionsLV
            //
            this.TransitionsLV.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TransitionsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.TimeCH,
            this.MachineCH,
            this.StateCH,
            this.TransitionCH});
            this.TransitionsLV.FullRowSelect = true;
            this.TransitionsLV.Location = new System.Drawing.Point(8, 326);
            this.TransitionsLV.Name = "TransitionsLV";
            this.TransitionsLV.Size = new System.Drawing.Size(868, 158);
            this.TransitionsLV.TabIndex = 4;
            this.TransitionsLV.UseCompatibleStateImageBehavior = false;
            this.TransitionsLV.View = System.Windows.Forms.View.Details;
            //
            // TimeCH
            //
            this.TimeCH.Text = "Time";
            this.TimeCH.Width = 160;
            //
            // MachineCH
            //
            this.MachineCH.Text = "State machine";
            this.MachineCH.Width = 120;
            //
            // StateCH
            //
            this.StateCH.Text = "Current state";
            this.StateCH.Width = 200;
            //
            // TransitionCH
            //
            this.TransitionCH.Text = "Last transition";
            this.TransitionCH.Width = 340;
            //
            // ModelLV
            //
            this.ModelLV.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ModelLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.KindCH,
            this.ElementCH,
            this.NumberCH,
            this.ElementNodeCH,
            this.SubMachineCH});
            this.ModelLV.FullRowSelect = true;
            this.ModelLV.Location = new System.Drawing.Point(8, 206);
            this.ModelLV.Name = "ModelLV";
            this.ModelLV.Size = new System.Drawing.Size(868, 112);
            this.ModelLV.TabIndex = 3;
            this.ModelLV.UseCompatibleStateImageBehavior = false;
            this.ModelLV.View = System.Windows.Forms.View.Details;
            //
            // KindCH
            //
            this.KindCH.Text = "Element";
            this.KindCH.Width = 80;
            //
            // ElementCH
            //
            this.ElementCH.Text = "Name";
            this.ElementCH.Width = 170;
            //
            // NumberCH
            //
            this.NumberCH.Text = "Number";
            this.NumberCH.Width = 70;
            //
            // ElementNodeCH
            //
            this.ElementNodeCH.Text = "NodeId";
            this.ElementNodeCH.Width = 350;
            //
            // SubMachineCH
            //
            this.SubMachineCH.Text = "Sub state machine";
            this.SubMachineCH.Width = 180;
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
            this.ClientSize = new System.Drawing.Size(884, 640);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart State Machines Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.OperationGB.ResumeLayout(false);
            this.OperationGB.PerformLayout();
            this.ProgramGB.ResumeLayout(false);
            this.ProgramGB.PerformLayout();
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
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.GroupBox OperationGB;
        private System.Windows.Forms.Label OperationStateLB;
        private System.Windows.Forms.TextBox OperationStateTB;
        private System.Windows.Forms.Label OperationTransitionLB;
        private System.Windows.Forms.TextBox OperationTransitionTB;
        private System.Windows.Forms.Label ProductionStateLB;
        private System.Windows.Forms.TextBox ProductionStateTB;
        private System.Windows.Forms.Button StartBatchBTN;
        private System.Windows.Forms.CheckBox InterlockCB;
        private System.Windows.Forms.Button PowerOnBTN;
        private System.Windows.Forms.Button PowerOffBTN;
        private System.Windows.Forms.Button StartBTN;
        private System.Windows.Forms.Button StopBTN;
        private System.Windows.Forms.Button FaultBTN;
        private System.Windows.Forms.Button ResetBTN;
        private System.Windows.Forms.GroupBox ProgramGB;
        private System.Windows.Forms.Label ProgramStateLB;
        private System.Windows.Forms.TextBox ProgramStateTB;
        private System.Windows.Forms.Label ProgramTransitionLB;
        private System.Windows.Forms.TextBox ProgramTransitionTB;
        private System.Windows.Forms.Button ProgramStartBTN;
        private System.Windows.Forms.Button ProgramSuspendBTN;
        private System.Windows.Forms.Button ProgramResumeBTN;
        private System.Windows.Forms.Button ProgramHaltBTN;
        private System.Windows.Forms.Button ProgramResetBTN;
        private System.Windows.Forms.ListView TransitionsLV;
        private System.Windows.Forms.ColumnHeader TimeCH;
        private System.Windows.Forms.ColumnHeader MachineCH;
        private System.Windows.Forms.ColumnHeader StateCH;
        private System.Windows.Forms.ColumnHeader TransitionCH;
        private System.Windows.Forms.ListView ModelLV;
        private System.Windows.Forms.ColumnHeader KindCH;
        private System.Windows.Forms.ColumnHeader ElementCH;
        private System.Windows.Forms.ColumnHeader NumberCH;
        private System.Windows.Forms.ColumnHeader ElementNodeCH;
        private System.Windows.Forms.ColumnHeader SubMachineCH;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
