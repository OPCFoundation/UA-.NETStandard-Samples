/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// A sample server which can be hosted by a test: the catalog entry of the sample
    /// and the registration its entry point uses to host it.
    /// </summary>
    public sealed class SampleServerUnderTest
    {
        /// <summary>
        /// The catalog entry of the sample.
        /// </summary>
        public SampleDefinition Sample { get; init; }

        /// <summary>
        /// Registers the server of the sample with the host of a test, through the
        /// same composition root its <c>Program.Main</c> uses: the server class, its
        /// node managers and the configuration file.
        /// </summary>
        public SampleServerRegistration ConfigureServices { get; init; }
    }

    /// <summary>
    /// Knows how every sample server is registered with a host, so a test can host
    /// any of them without knowing its classes.
    /// </summary>
    public static class SampleServerFactories
    {
        private static readonly Dictionary<string, SampleServerRegistration> s_registrations =
            new()
            {
                ["AlarmCondition"] = (services, file, configure) => services.AddAlarmConditionServer(file, configure),
                ["AliasNames"] = (services, file, configure) => services.AddAliasNamesServer(file, configure),
                ["Boiler"] = (services, file, configure) => services.AddBoilerServer(file, configure),
                ["DataAccess"] = (services, file, configure) => services.AddDataAccessServer(file, configure),
                ["DataTypes"] = (services, file, configure) => services.AddDataTypesServer(file, configure),
                ["Empty"] = (services, file, configure) => services.AddEmptyServer(file, configure),
                ["FileTransfer"] = (services, file, configure) => services.AddFileTransferServer(file, configure),
                ["HistoricalAccess"] = (services, file, configure) => services.AddHistoricalAccessServer(file, configure),
                ["HistoricalEvents"] = (services, file, configure) => services.AddHistoricalEventsServer(file, configure),
                ["Methods"] = (services, file, configure) => services.AddMethodsServer(file, configure),
                ["NodeManagement"] = (services, file, configure) => services.AddNodeManagementServer(file, configure),
                ["PerfTest"] = (services, file, configure) => services.AddPerfTestServer(file, configure),
                ["RoleManagement"] = (services, file, configure) => services.AddRoleManagementServer(file, configure),
                ["RuntimeNodeSets"] = (services, file, configure) => services.AddRuntimeNodeSetsServer(file, configure),
                ["SimpleEvents"] = (services, file, configure) => services.AddSimpleEventsServer(file, configure),
                ["StateMachines"] = (services, file, configure) => services.AddStateMachinesServer(file, configure),
                ["UserAuthentication"] = (services, file, configure) => services.AddUserAuthenticationServer(file, configure),
                ["Views"] = (services, file, configure) => services.AddViewsServer(file, configure),
                ["Sample"] = (services, file, configure) => services.AddUaSampleServer(file, configure),
                ["Reference"] = (services, file, configure) => services.AddReferenceServer(file, configure),
                // the console variant of the aggregation sample shares its sources with the
                // WinForms one and only differs in its configuration, which is the one that
                // can actually run: the WinForms configuration puts the server on the port of
                // the downstream server it aggregates
                ["AggregationConsole"] = (services, file, configure) => services.AddAggregationServer(file, configure),
            };

        /// <summary>
        /// Every sample server of the catalog which a test can host.
        /// </summary>
        public static IEnumerable<SampleServerUnderTest> All
            => SampleCatalog.Servers
                .Where(sample => s_registrations.ContainsKey(sample.Name))
                .Select(sample => new SampleServerUnderTest {
                    Sample = sample,
                    ConfigureServices = s_registrations[sample.Name],
                });

        /// <summary>
        /// The names of the samples this class can host.
        /// </summary>
        public static IEnumerable<string> CoveredSamples => s_registrations.Keys;
    }
}
