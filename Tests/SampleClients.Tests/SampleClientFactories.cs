/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// One sample client to drive, and how its own Program.Main creates its main form.
    /// </summary>
    public sealed class SampleClientUnderTest
    {
        /// <summary>
        /// The catalog entry of the sample.
        /// </summary>
        public SampleDefinition Sample { get; init; }

        /// <summary>
        /// Creates the main form of the client, exactly the way the sample does.
        /// </summary>
        public Func<ApplicationConfiguration, ITelemetryContext, Form> CreateMainForm { get; init; }

        /// <summary>
        /// The designer name of a control the sample enables once it is connected, or null
        /// if the sample has none. It proves that the post connect logic of the sample ran
        /// and not just the shared connect control.
        /// </summary>
        public string EnabledAfterConnect { get; init; }

        /// <inheritdoc/>
        public override string ToString() => Sample.Name;
    }

    /// <summary>
    /// Maps the samples in the catalog to their client main forms.
    /// </summary>
    /// <remarks>
    /// Written out rather than resolved by reflection, for the same reason as the server
    /// factories: a renamed or removed sample client breaks the build of the tests.
    /// </remarks>
    public static class SampleClientFactories
    {
        private static readonly Dictionary<string, SampleClientUnderTest> s_clients = Build();

        /// <summary>
        /// The sample clients tier 2 covers.
        /// </summary>
        public static IEnumerable<SampleClientUnderTest> All => s_clients.Values;

        /// <summary>
        /// The names of the samples which have a factory.
        /// </summary>
        public static IEnumerable<string> CoveredSamples => s_clients.Keys;

        private static Dictionary<string, SampleClientUnderTest> Build()
        {
            var clients = new List<SampleClientUnderTest> {
                Client("AlarmCondition", (configuration, telemetry) => new Quickstarts.AlarmConditionClient.MainForm(configuration, telemetry)),
                Client("Boiler", (configuration, telemetry) => new Quickstarts.Boiler.Client.MainForm(configuration, telemetry), "BoilerCB"),
                Client("DataAccess", (configuration, telemetry) => new Quickstarts.DataAccessClient.MainForm(configuration, telemetry), "BrowseNodesTV"),
                Client("DataTypes", (configuration, telemetry) => new Quickstarts.DataTypes.MainForm(configuration, telemetry)),
                Client("Empty", (configuration, telemetry) => new Quickstarts.EmptyClient.MainForm(configuration, telemetry)),
                Client("FileTransfer", (configuration, telemetry) => new Quickstarts.FileTransferClient.MainForm(configuration, telemetry), "UploadBTN"),
                Client("HistoricalAccess", (configuration, telemetry) => new Quickstarts.HistoricalAccess.Client.MainForm(configuration, telemetry)),
                Client("HistoricalEvents", (configuration, telemetry) => new Quickstarts.HistoricalEvents.Client.MainForm(configuration, telemetry)),
                Client("Methods", (configuration, telemetry) => new Quickstarts.MethodsClient.MainForm(configuration, telemetry), "StartBTN"),
                Client("PerfTest", (configuration, telemetry) => new Quickstarts.PerfTestClient.MainForm(configuration, telemetry)),
                Client("RoleManagement", (configuration, telemetry) => new Quickstarts.RoleManagement.Client.MainForm(configuration, telemetry), "RefreshBTN"),
                Client("SimpleEvents", (configuration, telemetry) => new Quickstarts.SimpleEvents.Client.MainForm(configuration, telemetry)),
                Client("StateMachines", (configuration, telemetry) => new Quickstarts.StateMachines.Client.MainForm(configuration, telemetry), "PowerOnBTN"),
                Client("UserAuthentication", (configuration, telemetry) => new Quickstarts.UserAuthenticationClient.MainForm(configuration, telemetry)),
                Client("Views", (configuration, telemetry) => new Quickstarts.ViewsClient.MainForm(configuration, telemetry)),
                Client("Reference", (configuration, telemetry) => new Quickstarts.ReferenceClient.MainForm(configuration, telemetry)),
            };

            return clients.ToDictionary(client => client.Sample.Name, StringComparer.Ordinal);
        }

        private static SampleClientUnderTest Client(
            string name,
            Func<ApplicationConfiguration, ITelemetryContext, Form> createMainForm,
            string enabledAfterConnect = null)
        {
            SampleDefinition sample = SampleCatalog.All.Single(entry => entry.Name == name);

            return new SampleClientUnderTest {
                Sample = sample,
                CreateMainForm = createMainForm,
                EnabledAfterConnect = enabledAfterConnect,
            };
        }
    }
}
