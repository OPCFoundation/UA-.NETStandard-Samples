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
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Prompts the user to specify a new value and then writes it to the server.
    /// </summary>
    public partial class SetValueDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SetValueDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        #endregion

        #region Public Interface
        public Variant? ShowDialog(Variant value, BuiltInType builtInType)
        {
            if (value != Variant.Null)
            {
                ValueTB.Text = value.ToString();
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            if (String.IsNullOrEmpty(ValueTB.Text))
            {
                return Variant.Null;
            }

            return ConvertToVariant(ValueTB.Text, builtInType);
        }
        #endregion

        #region Private Methods
        private static Variant ConvertToVariant(string text, BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                {
                    return Variant.From(Convert.ToBoolean(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.SByte:
                {
                    return Variant.From(Convert.ToSByte(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.Byte:
                {
                    return Variant.From(Convert.ToByte(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.Int16:
                {
                    return Variant.From(Convert.ToInt16(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.UInt16:
                {
                    return Variant.From(Convert.ToUInt16(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.Int32:
                {
                    return Variant.From(Convert.ToInt32(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.UInt32:
                {
                    return Variant.From(Convert.ToUInt32(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.Int64:
                {
                    return Variant.From(Convert.ToInt64(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.UInt64:
                {
                    return Variant.From(Convert.ToUInt64(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.Float:
                {
                    return Variant.From(Convert.ToSingle(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.Double:
                {
                    return Variant.From(Convert.ToDouble(text, CultureInfo.InvariantCulture));
                }

                case BuiltInType.DateTime:
                {
                    return Variant.From(new DateTimeUtc(Convert.ToDateTime(text, CultureInfo.InvariantCulture)));
                }

                case BuiltInType.Guid:
                {
                    return Variant.From(new Uuid(Guid.Parse(text)));
                }

                case BuiltInType.ByteString:
                {
                    return Variant.From(ByteString.From(Convert.FromBase64String(text)));
                }

                case BuiltInType.NodeId:
                {
                    return Variant.From(NodeId.Parse(text));
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return Variant.From(ExpandedNodeId.Parse(text));
                }

                case BuiltInType.StatusCode:
                {
                    return Variant.From(new StatusCode(UInt32.Parse(text, CultureInfo.InvariantCulture)));
                }

                case BuiltInType.QualifiedName:
                {
                    return Variant.From(QualifiedName.Parse(text));
                }

                case BuiltInType.LocalizedText:
                {
                    return Variant.From(new LocalizedText(text));
                }

                default:
                {
                    return Variant.From(text);
                }
            }
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }
        #endregion
    }
}
