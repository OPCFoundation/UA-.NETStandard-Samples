/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Presses the controls of a sample client which is never shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A form the harness runs but never displays does not behave like one a user is looking
    /// at, and every helper here exists because of one of the ways it differs. None of them
    /// assert: they report what happened and leave the wording of the failure to the fixture,
    /// the same way <see cref="WinFormsHarness"/> does.
    /// </para>
    /// <para>
    /// One thing no helper can paper over: <c>Control.Visible</c> is answered from the whole
    /// parent chain, so on a form which is never shown every control reports itself invisible
    /// however the sample set the property. A test which wants to know whether a sample showed
    /// or hid something has to read the state the sample keeps instead. <c>Enabled</c> is not
    /// affected and can be read as it is.
    /// </para>
    /// </remarks>
    public static class SampleFormDriver
    {
        /// <summary>
        /// Creates the window handles of a form and everything in it, without showing it.
        /// </summary>
        /// <remarks>
        /// The samples marshal their callbacks back with <c>BeginInvoke</c>, which needs a
        /// handle, and a tree or a list which never got one silently drops what is done to it.
        /// </remarks>
        public static void CreateHandles(Control root)
        {
            ArgumentNullException.ThrowIfNull(root);

            _ = root.Handle;

            foreach (Control control in Descendants(root))
            {
                _ = control.Handle;
            }
        }

        /// <summary>
        /// Every control under the given one.
        /// </summary>
        public static IEnumerable<Control> Descendants(Control parent)
        {
            ArgumentNullException.ThrowIfNull(parent);

            foreach (Control child in parent.Controls)
            {
                yield return child;

                foreach (Control grandChild in Descendants(child))
                {
                    yield return grandChild;
                }
            }
        }

        /// <summary>
        /// Presses a button of a sample by running its handler.
        /// </summary>
        /// <remarks>
        /// Not through <see cref="Button.PerformClick"/>: that one is gated on
        /// <c>CanSelect</c>, which is false while no parent of the control is visible, so on a
        /// form the harness never shows it does nothing at all - no handler runs, and nothing
        /// fails either. The handler is invoked directly instead.
        /// </remarks>
        /// <param name="owner">The form or control which declares the handler.</param>
        /// <param name="handlerName">The name of the handler method.</param>
        /// <param name="sender">What to pass as the sender, usually the button.</param>
        /// <returns>False when the sample has no such handler any more.</returns>
        public static bool TryInvokeHandler(object owner, string handlerName, object sender)
        {
            ArgumentNullException.ThrowIfNull(owner);

            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo handler = type.GetMethod(
                    handlerName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                if (handler != null)
                {
                    handler.Invoke(owner, [sender, EventArgs.Empty]);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Selects a row of a list the way a click would.
        /// </summary>
        /// <remarks>
        /// Through <c>SelectedIndices</c> rather than <c>ListViewItem.Selected</c>: on a list
        /// which has never been displayed the item level property does not reliably reach the
        /// control, which leaves the sample's handler looking at an empty selection and
        /// returning without doing anything.
        /// </remarks>
        /// <returns>False when the selection did not take.</returns>
        public static bool TrySelectRow(ListView list, int index)
        {
            ArgumentNullException.ThrowIfNull(list);

            if (index < 0 || index >= list.Items.Count)
            {
                return false;
            }

            list.Focus();
            list.SelectedIndices.Clear();
            list.SelectedIndices.Add(index);

            return list.SelectedItems.Count == 1;
        }

        /// <summary>
        /// Reads a number a sample displayed.
        /// </summary>
        /// <remarks>
        /// Never compare a displayed number as text: how a <c>Double</c> renders depends on
        /// the culture of the machine the test runs on, and these tests also run on a German
        /// one. The current culture is tried first because that is what the sample formatted
        /// with, and the invariant one after it for a value which was not formatted at all.
        /// </remarks>
        public static bool TryReadNumber(string shown, out double value)
        {
            value = 0;

            return !string.IsNullOrWhiteSpace(shown)
                && (double.TryParse(shown, NumberStyles.Any, CultureInfo.CurrentCulture, out value)
                    || double.TryParse(shown, NumberStyles.Any, CultureInfo.InvariantCulture, out value));
        }

        /// <summary>
        /// Pumps the message loop until the condition holds.
        /// </summary>
        /// <remarks>
        /// The samples do their work in <c>async void</c> handlers, so there is nothing to
        /// await: the only way to let one finish is to keep the loop turning and watch what it
        /// puts on the screen.
        /// </remarks>
        /// <returns>False when the condition never held.</returns>
        public static async Task<bool> PumpUntilAsync(
            Func<bool> condition,
            TimeSpan timeout,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(condition);

            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                Application.DoEvents();

                await Task.Delay(100, ct).ConfigureAwait(true);
            }

            return true;
        }

        /// <summary>
        /// The text of a label of the status bar of a sample.
        /// </summary>
        public static string StatusText(Form form, string statusStripName, string labelName)
        {
            var status = WinFormsHarness.FindControl(form, statusStripName) as StatusStrip;

            ToolStripItem label = status?.Items
                .Cast<ToolStripItem>()
                .FirstOrDefault(item => item.Name == labelName);

            return label?.Text ?? string.Empty;
        }

        #region Browse Tree
        /// <summary>
        /// The shared browse control a sample hosts, found by its designer field on the form.
        /// </summary>
        public static BrowseNodeCtrl BrowseControlOf(Form form, string browseControlName = "BrowseCTRL")
        {
            return WinFormsHarness.FindControl(form, browseControlName) as BrowseNodeCtrl;
        }

        /// <summary>
        /// The tree of the shared browse control a sample hosts.
        /// </summary>
        public static TreeView BrowseTreeOf(Form form, string browseControlName = "BrowseCTRL")
        {
            return BrowseControlOf(form, browseControlName)?.BrowseCTRL?.BrowseTV;
        }

        /// <summary>
        /// The attribute list of the shared browse control, which is where the control puts
        /// what it read for the node the user selected in the tree.
        /// </summary>
        public static ListView AttributeListOf(Form form, string browseControlName = "BrowseCTRL")
        {
            AttributesListViewCtrl attributes = BrowseControlOf(form, browseControlName)?.AttributesCTRL;

            return attributes == null ? null : WinFormsHarness.FindField<ListView>(attributes, "AttributesLV");
        }

        /// <summary>
        /// What the attribute list shows for one attribute, or null when it has no row for it.
        /// </summary>
        public static string AttributeText(ListView attributes, string attributeName)
        {
            ListViewItem row = attributes?.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => string.Equals(item.Text, attributeName, StringComparison.Ordinal));

            return row != null && row.SubItems.Count > 2 ? row.SubItems[2].Text : null;
        }

        /// <summary>
        /// Everything the attribute list shows, for a failure message.
        /// </summary>
        public static string AttributesSeen(ListView attributes)
        {
            if (attributes == null)
            {
                return "<no attribute list>";
            }

            return string.Join(
                ", ",
                attributes.Items
                    .Cast<ListViewItem>()
                    .Select(item => $"{item.Text}={(item.SubItems.Count > 2 ? item.SubItems[2].Text : string.Empty)}"));
        }

        /// <summary>
        /// The browse name of the node a tree node stands for, or null for the placeholder the
        /// control puts under a node it has not browsed yet.
        /// </summary>
        public static string BrowseNameOf(TreeNode node)
        {
            return (node?.Tag as ReferenceDescription)?.BrowseName.Name;
        }

        /// <summary>
        /// The browse names of the children of a tree node, for a failure message.
        /// </summary>
        public static string ChildrenOf(TreeNode node)
        {
            if (node == null)
            {
                return "<no node>";
            }

            return string.Join(
                ", ",
                node.Nodes.Cast<TreeNode>().Select(child => BrowseNameOf(child) ?? $"'{child.Text}'"));
        }

        /// <summary>
        /// Expands a node of the browse tree and waits for its real children.
        /// </summary>
        /// <remarks>
        /// The control answers <c>BeforeExpand</c> in an <c>async void</c> handler which
        /// browses the server and only then replaces the placeholder child with the real ones,
        /// so expanding is not done when <c>Expand</c> returns. A node which turns out to have
        /// no children at all ends up with none, which is why the wait is for the placeholder
        /// to be gone rather than for a child to appear.
        /// </remarks>
        public static async Task<bool> ExpandAsync(TreeNode node, TimeSpan timeout, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(node);

            node.Expand();

            return await PumpUntilAsync(() => !HasPlaceholder(node), timeout, ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Walks the browse tree down a path of browse names, expanding as it goes.
        /// </summary>
        /// <returns>
        /// The node the path ends at, or null when a step of it is not in the tree - the
        /// caller reports where it stopped with <see cref="ChildrenOf"/>.
        /// </returns>
        public static async Task<TreeNode> NavigateAsync(
            TreeNode start,
            IReadOnlyList<string> browseNames,
            TimeSpan timeout,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(browseNames);

            TreeNode current = start;

            foreach (string name in browseNames)
            {
                if (current == null || !await ExpandAsync(current, timeout, ct).ConfigureAwait(true))
                {
                    return null;
                }

                current = current.Nodes
                    .Cast<TreeNode>()
                    .FirstOrDefault(child => string.Equals(BrowseNameOf(child), name, StringComparison.Ordinal));
            }

            return current;
        }

        /// <summary>
        /// Selects a node of the browse tree, which is what makes the control read its
        /// attributes.
        /// </summary>
        public static void Select(TreeView tree, TreeNode node)
        {
            ArgumentNullException.ThrowIfNull(tree);

            tree.SelectedNode = node;
        }

        /// <summary>
        /// True while the control still shows the empty child it puts under a node it has not
        /// browsed yet.
        /// </summary>
        private static bool HasPlaceholder(TreeNode node)
        {
            return node.Nodes.Count == 1
                && node.Nodes[0].Tag == null
                && string.IsNullOrEmpty(node.Nodes[0].Text);
        }
        #endregion
    }
}
