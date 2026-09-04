/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Gds.Client.Model;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Shows the trust list of the selected application and offers the four things which
    /// can be done with it: reload it, merge the one the GDS holds into it, replace it with
    /// the one the GDS holds, and push it back to a server.
    /// </summary>
    /// <remarks>
    /// The reads, the writes and the certificate stores belong to the
    /// <see cref="TrustListModel"/>; this control shows what it returns and asks the
    /// questions the model must not ask.
    /// </remarks>
    public partial class ApplicationTrustListControl : UserControl
    {
        public ApplicationTrustListControl()
        {
            InitializeComponent();
            TrustListMasksComboBox.DataSource = Enum.GetValues<TrustListMasks>();
            TrustListMasksComboBox.SelectedItem = TrustListMasks.All;
        }

        private readonly TrustListModel m_model = new TrustListModel();
        private ITelemetryContext m_telemetry;

        public async Task Initialize(GlobalDiscoveryServerClient gds, ServerPushConfigurationClient server, RegisteredApplication application, bool isHttps, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            m_telemetry = telemetry;
            m_model.Initialize(gds, server, application, isHttps, telemetry);

            // display local trust list.
            if (m_model.HasApplication)
            {
                await CertificateStoreControl.Initialize(telemetry, m_model.TrustListStorePath, m_model.IssuerListStorePath, null, ct);
                MergeWithGdsButton.Enabled = m_model.CanPullFromGds;
            }

            ApplyChangesButton.Enabled = false;
        }

        private async void ReloadTrustListButton_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.HasApplication)
                {
                    await CertificateStoreControl.Initialize(m_telemetry, null, null, null);
                    return;
                }

                if (m_model.IsServerPush)
                {
                    if (!Enum.TryParse(TrustListMasksComboBox.SelectedItem.ToString(), out TrustListMasks masks))
                    {
                        masks = TrustListMasks.All;
                    }

                    ServerTrustList lists = await m_model.ReadFromServerAsync(masks);

                    CertificateStoreControl.Initialize(lists.TrustList, lists.RejectedCertificates, true);
                    return;
                }

                await CertificateStoreControl.Initialize(m_telemetry, m_model.TrustListStorePath, m_model.IssuerListStorePath, null);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
            }
        }

        private async void MergeWithGdsButton_Click(object sender, EventArgs e)
        {
            try
            {
                await PullFromGdsAsync(false);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
            }
        }

        private async void PullFromGdsButton_Click(object sender, EventArgs e)
        {
            try
            {
                await PullFromGdsAsync(true);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Text, ex);
            }
        }

        /// <summary>
        /// Downloads the trust list of the Global Discovery Server and shows what became
        /// of it.
        /// </summary>
        /// <param name="deleteBeforeAdd">Whether to replace the local list rather than merge
        /// the downloaded one into it.</param>
        /// <param name="ct">The cancellation token.</param>
        private async Task PullFromGdsAsync(bool deleteBeforeAdd, CancellationToken ct = default)
        {
            try
            {
                TrustListPullResult pull = await m_model.PullFromGdsAsync(deleteBeforeAdd, ct);

                switch (pull.Outcome)
                {
                    case TrustListPullOutcome.NoTrustList:
                    {
                        await CertificateStoreControl.Initialize(m_telemetry, null, null, null, ct);
                        break;
                    }

                    case TrustListPullOutcome.AwaitingPushToServer:
                    {
                        CertificateStoreControl.Initialize(pull.TrustList, null, deleteBeforeAdd);

                        MessageBox.Show(
                            Parent,
                            "The trust list (include CRLs) was downloaded from the GDS. It now has to be pushed to the Server.",
                            Parent.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        break;
                    }

                    default:
                    {
                        await CertificateStoreControl.Initialize(m_telemetry, m_model.TrustListStorePath, m_model.IssuerListStorePath, null, ct);

                        MessageBox.Show(
                            Parent,
                            "The trust list (include CRLs) was downloaded from the GDS and saved locally.",
                            Parent.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private async void PushToServerButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsServerPush)
                {
                    return;
                }

                bool applyChanges = await m_model.PushToServerAsync(CertificateStoreControl.GetTrustLists());

                if (applyChanges)
                {
                    MessageBox.Show(
                        Parent,
                        "The trust list was updated, however, the apply changes command must be sent before the server will use the new trust list.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    ApplyChangesButton.Enabled = true;
                }
            }
            catch (Exception exception)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Parent.Text, exception);
            }
        }

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.CornflowerBlue;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.MidnightBlue;
        }

        /// <summary>
        /// Tells the server to start using the trust list which was pushed to it.
        /// </summary>
        /// <remarks>
        /// A server which restarts itself to apply the changes is the expected outcome, not
        /// a failure, and the model reports it rather than throwing it.
        /// </remarks>
        private async void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            try
            {
                await m_model.ApplyChangesAsync();
            }
            catch (Exception exception)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_telemetry, Parent.Text, exception);
            }
        }
    }
}
