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
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Client.Controls.Common
{
    /// <summary>
    /// Allows the user to edit a complex value. The editor navigates the value
    /// as Variants: array elements are lifted with <see cref="VariantElements"/>
    /// and structure fields with <see cref="VariantFieldCollection"/>, so no
    /// boxed CLR values or reflection are involved.
    /// </summary>
    public partial class EditComplexValueCtrl : UserControl
    {
        /// <summary>
        /// Constructs the object.
        /// </summary>
        public EditComplexValueCtrl()
        {
            InitializeComponent();
            MaxDisplayTextLength = 100;
            ValuesDV.AutoGenerateColumns = false;
            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            ImageList = new ClientUtils().ImageList;
            #pragma warning restore CA2000

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Values");

            m_dataset.Tables[0].Columns.Add("AccessInfo", typeof(AccessInfo));
            m_dataset.Tables[0].Columns.Add("Name", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(string));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));

            ValuesDV.DataSource = m_dataset.Tables[0];
        }

        #region Private Fields
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        private ISession m_session;
        private AccessInfo m_value;
        private bool m_readOnly;
        private int m_maxDisplayTextLength;
        private event EventHandler m_ValueChanged;
        #endregion

        /// <summary>
        /// Tracks one navigation step into the value. The chain of parents
        /// leads back to the root value, and every node carries its current
        /// value as a Variant.
        /// </summary>
        private sealed class AccessInfo
        {
            public AccessInfo Parent;

            /// <summary>The declared type of the slot holding this value.</summary>
            public TypeInfo TypeInfo;

            /// <summary>The current value.</summary>
            public Variant Value;

            public string Name;

            /// <summary>The flat element index when the parent is an array or matrix.</summary>
            public int ElementIndex = -1;

            /// <summary>The display indexes when the parent is an array or matrix.</summary>
            public int[] Indexes;

            /// <summary>The field index when the parent is a structure.</summary>
            public int FieldIndex = -1;

            /// <summary>Whether the field belongs to a DataValue parent.</summary>
            public bool IsDataValueField;

            /// <summary>The captured fields when this node displays a structure.</summary>
            public VariantFieldCollection Fields;

            /// <summary>The structure instance the fields were captured from.</summary>
            public IEncodeable Structure;
        }

        #region Public Members
        /// <summary>
        /// The maximum length of a value string displayed in a column.
        /// </summary>
        [DefaultValue(100)]
        public int MaxDisplayTextLength
        {
            get
            {
                return m_maxDisplayTextLength;
            }

            set
            {
                if (value < 20)
                {
                    m_maxDisplayTextLength = 20;
                }

                m_maxDisplayTextLength = value;
            }
        }

        /// <summary>
        /// Returns true if the Back command can be called.
        /// </summary>
        public bool CanGoBack
        {
            get
            {
                return (NavigationMENU.Items.Count > 1);
            }
        }

        /// <summary>
        /// Returns true if the ArraySize can be changed.
        /// </summary>
        public bool CanSetArraySize
        {
            get
            {
                if (m_readOnly)
                {
                    return false;
                }

                AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

                if (info != null)
                {
                    return EffectiveType(info).ValueRank >= 0;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if the data type can be changed.
        /// </summary>
        public bool CanChangeType
        {
            get
            {
                if (m_readOnly)
                {
                    return false;
                }

                if (NavigationMENU.Items.Count > 0)
                {
                    AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

                    if (info != null)
                    {
                        // the type can only be changed when the value sits in a Variant slot.
                        return !info.TypeInfo.IsUnknown && info.TypeInfo.BuiltInType == BuiltInType.Variant && info.TypeInfo.ValueRank < 0;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns the current data type.
        /// </summary>
        public BuiltInType CurrentType
        {
            get
            {
                if (NavigationMENU.Items.Count > 0)
                {
                    AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

                    if (info != null)
                    {
                        return EffectiveType(info).BuiltInType;
                    }
                }

                return BuiltInType.Variant;
            }
        }

        /// <summary>
        /// Raised when the value is changed.
        /// </summary>
        public event EventHandler ValueChanged
        {
            add { m_ValueChanged += value; }
            remove { m_ValueChanged -= value; }
        }

        /// <summary>
        /// Changes the session used for editing the value.
        /// </summary>
        public void ChangeSession(ISession session)
        {
            m_session = session;
        }

        /// <summary>
        /// Moves the displayed value back.
        /// </summary>
        public void Back()
        {
            if (!CanGoBack)
            {
                return;
            }

            NavigationMENU_Click(NavigationMENU.Items[NavigationMENU.Items.Count - 2], null);
        }

        /// <summary>
        /// Changes the array size.
        /// </summary>
        public void SetArraySize()
        {
            if (!CanSetArraySize)
            {
                return;
            }

            EndEdit();

            AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

            TypeInfo currentType = EffectiveType(info);
            IReadOnlyList<Variant> elements = Array.Empty<Variant>();
            int[] dimensions = null;

            if (VariantElements.TryGetElements(info.Value, out ArrayOf<Variant> lifted, out int[] currentDimensions))
            {
                elements = lifted.ToList();
                dimensions = currentDimensions;
            }

            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            SetTypeDlg.SetTypeResult result = new SetTypeDlg().ShowDialog(m_session?.MessageContext?.Telemetry, currentType, dimensions);
            #pragma warning restore CA2000

            if (result == null)
            {
                return;
            }

            BuiltInType targetType = result.TypeInfo.BuiltInType;

            // convert to a scalar.
            if (result.ArrayDimensions == null || result.ArrayDimensions.Length < 1)
            {
                Variant scalar = elements.Count > 0 ? elements[0] : info.Value;
                info.Value = Convert(scalar, targetType, result.UseDefaultOnError);
            }

            // convert the elements and resize.
            else
            {
                int total = 1;

                for (int ii = 0; ii < result.ArrayDimensions.Length; ii++)
                {
                    total *= result.ArrayDimensions[ii];
                }

                var newElements = new List<Variant>(total);

                for (int ii = 0; ii < total; ii++)
                {
                    if (ii < elements.Count)
                    {
                        newElements.Add(Convert(elements[ii], targetType, result.UseDefaultOnError));
                    }
                    else
                    {
                        newElements.Add(TypeInfo.GetDefaultVariantValue(targetType));
                    }
                }

                info.Value = VariantElements.CreateFromElements(targetType, newElements, result.ArrayDimensions);
            }

            UpdateParent(info);

            NavigationMENU.Items.RemoveAt(NavigationMENU.Items.Count - 1);
            ShowValue(info);
        }

        /// <summary>
        /// Changes the data type.
        /// </summary>
        public void SetType(BuiltInType builtInType)
        {
            if (!CanChangeType)
            {
                return;
            }

            AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

            try
            {
                EndEdit();
            }
            catch (Exception)
            {
                info.Value = TypeInfo.GetDefaultVariantValue(EffectiveType(info).BuiltInType);
            }

            info.Value = Convert(info.Value, builtInType, true);
            UpdateParent(info);

            NavigationMENU.Items.RemoveAt(NavigationMENU.Items.Count - 1);
            ShowValueNoNotify(info);
        }

        /// <summary>
        /// Converts the value to the new built in type.
        /// </summary>
        private Variant Convert(Variant oldValue, BuiltInType targetType, bool useDefaultOnError)
        {
            if (oldValue.TypeInfo.BuiltInType == targetType)
            {
                return oldValue;
            }

            try
            {
                return oldValue.ConvertTo(targetType);
            }
            catch (Exception e)
            {
                if (!useDefaultOnError)
                {
                    throw new FormatException("Could not cast value to requested type.", e);
                }

                return TypeInfo.GetDefaultVariantValue(targetType);
            }
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        public async Task ShowValueAsync(
            NodeId nodeId,
            uint attributeId,
            string name,
            Variant value,
            bool readOnly,
            CancellationToken ct = default)
        {
            m_readOnly = readOnly;
            NavigationMENU.Items.Clear();

            if (m_readOnly)
            {
                ValuesDV.EditMode = DataGridViewEditMode.EditProgrammatically;
                TextValueTB.ReadOnly = true;
            }

            TypeInfo expectedType = TypeInfo.Unknown;
            NodeId dataTypeId = NodeId.Null;

            // determine the expected data type for non-value attributes.
            if (attributeId != 0 && attributeId != Attributes.Value)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(Attributes.GetDataTypeId(attributeId));
                expectedType = new TypeInfo(builtInType, Attributes.GetValueRank(attributeId));
            }

            // determine the expected data type for value attributes.
            else if (!nodeId.IsNull && m_session != null)
            {
                IVariableBase variable = await m_session.NodeCache.FindAsync(nodeId, ct) as IVariableBase;

                if (variable != null)
                {
                    #pragma warning disable CA1849 // Justification: sample keeps the existing synchronous call pattern.
                    BuiltInType builtInType = TypeInfo.GetBuiltInType(variable.DataType, m_session.TypeTree);
                    #pragma warning restore CA1849
                    expectedType = new TypeInfo(builtInType, variable.ValueRank);
                    dataTypeId = variable.DataType;
                }
            }

            // use the value.
            if (expectedType.IsUnknown)
            {
                expectedType = !value.IsNull ? value.TypeInfo : TypeInfo.Scalars.String;
            }

            // assign a name.
            if (String.IsNullOrEmpty(name))
            {
                if (attributeId != 0)
                {
                    name = Attributes.GetBrowseName(attributeId);
                }
                else
                {
                    name = expectedType.ToString();
                }
            }

            AccessInfo info = new AccessInfo();
            info.TypeInfo = expectedType;
            info.Value = value;

            if (value.IsNull)
            {
                info.Value = CreateDefaultValue(expectedType, dataTypeId);
            }

            info.Name = name;
            m_value = info;

            ShowValue(info);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        public void ShowValue(
            string name,
            NodeId dataType,
            int valueRank,
            Variant value)
        {
            TypeInfo expectedType = TypeInfo.Unknown;

            if (m_session != null && !dataType.IsNull)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType, m_session.TypeTree);
                expectedType = new TypeInfo(builtInType, valueRank);
            }
            else if (!value.IsNull)
            {
                expectedType = value.TypeInfo;
            }
            else
            {
                expectedType = new TypeInfo(BuiltInType.String, valueRank);
            }

            if (value.IsNull)
            {
                value = CreateDefaultValue(expectedType, dataType);
            }

            ShowValue(expectedType, name, value);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        public void ShowValue(
            TypeInfo expectedType,
            string name,
            Variant value)
        {
            m_readOnly = false;
            NavigationMENU.Items.Clear();

            // assign a type.
            if (expectedType.IsUnknown)
            {
                expectedType = !value.IsNull ? value.TypeInfo : TypeInfo.Scalars.String;
            }

            // assign a name.
            if (String.IsNullOrEmpty(name))
            {
                name = expectedType.ToString();
            }

            AccessInfo info = new AccessInfo();
            info.TypeInfo = expectedType;
            info.Value = value;

            if (value.IsNull)
            {
                info.Value = CreateDefaultValue(expectedType, NodeId.Null);
            }

            // ensure the value has the expected type.
            else if (expectedType.BuiltInType != BuiltInType.Variant &&
                value.TypeInfo.BuiltInType != expectedType.BuiltInType)
            {
                info.Value = value.ConvertTo(expectedType.BuiltInType);
            }

            info.Name = name;
            m_value = info;

            ShowValue(info);
        }

        /// <summary>
        /// Returns the edited value.
        /// </summary>
        #pragma warning disable CA1024 // Justification: sample public API shape is preserved by design.
        public Variant GetValue()
        #pragma warning restore CA1024
        {
            return m_value.Value;
        }

        /// <summary>
        /// Validates the value currently being edited.
        /// </summary>
        public void EndEdit()
        {
            if (NavigationMENU.Items.Count < 1)
            {
                return;
            }

            if (!TextValueTB.Visible)
            {
                ValuesDV.EndEdit();
                return;
            }

            if (m_readOnly)
            {
                return;
            }

            AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

            TypeInfo typeInfo = EffectiveType(info);

            // structures and byte strings shown as text are not edited in the text box.
            if (typeInfo.ValueRank >= 0 ||
                typeInfo.BuiltInType == BuiltInType.ExtensionObject ||
                typeInfo.BuiltInType == BuiltInType.ByteString ||
                typeInfo.BuiltInType == BuiltInType.DataValue)
            {
                return;
            }

            info.Value = Variant.From(TextValueTB.Text).ConvertTo(typeInfo.BuiltInType);
            UpdateParent(info);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        private void ShowValue(AccessInfo parent)
        {
            ShowValueNoNotify(parent);

            m_ValueChanged?.Invoke(this, null);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        private void ShowValueNoNotify(AccessInfo parent)
        {
            m_dataset.Tables[0].Clear();
            ValuesDV.Visible = true;
            TextValueTB.Visible = false;

            ToolStripItem item = NavigationMENU.Items.Add(parent.Name);
            item.Click += new EventHandler(NavigationMENU_Click);
            item.Tag = parent;

            Variant value = parent.Value;
            TypeInfo typeInfo = EffectiveType(parent);

            // display the elements of an array or matrix.
            if (typeInfo.ValueRank >= 0)
            {
                if (VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions))
                {
                    // elements of a variant array keep their own type; other
                    // arrays fix the element type.
                    TypeInfo elementType = (value.TypeInfo.BuiltInType == BuiltInType.Variant)
                        ? TypeInfo.Scalars.Variant
                        : new TypeInfo(value.TypeInfo.BuiltInType, ValueRanks.Scalar);

                    ValuesDV.Visible = true;
                    TextValueTB.Visible = false;

                    for (int ii = 0; ii < elements.Count; ii++)
                    {
                        AccessInfo info = new AccessInfo();
                        info.Parent = parent;
                        info.ElementIndex = ii;
                        info.Indexes = GetIndexFromCount(ii, dimensions);
                        info.TypeInfo = elementType;
                        info.Value = elements[ii];

                        ShowIndexedValue(info);
                    }
                }

                return;
            }

            // check for extension object.
            if (value.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                ExtensionObject extension = value.GetExtensionObject();

                if (extension.TryGetValue(out IEncodeable encodeable, GetMessageContext()) &&
                    VariantFieldCollection.TryCapture(encodeable, GetMessageContext(), out VariantFieldCollection fields) &&
                    fields.Count > 0)
                {
                    parent.Structure = encodeable;
                    parent.Fields = fields;

                    ValuesDV.Visible = true;
                    TextValueTB.Visible = false;

                    for (int ii = 0; ii < fields.Count; ii++)
                    {
                        AccessInfo info = new AccessInfo();
                        info.Parent = parent;
                        info.FieldIndex = ii;
                        info.TypeInfo = fields.GetSlotType(ii);
                        info.Value = fields.GetValue(ii);
                        info.Name = fields.GetName(ii);

                        ShowNamedValue(info);
                    }

                    return;
                }

                // show opaque bodies as text.
                if (extension.TryGetAsBinary(out ByteString bytes, GetMessageContext()))
                {
                    ShowTextValue(bytes);
                    return;
                }

                if (extension.TryGetAsXml(out XmlElement xml, GetMessageContext()))
                {
                    ShowTextValue(xml);
                    return;
                }

                ShowTextValue(value.ToString());
                return;
            }

            // display the components of a data value.
            if (value.TypeInfo.BuiltInType == BuiltInType.DataValue && value.TypeInfo.ValueRank < 0)
            {
                DataValue dataValue = value.GetDataValue();

                ValuesDV.Visible = true;
                TextValueTB.Visible = false;

                ShowNamedValue(CreateDataValueField(parent, 0, "Value", TypeInfo.Scalars.Variant, dataValue.WrappedValue));
                ShowNamedValue(CreateDataValueField(parent, 1, "StatusCode", TypeInfo.Scalars.StatusCode, Variant.From(dataValue.StatusCode)));
                ShowNamedValue(CreateDataValueField(parent, 2, "SourceTimestamp", TypeInfo.Scalars.DateTime, Variant.From(dataValue.SourceTimestamp)));
                ShowNamedValue(CreateDataValueField(parent, 3, "SourcePicoseconds", TypeInfo.Scalars.UInt16, Variant.From(dataValue.SourcePicoseconds)));
                ShowNamedValue(CreateDataValueField(parent, 4, "ServerTimestamp", TypeInfo.Scalars.DateTime, Variant.From(dataValue.ServerTimestamp)));
                ShowNamedValue(CreateDataValueField(parent, 5, "ServerPicoseconds", TypeInfo.Scalars.UInt16, Variant.From(dataValue.ServerPicoseconds)));
                return;
            }

            // check for XmlElements.
            if (value.TryGetValue(out XmlElement xmlElement))
            {
                ShowTextValue(xmlElement);
                return;
            }

            // check for ByteString.
            if (value.TryGetValue(out ByteString byteString))
            {
                ShowTextValue(byteString);
                return;
            }

            // display everything else as text.
            ShowTextValue(value, typeInfo);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the type describing the current value: the declared slot
        /// type unless the slot is a Variant, in which case the type carried
        /// by the value itself.
        /// </summary>
        private static TypeInfo EffectiveType(AccessInfo info)
        {
            TypeInfo typeInfo = info.TypeInfo;

            if (typeInfo.IsUnknown || typeInfo.BuiltInType == BuiltInType.Variant)
            {
                if (!info.Value.IsNull)
                {
                    return info.Value.TypeInfo;
                }

                if (typeInfo.IsUnknown)
                {
                    return TypeInfo.Scalars.String;
                }
            }

            return typeInfo;
        }

        /// <summary>
        /// Returns the message context used to access extension object bodies.
        /// </summary>
        private IServiceMessageContext GetMessageContext()
        {
            return m_session?.MessageContext ?? ServiceMessageContext.CreateEmpty(null);
        }

        /// <summary>
        /// Creates a default value for the expected type. For structured
        /// values the encodeable factory provides a default instance.
        /// </summary>
        private Variant CreateDefaultValue(TypeInfo expectedType, NodeId dataTypeId)
        {
            if (expectedType.BuiltInType == BuiltInType.ExtensionObject && expectedType.ValueRank < 0 &&
                m_session != null && !dataTypeId.IsNull)
            {
#pragma warning disable UA_NETStandard_1 // Experimental IType API required in 2.0 to create a default instance.
                if (m_session.Factory.TryGetType(new ExpandedNodeId(dataTypeId), out IType type) &&
                    type is IEncodeableType encodeableType)
                {
                    return Variant.FromStructure(encodeableType.CreateInstance());
                }
#pragma warning restore UA_NETStandard_1
            }

            return VariantElements.CreateDefault(expectedType);
        }

        /// <summary>
        /// Creates the access info for one component of a DataValue.
        /// </summary>
        private static AccessInfo CreateDataValueField(AccessInfo parent, int index, string name, TypeInfo slotType, Variant value)
        {
            return new AccessInfo
            {
                Parent = parent,
                FieldIndex = index,
                IsDataValueField = true,
                TypeInfo = slotType,
                Value = value,
                Name = name
            };
        }

        /// <summary>
        /// Returns the index based on the current count.
        /// </summary>
        private int[] GetIndexFromCount(int count, int[] dimensions)
        {
            int[] indexes = new int[(dimensions != null) ? dimensions.Length : 1];

            for (int ii = indexes.Length - 1; ii >= 0; ii--)
            {
                indexes[ii] = count % dimensions[ii];
                count /= dimensions[ii];
            }

            return indexes;
        }

        /// <summary>
        /// Adds the value at an array index to the control.
        /// </summary>
        private void ShowIndexedValue(AccessInfo info)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            StringBuilder buffer = new StringBuilder();
            buffer.Append('[');

            if (info.Indexes != null)
            {
                for (int ii = 0; ii < info.Indexes.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(',');
                    }

                    buffer.Append(info.Indexes[ii]);
                }
            }

            buffer.Append(']');
            info.Name = buffer.ToString();

            row[0] = info;
            row[1] = info.Name;
            row[2] = GetDataTypeString(info);
            row[3] = ValueToString(info.Value, EffectiveType(info));
            row[4] = ImageList.Images[ClientUtils.GetImageIndex(Attributes.Value, info.Value)];

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Returns the display name for the data type of the value.
        /// </summary>
        private string GetDataTypeString(AccessInfo accessInfo)
        {
            TypeInfo typeInfo = EffectiveType(accessInfo);

            if (typeInfo.BuiltInType == BuiltInType.ExtensionObject && typeInfo.ValueRank < 0)
            {
                ExtensionObject extension = accessInfo.Value.GetExtensionObject();

                if (extension.TryGetValue(out IEncodeable encodeable, GetMessageContext()))
                {
                    return VariantFieldCollection.GetTypeDisplayName(encodeable);
                }
            }

            return typeInfo.ToString();
        }

        /// <summary>
        /// Adds the value with the specified name to the control.
        /// </summary>
        private void ShowNamedValue(AccessInfo info)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = info;
            row[1] = (info.Name != null) ? info.Name : "unknown";
            row[2] = GetDataTypeString(info);
            row[3] = ValueToString(info.Value, EffectiveType(info));
            row[4] = ImageList.Images[ClientUtils.GetImageIndex(Attributes.Value, info.Value)];

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        private void ShowTextValue(Variant value, TypeInfo typeInfo)
        {
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.ByteString:
                {
                    ShowTextValue(value.GetByteString());
                    break;
                }

                case BuiltInType.XmlElement:
                {
                    ShowTextValue(value.GetXmlElement());
                    break;
                }

                case BuiltInType.String:
                {
                    ShowTextValue(value.GetString());
                    break;
                }

                default:
                {
                    ShowTextValue(ValueToString(value, typeInfo));
                    break;
                }
            }
        }

        /// <summary>
        /// Displays a string in the control.
        /// </summary>
        private void ShowTextValue(string value)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;

            if (value != null && value.Length > MaxDisplayTextLength)
            {
                TextValueTB.ScrollBars = ScrollBars.Both;
            }
            else
            {
                TextValueTB.ScrollBars = ScrollBars.None;
            }

            TextValueTB.Font = new Font("Segoe UI", TextValueTB.Font.Size);
            TextValueTB.Text = value;
        }

        /// <summary>
        /// Displays a complete byte string in the control.
        /// </summary>
        private void ShowTextValue(ByteString value)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;

            StringBuilder buffer = new StringBuilder();

            if (!value.IsNull)
            {
                int count = 0;

                foreach (byte b in value.Span)
                {
                    if (buffer.Length > 0 && (count % 30) == 0)
                    {
                        buffer.Append("\r\n");
                    }

                    buffer.AppendFormat("{0:X2} ", b);
                    count++;
                }
            }

            TextValueTB.Font = new Font("Courier New", TextValueTB.Font.Size);
            TextValueTB.Text = buffer.ToString();
        }

        /// <summary>
        /// Displays a complete XML element in the control.
        /// </summary>
        private void ShowTextValue(XmlElement value)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;

            StringBuilder buffer = new StringBuilder();

            if (!value.IsNull)
            {
                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                settings.Indent = true;
                settings.OmitXmlDeclaration = true;
                settings.NewLineHandling = System.Xml.NewLineHandling.Replace;
                settings.NewLineChars = "\r\n";
                settings.IndentChars = "    ";

                using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(buffer, settings))
                {
                    using (System.Xml.XmlNodeReader reader = new System.Xml.XmlNodeReader((System.Xml.XmlElement)value))
                    {
                        writer.WriteNode(reader, false);
                    }
                }
            }

            TextValueTB.Font = new Font("Courier New", TextValueTB.Font.Size);
            TextValueTB.Text = buffer.ToString();
        }

        /// <summary>
        /// Converts a value to a string for display in the grid.
        /// </summary>
        private string ValueToString(Variant value, TypeInfo typeInfo)
        {
            if (value.IsNull)
            {
                return String.Empty;
            }

            if (value.TypeInfo.ValueRank >= 0)
            {
                StringBuilder buffer = new StringBuilder();

                if (VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out _))
                {
                    buffer.Append("{ ");

                    foreach (Variant element in elements)
                    {
                        if (buffer.Length > 2)
                        {
                            buffer.Append(" | ");
                        }

                        if (buffer.Length > MaxDisplayTextLength)
                        {
                            buffer.Append("...");
                            break;
                        }

                        buffer.Append(ValueToString(element, element.TypeInfo));
                    }

                    buffer.Append(" }");
                }

                return buffer.ToString();
            }

            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.String:
                {
                    string text = value.GetString();

                    if (text != null && text.Length > MaxDisplayTextLength)
                    {
                        return string.Concat(text.AsSpan(0, MaxDisplayTextLength), "...");
                    }

                    return text;
                }

                case BuiltInType.ByteString:
                {
                    StringBuilder buffer = new StringBuilder();

                    ByteString bytes = value.GetByteString();

                    foreach (byte b in bytes.Span)
                    {
                        if (buffer.Length > MaxDisplayTextLength)
                        {
                            buffer.Append("...");
                            break;
                        }

                        buffer.AppendFormat("{0:X2}", b);
                    }

                    return buffer.ToString();
                }

                case BuiltInType.Enumeration:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DataValue:
                {
                    string text = value.ToString();

                    if (text != null && text.Length > MaxDisplayTextLength)
                    {
                        return string.Concat(text.AsSpan(0, MaxDisplayTextLength), "...");
                    }

                    return text;
                }
            }

            return value.ConvertTo(BuiltInType.String).TryGetValue(out string valueText) ? valueText : String.Empty;
        }

        /// <summary>
        /// Whether the value can be edited in the grid view.
        /// </summary>
        private bool IsSimpleValue(AccessInfo info)
        {
            if (info == null)
            {
                return true;
            }

            TypeInfo typeInfo = EffectiveType(info);

            if (typeInfo.ValueRank >= 0)
            {
                return false;
            }

            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.String:
                {
                    string text = info.Value.GetString();

                    if (text != null && text.Length >= MaxDisplayTextLength)
                    {
                        return false;
                    }

                    return true;
                }

                case BuiltInType.ByteString:
                case BuiltInType.XmlElement:
                case BuiltInType.QualifiedName:
                case BuiltInType.LocalizedText:
                case BuiltInType.DataValue:
                case BuiltInType.ExtensionObject:
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        private void NavigationMENU_Click(object sender, EventArgs e)
        {
            try
            {
                EndEdit();

                ToolStripItem item = sender as ToolStripItem;

                if (item != null)
                {
                    // remove all menu items appearing after the selected item.
                    for (int ii = NavigationMENU.Items.Count - 1; ii >= 0; ii--)
                    {
                        ToolStripItem target = NavigationMENU.Items[ii];
                        NavigationMENU.Items.Remove(target);

                        if (Object.ReferenceEquals(target, item))
                        {
                            break;
                        }
                    }

                    // show the current value.
                    AccessInfo info = (AccessInfo)item.Tag;
                    ShowValue(info);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        private void ValuesDV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewCell cell in ValuesDV.SelectedCells)
                {
                    DataRowView source = ValuesDV.Rows[cell.RowIndex].DataBoundItem as DataRowView;

                    if (cell.ColumnIndex == 3)
                    {
                        AccessInfo info = (AccessInfo)source.Row[0];
                        ShowValue(info);
                    }

                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        private void ValuesDV_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            try
            {
                if (this.Visible && e.ColumnIndex == 3)
                {
                    DataRowView source = ValuesDV.Rows[e.RowIndex].DataBoundItem as DataRowView;
                    AccessInfo info = (AccessInfo)source.Row[0];

                    if (IsSimpleValue(info))
                    {
                        Variant.From(e.FormattedValue as string).ConvertTo(EffectiveType(info).BuiltInType);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
                e.Cancel = true;
            }
        }

        private void ValuesDV_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (this.Visible && e.RowIndex >= 0 && e.ColumnIndex == 3)
                {
                    DataRowView source = ValuesDV.Rows[e.RowIndex].DataBoundItem as DataRowView;
                    AccessInfo info = (AccessInfo)source.Row[0];

                    if (IsSimpleValue(info))
                    {
                        info.Value = Variant.From((string)source.Row[3]).ConvertTo(EffectiveType(info).BuiltInType);
                        UpdateParent(info);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Recursively rebuilds the parent values from the changed child value.
        /// </summary>
        private void UpdateParent(AccessInfo info)
        {
            AccessInfo parent = info.Parent;

            if (parent == null)
            {
                return;
            }

            // replace the element in the parent array or matrix.
            if (info.ElementIndex >= 0)
            {
                if (VariantElements.TryGetElements(parent.Value, out ArrayOf<Variant> elements, out int[] dimensions))
                {
                    var newElements = new List<Variant>(elements.ToList());
                    newElements[info.ElementIndex] = info.Value;

                    BuiltInType elementType = parent.Value.TypeInfo.BuiltInType;
                    parent.Value = VariantElements.CreateFromElements(elementType, newElements, dimensions);
                }
            }

            // replace the component in the parent data value.
            else if (info.IsDataValueField)
            {
                DataValue dataValue = parent.Value.GetDataValue();

                parent.Value = Variant.From(new DataValue(
                    info.FieldIndex == 0 ? info.Value : dataValue.WrappedValue,
                    info.FieldIndex == 1 ? info.Value.GetStatusCode() : dataValue.StatusCode,
                    info.FieldIndex == 2 ? info.Value.GetDateTime() : dataValue.SourceTimestamp,
                    info.FieldIndex == 4 ? info.Value.GetDateTime() : dataValue.ServerTimestamp,
                    info.FieldIndex == 3 ? info.Value.GetUInt16() : dataValue.SourcePicoseconds,
                    info.FieldIndex == 5 ? info.Value.GetUInt16() : dataValue.ServerPicoseconds));
            }

            // replace the field in the parent structure.
            else if (info.FieldIndex >= 0 && parent.Fields != null && parent.Structure != null)
            {
                parent.Fields.SetValue(info.FieldIndex, info.Value);
                parent.Structure = parent.Fields.ApplyTo(parent.Structure);
                parent.Value = Variant.FromStructure(parent.Structure);
            }

            UpdateParent(parent);
        }
    }
}
