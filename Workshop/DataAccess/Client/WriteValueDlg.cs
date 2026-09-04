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
using Opc.Ua.Samples.WinForms;

namespace Quickstarts.DataAccessClient
{
    /// <summary>
    /// Prompts the user to specify a new value and then writes it to the server.
    /// </summary>
    /// <remarks>
    /// The dialog knows nothing about the session: it is given the current value, so it
    /// can convert the text to the same type, and a delegate which does the write.
    /// </remarks>
    public partial class WriteValueDlg : SampleForm
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public WriteValueDlg(ITelemetryContext telemetry)
        {
            InitializeComponent();

            m_telemetry = telemetry;
        }
        #endregion

        #region Private Fields
        private DataValue m_value;
        private Func<Variant, CancellationToken, Task<StatusCode>> m_write;
        private readonly ITelemetryContext m_telemetry;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to enter a value to write.
        /// </summary>
        /// <param name="current">The current value, whose type the new one has to have.</param>
        /// <param name="write">Writes the new value and returns the status the server answered.</param>
        /// <param name="m_telemetry">The m_telemetry context of the client, for error reporting.</param>
        /// <returns>True if successful. False if the operation was cancelled.</returns>
        public bool ShowDialog(
            DataValue current,
            Func<Variant, CancellationToken, Task<StatusCode>> write,
            ITelemetryContext m_telemetry)
        {
            m_value = current;
            m_write = write ?? throw new ArgumentNullException(nameof(write));
            ValueTB.Text = Utils.Format("{0}", m_value.WrappedValue);

            // display the dialog.
            return ShowDialog() == DialogResult.OK;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Changes the value in the text box to the data type required for the write operation.
        /// </summary>
        /// <returns>A value with the correct type.</returns>
        private Variant ChangeType()
        {
            switch (m_value.WrappedValue.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                {
                    return Variant.From(Convert.ToBoolean(ValueTB.Text));
                }

                case BuiltInType.SByte:
                {
                    return Variant.From(Convert.ToSByte(ValueTB.Text));
                }

                case BuiltInType.Byte:
                {
                    return Variant.From(Convert.ToByte(ValueTB.Text));
                }

                case BuiltInType.Int16:
                {
                    return Variant.From(Convert.ToInt16(ValueTB.Text));
                }

                case BuiltInType.UInt16:
                {
                    return Variant.From(Convert.ToUInt16(ValueTB.Text));
                }

                case BuiltInType.Int32:
                {
                    return Variant.From(Convert.ToInt32(ValueTB.Text));
                }

                case BuiltInType.UInt32:
                {
                    return Variant.From(Convert.ToUInt32(ValueTB.Text));
                }

                case BuiltInType.Int64:
                {
                    return Variant.From(Convert.ToInt64(ValueTB.Text));
                }

                case BuiltInType.UInt64:
                {
                    return Variant.From(Convert.ToUInt64(ValueTB.Text));
                }

                case BuiltInType.Float:
                {
                    return Variant.From(Convert.ToSingle(ValueTB.Text));
                }

                case BuiltInType.Double:
                {
                    return Variant.From(Convert.ToDouble(ValueTB.Text));
                }

                default:
                {
                    return Variant.From(ValueTB.Text);
                }
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Parses the value and writes it to server. Closes the dialog if successful.
        /// </summary>
        private async void OkBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                StatusCode result = await m_write(ChangeType(), CancellationToken.None);

                if (StatusCode.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, "Error Writing Value", exception);
            }
        }
        #endregion
    }
}
