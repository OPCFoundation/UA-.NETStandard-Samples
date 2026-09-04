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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AggregationClient.Model;
using Opc.Ua;
using Opc.Ua.Client.Controls;

namespace AggregationClient
{
    /// <summary>
    /// Shows the references of a node and lets the user pick one.
    /// </summary>
    /// <remarks>
    /// The browse itself - in both directions and paged through continuation points - is
    /// done by the model; the dialog only renders the rows it returns.
    /// </remarks>
    public partial class ShowReferencesDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ShowReferencesDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private AggregationClientModel m_model;
        private ReferenceDescription m_reference;
        #endregion

        #region Public Interface
        /// <summary>
        /// Shows the references of a node and returns the one the user picked, or null.
        /// </summary>
        public async Task<ReferenceDescription> ShowDialogAsync(AggregationClientModel model, NodeId nodeId, CancellationToken ct = default)
        {
            m_model = model;

            #region Task #B1 - Browse References
            await UpdateListAsync(nodeId, ct);
            #endregion

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return m_reference;
        }
        #endregion

        #region Task #B1 - Browse References
        /// <summary>
        /// Updates the list of references.
        /// </summary>
        private async Task UpdateListAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ReferencesLV.Items.Clear();

            IReadOnlyList<ReferenceRow> references = await m_model.BrowseReferencesAsync(nodeId, ct);

            foreach (ReferenceRow reference in references)
            {
                ListViewItem item = new ListViewItem(reference.ReferenceTypeName);

                item.SubItems.Add(reference.TargetName);
                item.SubItems.Add(reference.NodeClass.ToString());
                item.SubItems.Add(reference.TypeDefinitionName);

                item.Tag = reference.Reference;

                ReferencesLV.Items.Add(item);
            }

            // auto size the columns.
            for (int ii = 0; ii < ReferencesLV.Columns.Count; ii++)
            {
                ReferencesLV.Columns[ii].Width = -2;
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the click event for the OK button.
        /// </summary>
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                #region Task #B1 - Browse References
                if (ReferencesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                m_reference = ReferencesLV.SelectedItems[0].Tag as ReferenceDescription;
                #endregion

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model?.Telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
