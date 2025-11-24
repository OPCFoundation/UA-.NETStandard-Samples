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
using System.Drawing;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Displays a
    /// </summary>
    public partial class ReadEventHistoryDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadHistoryDlg"/> class.
        /// </summary>
        public ReadEventHistoryDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        private NodeId m_areaId;
        private FilterDeclaration m_filter;
        private ReadEventDetails m_details;
        private HistoryReadValueId m_nodeToRead;
        #endregion

        #region Public Members
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task<bool> ShowDialogAsync(ISession session, NodeId areaId, FilterDeclaration filter, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            m_session = session;
            m_telemetry = telemetry;
            m_areaId = areaId;
            m_filter = filter;

            EventAreaTB.Text = await session.NodeCache.GetDisplayTextAsync(m_areaId, ct);
            EventTypeTB.Text = await session.NodeCache.GetDisplayTextAsync(filter.EventTypeId, ct);
            EventFilterTB.Text = GetFilterFields(m_filter);

            await ResultsLV.SetSubscribedAsync(false, ct);
            await ResultsLV.ChangeSessionAsync(session, false, telemetry, ct);
            await ResultsLV.ChangeAreaAsync(areaId, false, ct);
            await ResultsLV.ChangeFilterAsync(filter, false, ct);

            // get the beginning of data.
            DateTime startTime;

            try
            {
                startTime = (await ReadFirstDateAsync(ct)).ToLocalTime();
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
            GoBTN.Visible = true;
            NextBTN.Visible = false;
            StopBTN.Enabled = false;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the filter fields formatted as a string.
        /// </summary>
        private string GetFilterFields(FilterDeclaration filter)
        {
            StringBuilder buffer = new StringBuilder();

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
        /// Releases any continuation points.
        /// </summary>
        private async Task ReleaseContinuationPointsAsync(CancellationToken ct = default)
        {
            if (m_details != null)
            {
                HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
                nodesToRead.Add(m_nodeToRead);

                HistoryReadResponse response = await m_session.HistoryReadAsync(
                    null,
                    new ExtensionObject(m_details),
                    TimestampsToReturn.Neither,
                    true,
                    nodesToRead,
                    ct);

                HistoryReadResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                NextBTN.Visible = false;
                StopBTN.Enabled = false;
                GoBTN.Visible = true;
                m_details = null;
                m_nodeToRead = null;
            }
        }

        /// <summary>
        /// Returns the UTC timestamp of the first event in the archive.
        /// </summary>
        private async Task<DateTime> ReadFirstDateAsync(CancellationToken ct = default)
        {
            // read the time of the first event in the archive.
            ReadEventDetails details = new ReadEventDetails();
            details.StartTime = new DateTime(1970, 1, 1);
            details.EndTime = DateTime.MinValue;
            details.NumValuesPerNode = 1;
            details.Filter = new EventFilter();
            details.Filter.AddSelectClause(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Time);

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_areaId;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResponse response = await m_session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Neither,
                false,
                nodesToRead,
                ct);

            HistoryReadResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            // get the data.
            HistoryEvent data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;

            // release the continuation point.
            if (results[0].ContinuationPoint != null)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                response = await m_session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Neither,
                    true,
                    nodesToRead,
                    ct);

                results = response.Results;
                diagnosticInfos = response.DiagnosticInfos;

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }

            // check if an event found.
            if (data == null || data.Events.Count == 0 || data.Events[0].EventFields.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNoDataAvailable);
            }

            // get the event time.
            DateTime? eventTime = data.Events[0].EventFields[0].Value as DateTime?;

            if (eventTime == null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }

            // return time as UTC value.
            return eventTime.Value;
        }

        /// <summary>
        /// Starts a new read operation.
        /// </summary>
        private async Task ReadFirstAsync(CancellationToken ct = default)
        {
            ResultsLV.ClearEventHistory();

            // set up the request parameters.
            ReadEventDetails details = new ReadEventDetails();
            details.StartTime = DateTime.MinValue;
            details.EndTime = DateTime.MinValue;
            details.NumValuesPerNode = 0;
            details.Filter = m_filter.GetFilter();

            if (StartTimeCK.Checked)
            {
                details.StartTime = StartTimeDP.Value.ToUniversalTime();
            }

            if (EndTimeCK.Checked)
            {
                details.EndTime = EndTimeDP.Value.ToUniversalTime();
            }

            if (MaxReturnValuesCK.Checked)
            {
                details.NumValuesPerNode = (uint)MaxReturnValuesNP.Value;
            }

            // read the events from the server.
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_areaId;

            await ReadNextAsync(details, nodeToRead, ct);
        }

        /// <summary>
        /// Continues a read operation.
        /// </summary>
        private async Task ReadNextAsync(ReadEventDetails details, HistoryReadValueId nodeToRead, CancellationToken ct = default)
        {
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResponse response = await m_session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Neither,
                false,
                nodesToRead,
                ct);

            ResponseHeader responseHeader = response.ResponseHeader;
            HistoryReadResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
            }

            // display results.
            HistoryEvent data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;
            await ResultsLV.AddEventHistoryAsync(data, ct);

            // check if a continuation point exists.
            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                NextBTN.Visible = true;
                StopBTN.Enabled = true;
                GoBTN.Visible = false;
                m_details = details;
                m_nodeToRead = nodeToRead;
            }

            // all done.
            else
            {
                NextBTN.Visible = false;
                StopBTN.Enabled = false;
                GoBTN.Visible = true;
                m_details = null;
                m_nodeToRead = null;
            }
        }
        #endregion

        #region Event Handlers
        private async void GoBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_details == null)
                {
                    await ReadFirstAsync();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, "Error Reading History", exception);
            }
        }

        private async void NextBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_details != null)
                {
                    await ReadNextAsync(m_details, m_nodeToRead);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, "Error Reading History", exception);
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
                ClientUtils.HandleException(m_telemetry, "Error Reading History", exception);
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
                if (m_session == null)
                {
                    return;
                }

                NodeId areaId = await new SelectNodeDlg().ShowDialogAsync(m_session, Opc.Ua.ObjectIds.Server, "Select Event Area", m_telemetry, default, Opc.Ua.ReferenceTypeIds.HasEventSource);

                if (areaId == null)
                {
                    return;
                }

                m_areaId = areaId;
                EventAreaTB.Text = await m_session.NodeCache.GetDisplayTextAsync(m_areaId);
                await ResultsLV.ChangeAreaAsync(areaId, false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void EventTypeBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                TypeDeclaration type = await new SelectTypeDlg().ShowDialogAsync(m_session, Opc.Ua.ObjectTypeIds.BaseEventType, "Select Event Type");

                if (type == null)
                {
                    return;
                }

                m_filter = new FilterDeclaration(type, m_filter);
                EventTypeTB.Text = await m_session.NodeCache.GetDisplayTextAsync(m_filter.EventTypeId);
                EventFilterTB.Text = GetFilterFields(m_filter);
                await ResultsLV.ChangeFilterAsync(m_filter, false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private async void EventFilterBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                if (!new ModifyFilterDlg().ShowDialog(m_filter, m_telemetry))
                {
                    return;
                }

                EventFilterTB.Text = GetFilterFields(m_filter);
                await ResultsLV.ChangeFilterAsync(m_filter, false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
