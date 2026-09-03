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
using System.Data;
using System.Globalization;
using Opc.Ua;

namespace Quickstarts.HistoricalEvents.Server
{
    public class ReportGenerator : IDisposable
    {
        /// <summary>
        /// Guards the report tables.
        /// </summary>
        /// <remarks>
        /// The simulation of the node manager writes new reports while the historian
        /// provider reads and writes them on behalf of a client, and a DataSet is not
        /// safe to use from several threads at once. Everything which touches the
        /// tables holds this.
        /// </remarks>
        public object SyncRoot { get; } = new object();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_dataset?.Dispose();
                m_dataset = null;
            }
        }

        public void Initialize()
        {
            m_dataset = new DataSet();

            m_dataset.Tables.Add("FluidLevelTests");
            m_dataset.Tables[0].Columns.Add(Opc.Ua.BrowseNames.EventId, typeof(string));
            m_dataset.Tables[0].Columns.Add(Opc.Ua.BrowseNames.Time, typeof(DateTime));
            m_dataset.Tables[0].Columns.Add(BrowseNames.NameWell, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.UidWell, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.TestDate, typeof(DateTime));
            m_dataset.Tables[0].Columns.Add(BrowseNames.TestReason, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.FluidLevel, typeof(double));
            m_dataset.Tables[0].Columns.Add(Opc.Ua.BrowseNames.EngineeringUnits, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.TestedBy, typeof(string));

            m_dataset.Tables.Add("InjectionTests");
            m_dataset.Tables[1].Columns.Add(Opc.Ua.BrowseNames.EventId, typeof(string));
            m_dataset.Tables[1].Columns.Add(Opc.Ua.BrowseNames.Time, typeof(DateTime));
            m_dataset.Tables[1].Columns.Add(BrowseNames.NameWell, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.UidWell, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.TestDate, typeof(DateTime));
            m_dataset.Tables[1].Columns.Add(BrowseNames.TestReason, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.TestDuration, typeof(double));
            m_dataset.Tables[1].Columns.Add(Opc.Ua.BrowseNames.EngineeringUnits, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.InjectedFluid, typeof(string));

            m_random = new Random();

            // look up the local timezone.
            TimeZoneInfo timeZone = TimeZoneInfo.Local;
            m_timeZone = new TimeZoneDataType();
            m_timeZone.Offset = (short)timeZone.GetUtcOffset(DateTime.Now).TotalMinutes;
            m_timeZone.DaylightSavingInOffset = timeZone.IsDaylightSavingTime(DateTime.Now);
        }

        #region Hardcoded Source Data
        static readonly string[] s_WellNames = new string[]
        {
            "Area51/Jupiter",
            "Area51/Titan",
            "Area99/Saturn",
            "Area99/Mars"
        };

        static readonly string[] s_WellUIDs = new string[]
        {
            "Well_24412",
            "Well_48306",
            "Well_86234",
            "Well_91423"
        };

        static readonly string[] s_TestReasons = new string[]
        {
            "initial",
            "periodic",
            "revision",
            "unknown",
            "other"
        };

        static readonly string[] s_Testers = new string[]
        {
            "Anne",
            "Bob",
            "Charley",
            "Dawn"
        };

        static readonly string[] s_UnitLengths = new string[]
        {
            "m",
            "yd"
        };

        static readonly string[] s_UnitTimes = new string[]
        {
            "s",
            "min",
            "h"
        };

        static readonly string[] s_InjectionFluids = new string[]
        {
            "oil",
            "gas",
            "non HC gas",
            "CO2",
            "water",
            "brine",
            "fresh water",
            "oil-gas",
            "oil-water",
            "gas-water",
            "condensate",
            "steam",
            "air",
            "dry",
            "unknown",
            "other"
        };
        #endregion

        private int GetRandom(int min, int max)
        {
#pragma warning disable CA5394 // Justification: sample data generation does not use randomness for security.
            return (int)(Math.Truncate(m_random.NextDouble() * (max - min + 1) + min));
#pragma warning restore CA5394
        }

        private string GetRandom(string[] values)
        {
            return values[GetRandom(0, values.Length - 1)];
        }

        public string[] GetAreas()
        {
            List<string> area = new List<string>();

            for (int ii = 0; ii < s_WellNames.Length; ii++)
            {
                int index = s_WellNames[ii].LastIndexOf('/');

                if (index >= 0)
                {
                    string areaName = s_WellNames[ii].Substring(0, index);

                    if (!area.Contains(areaName))
                    {
                        area.Add(areaName);
                    }
                }
            }

            return area.ToArray();
        }

        public WellInfo[] GetWells(string areaName)
        {
            List<WellInfo> wells = new List<WellInfo>();

            for (int ii = 0; ii < s_WellUIDs.Length; ii++)
            {
                WellInfo well = new WellInfo();
                well.Id = s_WellUIDs[ii];
                well.Name = s_WellUIDs[ii];

                if (s_WellNames.Length > ii)
                {
                    int index = s_WellNames[ii].LastIndexOf('/');

                    if (index >= 0)
                    {
                        if (s_WellNames[ii].Substring(0, index) == areaName)
                        {
                            well.Name = s_WellNames[ii].Substring(index + 1);
                            wells.Add(well);
                        }
                    }
                }
            }

            return wells.ToArray();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Nested sample helper type is part of existing API.")]
        public class WellInfo
        {
            #pragma warning disable CA1051 // Justification: sample helper data container intentionally exposes fields.
            public string Id;
            public string Name;
            #pragma warning restore CA1051
        }

        public DataRow GenerateFluidLevelTestReport()
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = Guid.NewGuid().ToString();
            row[1] = DateTime.UtcNow;

            int index = GetRandom(0, s_WellUIDs.Length - 1);
            row[2] = s_WellNames[index];
            row[3] = s_WellUIDs[index];

            row[4] = DateTime.UtcNow.AddHours(-GetRandom(0, 10));
            row[5] = GetRandom(s_TestReasons);
            row[6] = GetRandom(0, 1000);
            row[7] = GetRandom(s_UnitLengths);
            row[8] = GetRandom(s_Testers);

            m_dataset.Tables[0].Rows.Add(row);
            m_dataset.AcceptChanges();

            return row;
        }

        /// <summary>
        /// Deletes the event with the specified event id.
        /// </summary>
        public bool DeleteEvent(string eventId)
        {
            StringBuilder filter = new StringBuilder();

            filter.Append('(');
            filter.Append(Opc.Ua.BrowseNames.EventId);
            filter.Append('=');
            filter.Append('\'');
            filter.Append(eventId);
            filter.Append('\'');
            filter.Append(')');

            for (int ii = 0; ii < m_dataset.Tables.Count; ii++)
            {
                using DataView view = new DataView(m_dataset.Tables[ii], filter.ToString(), null, DataViewRowState.CurrentRows);

                if (view.Count > 0)
                {
                    view[0].Delete();
                    m_dataset.AcceptChanges();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Inserts, replaces or updates the report with the specified event id.
        /// </summary>
        /// <param name="reportType">The kind of report, which picks the table.</param>
        /// <param name="eventId">The event id of the report, as a guid in string form.</param>
        /// <param name="sourceTimestamp">The time the report was raised at.</param>
        /// <param name="fields">The fields of the report, keyed by browse path.</param>
        /// <param name="defaultWellId">The well to file the report under when its fields do not name one.</param>
        /// <param name="performUpdateType">Whether the report may be created, replaced or both.</param>
        /// <returns>What became of the report, as a Part 11 status code.</returns>
        /// <remarks>
        /// The columns of a report are typed and the fields of an incoming event are
        /// not, so a field which is missing or of the wrong type falls back to the
        /// default of its column rather than failing the whole write: a client which
        /// selected fewer fields than the report has still writes a usable row.
        ///
        /// Which well a report belongs to is a column of it rather than a property of
        /// the notifier it was written through, so a client which writes through the
        /// well itself and leaves the field out has the well filled in for it.
        /// </remarks>
        public StatusCode WriteEvent(
            ReportType reportType,
            string eventId,
            DateTime sourceTimestamp,
            IReadOnlyDictionary<string, Variant> fields,
            string defaultWellId,
            PerformUpdateType performUpdateType)
        {
            DataTable table = m_dataset.Tables[(int)reportType];
            DataRow existing = FindRow(table, eventId);

            if (existing != null && performUpdateType == PerformUpdateType.Insert)
            {
                return StatusCodes.BadEntryExists;
            }

            if (existing == null && performUpdateType == PerformUpdateType.Replace)
            {
                return StatusCodes.BadNoEntryExists;
            }

            DataRow row = existing ?? table.NewRow();

            row[Opc.Ua.BrowseNames.EventId] = eventId;
            row[Opc.Ua.BrowseNames.Time] = sourceTimestamp != DateTime.MinValue ? sourceTimestamp : DateTime.UtcNow;
            row[BrowseNames.NameWell] = GetText(fields, BrowseNames.NameWell, defaultWellId);
            row[BrowseNames.UidWell] = GetText(fields, BrowseNames.UidWell, defaultWellId);
            row[BrowseNames.TestDate] = GetTimestamp(fields, BrowseNames.TestDate, (DateTime)row[Opc.Ua.BrowseNames.Time]);
            row[BrowseNames.TestReason] = GetText(fields, BrowseNames.TestReason);

            if (reportType == ReportType.FluidLevelTest)
            {
                row[BrowseNames.FluidLevel] = GetNumber(fields, BrowseNames.FluidLevel);
                row[BrowseNames.TestedBy] = GetText(fields, BrowseNames.TestedBy);
                row[Opc.Ua.BrowseNames.EngineeringUnits] = GetEngineeringUnits(fields, BrowseNames.FluidLevel);
            }
            else
            {
                row[BrowseNames.TestDuration] = GetNumber(fields, BrowseNames.TestDuration);
                row[BrowseNames.InjectedFluid] = GetText(fields, BrowseNames.InjectedFluid);
                row[Opc.Ua.BrowseNames.EngineeringUnits] = GetEngineeringUnits(fields, BrowseNames.TestDuration);
            }

            if (existing == null)
            {
                table.Rows.Add(row);
            }

            m_dataset.AcceptChanges();

            return existing == null ? StatusCodes.GoodEntryInserted : StatusCodes.GoodEntryReplaced;
        }

        /// <summary>
        /// Returns the row of the report with the specified event id.
        /// </summary>
        private static DataRow FindRow(DataTable table, string eventId)
        {
            foreach (DataRow row in table.Rows)
            {
                if (String.Equals((string)row[Opc.Ua.BrowseNames.EventId], eventId, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a text field of an incoming event, or the fallback when it is
        /// missing or does not carry the type the column expects.
        /// </summary>
        private static string GetText(IReadOnlyDictionary<string, Variant> fields, string browseName, string fallback = null)
        {
            return TryGetField(fields, browseName, out Variant value) &&
                value.TryGetValue(out string text) &&
                !String.IsNullOrEmpty(text)
                ? text
                : fallback ?? String.Empty;
        }

        /// <summary>
        /// Returns a measurement of an incoming event, zero when it is missing or
        /// does not carry the type the column expects.
        /// </summary>
        private static double GetNumber(IReadOnlyDictionary<string, Variant> fields, string browseName)
        {
            return TryGetField(fields, browseName, out Variant value) && value.TryGetValue(out double number)
                ? number
                : 0.0;
        }

        /// <summary>
        /// Returns a timestamp field of an incoming event, or the fallback when it is
        /// missing or does not carry the type the column expects.
        /// </summary>
        private static DateTime GetTimestamp(IReadOnlyDictionary<string, Variant> fields, string browseName, DateTime fallback)
        {
            return TryGetField(fields, browseName, out Variant value) && value.TryGetValue(out DateTimeUtc timestamp)
                ? (DateTime)timestamp
                : fallback;
        }

        /// <summary>
        /// Looks a field of an incoming event up by the browse path which addresses it.
        /// </summary>
        private static bool TryGetField(IReadOnlyDictionary<string, Variant> fields, string key, out Variant value)
        {
            if (fields != null)
            {
                return fields.TryGetValue(key, out value);
            }

            value = Variant.Null;
            return false;
        }

        /// <summary>
        /// Returns the unit of the measurement of an incoming event.
        /// </summary>
        /// <remarks>
        /// The unit sits on the measurement rather than on the report, so a client
        /// addresses it through the two segment browse path the framework keys it by.
        /// The table stores the short name of it, which is what the report is built
        /// back from.
        /// </remarks>
        private static string GetEngineeringUnits(IReadOnlyDictionary<string, Variant> fields, string measurement)
        {
            string key = measurement + "/" + Opc.Ua.BrowseNames.EngineeringUnits;

            if (!TryGetField(fields, key, out Variant value))
            {
                return String.Empty;
            }

            if (value.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out EUInformation units))
            {
                return units.DisplayName.Text ?? String.Empty;
            }

            return value.TryGetValue(out string text) ? text : String.Empty;
        }

        /// <summary>
        /// Reads the report history for the specified time range.
        /// </summary>
        public DataView ReadHistoryForWellId(ReportType reportType, string uidWell, DateTime startTime, DateTime endTime)
        {
            StringBuilder filter = new StringBuilder();

            filter.Append('(');
            filter.Append(BrowseNames.UidWell);
            filter.Append('=');
            filter.Append('\'');
            filter.Append(uidWell);
            filter.Append('\'');
            filter.Append(')');

            return ReadHistory(reportType, filter, startTime, endTime);
        }

        /// <summary>
        /// Reads the report history for the specified time range.
        /// </summary>
        public DataView ReadHistoryForArea(ReportType reportType, string areaName, DateTime startTime, DateTime endTime)
        {
            StringBuilder filter = new StringBuilder();

            if (!String.IsNullOrEmpty(areaName))
            {
                filter.Append('(');
                filter.Append(BrowseNames.NameWell);
                filter.Append(" LIKE ");
                filter.Append('\'');
                filter.Append(areaName);
                filter.Append('*');
                filter.Append('\'');
                filter.Append(')');
            }

            return ReadHistory(reportType, filter, startTime, endTime);
        }

        /// <summary>
        /// Reads the history for the specified time range.
        /// </summary>
        private DataView ReadHistory(ReportType reportType, StringBuilder filter, DateTime startTime, DateTime endTime)
        {
            DateTime earlyTime = startTime;
            DateTime lateTime = endTime;

            if (endTime < startTime && endTime != DateTime.MinValue)
            {
                earlyTime = endTime;
                lateTime = startTime;
            }

            if (earlyTime != DateTime.MinValue)
            {
                if (filter.Length > 0)
                {
                    filter.Append(" AND ");
                }

                filter.Append('(');
                filter.Append(Opc.Ua.BrowseNames.Time);
                filter.Append(">=");
                filter.Append('#');
                // the DataView expression parser reads date literals with the invariant
                // culture, so the literal has to be written with it as well.
                filter.Append(earlyTime.ToString(CultureInfo.InvariantCulture));
                filter.Append('#');
                filter.Append(')');
            }

            if (lateTime != DateTime.MinValue)
            {
                if (filter.Length > 0)
                {
                    filter.Append(" AND ");
                }

                filter.Append('(');
                filter.Append(Opc.Ua.BrowseNames.Time);
                filter.Append('<');
                filter.Append('#');
                filter.Append(lateTime.ToString(CultureInfo.InvariantCulture));
                filter.Append('#');
                filter.Append(')');
            }

#pragma warning disable CA2000 // Justification: ownership is transferred to the caller.
            DataView view = new DataView(
                m_dataset.Tables[(int)reportType],
                filter.ToString(),
                Opc.Ua.BrowseNames.Time,
                DataViewRowState.CurrentRows);
#pragma warning restore CA2000

            return view;
        }

        /// <summary>
        /// Converts the DB row to a UA event,
        /// </summary>
        /// <param name="context">The UA context to use for the conversion.</param>
        /// <param name="namespaceIndex">The index assigned to the type model namespace.</param>
        /// <param name="reportType">The type of report.</param>
        /// <param name="row">The source for the report.</param>
        /// <returns>The new report.</returns>
        public Opc.Ua.BaseEventState GetReport(ISystemContext context, ushort namespaceIndex, ReportType reportType, DataRow row)
        {
            switch (reportType)
            {
                case ReportType.FluidLevelTest: return GetFluidLevelTestReport(context, namespaceIndex, row);
                case ReportType.InjectionTest: return GetInjectionTestReport(context, namespaceIndex, row);
            }

            return null;
        }

        public Opc.Ua.BaseEventState GetFluidLevelTestReport(ISystemContext SystemContext, ushort namespaceIndex, DataRow row)
        {
            // construct translation object with default text.
            TranslationInfo info = new TranslationInfo(
                "FluidLevelTestReport",
                "en-US",
                "A fluid level test report is available.");

            // construct the event.
            FluidLevelTestReportState e = new FluidLevelTestReportState(null);

            e.Initialize(
                SystemContext,
                null,
                EventSeverity.Medium,
                new LocalizedText(info));

            // override event id and time.                
            e.EventId.Value = new Guid((string)row[Opc.Ua.BrowseNames.EventId]).ToByteArray().ToByteString();
            e.Time.Value = (DateTime)row[Opc.Ua.BrowseNames.Time];


            string nameWell = (string)row[BrowseNames.NameWell];
            string uidWell = (string)row[BrowseNames.UidWell];

            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceName, nameWell, false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceNode, new NodeId(uidWell, namespaceIndex), false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.LocalTime, m_timeZone, false);

            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.NameWell, namespaceIndex), nameWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.UidWell, namespaceIndex), uidWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestDate, namespaceIndex), new DateTimeUtc((DateTime)row[BrowseNames.TestDate]), false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestReason, namespaceIndex), (string)row[BrowseNames.TestReason], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestedBy, namespaceIndex), (string)row[BrowseNames.TestedBy], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.FluidLevel, namespaceIndex), (double)row[BrowseNames.FluidLevel], false);
            e.FluidLevel.SetChildValue(SystemContext, Opc.Ua.BrowseNames.EngineeringUnits, new EUInformation((string)row[Opc.Ua.BrowseNames.EngineeringUnits], Namespaces.HistoricalEvents), false);

            return e;
        }

        public DataRow GenerateInjectionTestReport()
        {
            DataRow row = m_dataset.Tables[1].NewRow();

            row[0] = Guid.NewGuid().ToString();
            row[1] = DateTime.UtcNow;

            int index = GetRandom(0, s_WellUIDs.Length - 1);
            row[2] = s_WellNames[index];
            row[3] = s_WellUIDs[index];

            row[4] = DateTime.UtcNow.AddHours(-GetRandom(0, 10));
            row[5] = GetRandom(s_TestReasons);
            row[6] = GetRandom(0, 1000);
            row[7] = GetRandom(s_UnitTimes);
            row[8] = GetRandom(s_InjectionFluids);

            m_dataset.Tables[1].Rows.Add(row);
            m_dataset.AcceptChanges();

            return row;
        }

        public Opc.Ua.BaseEventState GetInjectionTestReport(ISystemContext SystemContext, ushort namespaceIndex, DataRow row)
        {
            // construct translation object with default text.
            TranslationInfo info = new TranslationInfo(
                "InjectionTestReport",
                "en-US",
                "An injection test report is available.");

            // construct the event.
            InjectionTestReportState e = new InjectionTestReportState(null);

            e.Initialize(
                SystemContext,
                null,
                EventSeverity.Medium,
                new LocalizedText(info));

            // override event id and time.                
            e.EventId.Value = new Guid((string)row[Opc.Ua.BrowseNames.EventId]).ToByteArray().ToByteString();
            e.Time.Value = (DateTime)row[Opc.Ua.BrowseNames.Time];

            string nameWell = (string)row[BrowseNames.NameWell];
            string uidWell = (string)row[BrowseNames.UidWell];

            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceName, nameWell, false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceNode, new NodeId(uidWell, namespaceIndex), false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.LocalTime, m_timeZone, false);

            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.NameWell, namespaceIndex), nameWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.UidWell, namespaceIndex), uidWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestDate, namespaceIndex), new DateTimeUtc((DateTime)row[BrowseNames.TestDate]), false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestReason, namespaceIndex), (string)row[BrowseNames.TestReason], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.InjectedFluid, namespaceIndex), (string)row[BrowseNames.InjectedFluid], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestDuration, namespaceIndex), (double)row[BrowseNames.TestDuration], false);
            e.TestDuration.SetChildValue(SystemContext, Opc.Ua.BrowseNames.EngineeringUnits, new EUInformation((string)row[Opc.Ua.BrowseNames.EngineeringUnits], Namespaces.HistoricalEvents), false);

            return e;
        }

        private DataSet m_dataset;
        private Random m_random;
        private TimeZoneDataType m_timeZone;
    }

    public enum ReportType
    {
        FluidLevelTest,
        InjectionTest
    }
}
