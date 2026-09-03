namespace Opc.Ua.Gds.Client.Controls
{
    partial class CertificateManagementDialog
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
            this.ButtonsPanel = new System.Windows.Forms.Panel();
            this.CloseButton = new System.Windows.Forms.Button();
            this.SelfSignedButton = new System.Windows.Forms.Button();
            this.DeleteButton = new System.Windows.Forms.Button();
            this.CheckRevocationButton = new System.Windows.Forms.Button();
            this.RefreshButton = new System.Windows.Forms.Button();
            this.StatusPanel = new System.Windows.Forms.Panel();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.CertificatesListView = new System.Windows.Forms.ListView();
            this.CertificateTypeColumn = new System.Windows.Forms.ColumnHeader();
            this.SubjectColumn = new System.Windows.Forms.ColumnHeader();
            this.IssuerColumn = new System.Windows.Forms.ColumnHeader();
            this.ExpiresColumn = new System.Windows.Forms.ColumnHeader();
            this.ThumbprintColumn = new System.Windows.Forms.ColumnHeader();
            this.ButtonsPanel.SuspendLayout();
            this.StatusPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // ButtonsPanel
            //
            this.ButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.ButtonsPanel.Controls.Add(this.CloseButton);
            this.ButtonsPanel.Controls.Add(this.SelfSignedButton);
            this.ButtonsPanel.Controls.Add(this.DeleteButton);
            this.ButtonsPanel.Controls.Add(this.CheckRevocationButton);
            this.ButtonsPanel.Controls.Add(this.RefreshButton);
            this.ButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPanel.Location = new System.Drawing.Point(0, 368);
            this.ButtonsPanel.Name = "ButtonsPanel";
            this.ButtonsPanel.Size = new System.Drawing.Size(824, 32);
            this.ButtonsPanel.TabIndex = 0;
            //
            // RefreshButton
            //
            this.RefreshButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.RefreshButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.RefreshButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RefreshButton.ForeColor = System.Drawing.Color.White;
            this.RefreshButton.Location = new System.Drawing.Point(0, 0);
            this.RefreshButton.Name = "RefreshButton";
            this.RefreshButton.Size = new System.Drawing.Size(129, 32);
            this.RefreshButton.TabIndex = 1;
            this.RefreshButton.Text = "Refresh";
            this.RefreshButton.UseVisualStyleBackColor = false;
            this.RefreshButton.Click += new System.EventHandler(this.RefreshButton_Click);
            //
            // CheckRevocationButton
            //
            this.CheckRevocationButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.CheckRevocationButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.CheckRevocationButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CheckRevocationButton.ForeColor = System.Drawing.Color.White;
            this.CheckRevocationButton.Location = new System.Drawing.Point(129, 0);
            this.CheckRevocationButton.Name = "CheckRevocationButton";
            this.CheckRevocationButton.Size = new System.Drawing.Size(160, 32);
            this.CheckRevocationButton.TabIndex = 2;
            this.CheckRevocationButton.Text = "Check Revocation";
            this.CheckRevocationButton.UseVisualStyleBackColor = false;
            this.CheckRevocationButton.Click += new System.EventHandler(this.CheckRevocationButton_Click);
            //
            // DeleteButton
            //
            this.DeleteButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.DeleteButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.DeleteButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.DeleteButton.ForeColor = System.Drawing.Color.White;
            this.DeleteButton.Location = new System.Drawing.Point(289, 0);
            this.DeleteButton.Name = "DeleteButton";
            this.DeleteButton.Size = new System.Drawing.Size(160, 32);
            this.DeleteButton.TabIndex = 3;
            this.DeleteButton.Text = "Delete (staged)";
            this.DeleteButton.UseVisualStyleBackColor = false;
            this.DeleteButton.Click += new System.EventHandler(this.DeleteButton_Click);
            //
            // SelfSignedButton
            //
            this.SelfSignedButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.SelfSignedButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.SelfSignedButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SelfSignedButton.ForeColor = System.Drawing.Color.White;
            this.SelfSignedButton.Location = new System.Drawing.Point(449, 0);
            this.SelfSignedButton.Name = "SelfSignedButton";
            this.SelfSignedButton.Size = new System.Drawing.Size(190, 32);
            this.SelfSignedButton.TabIndex = 4;
            this.SelfSignedButton.Text = "Create Self-Signed (staged)";
            this.SelfSignedButton.UseVisualStyleBackColor = false;
            this.SelfSignedButton.Click += new System.EventHandler(this.SelfSignedButton_Click);
            //
            // CloseButton
            //
            this.CloseButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.CloseButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.CloseButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CloseButton.ForeColor = System.Drawing.Color.White;
            this.CloseButton.Location = new System.Drawing.Point(695, 0);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(129, 32);
            this.CloseButton.TabIndex = 5;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = false;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            //
            // StatusPanel
            //
            this.StatusPanel.Controls.Add(this.StatusLabel);
            this.StatusPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.StatusPanel.Location = new System.Drawing.Point(0, 340);
            this.StatusPanel.Name = "StatusPanel";
            this.StatusPanel.Size = new System.Drawing.Size(824, 28);
            this.StatusPanel.TabIndex = 6;
            //
            // StatusLabel
            //
            this.StatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StatusLabel.Location = new System.Drawing.Point(0, 0);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Padding = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.StatusLabel.Size = new System.Drawing.Size(824, 28);
            this.StatusLabel.TabIndex = 0;
            this.StatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // CertificatesListView
            //
            this.CertificatesListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.CertificateTypeColumn,
            this.SubjectColumn,
            this.IssuerColumn,
            this.ExpiresColumn,
            this.ThumbprintColumn});
            this.CertificatesListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificatesListView.FullRowSelect = true;
            this.CertificatesListView.HideSelection = false;
            this.CertificatesListView.Location = new System.Drawing.Point(0, 0);
            this.CertificatesListView.MultiSelect = false;
            this.CertificatesListView.Name = "CertificatesListView";
            this.CertificatesListView.Size = new System.Drawing.Size(824, 340);
            this.CertificatesListView.TabIndex = 7;
            this.CertificatesListView.UseCompatibleStateImageBehavior = false;
            this.CertificatesListView.View = System.Windows.Forms.View.Details;
            //
            // CertificateTypeColumn
            //
            this.CertificateTypeColumn.Text = "Certificate Type";
            this.CertificateTypeColumn.Width = 150;
            //
            // SubjectColumn
            //
            this.SubjectColumn.Text = "Subject";
            this.SubjectColumn.Width = 230;
            //
            // IssuerColumn
            //
            this.IssuerColumn.Text = "Issuer";
            this.IssuerColumn.Width = 190;
            //
            // ExpiresColumn
            //
            this.ExpiresColumn.Text = "Expires";
            this.ExpiresColumn.Width = 90;
            //
            // ThumbprintColumn
            //
            this.ThumbprintColumn.Text = "Thumbprint";
            this.ThumbprintColumn.Width = 150;
            //
            // CertificateManagementDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(824, 400);
            this.Controls.Add(this.CertificatesListView);
            this.Controls.Add(this.StatusPanel);
            this.Controls.Add(this.ButtonsPanel);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CertificateManagementDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Certificates";
            this.ButtonsPanel.ResumeLayout(false);
            this.StatusPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPanel;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button SelfSignedButton;
        private System.Windows.Forms.Button DeleteButton;
        private System.Windows.Forms.Button CheckRevocationButton;
        private System.Windows.Forms.Button RefreshButton;
        private System.Windows.Forms.Panel StatusPanel;
        private System.Windows.Forms.Label StatusLabel;
        private System.Windows.Forms.ListView CertificatesListView;
        private System.Windows.Forms.ColumnHeader CertificateTypeColumn;
        private System.Windows.Forms.ColumnHeader SubjectColumn;
        private System.Windows.Forms.ColumnHeader IssuerColumn;
        private System.Windows.Forms.ColumnHeader ExpiresColumn;
        private System.Windows.Forms.ColumnHeader ThumbprintColumn;
    }
}
