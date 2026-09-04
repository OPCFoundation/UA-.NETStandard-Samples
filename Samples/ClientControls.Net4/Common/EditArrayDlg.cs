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
using System.Windows.Forms;
using System.Data;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a one dimensional array value held in a
    /// Variant, one element per row.
    /// </summary>
    public partial class EditArrayDlg : SampleForm
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditArrayDlg(ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            ArrayDV.AutoGenerateColumns = false;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Array");
            m_dataset.Tables[0].Columns.Add("Value", typeof(string));
            m_dataset.Tables[0].Columns.Add("Index", typeof(int));
            m_dataset.Tables[0].DefaultView.Sort = "Index";

            ArrayDV.DataSource = m_dataset.Tables[0];

            m_telemetry = telemetry;
        }
        #endregion

        #region Private Fields
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        private BuiltInType m_dataType;
        private readonly ITelemetryContext m_telemetry;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit an array value. Returns false if the user
        /// cancelled the edit; otherwise returns the edited array in
        /// <paramref name="result"/>.
        /// </summary>
        public bool TryShowDialog(
            Variant value,
            BuiltInType dataType,
            bool readOnly,
            string caption,
            out Variant result)
        {
            result = Variant.Null;

            if (caption != null)
            {
                this.Text = caption;
            }

            // detect the data type.
            if (dataType == BuiltInType.Null)
            {
                dataType = value.TypeInfo.BuiltInType;
            }

            m_dataType = dataType;
            ArrayDV.AllowUserToAddRows = !readOnly;
            ArrayDV.AllowUserToDeleteRows = !readOnly;
            ArrayDV.RowHeadersVisible = !readOnly;
            m_dataset.Tables[0].Clear();

            if (VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out _))
            {
                for (int ii = 0; ii < elements.Count; ii++)
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    row[0] = ElementToString(elements[ii]);
                    row[1] = ii;
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }

            m_dataset.AcceptChanges();

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            m_dataset.AcceptChanges();

            if (readOnly)
            {
                result = value;
                return true;
            }

            var newElements = new List<Variant>(m_dataset.Tables[0].DefaultView.Count);

            for (int ii = 0; ii < m_dataset.Tables[0].DefaultView.Count; ii++)
            {
                string text = m_dataset.Tables[0].DefaultView[ii].Row[0] as string;
                newElements.Add(Variant.From(text).ConvertTo(m_dataType));
            }

            result = VariantElements.CreateFromElements(m_dataType, newElements, new int[] { newElements.Count });
            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Converts an element to the text shown in a row. The text has to
        /// round trip through <see cref="Variant.ConvertTo"/> when the edited
        /// array is rebuilt.
        /// </summary>
        private string ElementToString(Variant element)
        {
            return element.ConvertTo(BuiltInType.String).TryGetValue(out string text) ? text : String.Empty;
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void ArrayDV_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            try
            {
                // throws if the text cannot be converted to the array element type.
                _ = Variant.From(e.FormattedValue as string).ConvertTo(m_dataType);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
                e.Cancel = true;
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                for (int ii = 0; ii < ArrayDV.SelectedRows.Count; ii++)
                {
                    DataGridViewRow row = ArrayDV.SelectedRows[ii];
                    DataRowView source = row.DataBoundItem as DataRowView;
                    source.Row.Delete();
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void InsertMI_Click(object sender, EventArgs e)
        {
            try
            {
                for (int ii = 0; ii < ArrayDV.SelectedRows.Count; ii++)
                {
                    DataGridViewRow currentRow = ArrayDV.SelectedRows[ii];
                    DataRowView source = currentRow.DataBoundItem as DataRowView;

                    int index = (int)source.Row[1];

                    for (int jj = 0; jj < m_dataset.Tables[0].Rows.Count; jj++)
                    {
                        int current = (int)m_dataset.Tables[0].Rows[jj][1];

                        if (current >= index)
                        {
                            m_dataset.Tables[0].Rows[jj][1] = current + 1;
                        }
                    }

                    DataRow row = m_dataset.Tables[0].NewRow();
                    row[0] = ElementToString(TypeInfo.GetDefaultVariantValue(m_dataType));
                    row[1] = index;
                    m_dataset.Tables[0].Rows.Add(row);
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
