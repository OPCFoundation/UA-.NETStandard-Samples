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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Quickstarts.HistoricalEvents.Client.Model;
using Opc.Ua.Samples.Client;

namespace Quickstarts.HistoricalEvents.Client
{
    // the SDK has an EventRecord of its own in Opc.Ua; the one the model hands out is meant.
    using EventRecord = Quickstarts.HistoricalEvents.Client.Model.EventRecord;

    /// <summary>
    /// Shows events, live or from history, one row per event.
    /// </summary>
    /// <remarks>
    /// The control knows nothing about the session: the model computes the text of every
    /// column before it hands an <see cref="EventRecord"/> over, and the host tells the
    /// control which columns the current filter has. Deleting the selected events from
    /// the history goes through a delegate the host sets, because only the host knows
    /// which area and filter the rows were read with.
    /// </remarks>
    public partial class EventListView : UserControl
    {
        public EventListView()
        {
            InitializeComponent();
        }

        #region Private Fields
        private FilterDeclaration m_filter;
        #endregion

        #region Public Members
        /// <summary>
        /// The telemetry context of the client, for reporting errors.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ITelemetryContext Telemetry { get; set; }

        /// <summary>
        /// Deletes events from the history. Null when the host does not offer that.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<IReadOnlyList<EventRecord>, CancellationToken, Task> DeleteEvents { get; set; }

        /// <summary>
        /// Rebuilds the columns for a filter: one for every field the filter shows in the
        /// list. The rows are cleared, because they were laid out for the previous filter.
        /// </summary>
        /// <param name="filter">The filter, or null for no columns.</param>
        public void SetColumns(FilterDeclaration filter)
        {
            m_filter = filter;
            EventsLV.Items.Clear();

            IReadOnlyList<string> names = HistoricalEventsClientModel.ColumnNamesOf(filter);

            // add or update existing columns.
            for (int ii = 0; ii < names.Count; ii++)
            {
                if (ii >= EventsLV.Columns.Count)
                {
                    EventsLV.Columns.Add(new ColumnHeader());
                }

                EventsLV.Columns[ii].Text = names[ii];
                EventsLV.Columns[ii].TextAlign = HorizontalAlignment.Left;
            }

            // remove extra columns.
            while (names.Count < EventsLV.Columns.Count)
            {
                EventsLV.Columns.RemoveAt(EventsLV.Columns.Count - 1);
            }

            AdjustColumns();
        }

        /// <summary>
        /// Removes every event from the list.
        /// </summary>
        public void Clear()
        {
            EventsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Adds one event to the list.
        /// </summary>
        /// <param name="record">The event, with the text of every column already computed.</param>
        /// <param name="atTop">True to insert it at the top, the way live events are shown;
        /// false to append it, the way history is shown in its order.</param>
        public void AddEvent(EventRecord record, bool atTop)
        {
            ArgumentNullException.ThrowIfNull(record);

            var item = new ListViewItem { Tag = record };

            for (int ii = 0; ii < record.DisplayTexts.Count; ii++)
            {
                if (ii == 0)
                {
                    item.Text = record.DisplayTexts[ii];
                }
                else
                {
                    item.SubItems.Add(record.DisplayTexts[ii]);
                }
            }

            if (atTop)
            {
                EventsLV.Items.Insert(0, item);
            }
            else
            {
                EventsLV.Items.Add(item);
            }

            AdjustColumns();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Fits the width of the columns to their content.
        /// </summary>
        private void AdjustColumns()
        {
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// The events behind the selected rows.
        /// </summary>
        private List<EventRecord> SelectedEvents()
        {
            var events = new List<EventRecord>();

            foreach (ListViewItem item in EventsLV.SelectedItems)
            {
                if (item.Tag is EventRecord record)
                {
                    events.Add(record);
                }
            }

            return events;
        }
        #endregion

        #region Event Handlers
        private void ViewDetailsMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count == 0 || m_filter == null)
                {
                    return;
                }

                if (EventsLV.SelectedItems[0].Tag is EventRecord record)
                {
                    using var dialog = new ViewEventDetailsDlg();
                    dialog.ShowDialog(m_filter, record.Fields.ToList());
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(Telemetry, this.Text, exception);
            }
        }

        private async void DeleteHistoryMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                List<EventRecord> events = SelectedEvents();

                if (events.Count == 0 || DeleteEvents == null)
                {
                    return;
                }

                await DeleteEvents(events, CancellationToken.None);

                // the rows stay, struck out, so the user sees what was deleted.
                foreach (ListViewItem item in EventsLV.SelectedItems)
                {
                    if (item.Tag is EventRecord)
                    {
                        item.Font = new Font(item.Font, FontStyle.Strikeout);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(Telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
