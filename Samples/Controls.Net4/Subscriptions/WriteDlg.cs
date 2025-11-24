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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Sample.Controls
{
    public partial class WriteDlg : Form
    {
        #region Constructors
        public WriteDlg()
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
        public async Task ShowAsync(Session session, WriteValueCollection values, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_session = session;

            await BrowseCTRL.SetViewAsync(m_session, BrowseViewType.Objects, null, telemetry, ct);
            WriteValuesCTRL.Initialize(session, values, telemetry);

            MoveBTN_ClickAsync(BackBTN, null);

            Show();
            BringToFront();
        }

        /// <summary>
        /// Writes the valus to the server.
        /// </summary>
        private async Task WriteAsync(CancellationToken ct = default)
        {
            WriteValueCollection nodesToWrite = Utils.Clone(WriteValuesCTRL.GetValues()) as WriteValueCollection;

            if (nodesToWrite == null || nodesToWrite.Count == 0)
            {
                return;
            }

            foreach (WriteValue nodeToWrite in nodesToWrite)
            {
                NumericRange indexRange;
                ServiceResult result = NumericRange.Validate(nodeToWrite.IndexRange, out indexRange);

                if (ServiceResult.IsGood(result) && indexRange != NumericRange.Empty)
                {
                    // apply the index range.
                    object valueToWrite = nodeToWrite.Value.Value;

                    result = indexRange.ApplyRange(ref valueToWrite);

                    if (ServiceResult.IsGood(result))
                    {
                        nodeToWrite.Value.Value = valueToWrite;
                    }
                }
            }

            WriteResponse response = await m_session.WriteAsync(
                null,
                nodesToWrite,
                ct);

            StatusCodeCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, nodesToWrite);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

            await WriteResultsCTRL.ShowValueAsync(results, true, ct);
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
                        await WriteValuesCTRL.AddValueAsync(reference);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void MoveBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (sender == NextBTN)
                {
                    await WriteAsync();

                    WriteValuesCTRL.Parent = SplitterPN.Panel1;

                    BackBTN.Visible = true;
                    NextBTN.Visible = false;
                    WriteBTN.Visible = true;
                    WriteValuesCTRL.Visible = true;
                    WriteResultsCTRL.Visible = true;
                    BrowseCTRL.Visible = false;
                }

                else if (sender == BackBTN)
                {
                    WriteValuesCTRL.Parent = SplitterPN.Panel2;

                    BackBTN.Visible = false;
                    NextBTN.Visible = true;
                    WriteBTN.Visible = false;
                    WriteResultsCTRL.Visible = false;
                    BrowseCTRL.Visible = true;
                    WriteValuesCTRL.Visible = true;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void WriteMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await WriteAsync();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
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
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
