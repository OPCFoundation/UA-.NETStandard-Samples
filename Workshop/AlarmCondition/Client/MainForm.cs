/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Client;
using Quickstarts.AlarmConditionClient.Model;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// A form which displays the condition events produced by the server.
    /// </summary>
    /// <remarks>
    /// The window owns the shared connect control and hands the session it opens to the
    /// <see cref="AlarmConditionClientModel"/>, which subscribes to the conditions, keeps
    /// their state and calls the Part 9 Methods. The window renders the snapshots the
    /// model reports into the list, one row per condition and branch, and turns every
    /// entry of the Conditions menu into one call on the model for the selected rows. A
    /// refusal of the server comes back as the status of a row and goes into its comment
    /// column, the way the sample has always shown it.
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            ConnectServerCTRL.Configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62544/Quickstarts/AlarmConditionServer";
            this.Text = configuration.ApplicationName;
            m_telemetry = telemetry;

            // created here, on the thread of the window, so that the model raises its
            // events on this thread and the handlers below can touch the controls directly
            m_model = new AlarmConditionClientModel(telemetry);
            m_model.ConditionChanged += Model_ConditionChanged;
            m_model.ConditionsCleared += Model_ConditionsCleared;
            m_model.Error += Model_Error;

            // initialize controls.
            Conditions_Severity_AllMI.Checked = true;
            Conditions_Severity_AllMI.Tag = EventSeverity.Min;
            Conditions_Severity_LowMI.Tag = EventSeverity.Low;
            Conditions_Severity_MediumMI.Tag = EventSeverity.Medium;
            Conditions_Severity_HighMI.Tag = EventSeverity.High;

            Condition_Type_AllMI.Checked = true;
            Condition_Type_DialogsMI.Checked = false;
            Condition_Type_AlarmsMI.Checked = false;
            Condition_Type_LimitAlarmsMI.Checked = false;
            Condition_Type_DiscreteAlarmsMI.Checked = false;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The column of the list which doubles as the status of the last call on a row.
        /// </summary>
        private const int kCommentColumn = 9;

        /// <summary>
        /// What the Help menu opens.
        /// </summary>
        private const string kHelpUrl =
            "https://github.com/OPCFoundation/UA-.NETStandard-Samples/blob/master/Workshop/AlarmCondition/README.md";

        private readonly ITelemetryContext m_telemetry;
        private readonly AlarmConditionClientModel m_model;
#pragma warning disable CA2213 // Justification: the audit window is closed by the disconnect and close handlers.
        private AuditEventForm m_auditEventForm;
#pragma warning restore CA2213
        #endregion

        #region Server Menu
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        /// <remarks>
        /// The model is detached first: it deletes its subscription before the control
        /// closes the session, because closing a session which still carries a
        /// subscription waits for the publish pipeline to drain.
        /// </remarks>
        private async void Server_DisconnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                CloseAuditWindow();
                await m_model.DetachAsync();
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                ISession session = ConnectServerCTRL.Session;

                if (session == null)
                {
                    CloseAuditWindow();
                    await m_model.DetachAsync();
                    ConditionsMI.Enabled = false;
                    ViewMI.Enabled = false;
                    return;
                }

                // the model subscribes to the conditions and asks the server for a
                // refresh while it attaches; the rows arrive through ConditionChanged.
                await m_model.AttachAsync(session);

                ConditionsMI.Enabled = true;
                ViewMI.Enabled = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                m_model.NotifyReconnectStarting();
                ConditionsMI.Enabled = false;
                ViewMI.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                // the subscription survives the reconnect; the model only asks the server
                // to replay the conditions, in case any of them changed in the meantime.
                await m_model.NotifyReconnectCompletedAsync();
                ConditionsMI.Enabled = true;
                ViewMI.Enabled = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        /// <remarks>
        /// FormClosing cannot await, so the model is detached on a thread pool thread and
        /// waited for; only then does the control close the session.
        /// </remarks>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            CloseAuditWindow();
            ClientUtils.WaitForTeardown(m_model.DetachAsync);
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Model Events
        /// <summary>
        /// Updates the row of a condition with the snapshot the model reports, creating
        /// the row when the condition is new.
        /// </summary>
        /// <remarks>
        /// The model raises this on the thread of the window and one event at a time, so
        /// the list is written directly and every row carries its snapshot before the
        /// next event arrives. A snapshot can still arrive after the window was closed.
        /// </remarks>
        private void Model_ConditionChanged(object sender, ConditionChangedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ConditionSnapshot snapshot = e.Snapshot;
            ListViewItem item = FindRow(snapshot.Key);

            // create a new entry.
            if (item == null)
            {
                item = new ListViewItem(String.Empty);

                for (int ii = 1; ii < ConditionsLV.Columns.Count; ii++)
                {
                    item.SubItems.Add(String.Empty);
                }

                ConditionsLV.Items.Add(item);
            }

            item.SubItems[0].Text = snapshot.SourceName;
            item.SubItems[1].Text = snapshot.ConditionName;
            item.SubItems[2].Text = snapshot.BranchText;
            item.SubItems[3].Text = snapshot.TypeName;
            item.SubItems[4].Text = snapshot.SeverityText;
            item.SubItems[5].Text = snapshot.Time.HasValue
                ? Utils.Format("{0:HH:mm:ss.fff}", snapshot.Time.Value.ToLocalTime())
                : null;
            item.SubItems[6].Text = snapshot.StateText;
            item.SubItems[7].Text = snapshot.Flags;
            item.SubItems[8].Text = snapshot.Message;
            item.SubItems[kCommentColumn].Text = snapshot.Comment;

            item.Tag = snapshot;

            // set the color based on the retain bit.
            if (!snapshot.Retain)
            {
                item.ForeColor = Color.DimGray;
            }
            else
            {
                item.ForeColor = snapshot.IsBranch ? Color.DarkGray : Color.Empty;
            }

            // adjust the width of the columns.
            for (int ii = 0; ii < ConditionsLV.Columns.Count; ii++)
            {
                ConditionsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Starts the list over: a refresh began, the filter changed or the model detached.
        /// </summary>
        private void Model_ConditionsCleared(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ConditionsLV.Items.Clear();
        }

        /// <summary>
        /// Reports a failure on a background path of the model.
        /// </summary>
        private void Model_Error(object sender, ModelErrorEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            ClientUtils.HandleException(m_telemetry, this.Text, e.Exception);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// The keys of the selected rows.
        /// </summary>
        private List<ConditionKey> SelectedKeys()
        {
            return ConditionsLV.SelectedItems
                .Cast<ListViewItem>()
                .Select(item => item.Tag)
                .OfType<ConditionSnapshot>()
                .Select(snapshot => snapshot.Key)
                .ToList();
        }

        /// <summary>
        /// The snapshot of the one selected row, or null when the selection is not one row.
        /// </summary>
        private ConditionSnapshot SelectedSnapshot()
        {
            return ConditionsLV.SelectedItems.Count == 1
                ? ConditionsLV.SelectedItems[0].Tag as ConditionSnapshot
                : null;
        }

        /// <summary>
        /// The row of a condition, or null.
        /// </summary>
        private ListViewItem FindRow(ConditionKey key)
        {
            return ConditionsLV.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => item.Tag is ConditionSnapshot snapshot && snapshot.Key == key);
        }

        /// <summary>
        /// Writes a refused call into the row it belongs to.
        /// </summary>
        /// <remarks>
        /// A call the server accepted needs nothing here: what it changed arrives as an
        /// event and repaints the row. The comment column doubles as the status of the
        /// last call on the row.
        /// </remarks>
        private void ApplyResults(IEnumerable<ConditionCallResult> results)
        {
            foreach (ConditionCallResult result in results)
            {
                if (result.Succeeded)
                {
                    continue;
                }

                ListViewItem item = FindRow(result.Key);

                if (item != null)
                {
                    item.SubItems[kCommentColumn].Text = Utils.Format("{0}", result.Status);
                }
            }
        }

        /// <summary>
        /// Asks the operator for the comment which accompanies a condition Method.
        /// </summary>
        /// <returns>The comment, or a null text when the operator cancelled.</returns>
        private static LocalizedText PromptForComment()
        {
            using (var dialog = new AddCommentDlg())
            {
#pragma warning disable CA1849 // Justification: Sample dialog API is synchronous and preserves current WinForms flow.
                string comment = dialog.ShowDialog(String.Empty);
#pragma warning restore CA1849

                return comment == null ? LocalizedText.Null : new LocalizedText(comment);
            }
        }

        /// <summary>
        /// Closes the audit window, which disposes its trail.
        /// </summary>
        private void CloseAuditWindow()
        {
            AuditEventForm auditEventForm = m_auditEventForm;

            if (auditEventForm != null)
            {
                m_auditEventForm = null;
                auditEventForm.Close();
            }
        }
        #endregion

        #region Conditions Menu
        /// <summary>
        /// Enables the menu entries which apply to the connection and the selection.
        /// </summary>
        private void ConditionsMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                bool connected = m_model.IsConnected;

                Conditions_SetAreaFilterMI.Enabled = connected;
                Conditions_SetTypeMI.Enabled = connected;
                Conditions_SetSeverityMI.Enabled = connected;
                Conditions_EnableMI.Enabled = connected;
                Conditions_DisableMI.Enabled = connected;
                Conditions_AddCommentMI.Enabled = connected;
                Conditions_RefreshMI.Enabled = connected;
                Conditions_AcknowledgeMI.Enabled = connected;
                Conditions_ConfirmMI.Enabled = connected;
                Conditions_RespondMI.Enabled = connected;
                Conditions_ShelvingMI.Enabled = connected;
                Conditions_MonitorMI.Enabled = connected;
                Conditions_SilenceMI.Enabled = connected;
                Conditions_SuppressionMI.Enabled = connected;
                Conditions_ResetMI.Enabled = connected;
                Conditions_GroupMembershipsMI.Enabled = connected;
                Conditions_MaintenanceModeMI.Enabled = connected;

                if (ConditionsLV.SelectedItems.Count == 0)
                {
                    Conditions_EnableMI.Enabled = false;
                    Conditions_DisableMI.Enabled = false;
                    Conditions_AddCommentMI.Enabled = false;
                    Conditions_AcknowledgeMI.Enabled = false;
                    Conditions_ConfirmMI.Enabled = false;
                    Conditions_RespondMI.Enabled = false;
                    Conditions_ShelvingMI.Enabled = false;
                    Conditions_MonitorMI.Enabled = false;
                    Conditions_SilenceMI.Enabled = false;
                    Conditions_SuppressionMI.Enabled = false;
                    Conditions_ResetMI.Enabled = false;
                    Conditions_GroupMembershipsMI.Enabled = false;
                    Conditions_MaintenanceModeMI.Enabled = false;
                }

                if (ConditionsLV.SelectedItems.Count > 1)
                {
                    Conditions_RespondMI.Enabled = false;
                    Conditions_MonitorMI.Enabled = false;
                    Conditions_GroupMembershipsMI.Enabled = false;
                    Conditions_MaintenanceModeMI.Enabled = false;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Asks the server to replay every condition it retains.
        /// </summary>
        private async void Conditions_RefreshMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                await m_model.RefreshAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Enables the selected conditions.
        /// </summary>
        private async void Conditions_EnableMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.EnableAsync(SelectedKeys()));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disables the selected conditions.
        /// </summary>
        private async void Conditions_DisableMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.DisableAsync(SelectedKeys()));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Adds a comment to the selected conditions.
        /// </summary>
        private async void Conditions_AddCommentMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.AddCommentAsync(keys, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Acknowledges the selected conditions.
        /// </summary>
        private async void Conditions_AcknowledgeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.AcknowledgeAsync(keys, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Confirms the selected conditions.
        /// </summary>
        private async void Conditions_ConfirmMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.ConfirmAsync(keys, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Silences the audible annunciation of the selected alarms.
        /// </summary>
        private async void Conditions_SilenceMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.SilenceAsync(SelectedKeys()));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Suppresses the selected alarms.
        /// </summary>
        private async void Conditions_SuppressMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.SuppressAsync(keys, true, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Unsuppresses the selected alarms.
        /// </summary>
        private async void Conditions_UnsuppressMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.SuppressAsync(keys, false, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Takes the selected alarms out of service.
        /// </summary>
        private async void Conditions_RemoveFromServiceMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.SetOutOfServiceAsync(keys, true, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Places the selected alarms back in service.
        /// </summary>
        private async void Conditions_PlaceInServiceMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.SetOutOfServiceAsync(keys, false, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Clears the latch of the selected alarms.
        /// </summary>
        private async void Conditions_ResetMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<ConditionKey> keys = SelectedKeys();
                LocalizedText comment = PromptForComment();

                if (comment.IsNull)
                {
                    return;
                }

                ApplyResults(await m_model.ResetAsync(keys, comment));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shows the alarm groups the selected condition belongs to.
        /// </summary>
        private async void Conditions_GroupMembershipsMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ConditionSnapshot snapshot = SelectedSnapshot();

                if (snapshot == null)
                {
                    return;
                }

                IReadOnlyList<string> names = await m_model.GetGroupMembershipNamesAsync(snapshot.Key);

                MessageBox.Show(
                    names.Count == 0
                        ? "The condition does not belong to any alarm group."
                        : String.Join(Environment.NewLine, names),
                    Utils.Format("Groups of {0}", snapshot.ConditionName),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Turns the maintenance flag of the source of the selected condition on or off.
        /// </summary>
        private async void Conditions_MaintenanceModeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ConditionSnapshot snapshot = SelectedSnapshot();

                if (snapshot == null)
                {
                    return;
                }

                MaintenanceModeResult result = await m_model.ToggleMaintenanceModeAsync(snapshot.Key);

                switch (result.Outcome)
                {
                    case MaintenanceModeOutcome.NoFlag:
                    {
                        MessageBox.Show(
                            "The source of this condition does not offer a maintenance flag.",
                            this.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        break;
                    }

                    case MaintenanceModeOutcome.Failed:
                    {
                        MessageBox.Show(
                            Utils.Format("The maintenance flag could not be written: {0}", result.Status),
                            this.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Unshelves the selected alarms.
        /// </summary>
        private async void Conditions_UnshelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.ShelveAsync(SelectedKeys(), ShelveRequest.Unshelve));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shelves the selected alarms until an operator unshelves them.
        /// </summary>
        private async void Conditions_ManualShelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.ShelveAsync(SelectedKeys(), ShelveRequest.Manual));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shelves the selected alarms until they go inactive once.
        /// </summary>
        private async void Conditions_OneShotShelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.ShelveAsync(SelectedKeys(), ShelveRequest.OneShot));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shelves the selected alarms for thirty seconds.
        /// </summary>
        private async void Conditions_TimedShelveMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ApplyResults(await m_model.ShelveAsync(SelectedKeys(), ShelveRequest.Timed(30000)));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Shows every field of the last event of the selected condition.
        /// </summary>
        private void Conditions_MonitorMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConditionSnapshot snapshot = SelectedSnapshot();

                if (snapshot == null)
                {
                    return;
                }

                ConditionDetails details = m_model.GetDetails(snapshot.Key);

                if (details == null)
                {
                    return;
                }

                using (var dialog = new ViewEventDetailsDlg())
                {
                    dialog.ShowDialog(details.Filter, details.Fields);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Lists only the conditions of at least the picked severity.
        /// </summary>
        private async void Conditions_SeverityMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Conditions_Severity_AllMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_AllMI);
                Conditions_Severity_LowMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_LowMI);
                Conditions_Severity_MediumMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_MediumMI);
                Conditions_Severity_HighMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_HighMI);

                await m_model.SetSeverityAsync((EventSeverity)((ToolStripMenuItem)sender).Tag);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Lists only the conditions of the picked types.
        /// </summary>
        private async void Conditions_TypeMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // the entries pick one type set at a time, so the one which was clicked is
                // the one which ends up checked. Comparing against the severity menu here -
                // which is what this line used to do - left "All" unreachable: it could
                // never be checked again once anything else had been picked.
                Condition_Type_AllMI.Checked = Object.ReferenceEquals(sender, Condition_Type_AllMI);
                Condition_Type_DialogsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_DialogsMI);
                Condition_Type_AlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_AlarmsMI);
                Condition_Type_LimitAlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_LimitAlarmsMI);
                Condition_Type_DiscreteAlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_DiscreteAlarmsMI);

                var selectedTypes = new List<NodeId>();

                if (Condition_Type_AllMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.ConditionType);
                }

                if (Condition_Type_DialogsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.DialogConditionType);
                }

                if (Condition_Type_AlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.AlarmConditionType);
                }

                if (Condition_Type_LimitAlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.ExclusiveLimitAlarmType);
                    selectedTypes.Add(ObjectTypeIds.NonExclusiveLimitAlarmType);
                }

                if (Condition_Type_DiscreteAlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.DiscreteAlarmType);
                }

                await m_model.SetEventTypesAsync(selectedTypes);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Lists only the conditions of the area the operator picks.
        /// </summary>
        private async void Conditions_SetAreaFilterMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                NodeId areaId;

                using (var dialog = new SetAreaFilterDlg())
                {
                    areaId = dialog.ShowDialog(m_model);
                }

                if (areaId.IsNull)
                {
                    return;
                }

                await m_model.SetAreaAsync(areaId);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Answers the selected dialog condition.
        /// </summary>
        private async void Conditions_RespondMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ConditionSnapshot snapshot = SelectedSnapshot();

                if (snapshot == null || !snapshot.IsDialog)
                {
                    return;
                }

                int selectedResponse;

                using (var responseDialog = new DialogResponseDlg())
                {
                    selectedResponse = responseDialog.ShowDialog(snapshot.DialogPrompt, snapshot.DialogResponses);
                }

                if (selectedResponse != -1)
                {
                    ApplyResults(new[] { await m_model.RespondAsync(snapshot.Key, selectedResponse) });
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion

        #region View Menu
        /// <summary>
        /// Opens the window which watches the audit trail of the server.
        /// </summary>
        /// <remarks>
        /// The window gets its own model on the same session: the audit trail is a
        /// streaming subscription which lives as long as the window, and closing the
        /// window is what deletes it on the server again.
        /// </remarks>
        private async void View_AuditEventsMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_auditEventForm == null)
                {
                    AuditTrailModel auditTrail = m_model.CreateAuditTrail();
                    var auditEventForm = new AuditEventForm(auditTrail);

                    try
                    {
                        await auditEventForm.StartAsync();
                    }
                    catch
                    {
                        // a window which never opened has nothing to close, so what it
                        // owns is released here
                        await auditTrail.DisposeAsync();
                        auditEventForm.Dispose();
                        throw;
                    }

                    auditEventForm.FormClosing += new FormClosingEventHandler(AuditEventForm_FormClosing);
                    m_auditEventForm = auditEventForm;
                }

                m_auditEventForm.Show();
                m_auditEventForm.BringToFront();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Forgets the audit window when the operator closes it.
        /// </summary>
        private void AuditEventForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Object.ReferenceEquals(m_auditEventForm, sender))
            {
                m_auditEventForm = null;
            }
        }
        #endregion

        #region Help Menu
        /// <summary>
        /// Opens the documentation of the sample.
        /// </summary>
        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                // The compiled help this used to open ("WebHelp/acclientoverview.htm") has
                // never been part of the repository, so the entry only ever produced an
                // error box. The documentation of the sample is its README.
                var browser = new System.Diagnostics.ProcessStartInfo(kHelpUrl) {
                    UseShellExecute = true,
                };

                System.Diagnostics.Process.Start(browser)?.Dispose();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    Utils.Format("The documentation is at {0}{1}{1}{2}", kHelpUrl, Environment.NewLine, exception.Message),
                    this.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit the application?", "UA Sample Client", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }
        #endregion
    }
}
