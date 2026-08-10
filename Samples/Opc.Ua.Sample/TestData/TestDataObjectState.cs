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
using Opc.Ua;
using Range = Opc.Ua.Range;

namespace TestData
{
    public partial class TestDataObjectState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, System.Threading.CancellationToken ct)
        {
            base.OnAfterCreate(context, node, ct);

            GenerateValues.OnCall = OnGenerateValues;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Initialzies the variable as a counter.
        /// </summary>
        protected void InitializeVariable(ISystemContext context, BaseVariableState variable, uint numericId)
        {
            variable.NumericId = numericId;

            // provide an implementation that produces a random value on each read.
            if (SimulationActive.Value)
            {
                variable.OnReadValue = DoDeviceRead;
            }

            // set a valid initial value.
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system != null)
            {
                GenerateValue(system, variable);
            }

            // allow writes if the simulation is not active.
            if (!SimulationActive.Value)
            {
                variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            // set the EU range.
            BaseVariableState euRange = variable.FindChild(context, new QualifiedName(Opc.Ua.BrowseNames.EURange)) as BaseVariableState;

            if (euRange != null)
            {
                if (context.TypeTable.IsTypeOf(variable.DataType, Opc.Ua.DataTypeIds.UInteger))
                {
                    euRange.Value = new Variant(new ExtensionObject(new Range(250, 50), false));
                }
                else
                {
                    euRange.Value = new Variant(new ExtensionObject(new Range(100, -100), false));
                }
            }

            variable.OnSimpleWriteValue = OnWriteAnalogValue;
        }

        /// <summary>
        /// Validates a written value.
        /// </summary>
        public ServiceResult OnWriteAnalogValue(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            try
            {

                BaseVariableState euRange = node.FindChild(context, new QualifiedName(Opc.Ua.BrowseNames.EURange)) as BaseVariableState;

                if (euRange == null)
                {
                    return ServiceResult.Good;
                }

                Range range = euRange.Value.AsBoxedObject() as Range;

                if (range == null)
                {
                    return ServiceResult.Good;
                }

                Array array = value.AsBoxedObject() as Array;

                if (array != null)
                {
                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        object element = array.GetValue(ii);

                        if (typeof(Variant).IsInstanceOfType(element))
                        {
                            element = ((Variant)element).AsBoxedObject();
                        }

                        double elementNumber = Convert.ToDouble(element);

                        if (elementNumber > range.High || elementNumber < range.Low)
                        {
                            return StatusCodes.BadOutOfRange;
                        }
                    }

                    return ServiceResult.Good;
                }

                double number = Convert.ToDouble(value.AsBoxedObject());

                if (number > range.High || number < range.Low)
                {
                    return StatusCodes.BadOutOfRange;
                }

                return ServiceResult.Good;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Generates a new value for the variable.
        /// </summary>
        protected void GenerateValue(TestDataSystem system, BaseVariableState variable)
        {
            variable.Value = ToVariant(system.ReadValue(variable));
            variable.Timestamp = DateTime.UtcNow;
            variable.StatusCode = StatusCodes.Good;
        }

        private static Variant ToVariant(object value)
        {
            switch (value)
            {
                case null: return Variant.Null;
                case Variant v: return v;
                case bool v: return Variant.From(v);
                case sbyte v: return Variant.From(v);
                case byte v: return Variant.From(v);
                case short v: return Variant.From(v);
                case ushort v: return Variant.From(v);
                case int v: return Variant.From(v);
                case uint v: return Variant.From(v);
                case long v: return Variant.From(v);
                case ulong v: return Variant.From(v);
                case float v: return Variant.From(v);
                case double v: return Variant.From(v);
                case string v: return Variant.From(v);
                case DateTime v: return Variant.From(new DateTimeUtc(v));
                case Guid v: return Variant.From(new Uuid(v));
                case ByteString v: return Variant.From(v);
                case byte[] v: return Variant.From(v.ToByteString());
                case XmlElement v: return Variant.From(v);
                case NodeId v: return Variant.From(v);
                case ExpandedNodeId v: return Variant.From(v);
                case StatusCode v: return Variant.From(v);
                case QualifiedName v: return Variant.From(v);
                case LocalizedText v: return Variant.From(v);
                case ExtensionObject v: return Variant.From(v);
                case bool[] v: return Variant.From(new ArrayOf<bool>(v));
                case sbyte[] v: return Variant.From(new ArrayOf<sbyte>(v));
                case short[] v: return Variant.From(new ArrayOf<short>(v));
                case ushort[] v: return Variant.From(new ArrayOf<ushort>(v));
                case int[] v: return Variant.From(new ArrayOf<int>(v));
                case uint[] v: return Variant.From(new ArrayOf<uint>(v));
                case long[] v: return Variant.From(new ArrayOf<long>(v));
                case ulong[] v: return Variant.From(new ArrayOf<ulong>(v));
                case float[] v: return Variant.From(new ArrayOf<float>(v));
                case double[] v: return Variant.From(new ArrayOf<double>(v));
                case string[] v: return Variant.From(new ArrayOf<string>(v));
                case DateTime[] v: return Variant.From(new ArrayOf<DateTimeUtc>(Array.ConvertAll(v, x => new DateTimeUtc(x))));
                case Guid[] v: return Variant.From(new ArrayOf<Uuid>(Array.ConvertAll(v, x => new Uuid(x))));
                case ByteString[] v: return Variant.From(new ArrayOf<ByteString>(v));
                case XmlElement[] v: return Variant.From(new ArrayOf<XmlElement>(v));
                case NodeId[] v: return Variant.From(new ArrayOf<NodeId>(v));
                case ExpandedNodeId[] v: return Variant.From(new ArrayOf<ExpandedNodeId>(v));
                case StatusCode[] v: return Variant.From(new ArrayOf<StatusCode>(v));
                case QualifiedName[] v: return Variant.From(new ArrayOf<QualifiedName>(v));
                case LocalizedText[] v: return Variant.From(new ArrayOf<LocalizedText>(v));
                case ExtensionObject[] v: return Variant.From(new ArrayOf<ExtensionObject>(v));
                case IEncodeable v: return Variant.From(new ExtensionObject(v, false));
                default: return Variant.Null;
            }
        }

        /// <summary>
        /// Handles the generate values method.
        /// </summary>
        protected virtual ServiceResult OnGenerateValues(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint count)
        {
            ClearChangeMasks(context, true);

            if (AreEventsMonitored)
            {
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                GenerateValuesEventState e = new GenerateValuesEventState(null);
                #pragma warning restore CA2000

                TranslationInfo message = new TranslationInfo(
                    "GenerateValuesEventType",
                    "en-US",
                    "New values generated for test source '{0}'.",
                    this.DisplayName);

                e.Initialize(
                    context,
                    this,
                    EventSeverity.MediumLow,
                    new LocalizedText(message));

                e.Iterations = PropertyState<uint>.With<VariantBuilder>(e);
                e.Iterations.Value = count;

                e.NewValueCount = PropertyState<uint>.With<VariantBuilder>(e);
                e.NewValueCount.Value = 10;

                ReportEvent(context, e);
            }

#if CONDITION_SAMPLES
            this.CycleComplete.RequestAcknowledgement(context, (ushort)EventSeverity.Low);
#endif

            return ServiceResult.Good;
        }

        /// <summary>
        /// Generates a new value each time the value is read.
        /// </summary>
        private ServiceResult DoDeviceRead(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            BaseVariableState variable = node as BaseVariableState;

            if (variable == null)
            {
                return ServiceResult.Good;
            }

            if (!SimulationActive.Value)
            {
                return ServiceResult.Good;
            }

            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system == null)
            {
                return StatusCodes.BadOutOfService;
            }

            try
            {
                value = ToVariant(system.ReadValue(variable));

                statusCode = StatusCodes.Good;
                timestamp = DateTime.UtcNow;

                ServiceResult error = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context,
                    indexRange,
                    dataEncoding,
                    ref value);

                if (ServiceResult.IsBad(error))
                {
                    statusCode = error.StatusCode;
                }

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                return new ServiceResult(e);
            }
        }
        #endregion
    }
}
