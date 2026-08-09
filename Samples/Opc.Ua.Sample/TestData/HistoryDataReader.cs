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
using System.Xml;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// A class used to read values from a history data source.
    /// </summary>
    public class HistoryDataReader : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Constructs a reader for the source.
        /// </summary>
        /// <param name="source">The source of the history data.</param>
        public HistoryDataReader(NodeId variableId, IHistoryDataSource source)
        {
            m_id = Guid.NewGuid();
            m_variableId = variableId;
            m_source = source;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // nothing to do.
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// A globally unique identifier for the instance.
        /// </summary>
        public Guid Id
        {
            get { return m_id; }
        }

        /// <summary>
        /// The identifier for the variable being read.
        /// </summary>
        public NodeId VariableId
        {
            get { return m_variableId; }
        }

        /// <summary>
        /// Starts reading raw values.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        /// <param name="request">The request parameters.</param>
        /// <param name="timestampsToReturn">The timestamps to return with the value.</param>
        /// <param name="indexRange">The range to return for array values.</param>
        /// <param name="dataEncoding">The data encoding to use for structured values.</param>
        /// <param name="values">The values to return.</param>
        public void BeginReadRaw(
            ServerSystemContext context,
            ReadRawModifiedDetails request,
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            List<DataValue> values)
        {
            m_request = request;

            // initialize start and end.
            m_startTime = (DateTime)m_request.StartTime;
            m_endTime = (DateTime)m_request.EndTime;

            if (m_endTime == DateTime.MinValue)
            {
                m_endTime = DateTime.MaxValue;
            }

            // check the direction.
            m_isForward = m_startTime < m_endTime;
            m_position = -1;

            DataValue value = DataValue.Null;

            // get first bound.
            if (m_request.ReturnBounds)
            {
                value = m_source.FirstRaw(m_startTime, !m_isForward, m_request.IsReadModified, out m_position);

                if (!value.IsNull)
                {
                    AddValue(timestampsToReturn, indexRange, dataEncoding, values, value);
                }
            }
        }

        /// <summary>
        /// Continues a read raw operation.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        /// <param name="timestampsToReturn">The timestamps to return with the value.</param>
        /// <param name="indexRange">The range to return for array values.</param>
        /// <param name="dataEncoding">The data encoding to use for structured values.</param>
        /// <param name="values">The values to return.</param>
        /// <returns>False if the operation halted because the maximum number of values was discovered.</returns>
        public bool NextReadRaw(
            ServerSystemContext context,
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            List<DataValue> values)
        {
            DataValue value = DataValue.Null;

            do
            {
                // check for limit.
                if (m_request.NumValuesPerNode > 0 && values.Count >= m_request.NumValuesPerNode)
                {
                    return false;
                }

                value = m_source.NextRaw(m_lastTime, m_isForward, m_request.IsReadModified, ref m_position);

                // no more data.
                if (value.IsNull)
                {
                    return true;
                }

                // check for bound.
                if ((m_isForward && value.ServerTimestamp >= m_endTime) || (!m_isForward && value.ServerTimestamp <= m_endTime))
                {
                    if (m_request.ReturnBounds)
                    {
                        AddValue(timestampsToReturn, indexRange, dataEncoding, values, value);
                        return true;
                    }
                }

                // add value.
                AddValue(timestampsToReturn, indexRange, dataEncoding, values, value);
            }
            #pragma warning disable CA1508 // Justification: Sample code retains existing ownership/lifetime and behavior.
            while (!value.IsNull);
            #pragma warning restore CA1508

            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds a DataValue to a list of values to return.
        /// </summary>
        private void AddValue(
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            List<DataValue> values,
            DataValue value)
        {
            // ignore invalid case.
            if (value.IsNull)
            {
                return;
            }

            // save the last timestamp returned.
            m_lastTime = (DateTime)value.ServerTimestamp;

            Variant wrappedValue = value.WrappedValue;
            StatusCode statusCode = value.StatusCode;
            DateTimeUtc sourceTimestamp = value.SourceTimestamp;
            DateTimeUtc serverTimestamp = value.ServerTimestamp;

            // check if the index range or data encoding can be applied.
            if (StatusCode.IsGood(value.StatusCode))
            {
                Variant valueToReturn = value.WrappedValue;

                // apply the index range.
                if (!indexRange.IsNull)
                {
                    StatusCode error = indexRange.ApplyRange(ref valueToReturn);

                    if (StatusCode.IsBad(error))
                    {
                        wrappedValue = Variant.Null;
                        statusCode = error;
                    }
                    else
                    {
                        wrappedValue = valueToReturn;
                    }
                }

                // apply the data encoding.
                if (!QualifiedName.IsNull(dataEncoding))
                {
                    wrappedValue = Variant.Null;
                    statusCode = StatusCodes.BadDataEncodingUnsupported;
                }
            }

            // apply the timestamps filter.
            if (timestampsToReturn == TimestampsToReturn.Neither || timestampsToReturn == TimestampsToReturn.Server)
            {
                sourceTimestamp = DateTime.MinValue;
            }

            if (timestampsToReturn == TimestampsToReturn.Neither || timestampsToReturn == TimestampsToReturn.Source)
            {
                serverTimestamp = DateTime.MinValue;
            }

            // add result.
            values.Add(new DataValue(
                wrappedValue,
                statusCode,
                sourceTimestamp,
                serverTimestamp,
                value.SourcePicoseconds,
                value.ServerPicoseconds));
        }
        #endregion

        #region Private Fields
        private Guid m_id;
        private NodeId m_variableId;
        private IHistoryDataSource m_source;
        private ReadRawModifiedDetails m_request;
        private DateTime m_startTime;
        private DateTime m_endTime;
        private bool m_isForward;
        private int m_position;
        private DateTime m_lastTime;
        #endregion
    }
}
