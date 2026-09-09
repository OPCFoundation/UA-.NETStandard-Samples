/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.DurableSubscriptionClient
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
            this.TopPN = new System.Windows.Forms.Panel();
            this.DisconnectBTN = new System.Windows.Forms.Button();
            this.SaveAndRestartBTN = new System.Windows.Forms.Button();
            this.CreateSubscriptionBTN = new System.Windows.Forms.Button();
            this.ConnectBTN = new System.Windows.Forms.Button();
            this.EndpointTB = new System.Windows.Forms.TextBox();
            this.EndpointLB = new System.Windows.Forms.Label();
            this.ValuesLV = new System.Windows.Forms.ListView();
            this.TimeCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.SequenceCH = new System.Windows.Forms.ColumnHeader();
            this.KindCH = new System.Windows.Forms.ColumnHeader();
            this.StatusCH = new System.Windows.Forms.ColumnHeader();
            this.StatusStrip = new System.Windows.Forms.StatusStrip();
            this.StatusLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.TopPN.SuspendLayout();
            this.StatusStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // TopPN
            //
            this.TopPN.Controls.Add(this.DisconnectBTN);
            this.TopPN.Controls.Add(this.SaveAndRestartBTN);
            this.TopPN.Controls.Add(this.CreateSubscriptionBTN);
            this.TopPN.Controls.Add(this.ConnectBTN);
            this.TopPN.Controls.Add(this.EndpointTB);
            this.TopPN.Controls.Add(this.EndpointLB);
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPN.Location = new System.Drawing.Point(0, 0);
            this.TopPN.Name = "TopPN";
            this.TopPN.Size = new System.Drawing.Size(784, 72);
            this.TopPN.TabIndex = 0;
            //
            // DisconnectBTN
            //
            this.DisconnectBTN.Location = new System.Drawing.Point(560, 39);
            this.DisconnectBTN.Name = "DisconnectBTN";
            this.DisconnectBTN.Size = new System.Drawing.Size(120, 26);
            this.DisconnectBTN.TabIndex = 5;
            this.DisconnectBTN.Text = "Disconnect + Delete";
            this.DisconnectBTN.UseVisualStyleBackColor = true;
            this.DisconnectBTN.Click += new System.EventHandler(this.DisconnectBTN_ClickAsync);
            //
            // SaveAndRestartBTN
            //
            this.SaveAndRestartBTN.Location = new System.Drawing.Point(320, 39);
            this.SaveAndRestartBTN.Name = "SaveAndRestartBTN";
            this.SaveAndRestartBTN.Size = new System.Drawing.Size(120, 26);
            this.SaveAndRestartBTN.TabIndex = 4;
            this.SaveAndRestartBTN.Text = "Save && Restart";
            this.SaveAndRestartBTN.UseVisualStyleBackColor = true;
            this.SaveAndRestartBTN.Click += new System.EventHandler(this.SaveAndRestartBTN_ClickAsync);
            //
            // CreateSubscriptionBTN
            //
            this.CreateSubscriptionBTN.Location = new System.Drawing.Point(80, 39);
            this.CreateSubscriptionBTN.Name = "CreateSubscriptionBTN";
            this.CreateSubscriptionBTN.Size = new System.Drawing.Size(160, 26);
            this.CreateSubscriptionBTN.TabIndex = 3;
            this.CreateSubscriptionBTN.Text = "Create Durable Subscription";
            this.CreateSubscriptionBTN.UseVisualStyleBackColor = true;
            this.CreateSubscriptionBTN.Click += new System.EventHandler(this.CreateSubscriptionBTN_ClickAsync);
            //
            // ConnectBTN
            //
            this.ConnectBTN.Location = new System.Drawing.Point(680, 9);
            this.ConnectBTN.Name = "ConnectBTN";
            this.ConnectBTN.Size = new System.Drawing.Size(92, 24);
            this.ConnectBTN.TabIndex = 2;
            this.ConnectBTN.Text = "Connect";
            this.ConnectBTN.UseVisualStyleBackColor = true;
            this.ConnectBTN.Click += new System.EventHandler(this.ConnectBTN_ClickAsync);
            //
            // EndpointTB
            //
            this.EndpointTB.Location = new System.Drawing.Point(80, 10);
            this.EndpointTB.Name = "EndpointTB";
            this.EndpointTB.Size = new System.Drawing.Size(594, 23);
            this.EndpointTB.TabIndex = 1;
            this.EndpointTB.Text = "opc.tcp://localhost:62581/Quickstarts/DurableSubscriptionServer";
            //
            // EndpointLB
            //
            this.EndpointLB.AutoSize = true;
            this.EndpointLB.Location = new System.Drawing.Point(12, 13);
            this.EndpointLB.Name = "EndpointLB";
            this.EndpointLB.Size = new System.Drawing.Size(45, 15);
            this.EndpointLB.TabIndex = 0;
            this.EndpointLB.Text = "Server:";
            //
            // ValuesLV
            //
            this.ValuesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.TimeCH,
            this.ValueCH,
            this.SequenceCH,
            this.KindCH,
            this.StatusCH});
            this.ValuesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValuesLV.FullRowSelect = true;
            this.ValuesLV.GridLines = true;
            this.ValuesLV.Location = new System.Drawing.Point(0, 72);
            this.ValuesLV.Name = "ValuesLV";
            this.ValuesLV.Size = new System.Drawing.Size(784, 367);
            this.ValuesLV.TabIndex = 1;
            this.ValuesLV.UseCompatibleStateImageBehavior = false;
            this.ValuesLV.View = System.Windows.Forms.View.Details;
            //
            // TimeCH
            //
            this.TimeCH.Text = "Source Timestamp";
            this.TimeCH.Width = 140;
            //
            // ValueCH
            //
            this.ValueCH.Text = "Value";
            this.ValueCH.Width = 300;
            //
            // SequenceCH
            //
            this.SequenceCH.Text = "Sequence";
            this.SequenceCH.Width = 90;
            //
            // KindCH
            //
            this.KindCH.Text = "Kind";
            this.KindCH.Width = 90;
            //
            // StatusCH
            //
            this.StatusCH.Text = "Status";
            this.StatusCH.Width = 140;
            //
            // StatusStrip
            //
            this.StatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.StatusLB});
            this.StatusStrip.Location = new System.Drawing.Point(0, 439);
            this.StatusStrip.Name = "StatusStrip";
            this.StatusStrip.Size = new System.Drawing.Size(784, 22);
            this.StatusStrip.TabIndex = 2;
            //
            // StatusLB
            //
            this.StatusLB.Name = "StatusLB";
            this.StatusLB.Size = new System.Drawing.Size(39, 17);
            this.StatusLB.Text = "Ready";
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 461);
            this.Controls.Add(this.ValuesLV);
            this.Controls.Add(this.StatusStrip);
            this.Controls.Add(this.TopPN);
            this.Name = "MainForm";
            this.Text = "Durable Subscription Client";
            this.TopPN.ResumeLayout(false);
            this.TopPN.PerformLayout();
            this.StatusStrip.ResumeLayout(false);
            this.StatusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Panel TopPN;
        private System.Windows.Forms.Label EndpointLB;
        private System.Windows.Forms.TextBox EndpointTB;
        private System.Windows.Forms.Button ConnectBTN;
        private System.Windows.Forms.Button CreateSubscriptionBTN;
        private System.Windows.Forms.Button SaveAndRestartBTN;
        private System.Windows.Forms.Button DisconnectBTN;
        private System.Windows.Forms.ListView ValuesLV;
        private System.Windows.Forms.ColumnHeader TimeCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.ColumnHeader SequenceCH;
        private System.Windows.Forms.ColumnHeader KindCH;
        private System.Windows.Forms.ColumnHeader StatusCH;
        private System.Windows.Forms.StatusStrip StatusStrip;
        private System.Windows.Forms.ToolStripStatusLabel StatusLB;
    }
}
