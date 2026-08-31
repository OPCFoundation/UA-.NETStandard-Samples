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

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// One sample, or one client/server pair of samples.
    /// </summary>
    /// <remarks>
    /// All paths are relative to the repository root and use forward slashes.
    /// </remarks>
    public sealed class SampleDefinition
    {
        /// <summary>
        /// The name of the sample, used as the test case name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// The server project of the sample, if it has one.
        /// </summary>
        public string ServerProject { get; init; }

        /// <summary>
        /// The application configuration of the server.
        /// </summary>
        public string ServerConfig { get; init; }

        /// <summary>
        /// The opc.tcp base address the server is expected to listen on.
        /// </summary>
        public string ServerUrl { get; init; }

        /// <summary>
        /// True if the server is a console application, false for WinForms.
        /// </summary>
        public bool ServerIsConsole { get; init; }

        /// <summary>
        /// The client project of the sample, if it has one.
        /// </summary>
        public string ClientProject { get; init; }

        /// <summary>
        /// The application configuration of the client.
        /// </summary>
        public string ClientConfig { get; init; }

        /// <summary>
        /// The source file which contains the endpoint url the client connects to by
        /// default. Null if the client discovers its endpoint instead of hard coding it.
        /// </summary>
        public string ClientEndpointSource { get; init; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>
    /// The list of samples covered by the test suite.
    /// </summary>
    /// <remarks>
    /// This table drives every tier of the test suite. Adding a sample to the repository
    /// means adding one entry here.
    /// </remarks>
    public static class SampleCatalog
    {
        /// <summary>
        /// All samples.
        /// </summary>
        public static IReadOnlyList<SampleDefinition> All { get; } = new List<SampleDefinition>
        {
            new SampleDefinition
            {
                Name = "AlarmCondition",
                ServerProject = "Workshop/AlarmCondition/Server/AlarmCondition Server.csproj",
                ServerConfig = "Workshop/AlarmCondition/Server/AlarmConditionServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62544/Quickstarts/AlarmConditionServer",
                ClientProject = "Workshop/AlarmCondition/Client/AlarmCondition Client.csproj",
                ClientConfig = "Workshop/AlarmCondition/Client/AlarmConditionClient.Config.xml",
                ClientEndpointSource = "Workshop/AlarmCondition/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "Boiler",
                ServerProject = "Workshop/Boiler/Server/Boiler Server.csproj",
                ServerConfig = "Workshop/Boiler/Server/BoilerServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62567/Quickstarts/BoilerServer",
                ClientProject = "Workshop/Boiler/Client/Boiler Client.csproj",
                ClientConfig = "Workshop/Boiler/Client/BoilerClient.Config.xml",
                ClientEndpointSource = "Workshop/Boiler/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "DataAccess",
                ServerProject = "Workshop/DataAccess/Server/DataAccess Server.csproj",
                ServerConfig = "Workshop/DataAccess/Server/DataAccessServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62548/Quickstarts/DataAccessServer",
                ClientProject = "Workshop/DataAccess/Client/DataAccess Client.csproj",
                ClientConfig = "Workshop/DataAccess/Client/DataAccessClient.Config.xml",
                ClientEndpointSource = "Workshop/DataAccess/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "DataTypes",
                ServerProject = "Workshop/DataTypes/Server/DataTypes Server.csproj",
                ServerConfig = "Workshop/DataTypes/Server/DataTypesServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62555/DataTypesServer",
                ClientProject = "Workshop/DataTypes/Client/DataTypes Client.csproj",
                ClientConfig = "Workshop/DataTypes/Client/DataTypesClient.Config.xml",
                ClientEndpointSource = "Workshop/DataTypes/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "Empty",
                ServerProject = "Workshop/Empty/Server/Empty Server.csproj",
                ServerConfig = "Workshop/Empty/Server/Quickstarts.EmptyServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62546/Quickstarts/EmptyServer",
                ClientProject = "Workshop/Empty/Client/Empty Client.csproj",
                ClientConfig = "Workshop/Empty/Client/Quickstarts.EmptyClient.Config.xml",
                ClientEndpointSource = "Workshop/Empty/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "HistoricalAccess",
                ServerProject = "Workshop/HistoricalAccess/Server/HistoricalAccess Server.csproj",
                ServerConfig = "Workshop/HistoricalAccess/Server/HistoricalAccessServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62550/Quickstarts/HistoricalAccessServer",
                ClientProject = "Workshop/HistoricalAccess/Client/HistoricalAccess Client.csproj",
                ClientConfig = "Workshop/HistoricalAccess/Client/HistoricalAccessClient.Config.xml",
                ClientEndpointSource = "Workshop/HistoricalAccess/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "HistoricalEvents",
                ServerProject = "Workshop/HistoricalEvents/Server/HistoricalEvents Server.csproj",
                ServerConfig = "Workshop/HistoricalEvents/Server/HistoricalEventsServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62553/Quickstarts/HistoricalEventsServer",
                ClientProject = "Workshop/HistoricalEvents/Client/HistoricalEvents Client.csproj",
                ClientConfig = "Workshop/HistoricalEvents/Client/HistoricalEventsClient.Config.xml",
                ClientEndpointSource = "Workshop/HistoricalEvents/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "Methods",
                ServerProject = "Workshop/Methods/Server/Methods Server.csproj",
                ServerConfig = "Workshop/Methods/Server/Quickstarts.MethodsServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62557/Quickstarts/MethodsServer",
                ClientProject = "Workshop/Methods/Client/Methods Client.csproj",
                ClientConfig = "Workshop/Methods/Client/Quickstarts.MethodsClient.Config.xml",
                ClientEndpointSource = "Workshop/Methods/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "PerfTest",
                ServerProject = "Workshop/PerfTest/Server/PerfTest Server.csproj",
                ServerConfig = "Workshop/PerfTest/Server/PerfTestServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62559/Quickstarts/PerfTestServer",
                ClientProject = "Workshop/PerfTest/Client/PerfTest Client.csproj",
                ClientConfig = "Workshop/PerfTest/Client/PerfTestClient.Config.xml",
                ClientEndpointSource = "Workshop/PerfTest/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "SimpleEvents",
                ServerProject = "Workshop/SimpleEvents/Server/SimpleEvents Server.csproj",
                ServerConfig = "Workshop/SimpleEvents/Server/SimpleEventsServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62563/Quickstarts/SimpleEventsServer",
                ClientProject = "Workshop/SimpleEvents/Client/SimpleEvents Client.csproj",
                ClientConfig = "Workshop/SimpleEvents/Client/SimpleEventsClient.Config.xml",
                ClientEndpointSource = "Workshop/SimpleEvents/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "StateMachines",
                ServerProject = "Workshop/StateMachines/Server/StateMachines Server.csproj",
                ServerConfig = "Workshop/StateMachines/Server/StateMachinesServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62571/Quickstarts/StateMachinesServer",
                ClientProject = "Workshop/StateMachines/Client/StateMachines Client.csproj",
                ClientConfig = "Workshop/StateMachines/Client/StateMachinesClient.Config.xml",
                ClientEndpointSource = "Workshop/StateMachines/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "UserAuthentication",
                ServerProject = "Workshop/UserAuthentication/Server/UserAuthentication Server.csproj",
                ServerConfig = "Workshop/UserAuthentication/Server/Quickstarts.UserAuthenticationServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62565/Quickstarts/UserAuthenticationServer",
                ClientProject = "Workshop/UserAuthentication/Client/UserAuthentication Client.csproj",
                ClientConfig = "Workshop/UserAuthentication/Client/Quickstarts.UserAuthenticationClient.Config.xml",
                ClientEndpointSource = "Workshop/UserAuthentication/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "Views",
                ServerProject = "Workshop/Views/Server/Views Server.csproj",
                ServerConfig = "Workshop/Views/Server/Quickstarts.ViewsServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62561/Quickstarts/ViewsServer",
                ClientProject = "Workshop/Views/Client/Views Client.csproj",
                ClientConfig = "Workshop/Views/Client/Quickstarts.ViewsClient.Config.xml",
                ClientEndpointSource = "Workshop/Views/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "Aggregation",
                ServerProject = "Workshop/Aggregation/Server/Aggregation Server.csproj",
                ServerConfig = "Workshop/Aggregation/Server/Quickstarts.AggregationServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62541/AggregationServer",
                ClientProject = "Workshop/Aggregation/Client/Aggregation Client.csproj",
                ClientConfig = "Workshop/Aggregation/Client/Quickstarts.AggregationClient.Config.xml",
                ClientEndpointSource = "Workshop/Aggregation/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "AggregationConsole",
                ServerProject = "Workshop/Aggregation/ConsoleAggregationServer/ConsoleAggregationServer.csproj",
                ServerConfig = "Workshop/Aggregation/ConsoleAggregationServer/Quickstarts.AggregationServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62530/AggregationServer",
                ServerIsConsole = true,
            },
            new SampleDefinition
            {
                Name = "Reference",
                ServerProject = "Samples/ReferenceServer/Reference Server.csproj",
                ServerConfig = "Samples/ReferenceServer/Quickstarts.ReferenceServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer",
                ClientProject = "Samples/ReferenceClient/Reference Client.csproj",
                ClientConfig = "Samples/ReferenceClient/Quickstarts.ReferenceClient.Config.xml",
                ClientEndpointSource = "Samples/ReferenceClient/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "Sample",
                ServerProject = "Samples/Server.Net4/UA Sample Server.csproj",
                ServerConfig = "Samples/Server.Net4/Opc.Ua.SampleServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:51210/UA/SampleServer",
                ClientProject = "Samples/Client.Net4/UA Sample Client.csproj",
                ClientConfig = "Samples/Client.Net4/Opc.Ua.SampleClient.Config.xml",
                // the sample client discovers its endpoint, it has no hard coded url
                ClientEndpointSource = null,
            },
            new SampleDefinition
            {
                Name = "Gds",
                ServerProject = "Samples/GDS/Server/GlobalDiscoveryServer.csproj",
                ServerConfig = "Samples/GDS/Server/Opc.Ua.GlobalDiscoveryServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:58810/GlobalDiscoveryServer",
                ClientProject = "Samples/GDS/Client/GlobalDiscoveryClient.csproj",
                ClientConfig = "Samples/GDS/Client/Opc.Ua.GdsClient.Config.xml",
                ClientEndpointSource = "Samples/GDS/Client/MainForm.cs",
            },
            new SampleDefinition
            {
                Name = "GdsConsole",
                ServerProject = "Samples/GDS/ConsoleServer/NetCoreGlobalDiscoveryServer.csproj",
                ServerConfig = "Samples/GDS/ConsoleServer/Opc.Ua.GlobalDiscoveryServer.Config.xml",
                ServerUrl = "opc.tcp://localhost:58810/GlobalDiscoveryServer",
                ServerIsConsole = true,
            },
            new SampleDefinition
            {
                Name = "Lds",
                ServerProject = "Samples/LDS/ConsoleServer/ConsoleLds.csproj",
                // the local discovery server builds its configuration in code
                ServerConfig = null,
                ServerUrl = null,
                ServerIsConsole = true,
            },
        };

        /// <summary>
        /// The samples which have a server.
        /// </summary>
        public static IEnumerable<SampleDefinition> Servers => All.Where(sample => sample.ServerProject != null);

        /// <summary>
        /// The samples which have a client.
        /// </summary>
        public static IEnumerable<SampleDefinition> Clients => All.Where(sample => sample.ClientProject != null);

        /// <summary>
        /// The samples whose client hard codes the endpoint url of its server.
        /// </summary>
        public static IEnumerable<SampleDefinition> ClientsWithHardCodedUrl
            => All.Where(sample => sample.ClientEndpointSource != null && sample.ServerUrl != null);
    }
}
