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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A dialog used to selected one or more nodes.
    /// </summary>
    public partial class SelectNodesDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectNodesDlg"/> class.
        /// </summary>
        public SelectNodesDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task<IList<ILocalNode>> ShowDialogAsync(ISession session, NodeId rootId, IList<NodeId> nodeIds, CancellationToken ct = default)
        {
            await BrowseCTRL.InitializeAsync(session, rootId, null, null, BrowseDirection.Forward, ct);
            await ReferencesCTRL.InitializeAsync(session, rootId, ct);
            await AttributesCTRL.InitializeAsync(session, rootId, ct);
            await NodesCTRL.InitializeAsync(session, nodeIds, ct);

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return NodesCTRL.GetNodeList();
        }
        #endregion

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseCTRL_NodesSelectedAsync(object sender, BrowseTreeCtrl.NodesSelectedEventArgs e)
        {
            try
            {
                foreach (ReferenceDescription reference in e.Nodes)
                {
                    if (!reference.NodeId.IsAbsolute)
                    {
                        await NodesCTRL.AddAsync((NodeId)reference.NodeId);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }

        }
    }
}
