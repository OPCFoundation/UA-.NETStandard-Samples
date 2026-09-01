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
using Opc.Ua;
using Opc.Ua.Client.Controls;


namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Edits a simple scalar value as text. The value is passed and returned
    /// as a Variant and the edited text is converted back to the value's
    /// built in type.
    /// </summary>
    public partial class SimpleValueEditDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Default constructor
        /// </summary>
        public SimpleValueEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Variant m_value;
        private BuiltInType m_targetType;
        private ITelemetryContext m_telemetry;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog. Returns false if the user cancelled the edit;
        /// otherwise returns the edited value in <paramref name="result"/>.
        /// </summary>
        public bool TryShowDialog(Variant value, ITelemetryContext telemetry, out Variant result)
        {
            result = Variant.Null;

            m_targetType = !value.IsNull ? value.TypeInfo.BuiltInType : BuiltInType.String;
            m_telemetry = telemetry;

            this.Text = Utils.Format("{0} ({1})", this.Text, m_targetType);

            // the displayed text has to round trip through ConvertTo when it is parsed back.
            ValueTB.Text = value.ConvertTo(BuiltInType.String).TryGetValue(out string text) ? text : String.Empty;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            result = m_value;
            return true;
        }

        /// <summary>
        /// Returns true if the dialog supports editing the type.
        /// </summary>
        public static bool IsSimpleType(TypeInfo typeInfo)
        {
            if (typeInfo.ValueRank >= 0)
            {
                return false;
            }

            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.String:
                case BuiltInType.DateTime:
                case BuiltInType.Guid:
                {
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_value = Variant.From(ValueTB.Text).ConvertTo(m_targetType);
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
