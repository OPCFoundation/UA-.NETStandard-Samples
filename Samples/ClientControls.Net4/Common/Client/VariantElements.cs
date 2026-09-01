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
    /// Provides type safe access to the elements of array and matrix Variants.
    /// The value editors use it to list, edit and rebuild array values without
    /// boxing them into CLR arrays.
    /// </summary>
    public static class VariantElements
    {
        /// <summary>
        /// Lifts the elements of an array or matrix Variant into one scalar
        /// Variant per element, in row-major order. Reports the dimensions of
        /// the value: one entry holding the length for a one dimensional
        /// array, one entry per dimension for a matrix.
        /// </summary>
        public static bool TryGetElements(in Variant value, out ArrayOf<Variant> elements, out int[] dimensions)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean: return TryLift<bool>(value, BuiltInType.Boolean, Variant.From, out elements, out dimensions);
                case BuiltInType.SByte: return TryLift<sbyte>(value, BuiltInType.SByte, Variant.From, out elements, out dimensions);
                case BuiltInType.Byte: return TryLift<byte>(value, BuiltInType.Byte, Variant.From, out elements, out dimensions);
                case BuiltInType.Int16: return TryLift<short>(value, BuiltInType.Int16, Variant.From, out elements, out dimensions);
                case BuiltInType.UInt16: return TryLift<ushort>(value, BuiltInType.UInt16, Variant.From, out elements, out dimensions);
                case BuiltInType.Int32: return TryLift<int>(value, BuiltInType.Int32, Variant.From, out elements, out dimensions);
                case BuiltInType.UInt32: return TryLift<uint>(value, BuiltInType.UInt32, Variant.From, out elements, out dimensions);
                case BuiltInType.Int64: return TryLift<long>(value, BuiltInType.Int64, Variant.From, out elements, out dimensions);
                case BuiltInType.UInt64: return TryLift<ulong>(value, BuiltInType.UInt64, Variant.From, out elements, out dimensions);
                case BuiltInType.Float: return TryLift<float>(value, BuiltInType.Float, Variant.From, out elements, out dimensions);
                case BuiltInType.Double: return TryLift<double>(value, BuiltInType.Double, Variant.From, out elements, out dimensions);
                case BuiltInType.String: return TryLift<string>(value, BuiltInType.String, Variant.From, out elements, out dimensions);
                case BuiltInType.DateTime: return TryLift<DateTimeUtc>(value, BuiltInType.DateTime, Variant.From, out elements, out dimensions);
                case BuiltInType.Guid: return TryLift<Uuid>(value, BuiltInType.Guid, Variant.From, out elements, out dimensions);
                case BuiltInType.ByteString: return TryLift<ByteString>(value, BuiltInType.ByteString, Variant.From, out elements, out dimensions);
                case BuiltInType.XmlElement: return TryLift<XmlElement>(value, BuiltInType.XmlElement, Variant.From, out elements, out dimensions);
                case BuiltInType.NodeId: return TryLift<NodeId>(value, BuiltInType.NodeId, Variant.From, out elements, out dimensions);
                case BuiltInType.ExpandedNodeId: return TryLift<ExpandedNodeId>(value, BuiltInType.ExpandedNodeId, Variant.From, out elements, out dimensions);
                case BuiltInType.StatusCode: return TryLift<StatusCode>(value, BuiltInType.StatusCode, Variant.From, out elements, out dimensions);
                case BuiltInType.QualifiedName: return TryLift<QualifiedName>(value, BuiltInType.QualifiedName, Variant.From, out elements, out dimensions);
                case BuiltInType.LocalizedText: return TryLift<LocalizedText>(value, BuiltInType.LocalizedText, Variant.From, out elements, out dimensions);
                case BuiltInType.ExtensionObject: return TryLift<ExtensionObject>(value, BuiltInType.ExtensionObject, Variant.From, out elements, out dimensions);
                case BuiltInType.DataValue: return TryLift<DataValue>(value, BuiltInType.DataValue, Variant.From, out elements, out dimensions);
                case BuiltInType.Variant:
                {
                    // a variant array already holds one Variant per element.
                    return TryLift<Variant>(value, BuiltInType.Variant, v => v, out elements, out dimensions);
                }

                case BuiltInType.Enumeration:
                {
                    // enumeration arrays are stored either as EnumValue elements
                    // (created from a CLR enum) or as raw Int32 elements.
                    if (TryLift<EnumValue>(value, BuiltInType.Enumeration, Variant.From, out elements, out dimensions))
                    {
                        return true;
                    }

                    return TryLift<int>(value, BuiltInType.Enumeration, Variant.From, out elements, out dimensions);
                }
            }

            elements = default;
            dimensions = null;
            return false;
        }

        /// <summary>
        /// Rebuilds an array or matrix Variant from scalar element Variants in
        /// row-major order. Each element is converted to the element type. A
        /// single entry in <paramref name="dimensions"/> produces a one
        /// dimensional array, multiple entries produce a matrix.
        /// </summary>
        public static Variant CreateFromElements(BuiltInType elementType, IReadOnlyList<Variant> elements, int[] dimensions)
        {
            switch (elementType)
            {
                case BuiltInType.Boolean: return Lower(elements, dimensions, v => v.GetBoolean(), Variant.From, Variant.From);
                case BuiltInType.SByte: return Lower(elements, dimensions, v => v.GetSByte(), Variant.From, Variant.From);
                case BuiltInType.Byte: return Lower(elements, dimensions, v => v.GetByte(), Variant.From, Variant.From);
                case BuiltInType.Int16: return Lower(elements, dimensions, v => v.GetInt16(), Variant.From, Variant.From);
                case BuiltInType.UInt16: return Lower(elements, dimensions, v => v.GetUInt16(), Variant.From, Variant.From);
                case BuiltInType.Int32: return Lower(elements, dimensions, v => v.GetInt32(), Variant.From, Variant.From);
                case BuiltInType.UInt32: return Lower(elements, dimensions, v => v.GetUInt32(), Variant.From, Variant.From);
                case BuiltInType.Int64: return Lower(elements, dimensions, v => v.GetInt64(), Variant.From, Variant.From);
                case BuiltInType.UInt64: return Lower(elements, dimensions, v => v.GetUInt64(), Variant.From, Variant.From);
                case BuiltInType.Float: return Lower(elements, dimensions, v => v.GetFloat(), Variant.From, Variant.From);
                case BuiltInType.Double: return Lower(elements, dimensions, v => v.GetDouble(), Variant.From, Variant.From);
                case BuiltInType.String: return Lower(elements, dimensions, v => v.GetString(), Variant.From, Variant.From);
                case BuiltInType.DateTime: return Lower(elements, dimensions, v => v.GetDateTime(), Variant.From, Variant.From);
                case BuiltInType.Guid: return Lower(elements, dimensions, v => v.GetGuid(), Variant.From, Variant.From);
                case BuiltInType.ByteString: return Lower(elements, dimensions, v => v.GetByteString(), Variant.From, Variant.From);
                case BuiltInType.XmlElement: return Lower(elements, dimensions, v => v.GetXmlElement(), Variant.From, Variant.From);
                case BuiltInType.NodeId: return Lower(elements, dimensions, v => v.GetNodeId(), Variant.From, Variant.From);
                case BuiltInType.ExpandedNodeId: return Lower(elements, dimensions, v => v.GetExpandedNodeId(), Variant.From, Variant.From);
                case BuiltInType.StatusCode: return Lower(elements, dimensions, v => v.GetStatusCode(), Variant.From, Variant.From);
                case BuiltInType.QualifiedName: return Lower(elements, dimensions, v => v.GetQualifiedName(), Variant.From, Variant.From);
                case BuiltInType.LocalizedText: return Lower(elements, dimensions, v => v.GetLocalizedText(), Variant.From, Variant.From);
                case BuiltInType.ExtensionObject: return Lower(elements, dimensions, v => v.GetExtensionObject(), Variant.From, Variant.From);
                case BuiltInType.DataValue: return Lower(elements, dimensions, v => v.GetDataValue(), Variant.From, Variant.From);
                case BuiltInType.Variant: return Lower(elements, dimensions, v => v, Variant.From, Variant.From);
                case BuiltInType.Enumeration: return Lower(elements, dimensions, v => v.GetEnumeration(), Variant.From, Variant.From);
                default:
                {
                    throw new NotSupportedException($"Cannot create an array of {elementType}.");
                }
            }
        }

        /// <summary>
        /// Returns a Variant holding a default value for the type info: the
        /// stack's default scalar for scalar types (see
        /// <see cref="TypeInfo.GetDefaultVariantValue(BuiltInType)"/>), an
        /// empty array for array types and an empty matrix for higher ranks.
        /// </summary>
        public static Variant CreateDefault(TypeInfo typeInfo)
        {
            if (typeInfo.IsUnknown || typeInfo.BuiltInType == BuiltInType.Variant)
            {
                return Variant.Null;
            }

            if (typeInfo.ValueRank < 0)
            {
                return TypeInfo.GetDefaultVariantValue(typeInfo.BuiltInType);
            }

            int[] dimensions = typeInfo.ValueRank <= 1 ? new int[1] : new int[typeInfo.ValueRank];
            return CreateFromElements(typeInfo.BuiltInType, Array.Empty<Variant>(), dimensions);
        }

        /// <summary>
        /// Lifts every element of the array or matrix held by the Variant into
        /// its own Variant using the supplied conversion.
        /// </summary>
        private static bool TryLift<T>(
            in Variant value,
            BuiltInType builtInType,
            Converter<T, Variant> from,
            out ArrayOf<Variant> elements,
            out int[] dimensions)
        {
            if (value.TryGetMatrix(out MatrixOf<T> matrix, builtInType))
            {
                dimensions = matrix.Dimensions;

                var lifted = new Variant[matrix.Count];
                int ii = 0;

                foreach (T element in matrix)
                {
                    lifted[ii++] = from(element);
                }

                elements = lifted;
                return true;
            }

            elements = default;
            dimensions = null;
            return false;
        }

        /// <summary>
        /// Rebuilds a typed array or matrix Variant from the element Variants
        /// using the supplied conversions.
        /// </summary>
        private static Variant Lower<T>(
            IReadOnlyList<Variant> elements,
            int[] dimensions,
            Converter<Variant, T> get,
            Converter<ArrayOf<T>, Variant> fromArray,
            Converter<MatrixOf<T>, Variant> fromMatrix)
        {
            var values = new T[elements.Count];

            for (int ii = 0; ii < elements.Count; ii++)
            {
                values[ii] = get(elements[ii]);
            }

            ArrayOf<T> array = values;

            if (dimensions != null && dimensions.Length > 1)
            {
                return fromMatrix(array.ToMatrix(dimensions));
            }

            return fromArray(array);
        }
    }
}
