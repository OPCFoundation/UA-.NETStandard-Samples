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
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A dialog to view or edit a complex value held in a Variant.
    /// </summary>
    public partial class ComplexValueEditDlg : SampleForm
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexValueEditDlg"/> class.
        /// </summary>
        public ComplexValueEditDlg(ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
            ValueCTRL.Telemetry = telemetry;
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog. Returns false if the user cancelled the edit;
        /// otherwise returns the edited value in <paramref name="result"/>.
        /// </summary>
        public bool TryShowDialog(Variant value, out Variant result)
        {
            result = Variant.Null;

            _ = ValueCTRL.ShowValueAsync(value);

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            result = ValueCTRL.GetValue();
            return true;
        }

        /// <summary>
        /// Displays the array value of a write request, greying out the
        /// elements outside the request's index range. Returns false if the
        /// user cancelled the edit; otherwise returns the edited array value
        /// in <paramref name="result"/>.
        /// </summary>
        public bool TryShowDialog(WriteValue value, out Variant result)
        {
            result = Variant.Null;

            _ = ValueCTRL.ShowValueAsync(value);

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            result = ValueCTRL.GetValue();
            return true;
        }

        /// <summary>
        /// Displays a data value with its status code and timestamps.
        /// </summary>
        public void ShowDialog(DataValue value)
        {
            _ = ValueCTRL.ShowValueAsync(value);

            ShowDialog();
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
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
