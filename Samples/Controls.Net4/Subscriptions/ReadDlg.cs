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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Sample.Controls
{
    public partial class ReadDlg : Form
    {
        #region Constructors
        public ReadDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task ShowAsync(Session session, ReadValueIdCollection valueIds, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_session = session;

            await BrowseCTRL.SetViewAsync(m_session, BrowseViewType.Objects, null, telemetry, ct);
            ReadValuesCTRL.Initialize(session, valueIds, telemetry);

            MoveBTN_ClickAsync(BackBTN, null);

            Show();
            BringToFront();
        }

        private async Task ReadAsync(CancellationToken ct = default)
        {
            ReadValueIdCollection nodesToRead = ReadValuesCTRL.GetValueIds();

            if (nodesToRead == null || nodesToRead.Count == 0)
            {
                return;
            }

            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                ct);

            DataValueCollection values = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            ReadResultsCTRL.Telemetry = m_session?.MessageContext?.Telemetry;
            await ReadResultsCTRL.ShowValueAsync(values, true, ct);
        }
        #endregion

        #region Event Handlers
        private async void BrowseCTRL_ItemsSelectedAsync(object sender, NodesSelectedEventArgs e)
        {
            try
            {
                foreach (ReferenceDescription reference in e.References)
                {
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasProperty || reference.IsForward)
                    {
                        await ReadValuesCTRL.AddValueIdAsync(reference);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void MoveBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (sender == NextBTN)
                {
                    await ReadAsync();

                    ReadValuesCTRL.Parent = SplitterPN.Panel1;

                    BackBTN.Visible = true;
                    NextBTN.Visible = false;
                    ReadBTN.Visible = true;
                    ReadValuesCTRL.Visible = true;
                    ReadResultsCTRL.Visible = true;
                    BrowseCTRL.Visible = false;
                }

                else if (sender == BackBTN)
                {
                    ReadValuesCTRL.Parent = SplitterPN.Panel2;

                    BackBTN.Visible = false;
                    NextBTN.Visible = true;
                    ReadBTN.Visible = false;
                    ReadResultsCTRL.Visible = false;
                    BrowseCTRL.Visible = true;
                    ReadValuesCTRL.Visible = true;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ReadMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ReadAsync();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CancelBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
