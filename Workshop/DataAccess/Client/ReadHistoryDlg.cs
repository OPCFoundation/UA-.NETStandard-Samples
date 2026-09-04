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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Quickstarts.DataAccessClient.Model;

namespace Quickstarts.DataAccessClient
{
    /// <summary>
    /// Prompts the user for a history read and shows the values page by page.
    /// </summary>
    /// <remarks>
    /// The dialog owns the Go/Next/Stop state machine of a paged read: Go starts a read,
    /// Next fetches the next page with the continuation point of the last one, Stop
    /// releases that continuation point. The reads themselves are done by the model.
    /// </remarks>
    public partial class ReadHistoryDlg : Form
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadHistoryDlg"/> class.
        /// </summary>
        public ReadHistoryDlg()
        {
            InitializeComponent();

            ReadTypeCB.Items.Add(ReadType.Raw);
            ReadTypeCB.Items.Add(ReadType.Processed);
            ReadTypeCB.Items.Add(ReadType.Modified);
            ReadTypeCB.Items.Add(ReadType.AtTime);

            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Interpolative);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Average);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_TimeAverage);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Count);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Maximum);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Minimum);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Total);
        }

        private enum ReadType
        {
            Raw,
            Modified,
            AtTime,
            Processed
        }

        private DataAccessClientModel m_model;
        private NodeId m_nodeId;
        private ByteString m_continuationPoint;
        private int m_index;

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        /// <param name="model">The model which reads the history.</param>
        /// <param name="nodeId">The variable whose history is read.</param>
        /// <param name="displayText">The text the window shows for the variable.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<bool> ShowDialogAsync(DataAccessClientModel model, NodeId nodeId, string displayText, CancellationToken ct = default)
        {
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_nodeId = nodeId;

            // update the title.
            if (!String.IsNullOrEmpty(displayText))
            {
                this.Text = Utils.Format("{0} [{1}]", this.Text, displayText);
            }

            // get the beginning of data.
            DateTime startTime;

            try
            {
                startTime = (await m_model.ReadFirstTimestampAsync(nodeId, ct)).ToLocalTime();
            }
            catch (Exception)
            {
                startTime = new DateTime(2000, 1, 1);
            }

            ReadTypeCB.SelectedItem = ReadType.Raw;
            StartTimeDP.MinDate = new DateTime(2000, 1, 1);
            StartTimeDP.Value = StartTimeDP.MinDate < startTime ? startTime : StartTimeDP.MinDate;
            StartTimeCK.Checked = true;
            EndTimeDP.Value = DateTime.Now;
            EndTimeCK.Checked = true;
            MaxReturnValuesNP.Value = 10;
            MaxReturnValuesCK.Checked = true;
            ReturnBoundsCK.Checked = true;
            AggregateCB.SelectedItem = BrowseNames.AggregateFunction_Average;
            ResampleIntervalNP.Value = 0;
            GoBTN.Visible = true;
            NextBTN.Visible = false;
            StopBTN.Enabled = false;

            return ShowDialog() == DialogResult.OK;
        }

        /// <summary>
        /// Shows a page of values and moves the buttons to the state of the read.
        /// </summary>
        private void ShowResults(HistoryPage page)
        {
            m_continuationPoint = page?.ContinuationPoint ?? default;

            bool hasMore = page != null && page.HasMore;

            GoBTN.Visible = !hasMore;
            NextBTN.Visible = hasMore;
            StopBTN.Enabled = hasMore;

            if (page == null)
            {
                return;
            }

            foreach (DataValue value in page.Values)
            {
                StatusCode status = value.StatusCode;

                string index = Utils.Format("[{0}]", m_index++);
                string timestamp = value.SourceTimestamp.ToLocalTime().ToString("yyyy-MM-dd hh:mm:ss");
                string text = Utils.Format("{0}", value.WrappedValue);
                string quality = Utils.Format("{0}", (StatusCode)status.CodeBits);
                string historyInfo = Utils.Format("{0:X2}", (int)status.AggregateBits);

                ListViewItem item = new ListViewItem(index);

                item.SubItems.Add(timestamp);
                item.SubItems.Add(text);
                item.SubItems.Add(quality);
                item.SubItems.Add(historyInfo);

                ResultsLV.Items.Add(item);
            }

            // adjust width of all columns.
            for (int ii = 0; ii < ResultsLV.Columns.Count; ii++)
            {
                ResultsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Tells the server that the rest of the current read is not wanted.
        /// </summary>
        private async Task ReleaseContinuationPointsAsync(CancellationToken ct = default)
        {
            ByteString continuationPoint = m_continuationPoint;

            m_continuationPoint = default;

            if (!continuationPoint.IsNull && continuationPoint.Length > 0)
            {
                await m_model.ReleaseContinuationPointAsync(m_nodeId, continuationPoint, ct);
            }

            ShowResults(null);
        }

        /// <summary>
        /// Reads the next page of the raw or modified history.
        /// </summary>
        private async Task ReadRawAsync(bool isReadModified, CancellationToken ct = default)
        {
            var request = new RawHistoryRequest(
                StartTimeCK.Checked ? StartTimeDP.Value.ToUniversalTime() : DateTime.MinValue,
                EndTimeCK.Checked ? EndTimeDP.Value.ToUniversalTime() : DateTime.MinValue,
                MaxReturnValuesCK.Checked ? (uint)MaxReturnValuesNP.Value : 0,
                ReturnBoundsCK.Checked,
                isReadModified);

            HistoryPage page = await m_model.ReadRawAsync(m_nodeId, request, m_continuationPoint, ct);

            ShowResults(page);
        }

        private Task ReadAtTimeAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Reads the next page of the aggregated history.
        /// </summary>
        private async Task ReadProcessedAsync(CancellationToken ct = default)
        {
            NodeId aggregateId = NodeId.Null;

            switch ((string)AggregateCB.SelectedItem)
            {
                case BrowseNames.AggregateFunction_Interpolative: { aggregateId = ObjectIds.AggregateFunction_Interpolative; break; }
                case BrowseNames.AggregateFunction_TimeAverage: { aggregateId = ObjectIds.AggregateFunction_TimeAverage; break; }
                case BrowseNames.AggregateFunction_Average: { aggregateId = ObjectIds.AggregateFunction_Average; break; }
                case BrowseNames.AggregateFunction_Count: { aggregateId = ObjectIds.AggregateFunction_Count; break; }
                case BrowseNames.AggregateFunction_Maximum: { aggregateId = ObjectIds.AggregateFunction_Maximum; break; }
                case BrowseNames.AggregateFunction_Minimum: { aggregateId = ObjectIds.AggregateFunction_Minimum; break; }
                case BrowseNames.AggregateFunction_Total: { aggregateId = ObjectIds.AggregateFunction_Total; break; }
            }

            var request = new ProcessedHistoryRequest(
                StartTimeDP.Value.ToUniversalTime(),
                EndTimeDP.Value.ToUniversalTime(),
                (double)ResampleIntervalNP.Value,
                aggregateId);

            HistoryPage page = await m_model.ReadProcessedAsync(m_nodeId, request, m_continuationPoint, ct);

            ShowResults(page);
        }

        private Task ReadAsync(CancellationToken ct = default)
        {
            switch ((ReadType)ReadTypeCB.SelectedItem)
            {
                case ReadType.Raw:
                {
                    return ReadRawAsync(false, ct);
                }

                case ReadType.Modified:
                {
                    return ReadRawAsync(true, ct);
                }

                case ReadType.AtTime:
                {
                    return ReadAtTimeAsync(ct);
                }

                case ReadType.Processed:
                {
                    return ReadProcessedAsync(ct);
                }
            }
            return Task.CompletedTask;
        }

        private async void GoBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                m_index = 0;
                ResultsLV.Items.Clear();
                m_continuationPoint = default;

                await ReadAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model.Telemetry, "Error Reading History", exception);
            }
        }

        private async void NextBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ReadAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model.Telemetry, "Error Reading History", exception);
            }
        }

        private async void StopBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ReleaseContinuationPointsAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model.Telemetry, "Error Reading History", exception);
            }
        }

        private async void ReadTypeCB_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                await ReleaseContinuationPointsAsync();
            }
            catch
            {
                // ignore is ok.
            }

            switch ((ReadType)ReadTypeCB.SelectedItem)
            {
                case ReadType.Raw:
                {
                    ReturnBoundsCK.Enabled = true;
                    AggregateCB.Enabled = false;
                    ResampleIntervalNP.Enabled = false;
                    StartTimeCK.Enabled = true;
                    EndTimeCK.Enabled = true;
                    MaxReturnValuesCK.Checked = true;
                    MaxReturnValuesCK.Enabled = true;
                    break;
                }

                case ReadType.Modified:
                {
                    ReturnBoundsCK.Enabled = false;
                    AggregateCB.Enabled = false;
                    ResampleIntervalNP.Enabled = false;
                    StartTimeCK.Enabled = true;
                    EndTimeCK.Enabled = true;
                    MaxReturnValuesCK.Checked = true;
                    MaxReturnValuesCK.Enabled = true;
                    break;
                }

                case ReadType.AtTime:
                {
                    ReturnBoundsCK.Enabled = false;
                    AggregateCB.Enabled = false;
                    ResampleIntervalNP.Enabled = true;
                    StartTimeCK.Enabled = true;
                    EndTimeCK.Enabled = false;
                    EndTimeDP.Checked = false;
                    MaxReturnValuesCK.Checked = true;
                    MaxReturnValuesCK.Enabled = false;
                    break;
                }

                case ReadType.Processed:
                {
                    ReturnBoundsCK.Enabled = false;
                    AggregateCB.Enabled = true;
                    ResampleIntervalNP.Enabled = true;
                    StartTimeCK.Checked = true;
                    StartTimeCK.Enabled = false;
                    EndTimeCK.Checked = true;
                    EndTimeCK.Enabled = false;
                    MaxReturnValuesCK.Checked = false;
                    MaxReturnValuesCK.Enabled = false;
                    break;
                }
            }
        }

        private void StartTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            StartTimeDP.Enabled = StartTimeCK.Checked;
        }

        private void EndTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            EndTimeDP.Enabled = EndTimeCK.Checked;
        }

        private void MaxReturnValuesCK_CheckedChanged(object sender, EventArgs e)
        {
            MaxReturnValuesNP.Enabled = MaxReturnValuesCK.Checked;
        }
    }
}
