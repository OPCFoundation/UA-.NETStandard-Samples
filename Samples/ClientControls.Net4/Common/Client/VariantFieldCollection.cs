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

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Presents the fields of any <see cref="IEncodeable"/> as named Variants
    /// so the value editors can navigate and update structures without using
    /// reflection over boxed CLR values.
    /// </summary>
    /// <remarks>
    /// The fields are captured by running the structure's own
    /// <see cref="IEncodeable.Encode"/> against an encoder that records each
    /// field as a Variant instead of serializing it. Edited fields are applied
    /// by running <see cref="IEncodeable.Decode"/> on a clone against a decoder
    /// that replays the recorded Variants. This works uniformly for generated
    /// types, the samples' source generated types and dynamically loaded
    /// complex types, including unions and structures with optional fields.
    /// </remarks>
    public sealed class VariantFieldCollection
    {
        private readonly List<Field> m_fields = new List<Field>();
        private readonly IServiceMessageContext m_context;
        private uint m_switchField;
        private uint m_encodingMask;

        private VariantFieldCollection(IServiceMessageContext context)
        {
            m_context = context;
        }

        private sealed class Field
        {
            public string Name;
            public Variant Value;
            public TypeInfo SlotType;
            public DiagnosticInfo Diagnostic;
            public ArrayOf<DiagnosticInfo> Diagnostics;
            public bool IsDiagnostic;
            public bool IsDiagnosticArray;
        }

        /// <summary>
        /// Captures the fields of the structure. Returns false if the
        /// structure cannot be encoded.
        /// </summary>
        public static bool TryCapture(IEncodeable value, IServiceMessageContext context, out VariantFieldCollection fields)
        {
            fields = null;

            if (value == null)
            {
                return false;
            }

            var collection = new VariantFieldCollection(context ?? ServiceMessageContext.CreateEmpty(null));

            try
            {
                using (var encoder = new CaptureEncoder(collection))
                {
                    value.Encode(encoder);
                }
            }
            catch (Exception)
            {
                return false;
            }

            fields = collection;
            return true;
        }

        /// <summary>
        /// The number of captured fields.
        /// </summary>
        public int Count => m_fields.Count;

        /// <summary>
        /// The name of the field.
        /// </summary>
        public string GetName(int index)
        {
            return m_fields[index].Name;
        }

        /// <summary>
        /// The current value of the field.
        /// </summary>
        public Variant GetValue(int index)
        {
            Field field = m_fields[index];

            if (field.IsDiagnostic)
            {
                return Variant.From(field.Diagnostic?.ToString() ?? String.Empty);
            }

            if (field.IsDiagnosticArray)
            {
                return Variant.From(field.Diagnostics.ToString());
            }

            return field.Value;
        }

        /// <summary>
        /// The declared type of the field slot. A Variant typed slot accepts
        /// values of any type.
        /// </summary>
        public TypeInfo GetSlotType(int index)
        {
            return m_fields[index].SlotType;
        }

        /// <summary>
        /// Whether the field value can be replaced. DiagnosticInfo fields are
        /// shown as text and cannot be edited.
        /// </summary>
        public bool IsEditable(int index)
        {
            Field field = m_fields[index];
            return !field.IsDiagnostic && !field.IsDiagnosticArray;
        }

        /// <summary>
        /// Replaces the value of a field. The value is converted to the
        /// declared slot type and the conversion throws if it is not possible.
        /// </summary>
        public void SetValue(int index, Variant value)
        {
            Field field = m_fields[index];

            if (!IsEditable(index))
            {
                throw new InvalidOperationException($"The field '{field.Name}' cannot be edited.");
            }

            TypeInfo slotType = field.SlotType;

            if (!slotType.IsUnknown &&
                slotType.BuiltInType != BuiltInType.Variant &&
                !value.IsNull &&
                value.TypeInfo.BuiltInType != slotType.BuiltInType)
            {
                value = value.ConvertTo(slotType.BuiltInType);
            }

            field.Value = value;
        }

        /// <summary>
        /// Applies the current field values to a clone of the structure and
        /// returns the updated clone.
        /// </summary>
        public IEncodeable ApplyTo(IEncodeable target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            IEncodeable clone = (IEncodeable)target.Clone();

            using (var decoder = new ReplayDecoder(this))
            {
                clone.Decode(decoder);
            }

            return clone;
        }

        private void Add(string name, Variant value, TypeInfo slotType)
        {
            m_fields.Add(new Field { Name = name, Value = value, SlotType = slotType });
        }

        private void AddDiagnostic(string name, DiagnosticInfo value)
        {
            m_fields.Add(new Field { Name = name, Diagnostic = value, IsDiagnostic = true, SlotType = TypeInfo.Scalars.DiagnosticInfo });
        }

        private void AddDiagnosticArray(string name, ArrayOf<DiagnosticInfo> values)
        {
            m_fields.Add(new Field { Name = name, Diagnostics = values, IsDiagnosticArray = true, SlotType = TypeInfo.Arrays.DiagnosticInfo });
        }

        #region CaptureEncoder
        /// <summary>
        /// Records every field written by <see cref="IEncodeable.Encode"/> as
        /// a named Variant.
        /// </summary>
        private sealed class CaptureEncoder : IEncoder
        {
            private readonly VariantFieldCollection m_fields;

            public CaptureEncoder(VariantFieldCollection fields)
            {
                m_fields = fields;
            }

            public EncodingType EncodingType => EncodingType.Binary;

            public bool CanOmitFields => false;

            public IServiceMessageContext Context => m_fields.m_context;

            public int Close() => 0;

            public string CloseAndReturnText() => null;

            public void Dispose()
            {
            }

            public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
            {
            }

            public void PushNamespace(string namespaceUri)
            {
            }

            public void PopNamespace()
            {
            }

            public void EncodeMessage<T>(T message) where T : IEncodeable, new()
                => throw new NotSupportedException("The field capture encoder does not encode messages.");

            public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId) where T : IEncodeable
                => throw new NotSupportedException("The field capture encoder does not encode messages.");

            public void WriteBoolean(string fieldName, bool value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Boolean);

            public void WriteSByte(string fieldName, sbyte value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.SByte);

            public void WriteByte(string fieldName, byte value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Byte);

            public void WriteInt16(string fieldName, short value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Int16);

            public void WriteUInt16(string fieldName, ushort value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.UInt16);

            public void WriteInt32(string fieldName, int value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Int32);

            public void WriteUInt32(string fieldName, uint value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.UInt32);

            public void WriteInt64(string fieldName, long value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Int64);

            public void WriteUInt64(string fieldName, ulong value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.UInt64);

            public void WriteFloat(string fieldName, float value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Float);

            public void WriteDouble(string fieldName, double value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Double);

            public void WriteString(string fieldName, string value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.String);

            public void WriteDateTime(string fieldName, DateTimeUtc value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.DateTime);

            public void WriteGuid(string fieldName, Uuid value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Guid);

            public void WriteByteString(string fieldName, ByteString value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.ByteString);

            public void WriteByteString(string fieldName, ReadOnlySpan<byte> value)
                => m_fields.Add(fieldName, Variant.From(ByteString.From(value.ToArray())), TypeInfo.Scalars.ByteString);

            public void WriteXmlElement(string fieldName, XmlElement value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.XmlElement);

            public void WriteNodeId(string fieldName, NodeId value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.NodeId);

            public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.ExpandedNodeId);

            public void WriteStatusCode(string fieldName, StatusCode value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.StatusCode);

            public void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value)
                => m_fields.AddDiagnostic(fieldName, value);

            public void WriteQualifiedName(string fieldName, QualifiedName value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.QualifiedName);

            public void WriteLocalizedText(string fieldName, LocalizedText value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.LocalizedText);

            public void WriteVariant(string fieldName, in Variant value)
                => m_fields.Add(fieldName, value, TypeInfo.Scalars.Variant);

            public void WriteDataValue(string fieldName, in DataValue value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.DataValue);

            public void WriteExtensionObject(string fieldName, ExtensionObject value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.ExtensionObject);

            public void WriteEncodeable<T>(string fieldName, T value) where T : IEncodeable, new()
                => m_fields.Add(fieldName, Variant.FromStructure(value), TypeInfo.Scalars.ExtensionObject);

            public void WriteEncodeable<T>(string fieldName, T value, ExpandedNodeId encodeableTypeId) where T : IEncodeable
                => m_fields.Add(fieldName, Variant.FromStructure(value), TypeInfo.Scalars.ExtensionObject);

            public void WriteEncodeableAsExtensionObject<T>(string fieldName, T value) where T : IEncodeable
                => m_fields.Add(fieldName, Variant.FromStructure(value), TypeInfo.Scalars.ExtensionObject);

            public void WriteEnumerated<T>(string fieldName, T value) where T : struct, Enum
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Enumeration);

            public void WriteEnumerated(string fieldName, EnumValue value)
                => m_fields.Add(fieldName, Variant.From(value), TypeInfo.Scalars.Enumeration);

            public void WriteBooleanArray(string fieldName, ArrayOf<bool> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Boolean);

            public void WriteSByteArray(string fieldName, ArrayOf<sbyte> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.SByte);

            public void WriteByteArray(string fieldName, ArrayOf<byte> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Byte);

            public void WriteInt16Array(string fieldName, ArrayOf<short> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Int16);

            public void WriteUInt16Array(string fieldName, ArrayOf<ushort> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.UInt16);

            public void WriteInt32Array(string fieldName, ArrayOf<int> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Int32);

            public void WriteUInt32Array(string fieldName, ArrayOf<uint> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.UInt32);

            public void WriteInt64Array(string fieldName, ArrayOf<long> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Int64);

            public void WriteUInt64Array(string fieldName, ArrayOf<ulong> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.UInt64);

            public void WriteFloatArray(string fieldName, ArrayOf<float> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Float);

            public void WriteDoubleArray(string fieldName, ArrayOf<double> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Double);

            public void WriteStringArray(string fieldName, ArrayOf<string> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.String);

            public void WriteDateTimeArray(string fieldName, ArrayOf<DateTimeUtc> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.DateTime);

            public void WriteGuidArray(string fieldName, ArrayOf<Uuid> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Guid);

            public void WriteByteStringArray(string fieldName, ArrayOf<ByteString> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.ByteString);

            public void WriteXmlElementArray(string fieldName, ArrayOf<XmlElement> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.XmlElement);

            public void WriteNodeIdArray(string fieldName, ArrayOf<NodeId> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.NodeId);

            public void WriteExpandedNodeIdArray(string fieldName, ArrayOf<ExpandedNodeId> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.ExpandedNodeId);

            public void WriteStatusCodeArray(string fieldName, ArrayOf<StatusCode> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.StatusCode);

            public void WriteDiagnosticInfoArray(string fieldName, ArrayOf<DiagnosticInfo> values)
                => m_fields.AddDiagnosticArray(fieldName, values);

            public void WriteQualifiedNameArray(string fieldName, ArrayOf<QualifiedName> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.QualifiedName);

            public void WriteLocalizedTextArray(string fieldName, ArrayOf<LocalizedText> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.LocalizedText);

            public void WriteVariantArray(string fieldName, ArrayOf<Variant> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Variant);

            public void WriteDataValueArray(string fieldName, ArrayOf<DataValue> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.DataValue);

            public void WriteExtensionObjectArray(string fieldName, ArrayOf<ExtensionObject> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.ExtensionObject);

            public void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values) where T : IEncodeable, new()
                => m_fields.Add(fieldName, Variant.FromStructure(values), TypeInfo.Arrays.ExtensionObject);

            public void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values, ExpandedNodeId encodeableTypeId) where T : IEncodeable
                => m_fields.Add(fieldName, Variant.FromStructure(values), TypeInfo.Arrays.ExtensionObject);

            public void WriteEncodeableArrayAsExtensionObjects<T>(string fieldName, ArrayOf<T> values) where T : IEncodeable
                => m_fields.Add(fieldName, Variant.FromStructure(values), TypeInfo.Arrays.ExtensionObject);

            public void WriteEnumeratedArray<T>(string fieldName, ArrayOf<T> values) where T : struct, Enum
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Enumeration);

            public void WriteEnumeratedArray(string fieldName, ArrayOf<EnumValue> values)
                => m_fields.Add(fieldName, Variant.From(values), TypeInfo.Arrays.Enumeration);

            public void WriteVariantValue(string fieldName, in Variant value)
                => m_fields.Add(fieldName, value, value.TypeInfo);

            public void WriteEncodeableMatrix<T>(string fieldName, MatrixOf<T> values) where T : IEncodeable, new()
                => WriteEncodeableMatrix(fieldName, values, default);

            public void WriteEncodeableMatrix<T>(string fieldName, MatrixOf<T> values, ExpandedNodeId encodeableTypeId) where T : IEncodeable
            {
                MatrixOf<ExtensionObject> extensions = values.ConvertAll(value => new ExtensionObject(value));
                m_fields.Add(fieldName, Variant.From(extensions), new TypeInfo(BuiltInType.ExtensionObject, extensions.Dimensions.Length));
            }

            public void WriteSwitchField(uint switchField, out string fieldName)
            {
                m_fields.m_switchField = switchField;
                fieldName = null;
            }

            public void WriteEncodingMask(uint encodingMask)
            {
                m_fields.m_encodingMask = encodingMask;
            }
        }
        #endregion

        #region ReplayDecoder
        /// <summary>
        /// Replays the captured (possibly edited) Variants to
        /// <see cref="IEncodeable.Decode"/>.
        /// </summary>
        private sealed class ReplayDecoder : IDecoder
        {
            private readonly VariantFieldCollection m_fields;
            private int m_position;

            public ReplayDecoder(VariantFieldCollection fields)
            {
                m_fields = fields;
            }

            public EncodingType EncodingType => EncodingType.Binary;

            public IServiceMessageContext Context => m_fields.m_context;

            public void Close()
            {
            }

            public void Dispose()
            {
            }

            public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
            {
            }

            public T DecodeMessage<T>() where T : IEncodeable
                => throw new NotSupportedException("The field replay decoder does not decode messages.");

            public void PushNamespace(string namespaceUri)
            {
            }

            public void PopNamespace()
            {
            }

            /// <summary>
            /// Returns the next captured field, preferring the field with the
            /// requested name if the decode order differs from the encode order.
            /// </summary>
            private Field Next(string fieldName)
            {
                List<Field> fields = m_fields.m_fields;

                if (m_position < fields.Count)
                {
                    Field field = fields[m_position];

                    if (fieldName == null || field.Name == null || field.Name == fieldName)
                    {
                        m_position++;
                        return field;
                    }
                }

                for (int ii = 0; ii < fields.Count; ii++)
                {
                    if (fields[ii].Name == fieldName)
                    {
                        m_position = ii + 1;
                        return fields[ii];
                    }
                }

                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "The field '{0}' was not captured from the structure.", fieldName);
            }

            public bool ReadBoolean(string fieldName) => Next(fieldName).Value.GetBoolean();

            public sbyte ReadSByte(string fieldName) => Next(fieldName).Value.GetSByte();

            public byte ReadByte(string fieldName) => Next(fieldName).Value.GetByte();

            public short ReadInt16(string fieldName) => Next(fieldName).Value.GetInt16();

            public ushort ReadUInt16(string fieldName) => Next(fieldName).Value.GetUInt16();

            public int ReadInt32(string fieldName) => Next(fieldName).Value.GetInt32();

            public uint ReadUInt32(string fieldName) => Next(fieldName).Value.GetUInt32();

            public long ReadInt64(string fieldName) => Next(fieldName).Value.GetInt64();

            public ulong ReadUInt64(string fieldName) => Next(fieldName).Value.GetUInt64();

            public float ReadFloat(string fieldName) => Next(fieldName).Value.GetFloat();

            public double ReadDouble(string fieldName) => Next(fieldName).Value.GetDouble();

            public string ReadString(string fieldName) => Next(fieldName).Value.GetString();

            public DateTimeUtc ReadDateTime(string fieldName) => Next(fieldName).Value.GetDateTime();

            public Uuid ReadGuid(string fieldName) => Next(fieldName).Value.GetGuid();

            public ByteString ReadByteString(string fieldName) => Next(fieldName).Value.GetByteString();

            public XmlElement ReadXmlElement(string fieldName) => Next(fieldName).Value.GetXmlElement();

            public NodeId ReadNodeId(string fieldName) => Next(fieldName).Value.GetNodeId();

            public ExpandedNodeId ReadExpandedNodeId(string fieldName) => Next(fieldName).Value.GetExpandedNodeId();

            public StatusCode ReadStatusCode(string fieldName) => Next(fieldName).Value.GetStatusCode();

            public DiagnosticInfo ReadDiagnosticInfo(string fieldName) => Next(fieldName).Diagnostic;

            public QualifiedName ReadQualifiedName(string fieldName) => Next(fieldName).Value.GetQualifiedName();

            public LocalizedText ReadLocalizedText(string fieldName) => Next(fieldName).Value.GetLocalizedText();

            public Variant ReadVariant(string fieldName) => Next(fieldName).Value;

            public DataValue ReadDataValue(string fieldName) => Next(fieldName).Value.GetDataValue();

            public ExtensionObject ReadExtensionObject(string fieldName) => Next(fieldName).Value.GetExtensionObject();

            public T ReadEncodeable<T>(string fieldName, ExpandedNodeId encodeableTypeId) where T : IEncodeable
                => Next(fieldName).Value.GetStructure<T>(default, Context);

            public T ReadEncodeable<T>(string fieldName) where T : IEncodeable, new()
                => Next(fieldName).Value.GetStructure<T>(default, Context);

            public T ReadEncodeableAsExtensionObject<T>(string fieldName) where T : IEncodeable
                => Next(fieldName).Value.GetStructure<T>(default, Context);

            public T ReadEnumerated<T>(string fieldName) where T : struct, Enum
                => Next(fieldName).Value.GetEnumeration<T>();

            public EnumValue ReadEnumerated(string fieldName) => Next(fieldName).Value.GetEnumeration();

            public ArrayOf<bool> ReadBooleanArray(string fieldName) => Next(fieldName).Value.GetBooleanArray();

            public ArrayOf<sbyte> ReadSByteArray(string fieldName) => Next(fieldName).Value.GetSByteArray();

            public ArrayOf<byte> ReadByteArray(string fieldName) => Next(fieldName).Value.GetByteArray();

            public ArrayOf<short> ReadInt16Array(string fieldName) => Next(fieldName).Value.GetInt16Array();

            public ArrayOf<ushort> ReadUInt16Array(string fieldName) => Next(fieldName).Value.GetUInt16Array();

            public ArrayOf<int> ReadInt32Array(string fieldName) => Next(fieldName).Value.GetInt32Array();

            public ArrayOf<uint> ReadUInt32Array(string fieldName) => Next(fieldName).Value.GetUInt32Array();

            public ArrayOf<long> ReadInt64Array(string fieldName) => Next(fieldName).Value.GetInt64Array();

            public ArrayOf<ulong> ReadUInt64Array(string fieldName) => Next(fieldName).Value.GetUInt64Array();

            public ArrayOf<float> ReadFloatArray(string fieldName) => Next(fieldName).Value.GetFloatArray();

            public ArrayOf<double> ReadDoubleArray(string fieldName) => Next(fieldName).Value.GetDoubleArray();

            public ArrayOf<string> ReadStringArray(string fieldName) => Next(fieldName).Value.GetStringArray();

            public ArrayOf<DateTimeUtc> ReadDateTimeArray(string fieldName) => Next(fieldName).Value.GetDateTimeArray();

            public ArrayOf<Uuid> ReadGuidArray(string fieldName) => Next(fieldName).Value.GetGuidArray();

            public ArrayOf<ByteString> ReadByteStringArray(string fieldName) => Next(fieldName).Value.GetByteStringArray();

            public ArrayOf<XmlElement> ReadXmlElementArray(string fieldName) => Next(fieldName).Value.GetXmlElementArray();

            public ArrayOf<NodeId> ReadNodeIdArray(string fieldName) => Next(fieldName).Value.GetNodeIdArray();

            public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string fieldName) => Next(fieldName).Value.GetExpandedNodeIdArray();

            public ArrayOf<StatusCode> ReadStatusCodeArray(string fieldName) => Next(fieldName).Value.GetStatusCodeArray();

            public ArrayOf<DiagnosticInfo> ReadDiagnosticInfoArray(string fieldName) => Next(fieldName).Diagnostics;

            public ArrayOf<QualifiedName> ReadQualifiedNameArray(string fieldName) => Next(fieldName).Value.GetQualifiedNameArray();

            public ArrayOf<LocalizedText> ReadLocalizedTextArray(string fieldName) => Next(fieldName).Value.GetLocalizedTextArray();

            public ArrayOf<Variant> ReadVariantArray(string fieldName) => Next(fieldName).Value.GetVariantArray();

            public ArrayOf<DataValue> ReadDataValueArray(string fieldName) => Next(fieldName).Value.GetDataValueArray();

            public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string fieldName) => Next(fieldName).Value.GetExtensionObjectArray();

            public ArrayOf<T> ReadEncodeableArray<T>(string fieldName) where T : IEncodeable, new()
                => Next(fieldName).Value.GetStructureArray<T>(default, Context);

            public ArrayOf<T> ReadEncodeableArray<T>(string fieldName, ExpandedNodeId encodeableTypeId) where T : IEncodeable
                => Next(fieldName).Value.GetStructureArray<T>(default, Context);

            public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string fieldName) where T : IEncodeable
                => Next(fieldName).Value.GetStructureArray<T>(default, Context);

            public MatrixOf<T> ReadEncodeableMatrix<T>(string fieldName, ExpandedNodeId encodeableTypeId) where T : IEncodeable
                => Next(fieldName).Value.GetStructureMatrix<T>(default, Context);

            public MatrixOf<T> ReadEncodeableMatrix<T>(string fieldName) where T : IEncodeable, new()
                => Next(fieldName).Value.GetStructureMatrix<T>(default, Context);

            public ArrayOf<T> ReadEnumeratedArray<T>(string fieldName) where T : struct, Enum
                => Next(fieldName).Value.GetEnumerationArray<T>();

            public ArrayOf<EnumValue> ReadEnumeratedArray(string fieldName)
            {
                Variant value = Next(fieldName).Value;

                if (value.TryGetValue(out ArrayOf<EnumValue> enumValues))
                {
                    return enumValues;
                }

                return EnumValue.From(value.GetInt32Array());
            }

            public Variant ReadVariantValue(string fieldName, TypeInfo typeInfo) => Next(fieldName).Value;

            public uint ReadSwitchField(IList<string> switches, out string fieldName)
            {
                fieldName = null;
                return m_fields.m_switchField;
            }

            public uint ReadEncodingMask(IList<string> masks)
            {
                return m_fields.m_encodingMask;
            }

            public bool HasField(string fieldName)
            {
                foreach (Field field in m_fields.m_fields)
                {
                    if (field.Name == fieldName)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        #endregion
    }
}
