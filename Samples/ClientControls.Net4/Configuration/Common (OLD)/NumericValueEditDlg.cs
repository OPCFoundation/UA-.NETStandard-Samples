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

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A dialog to edit a numeric value held in a Variant.
    /// </summary>
    public partial class NumericValueEditDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="NumericValueEditDlg"/> class.
        /// </summary>
        public NumericValueEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog. Returns false if the user cancelled the edit;
        /// otherwise returns the edited value converted to
        /// <paramref name="targetType"/> in <paramref name="result"/>.
        /// </summary>
        public bool TryShowDialog(Variant value, BuiltInType targetType, out Variant result)
        {
            result = Variant.Null;

            if (targetType == BuiltInType.Null || targetType == BuiltInType.Variant)
            {
                targetType = !value.IsNull ? value.TypeInfo.BuiltInType : BuiltInType.Double;
            }

            SetLimits(targetType);

            ValueCTRL.Value = value.TryGetDecimal(out decimal current) ? current : 0M;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            // convert through the invariant string so large integers keep full precision.
            result = Variant.From(ValueCTRL.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConvertTo(targetType);
            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets the limits according to the data type.
        /// </summary>
        private void SetLimits(BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.SByte:
                {
                    ValueCTRL.Minimum = SByte.MinValue;
                    ValueCTRL.Maximum = SByte.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.Byte:
                {
                    ValueCTRL.Minimum = Byte.MinValue;
                    ValueCTRL.Maximum = Byte.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.Int16:
                {
                    ValueCTRL.Minimum = Int16.MinValue;
                    ValueCTRL.Maximum = Int16.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.UInt16:
                {
                    ValueCTRL.Minimum = UInt16.MinValue;
                    ValueCTRL.Maximum = UInt16.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.Int32:
                case BuiltInType.Enumeration:
                {
                    ValueCTRL.Minimum = Int32.MinValue;
                    ValueCTRL.Maximum = Int32.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.UInt32:
                {
                    ValueCTRL.Minimum = UInt32.MinValue;
                    ValueCTRL.Maximum = UInt32.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.Int64:
                case BuiltInType.Integer:
                {
                    ValueCTRL.Minimum = Int64.MinValue;
                    ValueCTRL.Maximum = Int64.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.UInt64:
                case BuiltInType.UInteger:
                {
                    ValueCTRL.Minimum = UInt64.MinValue;
                    ValueCTRL.Maximum = UInt64.MaxValue;
                    ValueCTRL.DecimalPlaces = 0;
                    break;
                }

                case BuiltInType.Float:
                {
                    ValueCTRL.Minimum = decimal.MinValue;
                    ValueCTRL.Maximum = decimal.MaxValue;
                    ValueCTRL.DecimalPlaces = 6;
                    break;
                }

                default:
                {
                    ValueCTRL.Minimum = decimal.MinValue;
                    ValueCTRL.Maximum = decimal.MaxValue;
                    ValueCTRL.DecimalPlaces = 15;
                    break;
                }
            }
        }
        #endregion
    }
}
