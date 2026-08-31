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
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Gds.Client.Controls
{
    /// <summary>
    /// Displays the details of a certificate.
    /// </summary>
    public partial class EditValueDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditValueDlg()
        {
            InitializeComponent();

            SetTypeCB.Visible = false;
            SetArraySizeBTN.Visible = false;
        }
        #endregion

        #region Private Fields
        private ILogger m_logger = LoggerUtils.Null.Logger;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the details of the certificate.
        /// </summary>
        public void ShowDialog(
            ILogger logger,
            X509Certificate2 certificate,
            string caption)
        {
            m_logger = logger;

            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = true;

            ValueCTRL.ShowCertificate(certificate);

            base.ShowDialog();
        }
        #endregion

        #region Event Handlers
        private void ValueCTRL_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                BackBTN.Visible = ValueCTRL.CanGoBack;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_logger, Text, ex);
            }
        }

        private void BackBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.Back();
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_logger, Text, ex);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_logger, Text, ex);
            }
        }

        private void SetTypeBTN_Click(object sender, EventArgs e)
        {
            // the certificate view has no array size to change.
        }

        private void SetTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            // the certificate view has no type to change.
        }
        #endregion
    }
}
