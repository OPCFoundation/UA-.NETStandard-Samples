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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class WriteRequestListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public WriteRequestListViewCtrl()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Requests");

            m_dataset.Tables[0].Columns.Add("WriteValue", typeof(WriteValue));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("NodeName", typeof(string));
            m_dataset.Tables[0].Columns.Add("Attribute", typeof(string));
            m_dataset.Tables[0].Columns.Add("IndexRange", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("StatusCode", typeof(StatusCode));
            m_dataset.Tables[0].Columns.Add("SourceTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("Result", typeof(StatusCode));

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session used for the write request.
        /// </summary>
        public void ChangeSession(ISession session, ITelemetryContext telemetry)
        {
            m_session = session;
            m_telemetry = telemetry;
        }

        /// <summary>
        /// Adds the nodes to the write request.
        /// </summary>
        public async Task AddNodesAsync(CancellationToken ct, params WriteValue[] nodesToWrite)
        {
            if (nodesToWrite != null && nodesToWrite.Length > 0)
            {
                foreach (WriteValue nodeToWrite in nodesToWrite)
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    await UpdateRowAsync(row, nodeToWrite, ct);
                    nodeToWrite.Handle = row;
                    m_dataset.Tables[0].Rows.Add(row);
                }

                await ReadAsync(ct, nodesToWrite);
            }
        }

        /// <summary>
        /// Updates the values with the current values read from the server.
        /// </summary>
        public async Task ReadAsync(CancellationToken ct = default, params WriteValue[] nodesToWrite)
        {
            if (m_session == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            }

            // build list of values to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            if (nodesToWrite == null || nodesToWrite.Length == 0)
            {
                foreach (DataGridViewRow row in ResultsDV.Rows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    WriteValue value = (WriteValue)source.Row[0];
                    row.Selected = false;

                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = value.NodeId;
                    nodeToRead.AttributeId = value.AttributeId;
                    nodeToRead.IndexRange = value.IndexRange;
                    nodeToRead.Handle = value;

                    nodesToRead.Add(nodeToRead);
                }
            }
            else
            {
                foreach (WriteValue value in nodesToWrite)
                {
                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = value.NodeId;
                    nodeToRead.AttributeId = value.AttributeId;
                    nodeToRead.IndexRange = value.IndexRange;
                    nodeToRead.Handle = value;

                    nodesToRead.Add(nodeToRead);
                }
            }

            // read the values.
            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct);

            DataValueCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                WriteValue nodeToWrite = nodesToRead[ii].Handle as WriteValue;
                DataRow row = nodeToWrite.Handle as DataRow;

                if (StatusCode.IsGood(results[ii].StatusCode))
                {
                    nodeToWrite.Value = results[ii];
                    UpdateRow(row, results[ii]);
                }
            }
        }

        /// <summary>
        /// Reads the values displayed in the control and moves to the display results state.
        /// </summary>
        public async Task WriteAsync(CancellationToken ct = default)
        {
            if (m_session == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            }

            // build list of values to write.
            WriteValueCollection nodesToWrite = new WriteValueCollection();

            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                DataRowView source = row.DataBoundItem as DataRowView;
                WriteValue value = (WriteValue)source.Row[0];
                row.Selected = false;
                nodesToWrite.Add(value);
            }

            // read the values.
            WriteResponse response = await m_session.WriteAsync(
                null,
                nodesToWrite,
                ct);

            StatusCodeCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, nodesToWrite);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

            IndexRangeCH.Visible = false;
            DataTypeCH.Visible = false;
            ValueCH.Visible = false;
            StatusCodeCH.Visible = false;
            ResultCH.Visible = true;

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                DataRowView source = ResultsDV.Rows[ii].DataBoundItem as DataRowView;
                UpdateRow(source.Row, results[ii]);
            }
        }

        /// <summary>
        /// Returns the grid to edit WriteValues state.
        /// </summary>
        public void Back()
        {
            IndexRangeCH.Visible = true;
            DataTypeCH.Visible = true;
            ValueCH.Visible = true;
            StatusCodeCH.Visible = true;
            ResultCH.Visible = false;

            // clear any selection.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the row with the node to write.
        /// </summary>
        public void UpdateRow(DataRow row, StatusCode result)
        {
            row[10] = result;
        }

        /// <summary>
        /// Updates the row with the node to write.
        /// </summary>
        public void UpdateRow(DataRow row, DataValue value)
        {
            row[5] = (value.WrappedValue.TypeInfo != null) ? value.WrappedValue.TypeInfo.ToString() : String.Empty;
            row[6] = value.WrappedValue;
            row[7] = value.StatusCode;
            row[8] = (value.SourceTimestamp != DateTime.MinValue) ? Utils.Format("{0:hh:mm:ss.fff}", value.SourceTimestamp.ToLocalTime()) : String.Empty;
            row[9] = (value.ServerTimestamp != DateTime.MinValue) ? Utils.Format("{0:hh:mm:ss.fff}", value.ServerTimestamp.ToLocalTime()) : String.Empty;
        }

        /// <summary>
        /// Updates the row with the node to write.
        /// </summary>
        public async Task UpdateRowAsync(DataRow row, WriteValue nodeToWrite, CancellationToken ct = default)
        {
            row[0] = nodeToWrite;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(nodeToWrite.AttributeId, null)];
            row[2] = (m_session != null) ? await m_session.NodeCache.GetDisplayTextAsync(nodeToWrite.NodeId, ct) : Utils.ToString(nodeToWrite.NodeId);
            row[3] = Attributes.GetBrowseName(nodeToWrite.AttributeId);
            row[4] = nodeToWrite.IndexRange;

            UpdateRow(row, nodeToWrite.Value);
        }
        #endregion

        #region Event Handlers
        private void PopupMenu_Opening(object sender, CancelEventArgs e)
        {
            try
            {
                EditMI.Enabled = ResultsDV.SelectedRows.Count == 1;
                EditValueMI.Enabled = ResultsDV.SelectedRows.Count > 0;
                DeleteMI.Enabled = ResultsDV.SelectedRows.Count > 0;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void NewMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                WriteValue nodeToWrite = null;

                // choose the first selected row as a template.
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    WriteValue value = (WriteValue)source.Row[0];
                    nodeToWrite = (WriteValue)value.MemberwiseClone();
                    break;
                }

                if (nodeToWrite == null)
                {
                    nodeToWrite = new WriteValue() { AttributeId = Attributes.Value };
                }

                // prompt use to edit new value.
                WriteValue result = await new EditWriteValueDlg().ShowDialogAsync(m_session, nodeToWrite, m_telemetry);

                if (result != null)
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    await UpdateRowAsync(row, result);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // choose the first selected row.
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    WriteValue value = (WriteValue)source.Row[0];

                    WriteValue result = await new EditWriteValueDlg().ShowDialogAsync(m_session, value, m_telemetry);

                    if (result != null)
                    {
                        await UpdateRowAsync(source.Row, result);
                    }

                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void EditValueMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                // choose the first selected row as the templace.
                WriteValue nodeToWrite = null;

                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    nodeToWrite = (WriteValue)source.Row[0];
                    break;
                }

                if (nodeToWrite != null)
                {
                    // prompt use to edit value.
                    object value = new EditComplexValueDlg().ShowDialogAsync(
                        m_session,
                        nodeToWrite.NodeId,
                        nodeToWrite.AttributeId,
                        null,
                        nodeToWrite.Value.Value,
                        false,
                        "Edit Value");

                    if (value != null)
                    {
                        // update all selected rows with the new value.
                        foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                        {
                            DataRowView source = row.DataBoundItem as DataRowView;
                            nodeToWrite = (WriteValue)source.Row[0];
                            nodeToWrite.Value.Value = value;
                            await UpdateRowAsync(source.Row, nodeToWrite);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    source.Row.Delete();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
