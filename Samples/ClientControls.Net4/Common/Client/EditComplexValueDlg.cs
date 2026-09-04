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
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using System.Threading.Tasks;
using System.Threading;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value. The value is passed and returned as a
    /// Variant. Because Variant.Null is a legitimate value, the dialogs report
    /// a cancelled edit separately: the async overload returns null and the
    /// synchronous overloads return false.
    /// </summary>
    public partial class EditComplexValueDlg : SampleForm
    {
        private readonly ITelemetryContext m_telemetry;
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditComplexValueDlg(ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            for (BuiltInType ii = BuiltInType.Boolean; ii <= BuiltInType.StatusCode; ii++)
            {
                SetTypeCB.Items.Add(ii);
            }

            SetTypeCB.SelectedItem = BuiltInType.String;

            m_telemetry = telemetry;
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to view or edit the value. Returns null if the
        /// user cancelled the edit.
        /// </summary>
        public async Task<Variant?> ShowDialogAsync(
            ISession session,
            NodeId nodeId,
            uint attributeId,
            string name,
            Variant value,
            bool readOnly,
            string caption,
            CancellationToken ct = default)
        {
            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = !readOnly;

            ValueCTRL.ChangeSession(session);

            await ValueCTRL.ShowValueAsync(nodeId, attributeId, name, value, readOnly, ct);

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return ValueCTRL.GetValue();
        }

        /// <summary>
        /// Prompts the user to edit the value. Returns false if the user
        /// cancelled the edit.
        /// </summary>
        public bool TryShowDialog(
            ISession session,
            string name,
            NodeId dataType,
            int valueRank,
            Variant value,
            string caption,
            out Variant result)
        {
            result = Variant.Null;

            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = true;

            ValueCTRL.ChangeSession(session);
            ValueCTRL.ShowValue(name, dataType, valueRank, value);

            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            result = ValueCTRL.GetValue();
            return true;
        }

        /// <summary>
        /// Prompts the user to edit the value. Returns false if the user
        /// cancelled the edit.
        /// </summary>
        public bool TryShowDialog(
            TypeInfo expectedType,
            string name,
            Variant value,
            string caption,
            out Variant result)
        {
            result = Variant.Null;

            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = true;

            ValueCTRL.ChangeSession(null);
            ValueCTRL.ShowValue(expectedType, name, value);

            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            result = ValueCTRL.GetValue();
            return true;
        }

        /// <summary>
        /// Changes the session used.
        /// </summary>
        public void ChangeSession(ISession session)
        {
            ValueCTRL.ChangeSession(session);
        }

        /// <summary>
        /// Updates the value shown in the control.
        /// </summary>
        public Task UpdateValueAsync(
            NodeId nodeId,
            uint attributeId,
            string name,
            Variant value,
            CancellationToken ct = default)
        {
            return ValueCTRL.ShowValueAsync(nodeId, attributeId, name, value, true, ct);
        }
        #endregion

        #region Event Handlers
        private void ValueCTRL_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                BackBTN.Visible = ValueCTRL.CanGoBack;
                SetTypeCB.Visible = ValueCTRL.CanChangeType;
                SetTypeCB.SelectedItem = ValueCTRL.CurrentType;
                SetArraySizeBTN.Visible = ValueCTRL.CanSetArraySize;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void BackBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.Back();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.EndEdit();
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void SetTypeBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.SetArraySize();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        private void SetTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.SetType((BuiltInType)SetTypeCB.SelectedItem);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
