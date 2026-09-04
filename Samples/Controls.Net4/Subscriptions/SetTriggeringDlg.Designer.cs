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

namespace Opc.Ua.Sample.Controls
{
    partial class SetTriggeringDlg
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
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.TriggeredItemsLV = new System.Windows.Forms.CheckedListBox();
            this.TriggeredItemsLB = new System.Windows.Forms.Label();
            this.TriggeringItemLB = new System.Windows.Forms.Label();
            this.TriggeringItemTB = new System.Windows.Forms.TextBox();
            this.HintLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            //
            // ButtonsPN
            //
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 289);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(384, 31);
            this.ButtonsPN.TabIndex = 1;
            //
            // OkBTN
            //
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 0;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            //
            // CancelBTN
            //
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(305, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            //
            // MainPN
            //
            this.MainPN.Controls.Add(this.TriggeredItemsLV);
            this.MainPN.Controls.Add(this.TriggeredItemsLB);
            this.MainPN.Controls.Add(this.TriggeringItemTB);
            this.MainPN.Controls.Add(this.TriggeringItemLB);
            this.MainPN.Controls.Add(this.HintLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(4);
            this.MainPN.Size = new System.Drawing.Size(384, 289);
            this.MainPN.TabIndex = 0;
            //
            // TriggeringItemLB
            //
            this.TriggeringItemLB.AutoSize = true;
            this.TriggeringItemLB.Location = new System.Drawing.Point(7, 11);
            this.TriggeringItemLB.Name = "TriggeringItemLB";
            this.TriggeringItemLB.Size = new System.Drawing.Size(80, 13);
            this.TriggeringItemLB.TabIndex = 0;
            this.TriggeringItemLB.Text = "Triggering Item";
            //
            // TriggeringItemTB
            //
            this.TriggeringItemTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TriggeringItemTB.Location = new System.Drawing.Point(107, 8);
            this.TriggeringItemTB.Name = "TriggeringItemTB";
            this.TriggeringItemTB.ReadOnly = true;
            this.TriggeringItemTB.Size = new System.Drawing.Size(270, 20);
            this.TriggeringItemTB.TabIndex = 1;
            //
            // TriggeredItemsLB
            //
            this.TriggeredItemsLB.AutoSize = true;
            this.TriggeredItemsLB.Location = new System.Drawing.Point(7, 38);
            this.TriggeredItemsLB.Name = "TriggeredItemsLB";
            this.TriggeredItemsLB.Size = new System.Drawing.Size(84, 13);
            this.TriggeredItemsLB.TabIndex = 2;
            this.TriggeredItemsLB.Text = "Triggered Items";
            //
            // TriggeredItemsLV
            //
            this.TriggeredItemsLV.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TriggeredItemsLV.CheckOnClick = true;
            this.TriggeredItemsLV.FormattingEnabled = true;
            this.TriggeredItemsLV.IntegralHeight = false;
            this.TriggeredItemsLV.Location = new System.Drawing.Point(7, 56);
            this.TriggeredItemsLV.Name = "TriggeredItemsLV";
            this.TriggeredItemsLV.Size = new System.Drawing.Size(370, 190);
            this.TriggeredItemsLV.TabIndex = 3;
            //
            // HintLB
            //
            this.HintLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.HintLB.Location = new System.Drawing.Point(7, 249);
            this.HintLB.Name = "HintLB";
            this.HintLB.Size = new System.Drawing.Size(370, 34);
            this.HintLB.TabIndex = 4;
            this.HintLB.Text = "A checked item reports its queued notifications whenever the triggering item repor" +
    "ts, even when its monitoring mode is Sampling. An item may be triggered by severa" +
    "l items at once.";
            //
            // SetTriggeringDlg
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(384, 320);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.MinimumSize = new System.Drawing.Size(360, 280);
            this.Name = "SetTriggeringDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Set Triggering";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label TriggeringItemLB;
        private System.Windows.Forms.TextBox TriggeringItemTB;
        private System.Windows.Forms.Label TriggeredItemsLB;
        private System.Windows.Forms.CheckedListBox TriggeredItemsLV;
        private System.Windows.Forms.Label HintLB;
    }
}
