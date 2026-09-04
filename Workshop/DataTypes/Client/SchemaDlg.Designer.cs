/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.DataTypes
{
    partial class SchemaDlg
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
            this.TopPN = new System.Windows.Forms.Panel();
            this.TypeLB = new System.Windows.Forms.Label();
            this.TypeCB = new System.Windows.Forms.ComboBox();
            this.FormatLB = new System.Windows.Forms.Label();
            this.FormatCB = new System.Windows.Forms.ComboBox();
            this.NamespaceScopeCK = new System.Windows.Forms.CheckBox();
            this.SourceLB = new System.Windows.Forms.Label();
            this.SchemaTB = new System.Windows.Forms.TextBox();
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.CloseBTN = new System.Windows.Forms.Button();
            this.TopPN.SuspendLayout();
            this.ButtonsPN.SuspendLayout();
            this.SuspendLayout();
            //
            // TopPN
            //
            this.TopPN.Controls.Add(this.TypeLB);
            this.TopPN.Controls.Add(this.TypeCB);
            this.TopPN.Controls.Add(this.FormatLB);
            this.TopPN.Controls.Add(this.FormatCB);
            this.TopPN.Controls.Add(this.NamespaceScopeCK);
            this.TopPN.Controls.Add(this.SourceLB);
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPN.Location = new System.Drawing.Point(0, 0);
            this.TopPN.Name = "TopPN";
            this.TopPN.Padding = new System.Windows.Forms.Padding(4);
            this.TopPN.Size = new System.Drawing.Size(760, 62);
            this.TopPN.TabIndex = 0;
            //
            // TypeLB
            //
            this.TypeLB.AutoSize = true;
            this.TypeLB.Location = new System.Drawing.Point(6, 12);
            this.TypeLB.Name = "TypeLB";
            this.TypeLB.Size = new System.Drawing.Size(56, 13);
            this.TypeLB.TabIndex = 0;
            this.TypeLB.Text = "Data type";
            //
            // TypeCB
            //
            this.TypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TypeCB.Location = new System.Drawing.Point(70, 9);
            this.TypeCB.Name = "TypeCB";
            this.TypeCB.Size = new System.Drawing.Size(220, 21);
            this.TypeCB.TabIndex = 1;
            this.TypeCB.SelectedIndexChanged += new System.EventHandler(this.Selection_Changed);
            //
            // FormatLB
            //
            this.FormatLB.AutoSize = true;
            this.FormatLB.Location = new System.Drawing.Point(306, 12);
            this.FormatLB.Name = "FormatLB";
            this.FormatLB.Size = new System.Drawing.Size(39, 13);
            this.FormatLB.TabIndex = 2;
            this.FormatLB.Text = "Format";
            //
            // FormatCB
            //
            this.FormatCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.FormatCB.Location = new System.Drawing.Point(352, 9);
            this.FormatCB.Name = "FormatCB";
            this.FormatCB.Size = new System.Drawing.Size(160, 21);
            this.FormatCB.TabIndex = 3;
            this.FormatCB.SelectedIndexChanged += new System.EventHandler(this.Selection_Changed);
            //
            // NamespaceScopeCK
            //
            this.NamespaceScopeCK.AutoSize = true;
            this.NamespaceScopeCK.Location = new System.Drawing.Point(528, 11);
            this.NamespaceScopeCK.Name = "NamespaceScopeCK";
            this.NamespaceScopeCK.Size = new System.Drawing.Size(160, 17);
            this.NamespaceScopeCK.TabIndex = 4;
            this.NamespaceScopeCK.Text = "The whole namespace";
            this.NamespaceScopeCK.UseVisualStyleBackColor = true;
            this.NamespaceScopeCK.CheckedChanged += new System.EventHandler(this.Selection_Changed);
            //
            // SourceLB
            //
            this.SourceLB.AutoSize = true;
            this.SourceLB.Location = new System.Drawing.Point(6, 40);
            this.SourceLB.Name = "SourceLB";
            this.SourceLB.Size = new System.Drawing.Size(0, 13);
            this.SourceLB.TabIndex = 5;
            //
            // SchemaTB
            //
            this.SchemaTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SchemaTB.Font = new System.Drawing.Font("Consolas", 9F);
            this.SchemaTB.Location = new System.Drawing.Point(0, 62);
            this.SchemaTB.Multiline = true;
            this.SchemaTB.Name = "SchemaTB";
            this.SchemaTB.ReadOnly = true;
            this.SchemaTB.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.SchemaTB.Size = new System.Drawing.Size(760, 434);
            this.SchemaTB.TabIndex = 1;
            this.SchemaTB.WordWrap = false;
            //
            // ButtonsPN
            //
            this.ButtonsPN.Controls.Add(this.CloseBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 496);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(760, 40);
            this.ButtonsPN.TabIndex = 2;
            //
            // CloseBTN
            //
            this.CloseBTN.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.CloseBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.CloseBTN.Location = new System.Drawing.Point(672, 8);
            this.CloseBTN.Name = "CloseBTN";
            this.CloseBTN.Size = new System.Drawing.Size(80, 25);
            this.CloseBTN.TabIndex = 0;
            this.CloseBTN.Text = "Close";
            this.CloseBTN.UseVisualStyleBackColor = true;
            //
            // SchemaDlg
            //
            this.AcceptButton = this.CloseBTN;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(760, 536);
            this.Controls.Add(this.SchemaTB);
            this.Controls.Add(this.ButtonsPN);
            this.Controls.Add(this.TopPN);
            this.MinimizeBox = false;
            this.Name = "SchemaDlg";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Schemas";
            this.TopPN.ResumeLayout(false);
            this.TopPN.PerformLayout();
            this.ButtonsPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel TopPN;
        private System.Windows.Forms.Label TypeLB;
        private System.Windows.Forms.ComboBox TypeCB;
        private System.Windows.Forms.Label FormatLB;
        private System.Windows.Forms.ComboBox FormatCB;
        private System.Windows.Forms.CheckBox NamespaceScopeCK;
        private System.Windows.Forms.Label SourceLB;
        private System.Windows.Forms.TextBox SchemaTB;
        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button CloseBTN;
    }
}
