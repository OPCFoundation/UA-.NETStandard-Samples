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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Quickstarts.AlarmConditionClient.Model;
using Opc.Ua.Samples.WinForms;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    /// <remarks>
    /// The dialog browses through the model rather than through a session of its own:
    /// the model knows which references make up the area tree, and the dialog only turns
    /// what it answers into tree nodes.
    /// </remarks>
    public partial class SetAreaFilterDlg : SampleForm
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SetAreaFilterDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private AlarmConditionClientModel m_model;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        /// <param name="model">The model which browses the areas.</param>
        /// <returns>The selected area, or a null node id when the user cancelled.</returns>
        public NodeId ShowDialog(AlarmConditionClientModel model)
        {
            m_model = model ?? throw new ArgumentNullException(nameof(model));

            TreeNode root = new TreeNode(BrowseNames.Server);
            root.Nodes.Add(new TreeNode());
            BrowseTV.Nodes.Add(root);

            // The root is expanded once the dialog is on the screen. Expanding it here, on a
            // tree view whose window handle does not exist yet, marks the node as expanded
            // without ever raising BeforeExpand - so the browse never runs, and the node
            // cannot be expanded afterwards either, because it already counts as expanded.
            Shown += (sender, e) => root.Expand();

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return NodeId.Null;
            }

            // ensure selection is valid.
            if (BrowseTV.SelectedNode == null)
            {
                return NodeId.Null;
            }

            // get the selection.
            ReferenceDescription reference = (ReferenceDescription)BrowseTV.SelectedNode.Tag;

            if (reference == null)
            {
                return ObjectIds.Server;
            }

            // return the result.
            return (NodeId)reference.NodeId;
        }
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the DoubleClick event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void BrowseTV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (BrowseTV.SelectedNode == null)
                {
                    return;
                }

                if (OkBTN.Enabled)
                {
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the AfterSelect event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.TreeViewEventArgs"/> instance containing the event data.</param>
        private void BrowseTV_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;

                if (reference == null)
                {
                    OkBTN.Enabled = true;
                    return;
                }

                if (reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier)
                {
                    OkBTN.Enabled = true;
                    return;
                }

                OkBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the BeforeExpand event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.TreeViewCancelEventArgs"/> instance containing the event data.</param>
        private async void BrowseTV_BeforeExpandAsync(object sender, TreeViewCancelEventArgs e)
        {
            try
            {
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;
                e.Node.Nodes.Clear();

                // the root of the tree is the Server object, which has no reference of its
                // own; the model browses the sources below an area so that they show up
                // too, although only an area can be picked.
                NodeId parent = reference != null ? (NodeId)reference.NodeId : NodeId.Null;

                IReadOnlyList<ReferenceDescription> references = await m_model.BrowseAreasAsync(parent);

                // add the children to the control.
                for (int ii = 0; ii < references.Count; ii++)
                {
                    reference = references[ii];

                    TreeNode child = new TreeNode(reference.ToString());
                    child.Nodes.Add(new TreeNode());
                    child.Tag = reference;

                    e.Node.Nodes.Add(child);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model?.Telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
