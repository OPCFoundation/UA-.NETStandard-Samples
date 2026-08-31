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
using Opc.Ua.Server;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// One sample server to start, together with the way its own Program.Main creates it.
    /// </summary>
    public sealed class SampleServerUnderTest
    {
        /// <summary>
        /// The catalog entry of the sample.
        /// </summary>
        public SampleDefinition Sample { get; init; }

        /// <summary>
        /// Creates the server instance, exactly the way the sample does.
        /// </summary>
        public Func<ITelemetryContext, StandardServer> CreateServer { get; init; }

        /// <inheritdoc/>
        public override string ToString() => Sample.Name;
    }

    /// <summary>
    /// Maps the samples in the catalog to their server classes.
    /// </summary>
    /// <remarks>
    /// The factories are written out instead of being resolved by reflection on purpose:
    /// renaming or removing a sample server then breaks the build of the tests, which is
    /// the earliest and clearest moment to notice.
    /// </remarks>
    public static class SampleServerFactories
    {
        private static readonly Dictionary<string, Func<ITelemetryContext, StandardServer>> s_factories =
            new(StringComparer.Ordinal) {
                ["AlarmCondition"] = telemetry => new Quickstarts.AlarmConditionServer.AlarmConditionServer(telemetry),
                ["Boiler"] = telemetry => new Quickstarts.Boiler.Server.BoilerServer(telemetry),
                ["DataAccess"] = telemetry => new Quickstarts.DataAccessServer.DataAccessServer(telemetry),
                ["DataTypes"] = telemetry => new Quickstarts.DataTypes.DataTypesServer(telemetry),
                ["Empty"] = telemetry => new Quickstarts.EmptyServer.EmptyServer(telemetry),
                ["HistoricalAccess"] = telemetry => new Quickstarts.HistoricalAccessServer.HistoricalAccessServer(telemetry),
                ["HistoricalEvents"] = telemetry => new Quickstarts.HistoricalEvents.Server.HistoricalEventsServer(telemetry),
                ["Methods"] = telemetry => new Quickstarts.MethodsServer.MethodsServer(telemetry),
                ["PerfTest"] = telemetry => new Quickstarts.PerfTestServer.PerfTestServer(telemetry),
                ["RoleManagement"] = telemetry => new Quickstarts.RoleManagement.Server.RoleManagementServer(telemetry),
                ["SimpleEvents"] = telemetry => new Quickstarts.SimpleEvents.Server.SimpleEventsServer(telemetry),
                ["UserAuthentication"] = telemetry => new Quickstarts.UserAuthenticationServer.UserAuthenticationServer(telemetry),
                ["Views"] = telemetry => new Quickstarts.Views.Server.ViewsServer(telemetry),
                ["Sample"] = telemetry => new Opc.Ua.Sample.SampleServer(telemetry),
                ["Reference"] = CreateReferenceServer,

                // the console variant of the aggregation sample shares its sources with the
                // WinForms one and only differs in its configuration, which is the one that
                // can actually run: the WinForms configuration puts the server on the port of
                // the downstream server it aggregates
                ["AggregationConsole"] = telemetry => new AggregationServer.AggregationServer(telemetry),
            };

        /// <summary>
        /// The sample servers tier 1 covers.
        /// </summary>
        public static IEnumerable<SampleServerUnderTest> All
            => SampleCatalog.Servers
                .Where(sample => s_factories.ContainsKey(sample.Name))
                .Select(sample => new SampleServerUnderTest {
                    Sample = sample,
                    CreateServer = s_factories[sample.Name],
                });

        /// <summary>
        /// The names of the samples which have a factory.
        /// </summary>
        public static IEnumerable<string> CoveredSamples => s_factories.Keys;

        /// <summary>
        /// The reference server adds the node managers of the quickstart library, the same
        /// way Samples/ReferenceServer/Program.cs does.
        /// </summary>
        private static StandardServer CreateReferenceServer(ITelemetryContext telemetry)
        {
            var server = new Quickstarts.ReferenceServer.ReferenceServer(telemetry);

            Quickstarts.Servers.Utils.AddDefaultNodeManagers(server);

            return server;
        }
    }
}
