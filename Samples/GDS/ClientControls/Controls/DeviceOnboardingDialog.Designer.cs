namespace Opc.Ua.Gds.Client.Controls
{
    partial class DeviceOnboardingDialog
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
            this.UnregisterButton = new System.Windows.Forms.Button();
            this.RegisterButton = new System.Windows.Forms.Button();
            this.ClearButton = new System.Windows.Forms.Button();
            this.AddButton = new System.Windows.Forms.Button();
            this.HeaderPanel = new System.Windows.Forms.Panel();
            this.HintLabel = new System.Windows.Forms.Label();
            this.RegistrarTextBox = new System.Windows.Forms.Label();
            this.RegistrarLabel = new System.Windows.Forms.Label();
            this.StatusPanel = new System.Windows.Forms.Panel();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.TicketsListView = new System.Windows.Forms.ListView();
            this.TicketColumn = new System.Windows.Forms.ColumnHeader();
            this.SizeColumn = new System.Windows.Forms.ColumnHeader();
            this.ResultColumn = new System.Windows.Forms.ColumnHeader();
            this.ButtonsPanel.SuspendLayout();
            this.HeaderPanel.SuspendLayout();
            this.StatusPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // ButtonsPanel
            //
            this.ButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.ButtonsPanel.Controls.Add(this.CloseButton);
            this.ButtonsPanel.Controls.Add(this.UnregisterButton);
            this.ButtonsPanel.Controls.Add(this.RegisterButton);
            this.ButtonsPanel.Controls.Add(this.ClearButton);
            this.ButtonsPanel.Controls.Add(this.AddButton);
            this.ButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPanel.Location = new System.Drawing.Point(0, 308);
            this.ButtonsPanel.Name = "ButtonsPanel";
            this.ButtonsPanel.Size = new System.Drawing.Size(684, 32);
            this.ButtonsPanel.TabIndex = 0;
            //
            // AddButton
            //
            this.AddButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.AddButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.AddButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.AddButton.ForeColor = System.Drawing.Color.White;
            this.AddButton.Location = new System.Drawing.Point(0, 0);
            this.AddButton.Name = "AddButton";
            this.AddButton.Size = new System.Drawing.Size(129, 32);
            this.AddButton.TabIndex = 1;
            this.AddButton.Text = "Add Ticket(s)...";
            this.AddButton.UseVisualStyleBackColor = false;
            this.AddButton.Click += new System.EventHandler(this.AddButton_Click);
            //
            // ClearButton
            //
            this.ClearButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ClearButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ClearButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ClearButton.ForeColor = System.Drawing.Color.White;
            this.ClearButton.Location = new System.Drawing.Point(129, 0);
            this.ClearButton.Name = "ClearButton";
            this.ClearButton.Size = new System.Drawing.Size(100, 32);
            this.ClearButton.TabIndex = 2;
            this.ClearButton.Text = "Clear";
            this.ClearButton.UseVisualStyleBackColor = false;
            this.ClearButton.Click += new System.EventHandler(this.ClearButton_Click);
            //
            // RegisterButton
            //
            this.RegisterButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegisterButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.RegisterButton.Enabled = false;
            this.RegisterButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RegisterButton.ForeColor = System.Drawing.Color.White;
            this.RegisterButton.Location = new System.Drawing.Point(229, 0);
            this.RegisterButton.Name = "RegisterButton";
            this.RegisterButton.Size = new System.Drawing.Size(140, 32);
            this.RegisterButton.TabIndex = 3;
            this.RegisterButton.Text = "Register Tickets";
            this.RegisterButton.UseVisualStyleBackColor = false;
            this.RegisterButton.Click += new System.EventHandler(this.RegisterButton_Click);
            //
            // UnregisterButton
            //
            this.UnregisterButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.UnregisterButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.UnregisterButton.Enabled = false;
            this.UnregisterButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.UnregisterButton.ForeColor = System.Drawing.Color.White;
            this.UnregisterButton.Location = new System.Drawing.Point(369, 0);
            this.UnregisterButton.Name = "UnregisterButton";
            this.UnregisterButton.Size = new System.Drawing.Size(150, 32);
            this.UnregisterButton.TabIndex = 4;
            this.UnregisterButton.Text = "Unregister Tickets";
            this.UnregisterButton.UseVisualStyleBackColor = false;
            this.UnregisterButton.Click += new System.EventHandler(this.UnregisterButton_Click);
            //
            // CloseButton
            //
            this.CloseButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.CloseButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.CloseButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CloseButton.ForeColor = System.Drawing.Color.White;
            this.CloseButton.Location = new System.Drawing.Point(575, 0);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(109, 32);
            this.CloseButton.TabIndex = 5;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = false;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            //
            // HeaderPanel
            //
            this.HeaderPanel.Controls.Add(this.RegistrarTextBox);
            this.HeaderPanel.Controls.Add(this.RegistrarLabel);
            this.HeaderPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.HeaderPanel.Location = new System.Drawing.Point(0, 0);
            this.HeaderPanel.Name = "HeaderPanel";
            this.HeaderPanel.Size = new System.Drawing.Size(684, 28);
            this.HeaderPanel.TabIndex = 6;
            //
            // HintLabel
            //
            this.HintLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.HintLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.HintLabel.Location = new System.Drawing.Point(0, 28);
            this.HintLabel.Name = "HintLabel";
            this.HintLabel.Padding = new System.Windows.Forms.Padding(6, 0, 6, 4);
            this.HintLabel.Size = new System.Drawing.Size(684, 32);
            this.HintLabel.TabIndex = 8;
            this.HintLabel.Text = "A ticket is an opaque blob issued by the device manufacturer. The sample registrar " +
                "stores it verbatim and keys it by its SHA-256 hash, so any file can stand in for one.";
            //
            // RegistrarLabel
            //
            this.RegistrarLabel.AutoSize = true;
            this.RegistrarLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.RegistrarLabel.Location = new System.Drawing.Point(0, 0);
            this.RegistrarLabel.Name = "RegistrarLabel";
            this.RegistrarLabel.Padding = new System.Windows.Forms.Padding(6, 7, 6, 0);
            this.RegistrarLabel.Size = new System.Drawing.Size(70, 20);
            this.RegistrarLabel.TabIndex = 0;
            this.RegistrarLabel.Text = "Registrar";
            //
            // RegistrarTextBox
            //
            this.RegistrarTextBox.AutoSize = true;
            this.RegistrarTextBox.Dock = System.Windows.Forms.DockStyle.Left;
            this.RegistrarTextBox.Location = new System.Drawing.Point(70, 0);
            this.RegistrarTextBox.Name = "RegistrarTextBox";
            this.RegistrarTextBox.Padding = new System.Windows.Forms.Padding(6, 7, 6, 0);
            this.RegistrarTextBox.Size = new System.Drawing.Size(40, 20);
            this.RegistrarTextBox.TabIndex = 1;
            this.RegistrarTextBox.Text = "---";
            //
            // StatusPanel
            //
            this.StatusPanel.Controls.Add(this.StatusLabel);
            this.StatusPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.StatusPanel.Location = new System.Drawing.Point(0, 280);
            this.StatusPanel.Name = "StatusPanel";
            this.StatusPanel.Size = new System.Drawing.Size(684, 28);
            this.StatusPanel.TabIndex = 7;
            //
            // StatusLabel
            //
            this.StatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StatusLabel.Location = new System.Drawing.Point(0, 0);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Padding = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.StatusLabel.Size = new System.Drawing.Size(684, 28);
            this.StatusLabel.TabIndex = 0;
            this.StatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // TicketsListView
            //
            this.TicketsListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.TicketColumn,
            this.SizeColumn,
            this.ResultColumn});
            this.TicketsListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TicketsListView.FullRowSelect = true;
            this.TicketsListView.HideSelection = false;
            this.TicketsListView.Location = new System.Drawing.Point(0, 28);
            this.TicketsListView.Name = "TicketsListView";
            this.TicketsListView.Size = new System.Drawing.Size(684, 252);
            this.TicketsListView.TabIndex = 8;
            this.TicketsListView.UseCompatibleStateImageBehavior = false;
            this.TicketsListView.View = System.Windows.Forms.View.Details;
            //
            // TicketColumn
            //
            this.TicketColumn.Text = "Ticket";
            this.TicketColumn.Width = 320;
            //
            // SizeColumn
            //
            this.SizeColumn.Text = "Bytes";
            this.SizeColumn.Width = 80;
            //
            // ResultColumn
            //
            this.ResultColumn.Text = "Result";
            this.ResultColumn.Width = 260;
            //
            // DeviceOnboardingDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(684, 340);
            this.Controls.Add(this.TicketsListView);
            this.Controls.Add(this.HintLabel);
            this.Controls.Add(this.HeaderPanel);
            this.Controls.Add(this.StatusPanel);
            this.Controls.Add(this.ButtonsPanel);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DeviceOnboardingDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Device Onboarding (OPC 10000-21)";
            this.ButtonsPanel.ResumeLayout(false);
            this.HeaderPanel.ResumeLayout(false);
            this.HeaderPanel.PerformLayout();
            this.StatusPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPanel;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button UnregisterButton;
        private System.Windows.Forms.Button RegisterButton;
        private System.Windows.Forms.Button ClearButton;
        private System.Windows.Forms.Button AddButton;
        private System.Windows.Forms.Panel HeaderPanel;
        private System.Windows.Forms.Label RegistrarTextBox;
        private System.Windows.Forms.Label RegistrarLabel;
        private System.Windows.Forms.Label HintLabel;
        private System.Windows.Forms.Panel StatusPanel;
        private System.Windows.Forms.Label StatusLabel;
        private System.Windows.Forms.ListView TicketsListView;
        private System.Windows.Forms.ColumnHeader TicketColumn;
        private System.Windows.Forms.ColumnHeader SizeColumn;
        private System.Windows.Forms.ColumnHeader ResultColumn;
    }
}
