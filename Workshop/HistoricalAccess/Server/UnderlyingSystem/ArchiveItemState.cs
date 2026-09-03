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
using System.Data;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Stores the metadata for a node representing an item in the archive.
    /// </summary>
    public class ArchiveItemState : Opc.Ua.DataItemState
    {
        /// <summary>
        /// Creates a new instance of a item.
        /// </summary>
        public ArchiveItemState(ISystemContext context, ArchiveItem item, ushort namespaceIndex)
        :
            base(null)
        {
            m_archiveItem = item;

            this.TypeDefinitionId = VariableTypeIds.DataItemType;
            this.SymbolicName = m_archiveItem.Name;
            this.NodeId = ConstructId(m_archiveItem.UniquePath, namespaceIndex);
            this.BrowseName = new QualifiedName(m_archiveItem.Name, namespaceIndex);
            this.DisplayName = new LocalizedText(this.BrowseName.Name);
            this.Description = LocalizedText.Null;
            this.WriteMask = 0;
            this.UserWriteMask = 0;
            this.DataType = DataTypeIds.BaseDataType;
            this.ValueRank = ValueRanks.Scalar;
            this.AccessLevel = AccessLevels.HistoryReadOrWrite | AccessLevels.CurrentRead;
            this.UserAccessLevel = AccessLevels.HistoryReadOrWrite | AccessLevels.CurrentRead;
            this.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            this.Historizing = true;

            m_annotations = PropertyState<Annotation>.With<StructureBuilder<Annotation>>(this);
            m_annotations.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            m_annotations.TypeDefinitionId = VariableTypeIds.PropertyType;
            m_annotations.SymbolicName = Opc.Ua.BrowseNames.Annotations;
            m_annotations.BrowseName = new QualifiedName(Opc.Ua.BrowseNames.Annotations);
            m_annotations.DisplayName = new LocalizedText(m_annotations.BrowseName.Name);
            m_annotations.Description = LocalizedText.Null;
            m_annotations.WriteMask = 0;
            m_annotations.UserWriteMask = 0;
            m_annotations.DataType = DataTypeIds.Annotation;
            m_annotations.ValueRank = ValueRanks.Scalar;
            m_annotations.AccessLevel = AccessLevels.HistoryReadOrWrite;
            m_annotations.UserAccessLevel = AccessLevels.HistoryReadOrWrite;
            m_annotations.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            m_annotations.Historizing = false;
            this.AddChild(m_annotations);

            m_annotations.NodeId = NodeTypes.ConstructIdForComponent(m_annotations, namespaceIndex);

            m_configuration = new HistoricalDataConfigurationState(this);
            m_configuration.MaxTimeInterval = PropertyState<double>.With<VariantBuilder>(m_configuration);
            m_configuration.MinTimeInterval = PropertyState<double>.With<VariantBuilder>(m_configuration);
            m_configuration.StartOfArchive = PropertyState<DateTimeUtc>.With<VariantBuilder>(m_configuration);
            m_configuration.StartOfOnlineArchive = PropertyState<DateTimeUtc>.With<VariantBuilder>(m_configuration);

            m_configuration.Create(
                context,
                NodeId.Null,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                LocalizedText.Null,
                true);

            m_configuration.SymbolicName = Opc.Ua.BrowseNames.HAConfiguration;
            m_configuration.ReferenceTypeId = ReferenceTypeIds.HasHistoricalConfiguration;

            this.AddChild(m_configuration);
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        public void LoadConfiguration(ISystemContext context, ITelemetryContext telemetry)
        {
            DataFileReader reader = new DataFileReader();

            if (reader.LoadConfiguration(context, m_archiveItem, telemetry))
            {
                this.DataType = TypeInfo.GetDataTypeId(new TypeInfo(m_archiveItem.DataType, ValueRanks.Scalar));
                this.ValueRank = m_archiveItem.ValueRank;
                this.Historizing = m_archiveItem.Archiving;

                m_configuration.MinTimeInterval.Value = m_archiveItem.SamplingInterval;
                m_configuration.MaxTimeInterval.Value = m_archiveItem.SamplingInterval;
                m_configuration.Stepped.Value = m_archiveItem.Stepped;

                AggregateConfiguration configuration = m_archiveItem.AggregateConfiguration;
                m_configuration.AggregateConfiguration.PercentDataGood.Value = configuration.PercentDataGood;
                m_configuration.AggregateConfiguration.PercentDataBad.Value = configuration.PercentDataBad;
                m_configuration.AggregateConfiguration.UseSlopedExtrapolation.Value = configuration.UseSlopedExtrapolation;
                m_configuration.AggregateConfiguration.TreatUncertainAsBad.Value = configuration.TreatUncertainAsBad;
            }
        }

        /// <summary>
        /// Loads the data.
        /// </summary>
        public void ReloadFromSource(ISystemContext context, ITelemetryContext telemetry)
        {
            LoadConfiguration(context, telemetry);

            if (m_archiveItem.LastLoadTime == DateTime.MinValue || (m_archiveItem.Persistent && m_archiveItem.LastLoadTime.AddSeconds(10) < DateTime.UtcNow))
            {
                DataFileReader reader = new DataFileReader();
                reader.LoadHistoryData(context, m_archiveItem);

                // set the start of the archive.
                if (m_archiveItem.DataSet.Tables[0].DefaultView.Count > 0)
                {
                    m_configuration.StartOfArchive.Value = (DateTime)m_archiveItem.DataSet.Tables[0].DefaultView[0].Row[0];
                    m_configuration.StartOfOnlineArchive.Value = m_configuration.StartOfArchive.Value;
                }

                if (m_archiveItem.Archiving)
                {
                    // save the pattern used to produce new data.
                    m_pattern = new List<DataValue>();

                    foreach (DataRowView row in m_archiveItem.DataSet.Tables[0].DefaultView)
                    {
                        DataValue value = (DataValue)row.Row[2];
                        m_pattern.Add(value);
                        m_nextSampleTime = ((DateTime)value.SourceTimestamp).AddMilliseconds(m_archiveItem.SamplingInterval);
                    }

                    // fill in data until the present time.
                    m_patternIndex = 0;
                    NewSamples(context);
                }
            }


        }

        /// <summary>
        /// Creates a new sample.
        /// </summary>
        public IList<DataValue> NewSamples(ISystemContext context)
        {
            List<DataValue> newSamples = new List<DataValue>();

            while (m_pattern != null && m_nextSampleTime < DateTime.UtcNow)
            {
                DataValue value = new DataValue(
                    m_pattern[m_patternIndex].WrappedValue,
                    m_pattern[m_patternIndex].StatusCode,
                    m_nextSampleTime,
                    m_nextSampleTime);
                m_nextSampleTime = ((DateTime)value.SourceTimestamp).AddMilliseconds(m_archiveItem.SamplingInterval);
                newSamples.Add(value);

                DataRow row = m_archiveItem.DataSet.Tables[0].NewRow();

                row[0] = (DateTime)value.SourceTimestamp;
                row[1] = (DateTime)value.ServerTimestamp;
                row[2] = value;
                row[3] = value.WrappedValue.TypeInfo.BuiltInType;
                row[4] = value.WrappedValue.TypeInfo.ValueRank;

                m_archiveItem.DataSet.Tables[0].Rows.Add(row);
                m_patternIndex = (m_patternIndex + 1) % m_pattern.Count;
            }

            m_archiveItem.DataSet.AcceptChanges();
            return newSamples;
        }

        /// <summary>
        /// Commits everything written to the archive since the last commit.
        /// </summary>
        /// <remarks>
        /// Every update accepts its own changes unless the caller asked it not to,
        /// which is how a batch of them is made to succeed or fail as a whole: the
        /// caller commits once at the end, or discards the lot with
        /// <see cref="RollbackChanges"/>.
        /// </remarks>
        public void CommitChanges()
        {
            m_archiveItem.DataSet.AcceptChanges();
        }

        /// <summary>
        /// Discards everything written to the archive since the last commit.
        /// </summary>
        public void RollbackChanges()
        {
            m_archiveItem.DataSet.RejectChanges();
        }

        /// <summary>
        /// Updates the history.
        /// </summary>
        /// <param name="context">The context of the operation.</param>
        /// <param name="value">The value to insert or replace.</param>
        /// <param name="performUpdateType">Whether the value may be inserted, replaced or both.</param>
        /// <param name="commit">
        /// Whether to commit the change. A caller which applies a batch of values
        /// atomically passes false and commits or rolls the whole batch back itself.
        /// </param>
        public uint UpdateHistory(ServerSystemContext context, DataValue value, PerformUpdateType performUpdateType, bool commit = true)
        {
            bool replaced = false;

            if (performUpdateType == PerformUpdateType.Remove)
            {
                return StatusCodes.BadNotSupported.Code;
            }

            if (StatusCode.IsNotBad(value.StatusCode))
            {
                TypeInfo typeInfo = value.WrappedValue.TypeInfo;

                if (typeInfo.IsUnknown || typeInfo.BuiltInType != m_archiveItem.DataType || typeInfo.ValueRank != ValueRanks.Scalar)
                {
                    return StatusCodes.BadTypeMismatch.Code;
                }
            }

            // the sorted view compares full ticks, unlike a row filter whose date
            // literal is only precise to the second.
            DataRowView[] matches = m_archiveItem.DataSet.Tables[0].DefaultView.FindRows((DateTime)value.SourceTimestamp);

            DataRow row = null;

            if (matches.Length > 0)
            {
                if (performUpdateType == PerformUpdateType.Insert)
                {
                    return StatusCodes.BadEntryExists.Code;
                }

                // add record indicating it was replaced.
                AddModificationRecord(context, matches[0].Row, HistoryUpdateType.Replace);

                replaced = true;
                row = matches[0].Row;
            }

            // add record indicating it was inserted.
            if (!replaced)
            {
                if (performUpdateType == PerformUpdateType.Replace)
                {
                    return StatusCodes.BadNoEntryExists.Code;
                }

                DataRow modifiedRow = m_archiveItem.DataSet.Tables[1].NewRow();

                modifiedRow[0] = (DateTime)value.SourceTimestamp;
                modifiedRow[1] = (DateTime)value.ServerTimestamp;
                modifiedRow[2] = value;

                if (!value.WrappedValue.TypeInfo.IsUnknown)
                {
                    modifiedRow[3] = value.WrappedValue.TypeInfo.BuiltInType;
                    modifiedRow[4] = value.WrappedValue.TypeInfo.ValueRank;
                }
                else
                {
                    modifiedRow[3] = BuiltInType.Variant;
                    modifiedRow[4] = ValueRanks.Scalar;
                }

                modifiedRow[5] = HistoryUpdateType.Insert;
                modifiedRow[6] = GetModificationInfo(context, HistoryUpdateType.Insert);

                m_archiveItem.DataSet.Tables[1].Rows.Add(modifiedRow);

                row = m_archiveItem.DataSet.Tables[0].NewRow();
            }

            // add/update new record.
            row[0] = (DateTime)value.SourceTimestamp;
            row[1] = (DateTime)value.ServerTimestamp;
            row[2] = value;

            if (!value.WrappedValue.TypeInfo.IsUnknown)
            {
                row[3] = value.WrappedValue.TypeInfo.BuiltInType;
                row[4] = value.WrappedValue.TypeInfo.ValueRank;
            }
            else
            {
                row[3] = BuiltInType.Variant;
                row[4] = ValueRanks.Scalar;
            }

            if (!replaced)
            {
                m_archiveItem.DataSet.Tables[0].Rows.Add(row);
            }

            // accept all changes, unless the caller is applying a batch of them and
            // wants to decide about the batch as a whole.
            if (commit)
            {
                m_archiveItem.DataSet.AcceptChanges();
            }

            return StatusCodes.Good.Code;
        }

        /// <summary>
        /// Updates the annotation history.
        /// </summary>
        /// <remarks>
        /// The annotation time is the storage key. Two users may annotate the same
        /// instant, so an existing record is only replaced when the user matches.
        /// </remarks>
        public uint UpdateAnnotations(Annotation annotation, PerformUpdateType performUpdateType)
        {
            bool replaced = false;
            DateTime annotationTime = (DateTime)annotation.AnnotationTime;

            DataRow row = null;

            foreach (DataRowView existing in m_archiveItem.DataSet.Tables[2].DefaultView.FindRows(annotationTime))
            {
                Annotation current = (Annotation)existing.Row[5];

                replaced = (current.UserName == annotation.UserName);

                if (replaced)
                {
                    if (performUpdateType == PerformUpdateType.Insert)
                    {
                        return StatusCodes.BadEntryExists.Code;
                    }

                    row = existing.Row;
                    break;
                }
            }

            if (!replaced)
            {
                if (performUpdateType == PerformUpdateType.Replace)
                {
                    return StatusCodes.BadNoEntryExists.Code;
                }

                row = m_archiveItem.DataSet.Tables[2].NewRow();
            }

            // add/update new record.
            row[0] = annotationTime;
            row[1] = annotationTime;
            row[2] = new DataValue(new ExtensionObject(annotation), StatusCodes.Good, annotationTime, annotationTime);
            row[3] = BuiltInType.ExtensionObject;
            row[4] = ValueRanks.Scalar;
            row[5] = annotation;

            if (!replaced)
            {
                m_archiveItem.DataSet.Tables[2].Rows.Add(row);
            }

            // accept all changes.
            m_archiveItem.DataSet.AcceptChanges();

            return StatusCodes.Good.Code;
        }

        /// <summary>
        /// Deletes the annotations recorded at the specified annotation time.
        /// </summary>
        public uint DeleteAnnotations(DateTime annotationTime)
        {
            DataRowView[] matches = m_archiveItem.DataSet.Tables[2].DefaultView.FindRows(annotationTime);

            if (matches.Length == 0)
            {
                return StatusCodes.BadNoEntryExists.Code;
            }

            List<DataRow> rowsToDelete = new List<DataRow>();

            foreach (DataRowView match in matches)
            {
                rowsToDelete.Add(match.Row);
            }

            foreach (DataRow row in rowsToDelete)
            {
                row.Delete();
            }

            // accept all changes.
            m_archiveItem.DataSet.AcceptChanges();

            return StatusCodes.Good.Code;
        }

        /// <summary>
        /// Deletes the value recorded at the specified source timestamp.
        /// </summary>
        public uint DeleteHistory(ServerSystemContext context, DateTime sourceTimestamp)
        {
            DataRowView[] matches = m_archiveItem.DataSet.Tables[0].DefaultView.FindRows(sourceTimestamp);

            if (matches.Length == 0)
            {
                return StatusCodes.BadNoEntryExists.Code;
            }

            List<DataRow> rowsToDelete = new List<DataRow>();

            foreach (DataRowView match in matches)
            {
                // record the deleted value in the modified history.
                AddModificationRecord(context, match.Row, HistoryUpdateType.Delete);
                rowsToDelete.Add(match.Row);
            }

            foreach (DataRow row in rowsToDelete)
            {
                row.Delete();
            }

            // commit all changes.
            m_archiveItem.DataSet.AcceptChanges();

            return StatusCodes.Good.Code;
        }

        /// <summary>
        /// Deletes a value from the history.
        /// </summary>
        public uint DeleteHistory(ServerSystemContext context, DateTime startTime, DateTime endTime, bool isModified)
        {
            // ensure time goes up.
            if (endTime < startTime)
            {
                DateTime temp = startTime;
                startTime = endTime;
                endTime = temp;
            }

            // select the table.
            DataView view = isModified
                ? m_archiveItem.DataSet.Tables[1].DefaultView
                : m_archiveItem.DataSet.Tables[0].DefaultView;

            // collect the values to delete; the timestamps are compared with full
            // ticks, unlike a row filter whose date literals are only precise to
            // the second.
            List<DataRow> rowsToDelete = new List<DataRow>();

            for (int ii = 0; ii < view.Count; ii++)
            {
                DateTime timestamp = (DateTime)view[ii].Row[0];

                if (timestamp < startTime || timestamp >= endTime)
                {
                    continue;
                }

                if (!isModified)
                {
                    AddModificationRecord(context, view[ii].Row, HistoryUpdateType.Delete);
                }

                rowsToDelete.Add(view[ii].Row);
            }

            // delete rows.
            foreach (DataRow row in rowsToDelete)
            {
                row.Delete();
            }

            // commit all changes.
            m_archiveItem.DataSet.AcceptChanges();

            return StatusCodes.Good.Code;
        }

        /// <summary>
        /// Mirrors a row of the current data into the modified history.
        /// </summary>
        private void AddModificationRecord(ServerSystemContext context, DataRow source, HistoryUpdateType updateType)
        {
            DataRow modifiedRow = m_archiveItem.DataSet.Tables[1].NewRow();

            modifiedRow[0] = source[0];
            modifiedRow[1] = source[1];
            modifiedRow[2] = source[2];
            modifiedRow[3] = source[3];
            modifiedRow[4] = source[4];
            modifiedRow[5] = updateType;
            modifiedRow[6] = GetModificationInfo(context, updateType);

            m_archiveItem.DataSet.Tables[1].Rows.Add(modifiedRow);
        }

        /// <summary>
        /// Creates a modification info record.
        /// </summary>
        private ModificationInfo GetModificationInfo(ServerSystemContext context, HistoryUpdateType updateType)
        {
            ModificationInfo info = new ModificationInfo();
            info.UpdateType = updateType;
            info.ModificationTime = DateTime.UtcNow;

            info.UserName = (context.OperationContext as ISessionOperationContext)?.UserIdentity?.DisplayName;

            return info;
        }

        /// <summary>
        /// Reads the history for the specified time range.
        /// </summary>
        public DataView ReadHistory(DateTime startTime, DateTime endTime, bool isModified)
        {
            return ReadHistory(startTime, endTime, isModified, QualifiedName.Null);
        }

        /// <summary>
        /// Reads the history for the specified time range.
        /// </summary>
        public DataView ReadHistory(DateTime startTime, DateTime endTime, bool isModified, QualifiedName browseName)
        {
            if (isModified)
            {
                return m_archiveItem.DataSet.Tables[1].DefaultView;
            }

            if (browseName == Opc.Ua.BrowseNames.Annotations)
            {
                return m_archiveItem.DataSet.Tables[2].DefaultView;
            }

            return m_archiveItem.DataSet.Tables[0].DefaultView;
        }

        /// <summary>
        /// Finds the value at or before the timestamp.
        /// </summary>
        public int FindValueAtOrBefore(DataView view, DateTime timestamp, bool ignoreBad, out bool dataIgnored)
        {
            dataIgnored = false;

            // find the last value at or before the timestamp; the view is sorted
            // by source timestamp.
            int min = 0;
            int max = view.Count - 1;
            int position = -1;

            while (min <= max)
            {
                int middle = min + ((max - min) / 2);

                if ((DateTime)view[middle].Row[0] <= timestamp)
                {
                    position = middle;
                    min = middle + 1;
                }
                else
                {
                    max = middle - 1;
                }
            }

            // step to the first row of a group sharing one timestamp, and past bad
            // values when the caller asked for that. a row at the requested time
            // itself is always returned - the recorded value answers, whatever its
            // status says.
            while (position >= 0)
            {
                DateTime current = (DateTime)view[position].Row[0];

                while (position > 0 && (DateTime)view[position - 1].Row[0] == current)
                {
                    position--;
                }

                if (current == timestamp || !ignoreBad)
                {
                    break;
                }

                DataValue value = (DataValue)view[position].Row[2];

                if (!StatusCode.IsBad(value.StatusCode))
                {
                    break;
                }

                position--;
                dataIgnored = true;
            }

            return position;
        }

        /// <summary>
        /// Returns the next value after the current position.
        /// </summary>
        public int FindValueAfter(DataView view, int position, bool ignoreBad, out bool dataIgnored)
        {
            dataIgnored = false;

            if (position < 0 || position >= view.Count)
            {
                return -1;
            }

            DateTime timestamp = (DateTime)view[position].Row[0];

            // skip the current timestamp.
            while (position < view.Count && (DateTime)view[position].Row[0] == timestamp)
            {
                position++;
            }

            if (position >= view.Count)
            {
                return -1;
            }

            // find the value after.
            while (position < view.Count)
            {
                timestamp = (DateTime)view[position].Row[0];

                // ignore bad data.
                if (ignoreBad)
                {
                    DataValue value = (DataValue)view[position].Row[2];

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        position++;
                        dataIgnored = true;
                        continue;
                    }
                }

                break;
            }

            if (position >= view.Count)
            {
                return -1;
            }

            // return the position.
            return position;
        }

        /// <summary>
        /// Constructs a node identifier for a item object.
        /// </summary>
        public static NodeId ConstructId(string filePath, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = filePath;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = NodeTypes.Item;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// The item in the archive.
        /// </summary>
        public ArchiveItem ArchiveItem
        {
            get { return m_archiveItem; }
        }

        /// <summary>
        /// The item in the archive.
        /// </summary>
        public int SubscribeCount
        {
            get { return m_subscribeCount; }
            set { m_subscribeCount = value; }
        }
private ArchiveItem m_archiveItem;
        private HistoricalDataConfigurationState m_configuration;
        private PropertyState<Annotation> m_annotations;
        private int m_subscribeCount;
        private List<DataValue> m_pattern;
        private int m_patternIndex;
        private DateTime m_nextSampleTime;
    }
}
