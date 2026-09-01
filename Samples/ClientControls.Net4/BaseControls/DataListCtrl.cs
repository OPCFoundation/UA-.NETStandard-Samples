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
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a hierarchical view of a complex value. The tree is built from
    /// Variants: array elements are lifted with <see cref="VariantElements"/>
    /// and structure fields with <see cref="VariantFieldCollection"/>, so no
    /// boxed CLR values or reflection are involved. Edits propagate back up
    /// the tree and the edited root is available from <see cref="GetValue"/>.
    /// </summary>
    public partial class DataListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataListCtrl"/> class.
        /// </summary>
        public DataListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }

        #region Private Fields
        /// <summary>
		/// The columns to display in the control.
		/// </summary>
        private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "Name",  HorizontalAlignment.Left, null },
            new object[] { "Value", HorizontalAlignment.Left, null, 250 },
            new object[] { "Type",  HorizontalAlignment.Left, null }
        };

        private ValueState m_rootState;
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private Font m_defaultFont;
        #pragma warning restore CA2213
        private const string ExpandIcon = "ExpandPlus";
        private const string CollapseIcon = "ExpandMinus";
        #endregion

        #region Public Interface
        /// <summary>
        /// Whether to update the control when the value changes.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool AutoUpdate
        {
            get { return UpdatesMI.Checked; }
            set { UpdatesMI.Checked = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            m_rootState = null;
            AdjustColumns();
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        public Task ShowValueAsync(Variant value, CancellationToken ct = default)
        {
            return ShowValueAsync(value, false, ct);
        }

        /// <summary>
        /// Displays a value in the control. When <paramref name="overwrite"/>
        /// is set the previously expanded rows are expanded again so a live
        /// view keeps its shape across updates.
        /// </summary>
        public Task ShowValueAsync(Variant value, bool overwrite, CancellationToken ct = default)
        {
            HashSet<string> expandedPaths = overwrite ? CollectExpandedPaths() : null;

            ShowRoot(new ValueState { Value = value, SlotType = value.TypeInfo });

            if (expandedPaths != null)
            {
                RestoreExpandedPaths(expandedPaths);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Displays a data value with its status code and timestamps in the
        /// control.
        /// </summary>
        public Task ShowValueAsync(DataValue value, CancellationToken ct = default)
        {
            return ShowValueAsync(value, false, ct);
        }

        /// <summary>
        /// Displays a data value with its status code and timestamps in the
        /// control, expanding the previously expanded rows again.
        /// </summary>
        public Task ShowValueAsync(DataValue value, bool overwrite, CancellationToken ct = default)
        {
            HashSet<string> expandedPaths = overwrite ? CollectExpandedPaths() : null;

            ShowRoot(new ValueState { Value = Variant.From(value), SlotType = TypeInfo.Scalars.DataValue });

            if (expandedPaths != null)
            {
                RestoreExpandedPaths(expandedPaths);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Displays the array value of a write request in the control. The
        /// elements outside the request's index range are greyed out and
        /// cannot be edited.
        /// </summary>
        public Task ShowValueAsync(WriteValue value, CancellationToken ct = default)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            Variant arrayValue = value.Value.WrappedValue;
            var root = new ValueState { Value = arrayValue, SlotType = arrayValue.TypeInfo };

            NumericRange indexRange;
            ServiceResult result = NumericRange.Validate(value.IndexRange, out indexRange);

            if (ServiceResult.IsGood(result) && !indexRange.IsNull)
            {
                root.IndexRange = indexRange;
            }

            ShowRoot(root);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns the value including any edits made in the control.
        /// </summary>
        #pragma warning disable CA1024 // Justification: sample public API shape is preserved by design.
        public Variant GetValue()
        #pragma warning restore CA1024
        {
            return m_rootState != null ? m_rootState.Value : Variant.Null;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Enables the menu items.
        /// </summary>
        protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            RefreshMI.Enabled = true;
            ClearMI.Enabled = true;

            if (ItemsLV.SelectedItems.Count == 1)
            {
                ValueState state = ItemsLV.SelectedItems[0].Tag as ValueState;
                EditMI.Enabled = state != null && IsEditable(state);
            }
        }
        #endregion

        #region ValueState Class
        /// <summary>
        /// Stores the state associated with an item. The parent chain leads
        /// back to the root value so edits can be propagated upwards.
        /// </summary>
        private sealed class ValueState
        {
            public ValueState Parent;
            public ListViewItem Item;
            public bool Expanded;
            public bool Expandable;

            /// <summary>The current value of this node.</summary>
            public Variant Value;

            /// <summary>The declared type of the slot holding the value.</summary>
            public TypeInfo SlotType;

            /// <summary>The flat element index when the parent is an array.</summary>
            public int ElementIndex = -1;

            /// <summary>The field index when the parent is a structure or DataValue.</summary>
            public int FieldIndex = -1;

            /// <summary>Whether the field belongs to a DataValue parent.</summary>
            public bool IsDataValueField;

            /// <summary>The captured fields when this node holds a structure.</summary>
            public VariantFieldCollection Fields;

            /// <summary>The structure instance the fields were captured from.</summary>
            public IEncodeable Structure;

            /// <summary>The index range that restricts editing of root array elements.</summary>
            public NumericRange? IndexRange;

            /// <summary>Whether the row can be edited.</summary>
            public bool Enabled = true;
        }
        #endregion

        #region Private Members
        /// <summary>
        /// Displays the children of the root value as the top level rows.
        /// </summary>
        private void ShowRoot(ValueState root)
        {
            Clear();

            m_rootState = root;

            if (m_defaultFont != null && m_defaultFont != ItemsLV.Font)
            {
                m_defaultFont.Dispose();
            }

            if (root.Value.TypeInfo.BuiltInType == BuiltInType.ByteString && root.Value.TypeInfo.ValueRank < 0)
            {
                #pragma warning disable CA2000 // Justification: the font is kept in a field and disposed when it is replaced.
                m_defaultFont = new Font("Courier New", 8.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
                #pragma warning restore CA2000
            }
            else
            {
                m_defaultFont = ItemsLV.Font;
            }

            // switch to detail view.
            if (ItemsLV.View == View.List)
            {
                ItemsLV.Items.Clear();
                ItemsLV.View = View.Details;
            }

            List<ValueState> children = CreateChildren(root);

            // show a simple root value as a single row.
            if (children.Count == 0)
            {
                var child = new ValueState
                {
                    Parent = root,
                    Value = root.Value,
                    SlotType = root.SlotType,
                    ElementIndex = -1,
                    FieldIndex = 0,
                    IsDataValueField = false
                };

                InsertRow(child, ItemsLV.Items.Count, 0, "Value");
            }
            else
            {
                foreach (ValueState child in children)
                {
                    InsertRow(child, ItemsLV.Items.Count, 0, GetChildName(root, child));
                }
            }

            AdjustColumns();
        }

        /// <summary>
        /// Creates the child states of a node, or an empty list if the node is
        /// a simple value.
        /// </summary>
        private List<ValueState> CreateChildren(ValueState state)
        {
            var children = new List<ValueState>();
            Variant value = state.Value;

            // the components of a data value.
            if (value.TypeInfo.BuiltInType == BuiltInType.DataValue && value.TypeInfo.ValueRank < 0)
            {
                DataValue dataValue = value.GetDataValue();

                children.Add(new ValueState { Parent = state, FieldIndex = 0, IsDataValueField = true, SlotType = TypeInfo.Scalars.Variant, Value = dataValue.WrappedValue });
                children.Add(new ValueState { Parent = state, FieldIndex = 1, IsDataValueField = true, SlotType = TypeInfo.Scalars.StatusCode, Value = Variant.From(dataValue.StatusCode), Enabled = false });

                if (dataValue.SourceTimestamp != DateTimeUtc.MinValue)
                {
                    children.Add(new ValueState { Parent = state, FieldIndex = 2, IsDataValueField = true, SlotType = TypeInfo.Scalars.DateTime, Value = Variant.From(dataValue.SourceTimestamp), Enabled = false });
                }

                if (dataValue.ServerTimestamp != DateTimeUtc.MinValue)
                {
                    children.Add(new ValueState { Parent = state, FieldIndex = 4, IsDataValueField = true, SlotType = TypeInfo.Scalars.DateTime, Value = Variant.From(dataValue.ServerTimestamp), Enabled = false });
                }

                return children;
            }

            // the elements of an array or matrix.
            if (value.TypeInfo.ValueRank >= 0)
            {
                if (!VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions))
                {
                    return children;
                }

                if (!PromptOnLongList(elements.Count))
                {
                    return children;
                }

                TypeInfo elementType = (value.TypeInfo.BuiltInType == BuiltInType.Variant)
                    ? TypeInfo.Scalars.Variant
                    : new TypeInfo(value.TypeInfo.BuiltInType, ValueRanks.Scalar);

                for (int ii = 0; ii < elements.Count; ii++)
                {
                    bool enabled = true;

                    if (state.IndexRange is NumericRange indexRange)
                    {
                        enabled = (indexRange.Begin <= ii && indexRange.End >= ii) ||
                                  (indexRange.End < 0 && indexRange.Begin == ii);
                    }

                    children.Add(new ValueState
                    {
                        Parent = state,
                        ElementIndex = ii,
                        SlotType = elementType,
                        Value = elements[ii],
                        Enabled = enabled
                    });
                }

                return children;
            }

            // the fields of a structure.
            if (value.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                ExtensionObject extension = value.GetExtensionObject();

                if (extension.TryGetValue(out IEncodeable encodeable, ServiceMessageContext.CreateEmpty(null)) &&
                    VariantFieldCollection.TryCapture(encodeable, null, out VariantFieldCollection fields))
                {
                    state.Structure = encodeable;
                    state.Fields = fields;

                    for (int ii = 0; ii < fields.Count; ii++)
                    {
                        children.Add(new ValueState
                        {
                            Parent = state,
                            FieldIndex = ii,
                            SlotType = fields.GetSlotType(ii),
                            Value = fields.GetValue(ii),
                            Enabled = fields.IsEditable(ii)
                        });
                    }
                }

                return children;
            }

            return children;
        }

        /// <summary>
        /// Returns the name for a child row.
        /// </summary>
        private static string GetChildName(ValueState parent, ValueState child)
        {
            if (child.IsDataValueField)
            {
                switch (child.FieldIndex)
                {
                    case 0: return "Value";
                    case 1: return "StatusCode";
                    case 2: return "SourceTimestamp";
                    case 4: return "ServerTimestamp";
                }
            }

            if (child.ElementIndex >= 0)
            {
                return Utils.Format("[{0}]", child.ElementIndex);
            }

            if (parent.Fields != null && child.FieldIndex >= 0)
            {
                return parent.Fields.GetName(child.FieldIndex);
            }

            return "Value";
        }

        /// <summary>
        /// Inserts a row for the state at the index.
        /// </summary>
        private void InsertRow(ValueState state, int index, int depth, string name)
        {
            var listitem = new ListViewItem(name);

            listitem.SubItems.Add(GetValueText(state.Value));
            listitem.SubItems.Add(GetDataTypeText(state));

            listitem.Font = m_defaultFont;
            listitem.IndentCount = depth;
            listitem.Tag = state;

            state.Item = listitem;
            state.Expandable = IsExpandable(state);
            listitem.ImageKey = state.Expandable ? ExpandIcon : CollapseIcon;

            if (!state.Enabled)
            {
                listitem.ForeColor = Color.LightGray;
            }

            ItemsLV.Items.Insert(index, listitem);
        }

        /// <summary>
        /// Returns true if the node has children to display.
        /// </summary>
        private static bool IsExpandable(ValueState state)
        {
            Variant value = state.Value;

            if (value.IsNull)
            {
                return false;
            }

            if (value.TypeInfo.ValueRank >= 0)
            {
                return VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out _) && elements.Count > 0;
            }

            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.DataValue:
                {
                    return true;
                }

                case BuiltInType.ExtensionObject:
                {
                    ExtensionObject extension = value.GetExtensionObject();

                    return extension.TryGetValue(out IEncodeable encodeable, ServiceMessageContext.CreateEmpty(null)) &&
                        VariantFieldCollection.TryCapture(encodeable, null, out VariantFieldCollection fields) &&
                        fields.Count > 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true is the value is an editable type.
        /// </summary>
        private static bool IsEditable(ValueState state)
        {
            if (!state.Enabled)
            {
                return false;
            }

            if (state.Parent == null)
            {
                return false;
            }

            TypeInfo typeInfo = !state.Value.IsNull ? state.Value.TypeInfo : state.SlotType;

            if (typeInfo.ValueRank >= 0)
            {
                return false;
            }

            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.String:
                case BuiltInType.DateTime:
                case BuiltInType.Guid:
                case BuiltInType.LocalizedText:
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Shows the components of a value in the control.
        /// </summary>
        private void ShowChildren(ListViewItem listItem)
        {
            ValueState state = listItem.Tag as ValueState;

            if (state == null || !state.Expandable || state.Expanded)
            {
                return;
            }

            List<ValueState> children = CreateChildren(state);

            if (children.Count == 0)
            {
                return;
            }

            state.Expanded = true;
            listItem.ImageKey = CollapseIcon;

            int index = listItem.Index + 1;
            int depth = listItem.IndentCount + 1;

            foreach (ValueState child in children)
            {
                InsertRow(child, index++, depth, GetChildName(state, child));
            }

            AdjustColumns();
        }

        /// <summary>
        /// Hides the components of a value in the control.
        /// </summary>
        private void HideChildren(ListViewItem listItem)
        {
            ValueState state = listItem.Tag as ValueState;

            if (state == null || !state.Expandable || !state.Expanded)
            {
                return;
            }

            for (int ii = listItem.Index + 1; ii < ItemsLV.Items.Count;)
            {
                ListViewItem childItem = ItemsLV.Items[ii];

                if (childItem.IndentCount <= listItem.IndentCount)
                {
                    break;
                }

                childItem.Remove();
            }

            state.Expanded = false;
            listItem.ImageKey = ExpandIcon;
        }

        /// <summary>
        /// Returns the paths of the expanded rows. A path is the chain of row
        /// names from the top level down to the row.
        /// </summary>
        private HashSet<string> CollectExpandedPaths()
        {
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            var pathByDepth = new List<string>();

            foreach (ListViewItem item in ItemsLV.Items)
            {
                int depth = item.IndentCount;

                while (pathByDepth.Count > depth)
                {
                    pathByDepth.RemoveAt(pathByDepth.Count - 1);
                }

                string path = (depth > 0 ? pathByDepth[depth - 1] + "/" : String.Empty) + item.SubItems[0].Text;
                pathByDepth.Add(path);

                if (item.Tag is ValueState state && state.Expanded)
                {
                    expanded.Add(path);
                }
            }

            return expanded;
        }

        /// <summary>
        /// Expands the rows whose paths were expanded before a refresh.
        /// </summary>
        private void RestoreExpandedPaths(HashSet<string> expandedPaths)
        {
            var pathByDepth = new List<string>();

            // expanding inserts rows behind the current one, so a simple
            // forward walk visits them as well.
            for (int ii = 0; ii < ItemsLV.Items.Count; ii++)
            {
                ListViewItem item = ItemsLV.Items[ii];
                int depth = item.IndentCount;

                while (pathByDepth.Count > depth)
                {
                    pathByDepth.RemoveAt(pathByDepth.Count - 1);
                }

                string path = (depth > 0 ? pathByDepth[depth - 1] + "/" : String.Empty) + item.SubItems[0].Text;
                pathByDepth.Add(path);

                if (expandedPaths.Contains(path) && item.Tag is ValueState state && state.Expandable && !state.Expanded)
                {
                    ShowChildren(item);
                }
            }
        }

        /// <summary>
        /// Asks for confirmation before expanding a long list.
        /// </summary>
        private bool PromptOnLongList(int length)
        {
            if (length < 256)
            {
                return true;
            }

            DialogResult result = MessageBox.Show("It may take a long time to display the list are you sure you want to continue?", "Warning", MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Formats a value for display in the value column.
        /// </summary>
        private string GetValueText(Variant value)
        {
            if (value.IsNull)
            {
                return "(null)";
            }

            // format arrays as type and dimensions.
            if (value.TypeInfo.ValueRank >= 0)
            {
                if (VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions))
                {
                    if (dimensions != null && dimensions.Length > 1)
                    {
                        return Utils.Format("{0}[{1}]", value.TypeInfo.BuiltInType, string.Join(",", dimensions));
                    }

                    return Utils.Format("{0}[{1}]", value.TypeInfo.BuiltInType, elements.Count);
                }

                return value.ToString();
            }

            switch (value.TypeInfo.BuiltInType)
            {
                // format bytes.
                case BuiltInType.ByteString:
                {
                    ByteString bytes = value.GetByteString();
                    StringBuilder buffer = new StringBuilder();
                    int count = 0;

                    foreach (byte b in bytes.Span)
                    {
                        if (count != 0 && count % 16 == 0)
                        {
                            buffer.Append(' ');
                        }

                        buffer.AppendFormat("{0:X2} ", b);
                        count++;
                    }

                    return buffer.ToString();
                }

                // format xml elements.
                case BuiltInType.XmlElement:
                {
                    XmlElement xml = value.GetXmlElement();

                    if (xml.IsNull)
                    {
                        return "(null)";
                    }

                    string text = ((System.Xml.XmlElement)xml).OuterXml;

                    // show only the start tag for long elements.
                    int index = text.IndexOf('>', StringComparison.Ordinal);

                    if (index != -1 && index < text.Length - 1)
                    {
                        text = text.Substring(0, index + 1);
                    }

                    return text;
                }

                // format the body type of an extension object.
                case BuiltInType.ExtensionObject:
                {
                    ExtensionObject extension = value.GetExtensionObject();

                    if (extension.TryGetValue(out IEncodeable encodeable, ServiceMessageContext.CreateEmpty(null)))
                    {
                        return VariantFieldCollection.GetTypeDisplayName(encodeable);
                    }

                    return value.ToString();
                }

                // format the quality and value of a data value.
                case BuiltInType.DataValue:
                {
                    DataValue dataValue = value.GetDataValue();
                    StringBuilder formattedValue = new StringBuilder();

                    if (!StatusCode.IsGood(dataValue.StatusCode))
                    {
                        formattedValue.Append('[');
                        formattedValue.AppendFormat("Q:{0}", dataValue.StatusCode);
                        formattedValue.Append("] ");
                    }

                    formattedValue.Append(dataValue.WrappedValue.ToString());
                    return formattedValue.ToString();
                }

                // show the symbolic name for status codes.
                case BuiltInType.StatusCode:
                {
                    return value.GetStatusCode().ToString();
                }
            }

            // use default formatting.
            return value.ToString();
        }

        /// <summary>
        /// Returns the display name for the data type of the value.
        /// </summary>
        private static string GetDataTypeText(ValueState state)
        {
            Variant value = state.Value;

            if (value.IsNull)
            {
                return state.SlotType.ToString();
            }

            if (value.TypeInfo.BuiltInType == BuiltInType.ExtensionObject && value.TypeInfo.ValueRank < 0)
            {
                ExtensionObject extension = value.GetExtensionObject();

                if (extension.TryGetValue(out IEncodeable encodeable, ServiceMessageContext.CreateEmpty(null)))
                {
                    return VariantFieldCollection.GetTypeDisplayName(encodeable);
                }
            }

            return value.TypeInfo.ToString();
        }

        /// <summary>
        /// Propagates an edited value from a child state into its ancestors.
        /// </summary>
        private void UpdateParent(ValueState state)
        {
            ValueState parent = state.Parent;

            if (parent == null)
            {
                return;
            }

            // replace the element in the parent array or matrix.
            if (state.ElementIndex >= 0)
            {
                if (VariantElements.TryGetElements(parent.Value, out ArrayOf<Variant> elements, out int[] dimensions))
                {
                    var newElements = new List<Variant>(elements.ToList());
                    newElements[state.ElementIndex] = state.Value;

                    parent.Value = VariantElements.CreateFromElements(parent.Value.TypeInfo.BuiltInType, newElements, dimensions);
                }
            }

            // replace the component in the parent data value.
            else if (state.IsDataValueField)
            {
                DataValue dataValue = parent.Value.GetDataValue();

                parent.Value = Variant.From(new DataValue(
                    state.FieldIndex == 0 ? state.Value : dataValue.WrappedValue,
                    state.FieldIndex == 1 ? state.Value.GetStatusCode() : dataValue.StatusCode,
                    state.FieldIndex == 2 ? state.Value.GetDateTime() : dataValue.SourceTimestamp,
                    state.FieldIndex == 4 ? state.Value.GetDateTime() : dataValue.ServerTimestamp,
                    dataValue.SourcePicoseconds,
                    dataValue.ServerPicoseconds));
            }

            // replace the field in the parent structure.
            else if (state.FieldIndex >= 0 && parent.Fields != null && parent.Structure != null)
            {
                parent.Fields.SetValue(state.FieldIndex, state.Value);
                parent.Structure = parent.Fields.ApplyTo(parent.Structure);
                parent.Value = Variant.FromStructure(parent.Structure);
            }

            // a simple root shows the root value itself in its single row.
            else
            {
                parent.Value = state.Value;
            }

            // refresh the summary text of the ancestor row.
            if (parent.Item != null)
            {
                parent.Item.SubItems[1].Text = GetValueText(parent.Value);
            }

            UpdateParent(parent);
        }

        private void ItemsLV_MouseClickAsync(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                ListViewItem listItem = ItemsLV.GetItemAt(e.X, e.Y);

                if (listItem == null)
                {
                    return;
                }

                ValueState state = listItem.Tag as ValueState;

                if (state == null || !state.Expandable)
                {
                    return;
                }

                if (state.Expanded)
                {
                    HideChildren(listItem);
                }
                else
                {
                    ShowChildren(listItem);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        #region Event Handlers
        private void UpdatesMI_CheckedChanged(object sender, EventArgs e)
        {
        }

        private void RefreshMI_Click(object sender, EventArgs e)
        {
        }

        private void ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                Clear();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (ItemsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                ListViewItem listItem = ItemsLV.SelectedItems[0];
                ValueState state = listItem.Tag as ValueState;

                if (state == null || !IsEditable(state))
                {
                    return;
                }

                Variant editedValue;

                if (state.Value.TypeInfo.BuiltInType == BuiltInType.LocalizedText)
                {
                    // edit the text and keep the locale.
                    LocalizedText localizedText = state.Value.GetLocalizedText();

                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    string text = new StringValueEditDlg().ShowDialog(localizedText.Text);
                    #pragma warning restore CA2000

                    if (text == null)
                    {
                        return;
                    }

                    editedValue = Variant.From(new LocalizedText(localizedText.Locale, text));
                }
                else
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    if (!new SimpleValueEditDlg().TryShowDialog(state.Value, Telemetry, out editedValue))
                    #pragma warning restore CA2000
                    {
                        return;
                    }
                }

                // an edited node cannot have stale children on display.
                if (state.Expanded)
                {
                    HideChildren(listItem);
                }

                state.Value = editedValue;
                listItem.SubItems[1].Text = GetValueText(state.Value);

                UpdateParent(state);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void PopupMenu_Opening(object sender, CancelEventArgs e)
        {
            try
            {
                EditMI.Enabled = false;

                if (ItemsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                ValueState state = ItemsLV.SelectedItems[0].Tag as ValueState;
                EditMI.Enabled = state != null && IsEditable(state);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
