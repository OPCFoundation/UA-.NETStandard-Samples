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
using System.Linq;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Sample.Controls
{
    public partial class WriteValueEditDlg : Form
    {
        public WriteValueEditDlg()
        {
            InitializeComponent();

            AttributeIdCB.Items.AddRange(Attributes.BrowseNames.ToArray());
        }

        /// <summary>
        /// Prompts the user to specify the browse options.
        /// </summary>
        public async Task<bool> ShowDialogAsync(Session session, WriteValue value, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (value == null) throw new ArgumentNullException(nameof(value));

            NodeIdCTRL.Telemetry = telemetry;
            NodeIdCTRL.Browser = new Browser(session);

            INode node = await session.NodeCache.FindAsync(value.NodeId, ct);

            if (node != null)
            {
                DisplayNameTB.Text = node.ToString();
            }

            NodeIdCTRL.Identifier = value.NodeId;
            AttributeIdCB.SelectedItem = Attributes.GetBrowseName(value.AttributeId);
            IndexRangeTB.Text = value.IndexRange;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            value.NodeId = NodeIdCTRL.Identifier;
            value.AttributeId = Attributes.GetIdentifier((string)AttributeIdCB.SelectedItem);
            value.IndexRange = IndexRangeTB.Text;

            return true;
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                NodeId nodeId = NodeIdCTRL.Identifier;
            }
            catch (Exception)
            {
                MessageBox.Show("Please enter a valid node id.", this.Text);
            }

            try
            {
                if (!String.IsNullOrEmpty(IndexRangeTB.Text))
                {
                    NumericRange indexRange = NumericRange.Parse(IndexRangeTB.Text);
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Please enter a valid index range.", this.Text);
            }

            DialogResult = DialogResult.OK;
        }

        private void NodeIdCTRL_IdentifierChanged(object sender, EventArgs e)
        {
            if (NodeIdCTRL.Reference != null)
            {
                DisplayNameTB.Text = NodeIdCTRL.Reference.ToString();

                if (AttributeIdCB.SelectedItem == null)
                {
                    AttributeIdCB.SelectedItem = Attributes.GetBrowseName(Attributes.Value);
                }
            }
        }
    }
}
