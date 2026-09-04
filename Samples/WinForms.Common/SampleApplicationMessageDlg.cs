/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.WinForms
{
    /// <summary>
    /// The message box the stack asks a question through - whether to create a
    /// certificate, for example - for a sample which has a message loop.
    /// </summary>
    /// <remarks>
    /// Registered by <c>services.AddSampleWindows()</c> and handed to
    /// <see cref="ApplicationInstance.MessageDlg"/> by the entry point helper of the
    /// samples, so no sample has to install it itself.
    /// </remarks>
    public class SampleApplicationMessageDlg : IApplicationMessageDlg
    {
        private string m_message = string.Empty;
        private MessageBoxButtons m_buttons = MessageBoxButtons.OK;

        /// <inheritdoc/>
        public override void Message(string text, bool ask)
        {
            m_message = text;
            m_buttons = ask ? MessageBoxButtons.YesNo : MessageBoxButtons.OK;
        }

        /// <inheritdoc/>
        public override Task<bool> ShowAsync()
        {
            DialogResult result = MessageBox.Show(m_message, "OPC UA", m_buttons);

            return Task.FromResult(result is DialogResult.OK or DialogResult.Yes);
        }
    }
}
