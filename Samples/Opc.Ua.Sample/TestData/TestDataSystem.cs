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
using System.Text;
using System.Threading;
using System.Xml;
using System.IO;
using Opc.Ua;
using Opc.Ua.Server;
using Microsoft.Extensions.Logging;

namespace TestData
{
    public interface ITestDataSystemCallback
    {
        void OnDataChange(
            BaseVariableState variable,
            object value,
            StatusCode statusCode,
            DateTime timestamp);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Sample code preserves existing public API and behavior.")]
    public class TestDataSystem
    {
        public TestDataSystem(ITestDataSystemCallback callback,
                              NamespaceTable namespaceUris,
                              StringTable serverUris,
                              ITelemetryContext telemetry)
        {
            m_callback = callback;
            m_logger = telemetry.CreateLogger<TestDataSystem>();
            m_minimumSamplingInterval = Int32.MaxValue;
            m_monitoredNodes = new Dictionary<uint, BaseVariableState>();
            m_generator = new Opc.Ua.Test.DataGenerator(null, telemetry);
            m_generator.NamespaceUris = namespaceUris;
            m_generator.ServerUris = serverUris;
            m_historyArchive = new HistoryArchive(telemetry);
        }

        /// <summary>
        /// The number of nodes being monitored.
        /// </summary>
        public int MonitoredNodeCount
        {
            get
            {
                lock (m_lock)
                {
                    if (m_monitoredNodes == null)
                    {
                        return 0;
                    }

                    return m_monitoredNodes.Count;
                }
            }
        }

        /// <summary>
        /// Gets or sets the current system status.
        /// </summary>
        public StatusCode SystemStatus
        {
            get
            {
                lock (m_lock)
                {
                    return m_systemStatus;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_systemStatus = value;
                }
            }
        }

        /// <summary>
        /// Creates an archive for the variable.
        /// </summary>
        public void EnableHistoryArchiving(BaseVariableState variable)
        {
            if (variable == null)
            {
                return;
            }

            if (variable.ValueRank == ValueRanks.Scalar)
            {
                m_historyArchive.CreateRecord(variable.NodeId, TypeInfo.GetBuiltInType(variable.DataType));
            }
        }

        /// <summary>
        /// Returns the history file for the variable.
        /// </summary>
        public IHistoryDataSource GetHistoryFile(BaseVariableState variable)
        {
            if (variable == null)
            {
                return null;
            }

            return m_historyArchive.GetHistoryFile(variable.NodeId);
        }

        /// <summary>
        /// Returns a new value for the variable.
        /// </summary>
        public object ReadValue(BaseVariableState variable)
        {
            lock (m_lock)
            {
                switch (variable.NumericId)
                {
                    case TestData.Variables.ScalarValueObjectType_BooleanValue:
                    case TestData.Variables.UserScalarValueObjectType_BooleanValue:
                    {
                        return m_generator.GetRandomBoolean(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_SByteValue:
                    case TestData.Variables.UserScalarValueObjectType_SByteValue:
                    {
                        return m_generator.GetRandomSByte(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_SByteValue:
                    {
                        return (sbyte)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_ByteValue:
                    case TestData.Variables.UserScalarValueObjectType_ByteValue:
                    {
                        return m_generator.GetRandomByte(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_ByteValue:
                    {
                        return (byte)((m_generator.GetRandomUInt32(false) % 201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_Int16Value:
                    case TestData.Variables.UserScalarValueObjectType_Int16Value:
                    {
                        return m_generator.GetRandomInt16(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_Int16Value:
                    {
                        return (short)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_UInt16Value:
                    case TestData.Variables.UserScalarValueObjectType_UInt16Value:
                    {
                        return m_generator.GetRandomUInt16(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_UInt16Value:
                    {
                        return (ushort)((m_generator.GetRandomUInt32(false) % 201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_Int32Value:
                    case TestData.Variables.UserScalarValueObjectType_Int32Value:
                    {
                        return m_generator.GetRandomInt32(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_Int32Value:
                    case TestData.Variables.AnalogScalarValueObjectType_IntegerValue:
                    {
                        return (int)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_UInt32Value:
                    case TestData.Variables.UserScalarValueObjectType_UInt32Value:
                    {
                        return m_generator.GetRandomUInt32(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_UInt32Value:
                    case TestData.Variables.AnalogScalarValueObjectType_UIntegerValue:
                    {
                        return (uint)((m_generator.GetRandomUInt32(false) % 201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_Int64Value:
                    case TestData.Variables.UserScalarValueObjectType_Int64Value:
                    {
                        return m_generator.GetRandomInt64(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_Int64Value:
                    {
                        return (long)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_UInt64Value:
                    case TestData.Variables.UserScalarValueObjectType_UInt64Value:
                    {
                        return m_generator.GetRandomUInt64(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_UInt64Value:
                    {
                        return (ulong)((m_generator.GetRandomUInt32(false) % 201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_FloatValue:
                    case TestData.Variables.UserScalarValueObjectType_FloatValue:
                    {
                        return m_generator.GetRandomFloat(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_FloatValue:
                    {
                        return (float)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_DoubleValue:
                    case TestData.Variables.UserScalarValueObjectType_DoubleValue:
                    {
                        return m_generator.GetRandomDouble(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_DoubleValue:
                    case TestData.Variables.AnalogScalarValueObjectType_NumberValue:
                    {
                        return (double)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_StringValue:
                    case TestData.Variables.UserScalarValueObjectType_StringValue:
                    {
                        return m_generator.GetRandomString(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_DateTimeValue:
                    case TestData.Variables.UserScalarValueObjectType_DateTimeValue:
                    {
                        return m_generator.GetRandomDateTime(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_GuidValue:
                    case TestData.Variables.UserScalarValueObjectType_GuidValue:
                    {
                        return m_generator.GetRandomGuid(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_ByteStringValue:
                    case TestData.Variables.UserScalarValueObjectType_ByteStringValue:
                    {
                        return m_generator.GetRandomByteString(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_XmlElementValue:
                    case TestData.Variables.UserScalarValueObjectType_XmlElementValue:
                    {
                        return m_generator.GetRandomXmlElement(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_NodeIdValue:
                    case TestData.Variables.UserScalarValueObjectType_NodeIdValue:
                    {
                        return m_generator.GetRandomNodeId(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_ExpandedNodeIdValue:
                    case TestData.Variables.UserScalarValueObjectType_ExpandedNodeIdValue:
                    {
                        return m_generator.GetRandomExpandedNodeId(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_QualifiedNameValue:
                    case TestData.Variables.UserScalarValueObjectType_QualifiedNameValue:
                    {
                        return m_generator.GetRandomQualifiedName(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_LocalizedTextValue:
                    case TestData.Variables.UserScalarValueObjectType_LocalizedTextValue:
                    {
                        return m_generator.GetRandomLocalizedText(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_StatusCodeValue:
                    case TestData.Variables.UserScalarValueObjectType_StatusCodeValue:
                    {
                        return m_generator.GetRandomStatusCode(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_VariantValue:
                    case TestData.Variables.UserScalarValueObjectType_VariantValue:
                    {
                        return m_generator.GetRandomVariant(false).AsBoxedObject();
                    }

                    case TestData.Variables.ScalarValueObjectType_StructureValue:
                    {
                        return GetRandomStructure();
                    }

                    case TestData.Variables.ScalarValueObjectType_EnumerationValue:
                    {
                        return m_generator.GetRandomInt32(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_NumberValue:
                    {
                        return m_generator.GetRandomScalar(BuiltInType.Number, false).AsBoxedObject();
                    }

                    case TestData.Variables.ScalarValueObjectType_IntegerValue:
                    {
                        return m_generator.GetRandomScalar(BuiltInType.Integer, false).AsBoxedObject();
                    }

                    case TestData.Variables.ScalarValueObjectType_UIntegerValue:
                    {
                        return m_generator.GetRandomScalar(BuiltInType.UInteger, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_BooleanValue:
                    case TestData.Variables.UserArrayValueObjectType_BooleanValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Boolean, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_SByteValue:
                    case TestData.Variables.UserArrayValueObjectType_SByteValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.SByte, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_SByteValue:
                    {
                        sbyte[] values = ((ArrayOf<sbyte>)m_generator.GetRandomArray(BuiltInType.SByte, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (sbyte)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_ByteValue:
                    case TestData.Variables.UserArrayValueObjectType_ByteValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Byte, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_ByteValue:
                    {
                        byte[] values = ((ArrayOf<byte>)m_generator.GetRandomArray(BuiltInType.Byte, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (byte)((m_generator.GetRandomUInt32(false) % 201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_Int16Value:
                    case TestData.Variables.UserArrayValueObjectType_Int16Value:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Int16, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_Int16Value:
                    {
                        short[] values = ((ArrayOf<short>)m_generator.GetRandomArray(BuiltInType.Int16, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (short)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_UInt16Value:
                    case TestData.Variables.UserArrayValueObjectType_UInt16Value:
                    {
                        return m_generator.GetRandomArray(BuiltInType.UInt16, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_UInt16Value:
                    {
                        ushort[] values = ((ArrayOf<ushort>)m_generator.GetRandomArray(BuiltInType.UInt16, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (ushort)((m_generator.GetRandomUInt32(false) % 201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_Int32Value:
                    case TestData.Variables.UserArrayValueObjectType_Int32Value:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Int32, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_Int32Value:
                    case TestData.Variables.AnalogArrayValueObjectType_IntegerValue:
                    {
                        int[] values = ((ArrayOf<int>)m_generator.GetRandomArray(BuiltInType.Int32, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (int)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_UInt32Value:
                    case TestData.Variables.UserArrayValueObjectType_UInt32Value:
                    {
                        return m_generator.GetRandomArray(BuiltInType.UInt32, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_UInt32Value:
                    case TestData.Variables.AnalogArrayValueObjectType_UIntegerValue:
                    {
                        uint[] values = ((ArrayOf<uint>)m_generator.GetRandomArray(BuiltInType.UInt32, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (uint)((m_generator.GetRandomUInt32(false) % 201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_Int64Value:
                    case TestData.Variables.UserArrayValueObjectType_Int64Value:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Int64, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_Int64Value:
                    {
                        long[] values = ((ArrayOf<long>)m_generator.GetRandomArray(BuiltInType.Int64, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (long)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_UInt64Value:
                    case TestData.Variables.UserArrayValueObjectType_UInt64Value:
                    {
                        return m_generator.GetRandomArray(BuiltInType.UInt64, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_UInt64Value:
                    {
                        ulong[] values = ((ArrayOf<ulong>)m_generator.GetRandomArray(BuiltInType.UInt64, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (ulong)((m_generator.GetRandomUInt32(false) % 201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_FloatValue:
                    case TestData.Variables.UserArrayValueObjectType_FloatValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Float, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_FloatValue:
                    {
                        float[] values = ((ArrayOf<float>)m_generator.GetRandomArray(BuiltInType.Float, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (float)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_DoubleValue:
                    case TestData.Variables.UserArrayValueObjectType_DoubleValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Double, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_DoubleValue:
                    case TestData.Variables.AnalogArrayValueObjectType_NumberValue:
                    {
                        double[] values = ((ArrayOf<double>)m_generator.GetRandomArray(BuiltInType.Double, 100, false, false).AsBoxedObject()).ToArray();

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (double)(((int)(m_generator.GetRandomUInt32(false) % 201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_StringValue:
                    case TestData.Variables.UserArrayValueObjectType_StringValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.String, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_DateTimeValue:
                    case TestData.Variables.UserArrayValueObjectType_DateTimeValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.DateTime, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_GuidValue:
                    case TestData.Variables.UserArrayValueObjectType_GuidValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Guid, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_ByteStringValue:
                    case TestData.Variables.UserArrayValueObjectType_ByteStringValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.ByteString, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_XmlElementValue:
                    case TestData.Variables.UserArrayValueObjectType_XmlElementValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.XmlElement, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_NodeIdValue:
                    case TestData.Variables.UserArrayValueObjectType_NodeIdValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.NodeId, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_ExpandedNodeIdValue:
                    case TestData.Variables.UserArrayValueObjectType_ExpandedNodeIdValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.ExpandedNodeId, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_QualifiedNameValue:
                    case TestData.Variables.UserArrayValueObjectType_QualifiedNameValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.QualifiedName, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_LocalizedTextValue:
                    case TestData.Variables.UserArrayValueObjectType_LocalizedTextValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.LocalizedText, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_StatusCodeValue:
                    case TestData.Variables.UserArrayValueObjectType_StatusCodeValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.StatusCode, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_VariantValue:
                    case TestData.Variables.UserArrayValueObjectType_VariantValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Variant, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_StructureValue:
                    {
                        // the generator has no random extension objects, so the array may be null.
                        object random = m_generator.GetRandomArray(BuiltInType.ExtensionObject, 10, false, false).AsBoxedObject();

                        ExtensionObject[] values = (random is ArrayOf<ExtensionObject> array) ? array.ToArray() : null;

                        for (int ii = 0; values != null && ii < values.Length; ii++)
                        {
                            values[ii] = GetRandomStructure();
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_EnumerationValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Int32, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_NumberValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Number, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_IntegerValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Integer, 100, false, false).AsBoxedObject();
                    }

                    case TestData.Variables.ArrayValueObjectType_UIntegerValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.UInteger, 100, false, false).AsBoxedObject();
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Returns a random structure.
        /// </summary>
        private ExtensionObject GetRandomStructure()
        {
            if (m_generator.GetRandomBoolean())
            {
                ScalarValueDataType value = new ScalarValueDataType();

                value.BooleanValue = m_generator.GetRandomBoolean(false);
                value.SByteValue = m_generator.GetRandomSByte(false);
                value.ByteValue = m_generator.GetRandomByte(false);
                value.Int16Value = m_generator.GetRandomInt16(false);
                value.UInt16Value = m_generator.GetRandomUInt16(false);
                value.Int32Value = m_generator.GetRandomInt32(false);
                value.UInt32Value = m_generator.GetRandomUInt32(false);
                value.Int64Value = m_generator.GetRandomInt64(false);
                value.UInt64Value = m_generator.GetRandomUInt64(false);
                value.FloatValue = m_generator.GetRandomFloat(false);
                value.DoubleValue = m_generator.GetRandomDouble(false);
                value.StringValue = m_generator.GetRandomString(false);
                value.DateTimeValue = m_generator.GetRandomDateTime(false);
                value.GuidValue = m_generator.GetRandomGuid(false);
                value.ByteStringValue = m_generator.GetRandomByteString(false);
                value.XmlElementValue = m_generator.GetRandomXmlElement(false);
                value.NodeIdValue = m_generator.GetRandomNodeId(false);
                value.ExpandedNodeIdValue = m_generator.GetRandomExpandedNodeId(false);
                value.QualifiedNameValue = m_generator.GetRandomQualifiedName(false);
                value.LocalizedTextValue = m_generator.GetRandomLocalizedText(false);
                value.StatusCodeValue = m_generator.GetRandomStatusCode(false);
                value.VariantValue = m_generator.GetRandomVariant(false);

                return new ExtensionObject(value.TypeId, value);
            }
            else
            {
                ArrayValueDataType value = new ArrayValueDataType();

                value.BooleanValue = (ArrayOf<bool>)m_generator.GetRandomArray(BuiltInType.Boolean, 10, false, false);
                value.SByteValue = (ArrayOf<sbyte>)m_generator.GetRandomArray(BuiltInType.SByte, 10, false, false);
                value.ByteValue = (ArrayOf<byte>)m_generator.GetRandomArray(BuiltInType.Byte, 10, false, false);
                value.Int16Value = (ArrayOf<short>)m_generator.GetRandomArray(BuiltInType.Int16, 10, false, false);
                value.UInt16Value = (ArrayOf<ushort>)m_generator.GetRandomArray(BuiltInType.UInt16, 10, false, false);
                value.Int32Value = (ArrayOf<int>)m_generator.GetRandomArray(BuiltInType.Int32, 10, false, false);
                value.UInt32Value = (ArrayOf<uint>)m_generator.GetRandomArray(BuiltInType.UInt32, 10, false, false);
                value.Int64Value = (ArrayOf<long>)m_generator.GetRandomArray(BuiltInType.Int64, 10, false, false);
                value.UInt64Value = (ArrayOf<ulong>)m_generator.GetRandomArray(BuiltInType.UInt64, 10, false, false);
                value.FloatValue = (ArrayOf<float>)m_generator.GetRandomArray(BuiltInType.Float, 10, false, false);
                value.DoubleValue = (ArrayOf<double>)m_generator.GetRandomArray(BuiltInType.Double, 10, false, false);
                value.StringValue = (ArrayOf<string>)m_generator.GetRandomArray(BuiltInType.String, 10, false, false);
                value.DateTimeValue = (ArrayOf<DateTimeUtc>)m_generator.GetRandomArray(BuiltInType.DateTime, 10, false, false);
                value.GuidValue = (ArrayOf<Uuid>)m_generator.GetRandomArray(BuiltInType.Guid, 10, false, false);
                value.ByteStringValue = (ArrayOf<ByteString>)m_generator.GetRandomArray(BuiltInType.ByteString, 10, false, false);
                value.XmlElementValue = (ArrayOf<Opc.Ua.XmlElement>)m_generator.GetRandomArray(BuiltInType.XmlElement, 10, false, false);
                value.NodeIdValue = (ArrayOf<NodeId>)m_generator.GetRandomArray(BuiltInType.NodeId, 10, false, false);
                value.ExpandedNodeIdValue = (ArrayOf<ExpandedNodeId>)m_generator.GetRandomArray(BuiltInType.ExpandedNodeId, 10, false, false);
                value.QualifiedNameValue = (ArrayOf<QualifiedName>)m_generator.GetRandomArray(BuiltInType.QualifiedName, 10, false, false);
                value.LocalizedTextValue = (ArrayOf<LocalizedText>)m_generator.GetRandomArray(BuiltInType.LocalizedText, 10, false, false);
                value.StatusCodeValue = (ArrayOf<StatusCode>)m_generator.GetRandomArray(BuiltInType.StatusCode, 10, false, false);

                value.VariantValue = (ArrayOf<Variant>)m_generator.GetRandomArray(BuiltInType.Variant, 10, false, false);

                return new ExtensionObject(value.TypeId, value);
            }
        }

        public void StartMonitoringValue(uint monitoredItemId, double samplingInterval, BaseVariableState variable)
        {
            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    m_monitoredNodes = new Dictionary<uint, BaseVariableState>();
                }

                m_monitoredNodes[monitoredItemId] = variable;

                SetSamplingInterval(samplingInterval);
            }
        }

        public void SetSamplingInterval(double samplingInterval)
        {
            lock (m_lock)
            {
                if (samplingInterval < 0)
                {
                    // m_samplingEvent.Set();
                    m_minimumSamplingInterval = Int32.MaxValue;

                    if (m_timer != null)
                    {
                        m_timer.Dispose();
                        m_timer = null;
                    }

                    return;
                }

                if (m_minimumSamplingInterval > samplingInterval)
                {
                    m_minimumSamplingInterval = (int)samplingInterval;

                    if (m_minimumSamplingInterval < 100)
                    {
                        m_minimumSamplingInterval = 100;
                    }

                    if (m_timer != null)
                    {
                        m_timer.Dispose();
                        m_timer = null;
                    }

                    m_timer = new Timer(DoSample, null, m_minimumSamplingInterval, m_minimumSamplingInterval);
                }
            }
        }

        void DoSample(object state)
        {
            if (m_logger.IsEnabled(LogLevel.Trace))
            {
                m_logger.LogTrace("DoSample HiRes={HiResNow:ss.ffff} Now={Now:ss.ffff}", DateTime.UtcNow, DateTime.UtcNow);
            }

            Queue<Sample> samples = new Queue<Sample>();

            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    return;
                }

                foreach (BaseVariableState variable in m_monitoredNodes.Values)
                {
                    Sample sample = new Sample();

                    sample.Variable = variable;
                    sample.Value = ReadValue(sample.Variable);
                    sample.StatusCode = StatusCodes.Good;
                    sample.Timestamp = DateTime.UtcNow;

                    samples.Enqueue(sample);
                }
            }

            while (samples.Count > 0)
            {
                Sample sample = samples.Dequeue();

                m_callback.OnDataChange(
                    sample.Variable,
                    sample.Value,
                    sample.StatusCode,
                    sample.Timestamp);
            }
        }

        public void StopMonitoringValue(uint monitoredItemId)
        {
            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    return;
                }

                m_monitoredNodes.Remove(monitoredItemId);

                if (m_monitoredNodes.Count == 0)
                {
                    SetSamplingInterval(-1);
                }
            }
        }

        private class Sample
        {
            public BaseVariableState Variable;
            public object Value;
            public StatusCode StatusCode;
            public DateTime Timestamp;
        }

        #region Private Fields
        private object m_lock = new object();
        private ITestDataSystemCallback m_callback;
        private readonly ILogger m_logger;
        private Opc.Ua.Test.DataGenerator m_generator;
        private int m_minimumSamplingInterval;
        private Dictionary<uint, BaseVariableState> m_monitoredNodes;
        private Timer m_timer;
        private StatusCode m_systemStatus;
        private HistoryArchive m_historyArchive;
        #endregion
    }
}
