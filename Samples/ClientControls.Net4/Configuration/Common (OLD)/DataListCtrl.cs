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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Serialization;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a hierarchical view of a complex value.
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

        private bool m_latestValue = true;
        private bool m_expanding;
        private int m_depth;
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private Font m_defaultFont;
        #pragma warning restore CA2213
        private MonitoredItem m_monitoredItem;
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
        /// Whether to only display the latest value for a monitored item.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool LatestValue
        {
            get { return m_latestValue; }
            set { m_latestValue = value; }
        }

        /// <summary>
        /// The monitored item associated with the value.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MonitoredItem MonitoredItem
        {
            get { return m_monitoredItem; }
            set { m_monitoredItem = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        public Task ShowValueAsync(object value, CancellationToken ct = default)
        {
            return ShowValueAsync(value, false, ct);
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        public async Task ShowValueAsync(object value, bool overwrite, CancellationToken ct = default)
        {
            if (!overwrite)
            {
                Clear();
            }

            if (value is byte[])
            {
                m_defaultFont = new Font("Courier New", 8.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
            }
            else
            {
                m_defaultFont = ItemsLV.Font;
            }

            m_expanding = false;
            m_depth = 0;

            // show the value.
            int index = 0;
            await ShowValueAsync(index, overwrite, value, ct);

            // adjust columns.
            AdjustColumns();
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
                EditMI.Enabled = IsEditableType(state.Component);
            }
        }
        #endregion

        #region ValueState Class
        /// <summary>
        /// Stores the state associated with an item.
        /// </summary>
        private sealed class ValueState
        {
            public bool Expanded = false;
            public bool Expandable = false;
            public object Value = null;
            public object Component = null;
            public object ComponentId = null;
            public object ComponentIndex = null;
        }
        #endregion

        #region Private Members
        /// <summary>
        /// Returns true is the value is an editable type.
        /// </summary>
        private bool IsEditableType(object value)
        {
            if (value is bool) return true;
            if (value is sbyte) return true;
            if (value is byte) return true;
            if (value is short) return true;
            if (value is ushort) return true;
            if (value is int) return true;
            if (value is uint) return true;
            if (value is long) return true;
            if (value is ulong) return true;
            if (value is float) return true;
            if (value is double) return true;
            if (value is string) return true;
            if (value is DateTime) return true;
            if (value is Guid) return true;
            if (value is LocalizedText) return true;

            return false;
        }

        /// <summary>
        /// Shows the components of a value in the control.
        /// </summary>
        private async Task ShowChildrenAsync(ListViewItem listItem, CancellationToken ct = default)
        {
            ValueState state = listItem.Tag as ValueState;

            if (state == null || !state.Expandable || state.Expanded)
            {
                return;
            }

            m_expanding = true;
            m_depth = listItem.IndentCount + 1;

            state.Expanded = true;
            listItem.ImageKey = CollapseIcon;

            int index = listItem.Index + 1;
            bool overwrite = false;

            await ShowValueAsync(index, overwrite, state.Component, ct);

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
        /// Returns the list item at the specified index.
        /// </summary>
        private ListViewItem GetListItem(int index, ref bool overwrite, string name, string type)
        {
            ListViewItem listitem = null;

            // switch to detail view as soon as an item is added.
            if (ItemsLV.View == View.List)
            {
                ItemsLV.Items.Clear();
                ItemsLV.View = View.Details;
            }

            // check if there is an item that could be re-used.
            if (!m_expanding && index < ItemsLV.Items.Count)
            {
                listitem = ItemsLV.Items[index];

                // check if still possible to overwrite values.
                if (overwrite)
                {
                    if (listitem.SubItems[0].Text != name || listitem.SubItems[2].Text != type)
                    {
                        overwrite = false;
                    }
                }

                listitem.SubItems[0].Text = name;
                listitem.SubItems[2].Text = type;

                return listitem;
            }

            overwrite = false;

            listitem = new ListViewItem(name);

            listitem.SubItems.Add(String.Empty);
            listitem.SubItems.Add(type);

            listitem.Font = m_defaultFont;
            listitem.ImageKey = ExpandIcon;
            listitem.IndentCount = m_depth;
            listitem.Tag = new ValueState();

            if (!m_expanding)
            {
                ItemsLV.Items.Add(listitem);
            }
            else
            {
                ItemsLV.Items.Insert(index, listitem);
            }

            return listitem;
        }

        /// <summary>
        /// Returns true if the type can be expanded.
        /// </summary>
        private static object GetExtensionObjectBody(ExtensionObject extension)
        {
            if (extension.TryGetAsBinary(out ByteString bytes, ServiceMessageContext.CreateEmpty(null)))
            {
                return bytes.ToArray();
            }

            if (extension.TryGetAsXml(out XmlElement xml, ServiceMessageContext.CreateEmpty(null)))
            {
                return xml;
            }

            if (extension.TryGetValue(out IEncodeable encodeable, ServiceMessageContext.CreateEmpty(null)))
            {
                return encodeable;
            }

            return null;
        }

        private static object GetIdentifier(NodeId value)
        {
            switch (value.IdType)
            {
                case IdType.Numeric:
                    return value.TryGetValue(out uint numeric) ? (object)numeric : value.IdentifierAsString;
                case IdType.String:
                    return value.TryGetValue(out string text) ? text : value.IdentifierAsString;
                case IdType.Guid:
                    return value.TryGetValue(out Guid uuid) ? (object)uuid : value.IdentifierAsString;
                case IdType.Opaque:
                    return value.TryGetValue(out ByteString bytes) ? (object)bytes.ToArray() : value.IdentifierAsString;
                default:
                    return value.IdentifierAsString;
            }
        }

        private static object GetIdentifier(ExpandedNodeId value)
        {
            switch (value.IdType)
            {
                case IdType.Numeric:
                    return value.TryGetValue(out uint numeric) ? (object)numeric : value.IdentifierAsString;
                case IdType.String:
                    return value.TryGetValue(out string text) ? text : value.IdentifierAsString;
                case IdType.Guid:
                    return value.TryGetValue(out Guid uuid) ? (object)uuid : value.IdentifierAsString;
                case IdType.Opaque:
                    return value.TryGetValue(out ByteString bytes) ? (object)bytes.ToArray() : value.IdentifierAsString;
                default:
                    return value.IdentifierAsString;
            }
        }

        private static string GetDataMemberName(PropertyInfo property)
        {
            foreach (Attribute attribute in property.GetCustomAttributes(true))
            {
                if (attribute is DataMemberAttribute dataMember && !String.IsNullOrEmpty(dataMember.Name))
                {
                    return dataMember.Name;
                }
            }

            return property.Name;
        }

        private bool IsExpandableType(object value)
        {
            // check for null.
            if (value == null)
            {
                return false;
            }

            // check for Variant.
            if (value is Variant)
            {
                return IsExpandableType(((Variant)value).AsBoxedObject());
            }

            // check for bytes.
            byte[] bytes = value as byte[];

            if (bytes != null)
            {
                return false;
            }

            // check for xml element.
            System.Xml.XmlElement xml = value as System.Xml.XmlElement;

            if (xml != null)
            {
                if (xml.ChildNodes.Count == 1 && xml.ChildNodes[0] is System.Xml.XmlText)
                {
                    return false;
                }

                return xml.HasChildNodes;
            }

            // check for array.
            Array array = value as Array;

            if (array == null)
            {
                Matrix matrix = value as Matrix;

                if (matrix != null)
                {
                    array = matrix.ToArray();
                }
            }

            if (array != null)
            {
                return array.Length > 0;
            }

            // check for list.
            IList list = value as IList;

            if (list != null)
            {
                return list.Count > 0;
            }

            // check for encodeable object.
            IEncodeable encodeable = value as IEncodeable;

            if (encodeable != null)
            {
                return true;
            }

            // check for extension object.
            if (value is ExtensionObject extension)
            {
                return IsExpandableType(GetExtensionObjectBody(extension));
            }

            // check for data value.
            if (value is DataValue datavalue)
            {
                return true;
            }

            // check for event value.
            EventFieldList eventFields = value as EventFieldList;

            if (eventFields != null)
            {
                return true;
            }

            // must be a simple value.
            return false;
        }

        /// <summary>
        /// Formats a value for display in the control.
        /// </summary>
        private async Task<string> GetValueTextAsync(object value, CancellationToken ct = default)
        {
            // check for null.
            if (value == null)
            {
                return "(null)";
            }

            // format bytes.
            byte[] bytes = value as byte[];

            if (bytes != null)
            {
                StringBuilder buffer = new StringBuilder();

                for (int ii = 0; ii < bytes.Length; ii++)
                {
                    if (ii != 0 && ii % 16 == 0)
                    {
                        buffer.Append(' ');
                    }

                    buffer.AppendFormat("{0:X2} ", bytes[ii]);
                }

                return buffer.ToString();
            }

            // format xml element.
            System.Xml.XmlElement xml = value as System.Xml.XmlElement;

            if (xml != null)
            {
                // return the entire element if not expandable.
                if (!IsExpandableType(xml))
                {
                    return xml.OuterXml;
                }

                // show only the start tag.
                string text = xml.OuterXml;

                int index = text.IndexOf('>', StringComparison.Ordinal);

                if (index != -1)
                {
                    text = text.Substring(0, index);
                }

                return text;
            }

            // format array.
            Array array = value as Array;

            if (array != null)
            {
                if (array.Rank > 1)
                {
                    int[] lenghts = new int[array.Rank];

                    for (int i = 0; i < array.Rank; ++i)
                    {
                        lenghts[i] = array.GetLength(i);
                    }

                    return Utils.Format("{0}[{1}]", value.GetType().GetElementType().Name, string.Join(",", lenghts));
                }
                else
                {
                    return Utils.Format("{0}[{1}]", value.GetType().GetElementType().Name, array.Length);
                }
            }

            // format list.
            IList list = value as IList;

            if (list != null)
            {
                string type = value.GetType().Name;

                if (type.EndsWith("Collection", StringComparison.Ordinal))
                {
                    type = type.Substring(0, type.Length - "Collection".Length);
                }
                else
                {
                    type = "Object";
                }

                return Utils.Format("{0}[{1}]", type, list.Count);
            }

            // format encodeable object.
            IEncodeable encodeable = value as IEncodeable;

            if (encodeable != null)
            {
                return encodeable.GetType().Name;
            }

            // format extension object.
            if (value is ExtensionObject extension)
            {
                return await GetValueTextAsync(GetExtensionObjectBody(extension), ct);
            }

            // check for event value.
            EventFieldList eventFields = value as EventFieldList;

            if (eventFields != null)
            {
                if (m_monitoredItem != null)
                {
                    return String.Format("{0}", await m_monitoredItem.GetEventTypeAsync(eventFields, ct));
                }

                return eventFields.GetType().Name;
            }

            // check for data value.
            if (value is DataValue dataValue)
            {
                StringBuilder formattedValue = new StringBuilder();

                if (!StatusCode.IsGood(dataValue.StatusCode))
                {
                    formattedValue.Append('[');
                    formattedValue.AppendFormat("Q:{0}", dataValue.StatusCode);
                }

                DateTime now = DateTime.UtcNow;

                #pragma warning disable CA1508 // Justification: sample control flow is intentional and analyzer reports a false positive.
                if ((dataValue.ServerTimestamp > now) || (dataValue.SourceTimestamp > now))
                #pragma warning restore CA1508
                {
                    if (formattedValue.ToString().Length > 0)
                    {
                        formattedValue.Append(", ");
                    }
                    else
                    {
                        formattedValue.Append('[');
                    }

                    formattedValue.Append("T:future");
                }

                if (formattedValue.ToString().Length > 0)
                {
                    formattedValue.Append("] ");
                }

                formattedValue.AppendFormat("{0}", dataValue.WrappedValue.AsBoxedObject());
                return formattedValue.ToString();
            }

            // use default formatting.
            return Utils.Format("{0}", value);
        }

        /// <summary>
        /// Updates the list with the specified value.
        /// </summary>
        private async Task<(int, bool)> UpdateListAsync(
            int index,
            bool overwrite,
            object value,
            object componentValue,
            object componentId,
            string name,
            string type,
            CancellationToken ct = default)
        {
            // get the list item to update.
            ListViewItem listitem = GetListItem(index, ref overwrite, name, type);
            if (componentValue is StatusCode)
            {
                listitem.SubItems[1].Text = componentValue.ToString();
            }
            else
            {
                // update list item.
                listitem.SubItems[1].Text = await GetValueTextAsync(componentValue, ct);
            }

            // move to next item.
            index++;

            ValueState state = listitem.Tag as ValueState;

            // recursively update sub-values if item is expanded.
            if (overwrite)
            {
                if (state.Expanded && state.Expandable)
                {
                    m_depth++;
                    (index, overwrite) = await ShowValueAsync(index, overwrite, componentValue, ct);
                    m_depth--;
                }
            }

            // update state.
            state.Expandable = IsExpandableType(componentValue);
            state.Value = value;
            state.Component = componentValue;
            state.ComponentId = componentId;
            state.ComponentIndex = index;

            if (!state.Expandable)
            {
                listitem.ImageKey = CollapseIcon;
            }
            return (index, overwrite);
        }

        /// <summary>
        /// Updates the list with the specified value.
        /// </summary>
        private async Task<(int, bool)> UpdateListAsync(
            int index,
            bool overwrite,
            object value,
            object componentValue,
            object componentId,
            string name,
            string type,
            bool enabled,
            CancellationToken ct = default)
        {
            // get the list item to update.
            ListViewItem listitem = GetListItem(index, ref overwrite, name, type);

            if (!enabled)
            {
                listitem.ForeColor = Color.LightGray;
            }

            // update list item.
            listitem.SubItems[1].Text = await GetValueTextAsync(componentValue, ct);

            // move to next item.
            index++;

            ValueState state = listitem.Tag as ValueState;

            // recursively update sub-values if item is expanded.
            if (overwrite)
            {
                if (state.Expanded && state.Expandable)
                {
                    m_depth++;
                    (index, overwrite) = await ShowValueAsync(index, overwrite, componentValue, ct);
                    m_depth--;
                }
            }

            // update state.
            state.Expandable = IsExpandableType(componentValue);
            state.Value = value;
            state.Component = componentValue;
            state.ComponentId = componentId;
            state.ComponentIndex = index;

            if (!state.Expandable)
            {
                listitem.ImageKey = CollapseIcon;
            }
            return (index, overwrite);
        }

        /// <summary>
        /// Shows property of an encodeable object in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, IEncodeable value, PropertyInfo property, CancellationToken ct = default)
        {
            // get the name of the property.
            string name = GetDataMemberName(property);

            if (name == null)
            {
                return Task.FromResult((index, overwrite));
            }

            // get the property value.
            object propertyValue = null;

            MethodInfo[] accessors = property.GetAccessors();

            for (int ii = 0; ii < accessors.Length; ii++)
            {
                if (accessors[ii].ReturnType == property.PropertyType)
                {
                    propertyValue = accessors[ii].Invoke(value, null);
                    break;
                }
            }

            if (propertyValue is Variant)
            {
                propertyValue = ((Variant)propertyValue).AsBoxedObject();
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, propertyValue, property, name, property.PropertyType.Name, ct);
        }

        /// <summary>
        /// Shows the element of an array in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, Array value, int element, CancellationToken ct = default)
        {
            // get the name of the element.
            string name = Utils.Format("[{0}]", element);

            // get the element value.
            object elementValue = null;

            if (value.Rank > 1)
            {
                int[] smallArrayDimmensions = new int[value.Rank - 1];
                int length = 1;

                for (int i = 0; i < value.Rank - 1; ++i)
                {
                    smallArrayDimmensions[i] = value.GetLength(i + 1);
                    length *= smallArrayDimmensions[i];
                }

                Array flatArray = Array.CreateInstance(value.GetType().GetElementType(), value.Length);
                Array.Copy(value, flatArray, value.Length);
                Array flatSmallArray = Array.CreateInstance(value.GetType().GetElementType(), length);
                Array.Copy(flatArray, element * value.GetLength(1), flatSmallArray, 0, length);
                Array smallArray = Array.CreateInstance(value.GetType().GetElementType(), smallArrayDimmensions);
                int[] indexes = new int[smallArrayDimmensions.Length];

                for (int ii = 0; ii < flatSmallArray.Length; ii++)
                {
                    smallArray.SetValue(flatSmallArray.GetValue(ii), indexes);

                    for (int jj = indexes.Length - 1; jj >= 0; jj--)
                    {
                        indexes[jj]++;

                        if (indexes[jj] < smallArrayDimmensions[jj])
                        {
                            break;
                        }

                        indexes[jj] = 0;
                    }
                }

                elementValue = smallArray;
            }
            else
            {
                elementValue = value.GetValue(element);
            }

            // get the type name.
            string type = null;

            if (elementValue != null)
            {
                type = elementValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, elementValue, element, name, type, ct);
        }

        /// <summary>
        /// Shows the element of an array in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, Array value, int element, bool enabled, CancellationToken ct = default)
        {
            // get the name of the element.
            string name = Utils.Format("[{0}]", element);

            // get the element value.
            object elementValue = value.GetValue(element);

            // get the type name.
            string type = null;

            if (elementValue != null)
            {
                type = elementValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, elementValue, element, name, type, enabled, ct);
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
        /// Shows the element of a list in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, IList value, int element, CancellationToken ct = default)
        {
            // get the name of the element.
            string name = Utils.Format("[{0}]", element);

            // get the element value.
            object elementValue = value[element];

            // get the type name.
            string type = null;

            if (elementValue != null)
            {
                type = elementValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, elementValue, element, name, type, ct);
        }

        /// <summary>
        /// Shows an XML element in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, System.Xml.XmlElement value, int childIndex, CancellationToken ct = default)
        {
            // ignore children that are not elements.
            System.Xml.XmlElement child = value.ChildNodes[childIndex] as System.Xml.XmlElement;

            if (child == null)
            {
                return Task.FromResult((index, overwrite));
            }

            // get the name of the element.
            string name = Utils.Format("{0}", child.Name);

            // get the type name.
            string type = value.GetType().Name;

            // update the list view.
            return UpdateListAsync(index, overwrite, value, child, childIndex, name, type, ct);
        }

        /// <summary>
        /// Shows an event in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, EventFieldList value, int fieldIndex, CancellationToken ct = default)
        {
            // ignore children that are not elements.
            object field = value.EventFields[fieldIndex].AsBoxedObject();

            if (field == null)
            {
                return Task.FromResult((index, overwrite));
            }

            // get the name of the element.
            string name = null;

            if (m_monitoredItem != null)
            {
                name = m_monitoredItem.GetFieldName(fieldIndex);
            }

            // get the type name.
            string type = value.GetType().Name;

            // update the list view.
            return UpdateListAsync(index, overwrite, value, field, fieldIndex, name, type, ct);
        }

        /// <summary>
        /// Shows a byte array in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, byte[] value, int blockStart, CancellationToken ct = default)
        {
            // get the name of the element.
            string name = Utils.Format("[{0:X4}]", blockStart);

            int bytesLeft = value.Length - blockStart;

            if (bytesLeft > 16)
            {
                bytesLeft = 16;
            }

            // get the element value.
            byte[] blockValue = new byte[bytesLeft];
            Array.Copy(value, blockStart, blockValue, 0, bytesLeft);

            // get the type name.
            string type = value.GetType().Name;

            // update the list view.
            return UpdateListAsync(index, overwrite, value, blockValue, blockStart, name, type, ct);
        }

        /// <summary>
        /// Shows a data value in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, DataValue value, int component, CancellationToken ct = default)
        {
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "Value";
                    componentValue = value.WrappedValue.AsBoxedObject();

                    if (componentValue is ExtensionObject extension)
                    {
                        componentValue = GetExtensionObjectBody(extension);
                    }

                    break;
                }

                case 1:
                {
                    name = "StatusCode";
                    componentValue = value.StatusCode;
                    break;
                }

                case 2:
                {
                    if (value.SourceTimestamp != DateTime.MinValue)
                    {
                        name = "SourceTimestamp";
                        componentValue = value.SourceTimestamp;
                    }

                    break;
                }

                case 3:
                {
                    if (value.ServerTimestamp != DateTime.MinValue)
                    {
                        name = "ServerTimestamp";
                        componentValue = value.ServerTimestamp;
                    }

                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return Task.FromResult((index, overwrite));
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, componentValue, component, name, type, ct);
        }

        /// <summary>
        /// Shows a node id in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, NodeId value, int component, CancellationToken ct = default)
        {
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "IdType";
                    componentValue = value.IdType;
                    break;
                }

                case 1:
                {
                    name = "Identifier";
                    componentValue = GetIdentifier(value);
                    break;
                }

                case 2:
                {
                    name = "NamespaceIndex";
                    componentValue = value.NamespaceIndex;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return Task.FromResult((index, overwrite));
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, componentValue, component, name, type, ct);
        }

        /// <summary>
        /// Shows am expanded node id in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, ExpandedNodeId value, int component, CancellationToken ct = default)
        {
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "IdType";
                    componentValue = value.IdType;
                    break;
                }

                case 1:
                {
                    name = "Identifier";
                    componentValue = GetIdentifier(value);
                    break;
                }

                case 2:
                {
                    name = "NamespaceIndex";
                    componentValue = value.NamespaceIndex;
                    break;
                }

                case 3:
                {
                    name = "NamespaceUri";
                    componentValue = value.NamespaceUri;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return Task.FromResult((index, overwrite));
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, componentValue, component, name, type, ct);
        }

        /// <summary>
        /// Shows qualified name in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, QualifiedName value, int component, CancellationToken ct = default)
        {
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "Name";
                    componentValue = value.Name;
                    break;
                }

                case 1:
                {
                    name = "NamespaceIndex";
                    componentValue = value.NamespaceIndex;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return Task.FromResult((index, overwrite)); ;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, componentValue, component, name, type, ct);
        }

        /// <summary>
        /// Shows localized text in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, LocalizedText value, int component, CancellationToken ct = default)
        {
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "Text";
                    componentValue = value.Text;
                    break;
                }

                case 1:
                {
                    name = "Locale";
                    componentValue = value.Locale;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return Task.FromResult((index, overwrite)); ;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, componentValue, component, name, type, ct);
        }

        /// <summary>
        /// Shows a string in the control.
        /// </summary>
        private Task<(int, bool)> ShowValueAsync(int index, bool overwrite, string value, CancellationToken ct = default)
        {
            string name = "Value";
            object componentValue = value;

            // don't display empty components.
            #pragma warning disable CA1508 // Justification: sample control flow is intentional and analyzer reports a false positive.
            if (name == null)
            #pragma warning restore CA1508
            {
                return Task.FromResult((index, overwrite));
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }

            // update the list view.
            return UpdateListAsync(index, overwrite, value, componentValue, 0, name, type, ct);
        }

        /// <summary>
        /// Shows a value in control.
        /// </summary>
        private async Task<(int, bool)> ShowValueAsync(int index, bool overwrite, object value, CancellationToken ct = default)
        {
            if (value == null)
            {
                return (index, overwrite);
            }

            // show monitored items.
            MonitoredItem monitoredItem = value as MonitoredItem;

            if (monitoredItem != null)
            {
                m_monitoredItem = monitoredItem;
                return await ShowValueAsync(index, overwrite, monitoredItem.LastValue, ct);
            }

            // show data changes
            MonitoredItemNotification datachange = value as MonitoredItemNotification;

            if (datachange != null)
            {
                return await ShowValueAsync(index, overwrite, datachange.Value, ct);
            }

            // show write value with IndexRange
            WriteValue writevalue = value as WriteValue;

            if (writevalue != null)
            {
                // check if the value is an array
                Array arrayvalue = writevalue.Value.WrappedValue.AsBoxedObject() as Array;

                if (arrayvalue != null)
                {
                    NumericRange indexRange;
                    ServiceResult result = NumericRange.Validate(writevalue.IndexRange, out indexRange);

                    if (ServiceResult.IsGood(result) && !indexRange.IsNull)
                    {
                        for (int ii = 0; ii < arrayvalue.Length; ii++)
                        {
                            bool enabled = ((indexRange.Begin <= ii && indexRange.End >= ii) ||
                                            (indexRange.End < 0 && indexRange.Begin == ii));

                            (index, overwrite) = await ShowValueAsync(index, overwrite, arrayvalue, ii, enabled, ct);
                        }

                        return (index, overwrite);
                    }
                }
            }

            // show events
            EventFieldList eventFields = value as EventFieldList;

            if (eventFields != null)
            {
                for (int ii = 0; ii < eventFields.EventFields.Count; ii++)
                {
                    (index, overwrite) = await ShowValueAsync(index, overwrite, eventFields, ii, ct);
                }

                return (index, overwrite);
            }

            // show extension bodies.
            if (value is ExtensionObject extension)
            {
                return await ShowValueAsync(index, overwrite, GetExtensionObjectBody(extension), ct);
            }

            // show encodeables.
            IEncodeable encodeable = value as IEncodeable;

            if (encodeable != null)
            {
                PropertyInfo[] properties = encodeable.GetType().GetProperties();

                foreach (PropertyInfo property in properties)
                {
                    (index, overwrite) = await ShowValueAsync(index, overwrite, encodeable, property, ct);
                }

                return (index, overwrite);
            }

            // show bytes.
            byte[] bytes = value as byte[];

            if (bytes != null)
            {
                if (PromptOnLongList(bytes.Length / 16))
                {
                    for (int ii = 0; ii < bytes.Length; ii += 16)
                    {
                        (index, overwrite) = await ShowValueAsync(index, overwrite, bytes, ii, ct);
                    }
                }
                return (index, overwrite);
            }

            // show arrays
            Array array = value as Array;

            if (array == null)
            {
                Matrix matrix = value as Matrix;

                if (matrix != null)
                {
                    array = matrix.ToArray();
                }
            }

            if (array != null)
            {
                if (PromptOnLongList(array.GetLength(0)))
                {
                    for (int ii = 0; ii < array.GetLength(0); ii++)
                    {
                        (index, overwrite) = await ShowValueAsync(index, overwrite, array, ii, ct);
                    }
                }
                return (index, overwrite);
            }

            // show lists
            IList list = value as IList;

            if (list != null)
            {
                if (PromptOnLongList(list.Count))
                {
                    for (int ii = 0; ii < list.Count; ii++)
                    {
                        (index, overwrite) = await ShowValueAsync(index, overwrite, list, ii, ct);
                    }
                }
                return (index, overwrite);
            }

            // show xml elements
            System.Xml.XmlElement xml = value as System.Xml.XmlElement;

            if (xml != null)
            {
                if (PromptOnLongList(xml.ChildNodes.Count))
                {
                    for (int ii = 0; ii < xml.ChildNodes.Count; ii++)
                    {
                        (index, overwrite) = await ShowValueAsync(index, overwrite, xml, ii, ct);
                    }
                }
                return (index, overwrite);
            }

            // show data value.
            if (value is DataValue datavalue)
            {
                (index, overwrite) = await ShowValueAsync(index, overwrite, datavalue, 0, ct);
                (index, overwrite) = await ShowValueAsync(index, overwrite, datavalue, 1, ct);
                (index, overwrite) = await ShowValueAsync(index, overwrite, datavalue, 2, ct);
                return await ShowValueAsync(index, overwrite, datavalue, 3, ct);
            }

            // show node id value.
            if (value is NodeId nodeId && !nodeId.IsNull)
            {
                (index, overwrite) = await ShowValueAsync(index, overwrite, nodeId, 0, ct);
                (index, overwrite) = await ShowValueAsync(index, overwrite, nodeId, 1, ct);
                return await ShowValueAsync(index, overwrite, nodeId, 2, ct);
            }

            // show expanded node id value.
            if (value is ExpandedNodeId expandedNodeId && !expandedNodeId.IsNull)
            {
                (index, overwrite) = await ShowValueAsync(index, overwrite, expandedNodeId, 0, ct);
                (index, overwrite) = await ShowValueAsync(index, overwrite, expandedNodeId, 1, ct);
                (index, overwrite) = await ShowValueAsync(index, overwrite, expandedNodeId, 2, ct);
                return await ShowValueAsync(index, overwrite, expandedNodeId, 3, ct);
            }

            // show qualified name value.
            if (value is QualifiedName qualifiedName && !qualifiedName.IsNull)
            {
                (index, overwrite) = await ShowValueAsync(index, overwrite, qualifiedName, 0, ct);
                return await ShowValueAsync(index, overwrite, qualifiedName, 1, ct);
            }

            // show qualified name value.
            if (value is LocalizedText localizedText && !localizedText.IsNull)
            {
                (index, overwrite) = await ShowValueAsync(index, overwrite, localizedText, 0, ct);
                return await ShowValueAsync(index, overwrite, localizedText, 1, ct);
            }

            // show variant.
            Variant? variant = value as Variant?;

            if (variant != null)
            {
                return await ShowValueAsync(index, overwrite, variant.Value.AsBoxedObject(), ct);
            }

            // show unknown types as strings.
            return await ShowValueAsync(index, overwrite, String.Format("{0}", value), ct);
        }

        private async void ItemsLV_MouseClickAsync(object sender, MouseEventArgs e)
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
                    await ShowChildrenAsync(listItem);
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
            try
            {
                /*
                if (m_monitoredItem != null)
                {
                    if (UpdatesMI.Checked)
                    {
                        m_monitoredItem.Notification += m_MonitoredItemNotification;
                    }
                    else
                    {
                        m_monitoredItem.Notification -= m_MonitoredItemNotification;
                    }
                }
                */
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void RefreshMI_Click(object sender, EventArgs e)
        {
            try
            {
                /*
                Clear();
                ShowValueAsync(m_monitoredItem);
                */
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
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

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (ItemsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                ValueState state = ItemsLV.SelectedItems[0].Tag as ValueState;

                if (!IsEditableType(state.Component))
                {
                    return;
                }

                object value = null;
                if (state.Component is LocalizedText)
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    value = new StringValueEditDlg().ShowDialog(state.Component.ToString());
                    #pragma warning restore CA2000
                    if (value != null)
                    {
                        value = new LocalizedText(((LocalizedText)state.Component).Locale, ((LocalizedText)state.Component).Locale, value.ToString());
                    }
                }
                else
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    value = new SimpleValueEditDlg().ShowDialog(state.Component, state.Component.GetType(), Telemetry);
                    #pragma warning restore CA2000
                }

                if (value == null)
                {
                    return;
                }

                if (state.Value is IEncodeable)
                {
                    PropertyInfo property = (PropertyInfo)state.ComponentId;

                    MethodInfo[] accessors = property.GetAccessors();

                    for (int ii = 0; ii < accessors.Length; ii++)
                    {
                        if (accessors[ii].ReturnType == typeof(void))
                        {
                            accessors[ii].Invoke(state.Value, new object[] { value });
                            state.Component = value;
                            break;
                        }
                    }
                }

                if (state.Value is DataValue datavalue)
                {
                    int component = (int)state.ComponentId;

                    switch (component)
                    {
                        case 0: { state.Value = datavalue.WithWrappedValue(ClientUtils.ToVariant(value)); state.Component = value; break; }
                    }
                }

                if (state.Value is IList)
                {
                    int ii = (int)state.ComponentId;
                    ((IList)state.Value)[ii] = value;
                    state.Component = value;
                }

                m_expanding = false;
                int index = (int)state.ComponentIndex - 1;
                int indentCount = ItemsLV.Items[index].IndentCount;

                while (index > 0 && ItemsLV.Items[index - 1].IndentCount == indentCount)
                {
                    --index;
                }

                bool overwrite = true;
                await ShowValueAsync(index, overwrite, state.Value);
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

                EditMI.Enabled = (ItemsLV.SelectedItems[0].ForeColor != Color.LightGray);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
