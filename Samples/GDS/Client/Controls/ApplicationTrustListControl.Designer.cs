namespace Opc.Ua.Gds.Client
{
    partial class ApplicationTrustListControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.RegistrationPanel = new System.Windows.Forms.Panel();
            this.CertificateStoreControl = new Opc.Ua.Gds.Client.Controls.CertificateStoreControl();
            this.RegistrationButtonsPanel = new System.Windows.Forms.Panel();
            this.ApplyChangesButton = new System.Windows.Forms.Button();
            this.PushToServerButton = new System.Windows.Forms.Button();
            this.MergeWithGdsButton = new System.Windows.Forms.Button();
            this.PullFromGdsButton = new System.Windows.Forms.Button();
            this.ReadTrustListButton = new System.Windows.Forms.Button();
            this.TrustListMasksComboBox = new System.Windows.Forms.ComboBox();
            this.ToolTips = new System.Windows.Forms.ToolTip(this.components);
            this.panel1 = new System.Windows.Forms.Panel();
            this.label1 = new System.Windows.Forms.Label();
            this.RegistrationPanel.SuspendLayout();
            this.RegistrationButtonsPanel.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // RegistrationPanel
            // 
            this.RegistrationPanel.Controls.Add(this.CertificateStoreControl);
            this.RegistrationPanel.Controls.Add(this.RegistrationButtonsPanel);
            this.RegistrationPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationPanel.Location = new System.Drawing.Point(0, 0);
            this.RegistrationPanel.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.RegistrationPanel.Name = "RegistrationPanel";
            this.RegistrationPanel.Size = new System.Drawing.Size(1318, 1066);
            this.RegistrationPanel.TabIndex = 50;
            // 
            // CertificateStoreControl
            // 
            this.CertificateStoreControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateStoreControl.Location = new System.Drawing.Point(0, 0);
            this.CertificateStoreControl.Margin = new System.Windows.Forms.Padding(6, 8, 6, 8);
            this.CertificateStoreControl.Name = "CertificateStoreControl";
            this.CertificateStoreControl.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.CertificateStoreControl.Size = new System.Drawing.Size(1318, 1017);
            this.CertificateStoreControl.TabIndex = 14;
            // 
            // RegistrationButtonsPanel
            // 
            this.RegistrationButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegistrationButtonsPanel.Controls.Add(this.ApplyChangesButton);
            this.RegistrationButtonsPanel.Controls.Add(this.PushToServerButton);
            this.RegistrationButtonsPanel.Controls.Add(this.MergeWithGdsButton);
            this.RegistrationButtonsPanel.Controls.Add(this.PullFromGdsButton);
            this.RegistrationButtonsPanel.Controls.Add(this.ReadTrustListButton);
            this.RegistrationButtonsPanel.Controls.Add(this.panel1);
            this.RegistrationButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RegistrationButtonsPanel.Location = new System.Drawing.Point(0, 1017);
            this.RegistrationButtonsPanel.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.RegistrationButtonsPanel.Name = "RegistrationButtonsPanel";
            this.RegistrationButtonsPanel.Size = new System.Drawing.Size(1318, 49);
            this.RegistrationButtonsPanel.TabIndex = 13;
            // 
            // ApplyChangesButton
            // 
            this.ApplyChangesButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ApplyChangesButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ApplyChangesButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApplyChangesButton.ForeColor = System.Drawing.Color.White;
            this.ApplyChangesButton.Location = new System.Drawing.Point(1101, 0);
            this.ApplyChangesButton.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.ApplyChangesButton.Name = "ApplyChangesButton";
            this.ApplyChangesButton.Size = new System.Drawing.Size(194, 49);
            this.ApplyChangesButton.TabIndex = 5;
            this.ApplyChangesButton.Text = "Apply Changes";
            this.ApplyChangesButton.UseVisualStyleBackColor = false;
            this.ApplyChangesButton.Click += new System.EventHandler(this.ApplyChangesButton_Click);
            // 
            // PushToServerButton
            // 
            this.PushToServerButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.PushToServerButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.PushToServerButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PushToServerButton.ForeColor = System.Drawing.Color.White;
            this.PushToServerButton.Location = new System.Drawing.Point(907, 0);
            this.PushToServerButton.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.PushToServerButton.Name = "PushToServerButton";
            this.PushToServerButton.Size = new System.Drawing.Size(194, 49);
            this.PushToServerButton.TabIndex = 3;
            this.PushToServerButton.Text = "Push To Server";
            this.ToolTips.SetToolTip(this.PushToServerButton, "Updates the Trust List on the remote Server.");
            this.PushToServerButton.UseVisualStyleBackColor = false;
            this.PushToServerButton.Click += new System.EventHandler(this.PushToServerButton_Click);
            this.PushToServerButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.PushToServerButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // MergeWithGdsButton
            // 
            this.MergeWithGdsButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.MergeWithGdsButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.MergeWithGdsButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MergeWithGdsButton.ForeColor = System.Drawing.Color.White;
            this.MergeWithGdsButton.Location = new System.Drawing.Point(713, 0);
            this.MergeWithGdsButton.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.MergeWithGdsButton.Name = "MergeWithGdsButton";
            this.MergeWithGdsButton.Size = new System.Drawing.Size(194, 49);
            this.MergeWithGdsButton.TabIndex = 4;
            this.MergeWithGdsButton.Text = "Merge with GDS";
            this.ToolTips.SetToolTip(this.MergeWithGdsButton, "Adds the Certificsates and CRLs provided by the GDS to the Trust List.");
            this.MergeWithGdsButton.UseVisualStyleBackColor = false;
            this.MergeWithGdsButton.Click += new System.EventHandler(this.MergeWithGdsButton_Click);
            this.MergeWithGdsButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.MergeWithGdsButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // PullFromGdsButton
            // 
            this.PullFromGdsButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.PullFromGdsButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.PullFromGdsButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PullFromGdsButton.ForeColor = System.Drawing.Color.White;
            this.PullFromGdsButton.Location = new System.Drawing.Point(519, 0);
            this.PullFromGdsButton.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.PullFromGdsButton.Name = "PullFromGdsButton";
            this.PullFromGdsButton.Size = new System.Drawing.Size(194, 49);
            this.PullFromGdsButton.TabIndex = 0;
            this.PullFromGdsButton.Text = "Replace with GDS";
            this.ToolTips.SetToolTip(this.PullFromGdsButton, "Replaces all Certificates and CRLs in the Trust Lsts with the contents of the Tru" +
        "st List provided by the GDS.");
            this.PullFromGdsButton.UseVisualStyleBackColor = false;
            this.PullFromGdsButton.Click += new System.EventHandler(this.PullFromGdsButton_Click);
            this.PullFromGdsButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.PullFromGdsButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // ReadTrustListButton
            // 
            this.ReadTrustListButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ReadTrustListButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ReadTrustListButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ReadTrustListButton.ForeColor = System.Drawing.Color.White;
            this.ReadTrustListButton.Location = new System.Drawing.Point(325, 0);
            this.ReadTrustListButton.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.ReadTrustListButton.Name = "ReadTrustListButton";
            this.ReadTrustListButton.Size = new System.Drawing.Size(194, 49);
            this.ReadTrustListButton.TabIndex = 2;
            this.ReadTrustListButton.Text = "Reload";
            this.ToolTips.SetToolTip(this.ReadTrustListButton, "Reloads the Trust List from disk or by reading it from the remote Server.");
            this.ReadTrustListButton.UseVisualStyleBackColor = false;
            this.ReadTrustListButton.Click += new System.EventHandler(this.ReloadTrustListButton_ClickAsync);
            this.ReadTrustListButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.ReadTrustListButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // TrustListMasksComboBox
            // 
            this.TrustListMasksComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TrustListMasksComboBox.DropDownWidth = 168;
            this.TrustListMasksComboBox.FormattingEnabled = true;
            this.TrustListMasksComboBox.Location = new System.Drawing.Point(137, 11);
            this.TrustListMasksComboBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 1);
            this.TrustListMasksComboBox.Name = "TrustListMasksComboBox";
            this.TrustListMasksComboBox.Size = new System.Drawing.Size(168, 28);
            this.TrustListMasksComboBox.TabIndex = 6;
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.SystemColors.Control;
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.TrustListMasksComboBox);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Left;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(325, 49);
            this.panel1.TabIndex = 7;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(128, 20);
            this.label1.TabIndex = 7;
            this.label1.Text = "Trust List Masks:";
            // 
            // ApplicationTrustListControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RegistrationPanel);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "ApplicationTrustListControl";
            this.Size = new System.Drawing.Size(1318, 1066);
            this.RegistrationPanel.ResumeLayout(false);
            this.RegistrationButtonsPanel.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel RegistrationPanel;
        private System.Windows.Forms.Panel RegistrationButtonsPanel;
        private System.Windows.Forms.Button PullFromGdsButton;
        private System.Windows.Forms.Button ReadTrustListButton;
        private Opc.Ua.Gds.Client.Controls.CertificateStoreControl CertificateStoreControl;
        private System.Windows.Forms.Button PushToServerButton;
        private System.Windows.Forms.Button MergeWithGdsButton;
        private System.Windows.Forms.ToolTip ToolTips;
        private System.Windows.Forms.Button ApplyChangesButton;
        private System.Windows.Forms.ComboBox TrustListMasksComboBox;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label label1;
    }
}
