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
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Controls;

namespace Quickstarts.HistoricalAccessServer
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Initialize the user interface.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            // the aggregate test harness below is run against the recorded data instead
            // of the server, and needs nothing but a telemetry context:
            // DoTests(telemetry, false, false, "Quickstarts.HistoricalAccessServer.Data.Historian1.txt", "..\\..\\Data\\Historian1ExpectedData.csv");
            // DoTests(telemetry, false, true, "Quickstarts.HistoricalAccessServer.Data.Historian2.txt", "..\\..\\Data\\Historian2ExpectedData.csv");
            // DoTests(telemetry, true, true, "Quickstarts.HistoricalAccessServer.Data.Historian3.txt", "..\\..\\Data\\Historian3ExpectedData.csv");

            // the generic host owns the logging and the lifetime of the sample; the
            // server is hosted by the stack, its configuration is loaded straight from
            // the configuration file, and the form shows the running server.
            SampleWinFormsHost.Run<ServerForm>(
                args,
                services => services
                    .AddHistoricalAccessServer(),
                ExceptionDlg.Show);
        }

        internal sealed class TestCase
        {
            public int TestId { get; set; }
            public string DataPath { get; set; }
            public string ExpectedResultsPath { get; set; }
            public NodeId AggregateId { get; set; }
            public bool Stepped { get; set; }
            public bool TreatUncertainAsBad { get; set; }
            public bool UseSlopedExtrapolation { get; set; }
        }

        static void DoTests(ITelemetryContext telemetry, bool stepped, bool treatUncertainAsBad, string dataPath, string expectedResultsPath)
        {
            TestCase test8 = new TestCase() {
                TestId = 8,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_Count,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test8, "..\\..\\Data\\Results8.csv");

            TestCase test9 = new TestCase() {
                TestId = 9,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_Total,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test9, "..\\..\\Data\\Results9.csv");

            TestCase test7 = new TestCase() {
                TestId = 7,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_TimeAverage2,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test7, "..\\..\\Data\\Results7.csv");

            TestCase test6 = new TestCase() {
                TestId = 6,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_TimeAverage,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test6, "..\\..\\Data\\Results6.csv");

            TestCase test5 = new TestCase() {
                TestId = 5,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_Average,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test5, "..\\..\\Data\\Results5.csv");

            TestCase test1 = new TestCase() {
                TestId = 1,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_Interpolative,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test1, "..\\..\\Data\\Results1.csv");

            TestCase test2 = new TestCase() {
                TestId = 2,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_Interpolative,
                Stepped = stepped,
                TreatUncertainAsBad = !treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test2, "..\\..\\Data\\Results2.csv");

            TestCase test3 = new TestCase() {
                TestId = 3,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_StartBound,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test3, "..\\..\\Data\\Results3.csv");

            TestCase test4 = new TestCase() {
                TestId = 4,
                DataPath = dataPath,
                ExpectedResultsPath = expectedResultsPath,
                AggregateId = ObjectIds.AggregateFunction_EndBound,
                Stepped = stepped,
                TreatUncertainAsBad = treatUncertainAsBad,
                UseSlopedExtrapolation = !stepped
            };

            DoTest(telemetry, test4, "..\\..\\Data\\Results4.csv");
            /*
            */
        }

        /// <summary>
        /// Gets the expected results.
        /// </summary>
        static List<DataValue> GetExpectedResults(string filePath, int testId)
        {
            DateTime startTime = DateTime.UtcNow;
            startTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, 0, 0, DateTimeKind.Utc);

            List<DataValue> results = new List<DataValue>();

            using (StreamReader reader = new StreamReader(filePath))
            {
                string header1 = reader.ReadLine();

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine().Trim();

                    if (String.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    string[] fields = line.Split(',');

                    if (String.IsNullOrEmpty(fields[0]))
                    {
                        continue;
                    }

                    double time = Convert.ToDouble(fields[0], CultureInfo.InvariantCulture);
                    double value = Convert.ToDouble(fields[2 * testId - 1], CultureInfo.InvariantCulture);

                    StatusCode statusCode = StatusCodes.Good;

                    switch (fields[2 * testId])
                    {
                        case "GI": { statusCode = statusCode.WithAggregateBits(AggregateBits.Interpolated); break; }
                        case "GC": { statusCode = statusCode.WithAggregateBits(AggregateBits.Calculated); break; }
                        case "UC": { statusCode = StatusCodes.UncertainSubNormal.WithAggregateBits(AggregateBits.Calculated); break; }
                        case "UI": { statusCode = StatusCodes.UncertainSubNormal.WithAggregateBits(AggregateBits.Interpolated); break; }
                        case "UR": { statusCode = StatusCodes.Uncertain; break; }
                        case "BR": { statusCode = StatusCodes.BadNoData; break; }
                        case "BD": { statusCode = StatusCodes.Bad; break; }
                    }

                    DataValue dataValue = new DataValue(
                        StatusCode.IsBad(statusCode) ? Variant.Null : Variant.From(value),
                        statusCode,
                        startTime.AddSeconds(time),
                        startTime.AddSeconds(time));
                    results.Add(dataValue);
                }
            }

            return results;
        }

        static void DoTest(ITelemetryContext telemetry, TestCase test, string filePath)
        {
            ILogger logger = telemetry.CreateLogger(nameof(Program));

            List<DataValue> expectedValues = GetExpectedResults(test.ExpectedResultsPath, test.TestId);

            ArchiveItem item = new ArchiveItem(test.DataPath, Assembly.GetExecutingAssembly(), test.DataPath);

            DataFileReader reader = new DataFileReader();
            reader.LoadConfiguration(null, item, telemetry);
            reader.LoadHistoryData(null, item);

            AggregateConfiguration configuration = new AggregateConfiguration();
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.TreatUncertainAsBad = test.TreatUncertainAsBad;
            configuration.UseSlopedExtrapolation = test.UseSlopedExtrapolation;
            configuration.UseServerCapabilitiesDefaults = false;

            DateTime startTime = DateTime.UtcNow;
            startTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, 0, 0, DateTimeKind.Utc);

            AggregateCalculator calculator = new AggregateCalculator(
                test.AggregateId,
                startTime.AddSeconds(0),
                startTime.AddSeconds(100),
                5000,
                test.Stepped,
                configuration,
                telemetry);

            StringBuilder buffer = new StringBuilder();
            List<DataValue> values = new List<DataValue>();

            foreach (DataRowView row in item.DataSet.Tables[0].DefaultView)
            {
                DataValue rawValue = (DataValue)row.Row[2];

                if (!calculator.QueueRawValue(rawValue))
                {
                    logger.LogTrace("Oops!");
                    continue;
                }

                if (calculator.TryGetProcessedValue(false, out DataValue processedValue))
                {
                    values.Add(processedValue);
                }
            }

            while (calculator.TryGetProcessedValue(true, out DataValue processedValue))
            {
                values.Add(processedValue);
            }

            for (int ii = 0; ii < values.Count && ii < expectedValues.Count; ii++)
            {
                if (values[ii].SourceTimestamp != expectedValues[ii].SourceTimestamp)
                {
                    logger.LogTrace("Wrong Status Timestamp");
                    continue;
                }

                if (values[ii].StatusCode != expectedValues[ii].StatusCode)
                {
                    logger.LogTrace("Wrong Status Code");
                    continue;
                }

                if (StatusCode.IsNotBad(values[ii].StatusCode))
                {
                    if (!values[ii].WrappedValue.ConvertTo(BuiltInType.Double).TryGetValue(out double value1) ||
                        !expectedValues[ii].WrappedValue.ConvertTo(BuiltInType.Double).TryGetValue(out double value2))
                    {
                        logger.LogTrace("Wrong Value");
                        continue;
                    }

                    if (Math.Round(value1, 4) != Math.Round(value2, 4))
                    {
                        logger.LogTrace("Wrong Value");
                        continue;
                    }
                }
            }

            foreach (DataValue processedValue in values)
            {
                buffer.Append(processedValue.SourceTimestamp.ToString("HH:mm:ss"));
                buffer.Append(", ");
                buffer.Append(processedValue.WrappedValue);
                buffer.Append(", ");
                buffer.Append(new StatusCode(processedValue.StatusCode.CodeBits));
                buffer.Append(", ");
                buffer.Append(processedValue.StatusCode.AggregateBits);
                buffer.Append("\r\n");
            }

            // write to the file.
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.Write(buffer.ToString());
            }
        }
    }

    /// <summary>
    /// The <b>HistoricalAccessServer</b> namespace contains classes which implement a UA Data Access Server.
    /// </summary>
    /// <exclude/>
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class NamespaceDoc
    {
    }
}
