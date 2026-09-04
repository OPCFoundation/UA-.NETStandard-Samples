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
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Quickstarts.HistoricalEvents.Client.Model;
using Opc.Ua.Samples.Client;
using Opc.Ua.Samples.WinForms;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Prompts the user to select an event type to use as an event filter.
    /// </summary>
    /// <remarks>
    /// The dialog only renders: the model browses the subtypes of a type when a node of
    /// the tree is expanded, and describes the fields of a type when one is selected.
    /// </remarks>
    public partial class SelectTypeDlg : SampleForm
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SelectTypeDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private HistoricalEventsClientModel m_model;
        private NodeId m_rootId;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the event types below a root in a tree view.
        /// </summary>
        /// <param name="model">The model which browses the types.</param>
        /// <param name="rootId">The root of the tree. BaseEventType when null.</param>
        /// <param name="caption">The caption of the dialog.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The selected type with its fields, or null when the dialog was cancelled.</returns>
        public async Task<TypeDeclaration> ShowDialogAsync(HistoricalEventsClientModel model, NodeId rootId, string caption, CancellationToken ct = default)
        {
            m_model = model ?? throw new ArgumentNullException(nameof(model));

            // set the caption.
            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            // set default root.
            if ((rootId).IsNull)
            {
                rootId = Opc.Ua.ObjectTypeIds.BaseEventType;
            }

            m_rootId = rootId;

            // display root.
            var root = new TreeNode(await m_model.GetDisplayTextAsync(rootId, ct));
            root.Nodes.Add(new TreeNode());
            BrowseTV.Nodes.Add(root);
            root.Expand();
            BrowseTV.SelectedNode = root;

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            // ensure selection is valid.
            if (BrowseTV.SelectedNode == null)
            {
                return null;
            }

            var declaration = new TypeDeclaration {
                NodeId = SelectedTypeId(BrowseTV.SelectedNode),
                Declarations = new List<InstanceDeclaration>(),
            };

            // update selected fields.
            for (int ii = 0; ii < DeclarationsLV.Items.Count; ii++)
            {
                if (DeclarationsLV.Items[ii].Tag is InstanceDeclaration instance)
                {
                    declaration.Declarations.Add(instance);
                }
            }

            // return the result.
            return declaration;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// The type a node of the tree stands for: the root for the root node, the browsed
        /// reference for every other one.
        /// </summary>
        private NodeId SelectedTypeId(TreeNode node)
        {
            if (node.Tag is ReferenceDescription reference)
            {
                return (NodeId)reference.NodeId;
            }

            return m_rootId;
        }
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
        private async void BrowseTV_AfterSelectAsync(object sender, TreeViewEventArgs e)
        {
            try
            {
                DeclarationsLV.Items.Clear();

                if (e.Node == null)
                {
                    OkBTN.Enabled = false;
                    return;
                }

                OkBTN.Enabled = true;

                // get the instance declarations of the selected type.
                TypeDeclaration type = await m_model.DescribeEventTypeAsync(SelectedTypeId(e.Node));

                // populate the list box.
                foreach (InstanceDeclaration instance in type.Declarations)
                {
                    var item = new ListViewItem(instance.DisplayPath);
                    item.SubItems.Add(instance.DataTypeDisplayText);
                    item.SubItems.Add(instance.Description);
                    item.Tag = instance;

                    DeclarationsLV.Items.Add(item);
                }

                // resize columns to fit text.
                for (int ii = 0; ii < DeclarationsLV.Columns.Count; ii++)
                {
                    DeclarationsLV.Columns[ii].Width = -2;
                }
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
                e.Node.Nodes.Clear();

                // add the subtypes to the control.
                IReadOnlyList<ReferenceDescription> references = await m_model.BrowseSubtypesAsync(SelectedTypeId(e.Node));

                foreach (ReferenceDescription reference in references)
                {
                    // the placeholder child is what gives the node its expand button.
                    var child = new TreeNode(reference.ToString()) { Tag = reference };
                    child.Nodes.Add(new TreeNode());

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
