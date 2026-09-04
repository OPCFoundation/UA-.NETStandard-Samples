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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.WinForms;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Sample.Controls
{
    public partial class NodeAttributesDlg : SampleForm
    {
        #region Constructors
        public NodeAttributesDlg(ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private ExpandedNodeId m_nodeId;
        private readonly ITelemetryContext m_telemetry;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task ShowDialogAsync(ISession session, ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (nodeId.IsNull) throw new ArgumentNullException(nameof(nodeId));

            m_session = session;
            m_nodeId = nodeId;
            await AttributesCTRL.InitializeAsync(session, nodeId, m_telemetry, ct);

            if (ShowDialog() != DialogResult.OK)
            {
                return;
            }
        }
        #endregion

        private async void OkBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await AttributesCTRL.InitializeAsync(m_session, m_nodeId, m_telemetry);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
