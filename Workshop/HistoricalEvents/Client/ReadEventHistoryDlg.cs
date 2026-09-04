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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Quickstarts.HistoricalEvents.Client.Model;

namespace Quickstarts.HistoricalEvents.Client
{
    // the SDK has an EventRecord of its own in Opc.Ua; the one the model hands out is meant.
    using EventRecord = Quickstarts.HistoricalEvents.Client.Model.EventRecord;

    /// <summary>
    /// Prompts the user for a read of the event history of an area and shows the events
    /// page by page.
    /// </summary>
    /// <remarks>
    /// The dialog owns the Go/Next/Stop state machine of a paged read: Go starts a read,
    /// Next fetches the next page with what the last one handed back, Stop releases the
    /// continuation point. The reads themselves are done by the model.
    /// </remarks>
    public partial class ReadEventHistoryDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadEventHistoryDlg"/> class.
        /// </summary>
        public ReadEventHistoryDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private HistoricalEventsClientModel m_model;
        private NodeId m_areaId;
        private FilterDeclaration m_filter;
        private EventHistoryContinuation m_continuation;
        #endregion

        #region Public Members
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        /// <param name="model">The model which reads the history.</param>
        /// <param name="areaId">The area to start with.</param>
        /// <param name="filter">The filter to start with. The dialog changes it in place.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<bool> ShowDialogAsync(HistoricalEventsClientModel model, NodeId areaId, FilterDeclaration filter, CancellationToken ct = default)
        {
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_areaId = areaId;
            m_filter = filter ?? throw new ArgumentNullException(nameof(filter));

            EventAreaTB.Text = await m_model.GetDisplayTextAsync(m_areaId, ct);
            EventTypeTB.Text = await m_model.GetDisplayTextAsync(m_filter.EventTypeId, ct);
            EventFilterTB.Text = GetFilterFields(m_filter);

            // the list deletes through the model, with the area and filter of the dialog.
            ResultsLV.Telemetry = m_model.Telemetry;
            ResultsLV.DeleteEvents = (events, token) => m_model.DeleteEventsAsync(m_areaId, m_filter, events, token);
            ResultsLV.SetColumns(m_filter);

            // get the beginning of data.
            DateTime startTime;

            try
            {
                startTime = (await m_model.ReadFirstEventTimeAsync(m_areaId, ct)).ToLocalTime();
            }
            catch (Exception)
            {
                startTime = new DateTime(2000, 1, 1);
            }

            StartTimeDP.Value = startTime;
            StartTimeCK.Checked = true;
            EndTimeDP.Value = DateTime.Now;
            EndTimeCK.Checked = true;
            MaxReturnValuesNP.Value = 10;
            MaxReturnValuesCK.Checked = true;
            ShowReadState(null);

            return ShowDialog() == DialogResult.OK;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the filter fields formatted as a string.
        /// </summary>
        private static string GetFilterFields(FilterDeclaration filter)
        {
            var buffer = new StringBuilder();

            foreach (FilterDeclarationField field in filter.Fields)
            {
                if (field.FilterEnabled)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(field.InstanceDeclaration.DisplayName);
                }
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Shows a page of events and moves the buttons to the state of the read.
        /// </summary>
        private void ShowPage(EventHistoryPage page)
        {
            foreach (EventRecord record in page.Events)
            {
                ResultsLV.AddEvent(record, false);
            }

            ShowReadState(page.Continuation);
        }

        /// <summary>
        /// Moves the buttons to the state of the read: Next and Stop while the server holds
        /// more events, Go otherwise.
        /// </summary>
        private void ShowReadState(EventHistoryContinuation continuation)
        {
            m_continuation = continuation;

            bool hasMore = continuation != null;

            NextBTN.Visible = hasMore;
            StopBTN.Enabled = hasMore;
            GoBTN.Visible = !hasMore;
        }

        /// <summary>
        /// Tells the server that the rest of the current read is not wanted.
        /// </summary>
        private async Task ReleaseContinuationPointsAsync(CancellationToken ct = default)
        {
            EventHistoryContinuation continuation = m_continuation;

            if (continuation != null)
            {
                await m_model.ReleaseContinuationPointAsync(continuation, ct);
            }

            ShowReadState(null);
        }

        /// <summary>
        /// Starts a new read operation.
        /// </summary>
        private async Task ReadFirstAsync(CancellationToken ct = default)
        {
            ResultsLV.Clear();

            var request = new EventHistoryRequest(
                StartTimeCK.Checked ? StartTimeDP.Value.ToUniversalTime() : DateTime.MinValue,
                EndTimeCK.Checked ? EndTimeDP.Value.ToUniversalTime() : DateTime.MinValue,
                MaxReturnValuesCK.Checked ? (uint)MaxReturnValuesNP.Value : 0);

            ShowPage(await m_model.ReadHistoryAsync(m_areaId, m_filter, request, ct));
        }

        /// <summary>
        /// Continues a read operation.
        /// </summary>
        private async Task ReadNextAsync(CancellationToken ct = default)
        {
            ShowPage(await m_model.ReadNextAsync(m_continuation, ct));
        }
        #endregion

        #region Event Handlers
        private async void GoBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_continuation == null)
                {
                    await ReadFirstAsync();
                }
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
                if (m_continuation != null)
                {
                    await ReadNextAsync();
                }
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

        private async void EventAreaBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                // a shared dialog which browses the server itself.
                using var dialog = new SelectNodeDlg();
                NodeId areaId = await dialog.ShowDialogAsync(m_model.Session, Opc.Ua.ObjectIds.Server, "Select Event Area", m_model.Telemetry, default, Opc.Ua.ReferenceTypeIds.HasEventSource);

                if (areaId.IsNull)
                {
                    return;
                }

                m_areaId = areaId;
                EventAreaTB.Text = await m_model.GetDisplayTextAsync(m_areaId);
                ResultsLV.Clear();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model.Telemetry, this.Text, exception);
            }
        }

        private async void EventTypeBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                using var dialog = new SelectTypeDlg();
                TypeDeclaration type = await dialog.ShowDialogAsync(m_model, Opc.Ua.ObjectTypeIds.BaseEventType, "Select Event Type");

                if (type == null)
                {
                    return;
                }

                m_filter = new FilterDeclaration(type, m_filter);
                EventTypeTB.Text = await m_model.GetDisplayTextAsync(m_filter.EventTypeId);
                EventFilterTB.Text = GetFilterFields(m_filter);
                ResultsLV.SetColumns(m_filter);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model.Telemetry, this.Text, exception);
            }
        }

        private void EventFilterBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (!m_model.IsConnected)
                {
                    return;
                }

                using var dialog = new ModifyFilterDlg();
                if (!dialog.ShowDialog(m_filter, m_model.Telemetry))
                {
                    return;
                }

                EventFilterTB.Text = GetFilterFields(m_filter);
                ResultsLV.SetColumns(m_filter);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model.Telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
