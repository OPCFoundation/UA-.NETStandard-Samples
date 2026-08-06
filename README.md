
# Official OPC UA .Net Standard Samples from the OPC Foundation

## Help wanted!

The samples in this repository are hosted by the OPC Foundation to keep the sample code available for the community. 

However, to avoid internal competition with OPC members that offer commercial SDKs, the OPC Foundation limits its own resources for maintenance and support to a minimum.

Contributions of the community to improve and maintain the samples and to help with issues to keep this repository alive are very welcome.

Please follow the steps outlined [here](#contributing) to start contributing.

## Overview

Sample Servers and Clients, including all required controls, for .NET Framework 4.6.2, .NET Core 2.0 and UWP.

Integrates the offical OPC UA [NuGet](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) package containing the OPC UA reference implementation targeting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/standard/net-standard).

.Net Standard allows you develop apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. 
Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

## For more information and license terms, see [here](http://opcfoundation.github.io/UA-.NETStandard).

## Features included
1. Sample Servers and Clients, including all required controls, for .NET 4.6.2, .NET Core >= 2.1, and UWP.
2. Sessions (including UI support in the samples).
3. Subscriptions (including UI support in the samples).
4. OPC UA [Aggregation Server](Workshop/Aggregation/README.md).
5. OPC Classic adapter for OPC UA. (Removed in 12/2024)
6. OPC UA [Global Discovery Client and Global Discovery Server](Samples/GDS/README.md).
7. OPC UA Xamarin Client. (Removed in 12/2024)
8. OPC UA [Quickstart Samples](Workshop).
9. The Core UA stack and SDK has been tested with Mono 5.4 to add support for the Xamarin Client and the Mono console application samples. (Removed as of 12/2024)

## Sample Projects Overview

Use this table to quickly find the project that suits your needs. If you already have an external OPC UA server (e.g. KepServerEX, Prosys, or another vendor server), you only need to run a **Client** sample and point it at your server's endpoint URL.

### Core Samples (`Samples/`)

| Project | Type | Description |
|---------|------|-------------|
| [ReferenceServer](Samples/ReferenceServer) | **Server** | The OPC UA Reference Server. Implements a rich address space designed for OPC UA Compliance Test Tool (UACTT) testing. Use this as a standards-compliant server to test clients against. |
| [ReferenceClient](Samples/ReferenceClient) | **Client** | A Windows Forms OPC UA client that can connect to any OPC UA server. Use this if you want to browse, read, write, subscribe to nodes, or test client connectivity against an external server such as KepServerEX. |
| [UA Sample Server (.NET 4.6)](Samples/Server.Net4) | **Server** | A feature-rich OPC UA sample server with Windows Forms UI for .NET Framework 4.6. Demonstrates sessions, subscriptions and a sample address space. |
| [UA Sample Client (.NET 4.6)](Samples/Client.Net4) | **Client** | A feature-rich OPC UA sample client with Windows Forms UI for .NET Framework 4.6. Can connect to any OPC UA server. Use this to explore and interact with an existing server. |
| [GlobalDiscoveryServer](Samples/GDS/Server) | **Server** | OPC UA Global Discovery Server (GDS) for .NET 4.6 with SQL Server as database. Manages application registration and certificate issuance across a UA network. |
| [NetCoreGlobalDiscoveryServer](Samples/GDS/ConsoleServer) | **Server** | Cross-platform console GDS server using a JSON-based database. Use for testing GDS workflows without SQL Server. |
| [GlobalDiscoveryClient](Samples/GDS/Client) | **Client** | Windows Forms GDS client. Use this to register applications, request CA-signed certificates, and manage trust lists via a running GDS server. |

### Quickstart Workshop Samples (`Workshop/`)

These paired client/server samples each demonstrate a specific OPC UA feature set. If you have an external server that supports the relevant feature, you can run only the **Client** against it.

| Project | Type | Description |
|---------|------|-------------|
| [Aggregation Server](Workshop/Aggregation/Server) | **Server** | Aggregates multiple OPC UA servers into a single namespace. Browse aggregated servers via the Aggregation Client or any OPC UA client. |
| [Aggregation Client](Workshop/Aggregation/Client) | **Client** | Connects to an Aggregation Server and browses its aggregated address space. |
| [ConsoleAggregationServer](Workshop/Aggregation/ConsoleAggregationServer) | **Server** | Cross-platform console version of the Aggregation Server. |
| [AlarmCondition Server](Workshop/AlarmCondition/Server) | **Server** | Demonstrates OPC UA Alarms & Conditions (A&C). Generates alarm events of various types. |
| [AlarmCondition Client](Workshop/AlarmCondition/Client) | **Client** | Subscribes to and displays OPC UA alarm events from an A&C server. |
| [Boiler Server](Workshop/Boiler/Server) | **Server** | Simulates a boiler process with UA object instances. Demonstrates object types, methods and data variables. |
| [Boiler Client](Workshop/Boiler/Client) | **Client** | Connects to the Boiler Server to read boiler state and invoke control methods. |
| [DataAccess Server](Workshop/DataAccess/Server) | **Server** | Demonstrates the OPC UA DataAccess profile with analog items, discrete items and array variables. |
| [DataAccess Client](Workshop/DataAccess/Client) | **Client** | Reads and writes DataAccess items (analog/discrete) on a DataAccess server. |
| [DataTypes Server](Workshop/DataTypes/Server) | **Server** | Demonstrates how to define and expose custom structured data types in OPC UA. |
| [DataTypes Client](Workshop/DataTypes/Client) | **Client** | Reads custom structured data types from a DataTypes server. |
| [HistoricalAccess Server](Workshop/HistoricalAccess/Server) | **Server** | Stores historical data for variables and supports UA HistoricalAccess services (ReadRaw, ReadProcessed, etc.). |
| [HistoricalAccess Client](Workshop/HistoricalAccess/Client) | **Client** | Queries and displays historical data from a HistoricalAccess server. |
| [HistoricalEvents Server](Workshop/HistoricalEvents/Server) | **Server** | Stores historical events and supports UA HistoricalAccess for events. |
| [HistoricalEvents Client](Workshop/HistoricalEvents/Client) | **Client** | Queries and displays historical events from a HistoricalEvents server. |
| [Methods Server](Workshop/Methods/Server) | **Server** | Exposes UA methods with various input/output arguments to demonstrate method invocation. |
| [Methods Client](Workshop/Methods/Client) | **Client** | Discovers and calls UA methods on a Methods server. |
| [PerfTest Server](Workshop/PerfTest/Server) | **Server** | Performance test server that exposes a large number of monitored variables for throughput benchmarking. |
| [PerfTest Client](Workshop/PerfTest/Client) | **Client** | Performance test client that subscribes to many variables and measures data-change throughput. |
| [SimpleEvents Server](Workshop/SimpleEvents/Server) | **Server** | Generates simple OPC UA events to demonstrate event subscriptions. |
| [SimpleEvents Client](Workshop/SimpleEvents/Client) | **Client** | Subscribes to and displays simple events from an events server. |
| [UserAuthentication Server](Workshop/UserAuthentication/Server) | **Server** | Demonstrates OPC UA user authentication with username/password and certificate-based identity tokens. |
| [UserAuthentication Client](Workshop/UserAuthentication/Client) | **Client** | Connects using different user identity tokens (anonymous, username, certificate) to a UserAuthentication server. |
| [Views Server](Workshop/Views/Server) | **Server** | Demonstrates OPC UA Views — subsets of the address space exposed as named views. |
| [Views Client](Workshop/Views/Client) | **Client** | Browses and reads nodes through Views defined on a Views server. |
| [Empty Server](Workshop/Empty/Server) | **Server** | A minimal server template with no custom nodes. Use as a starting point to build your own OPC UA server. |
| [Empty Client](Workshop/Empty/Client) | **Client** | A minimal client template. Use as a starting point to build your own OPC UA client. |

> **Quick tip:** If you already have an OPC UA server running (e.g. KepServerEX, Prosys OPC UA Server, or another vendor product), you do **not** need to start any server sample. Simply run the **ReferenceClient** or **UA Sample Client** and connect to your server's endpoint URL (e.g. `opc.tcp://myserver:49320`).

## Project Information

### General Project Info

[![Github top language](https://img.shields.io/github/languages/top/OPCFoundation/UA-.NETStandard-Samples)](https://github.com/OPCFoundation/UA-.NETStandard-Samples)
[![Github stars](https://img.shields.io/github/stars/OPCFoundation/UA-.NETStandard-Samples?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard-Samples)
[![Github forks](https://img.shields.io/github/forks/OPCFoundation/UA-.NETStandard-Samples?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard-Samples)
[![Github size](https://img.shields.io/github/repo-size/OPCFoundation/UA-.NETStandard-Samples?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard-Samples)

### Build Status

[![Build Status](https://opcfoundation.visualstudio.com/opcua-netstandard/_apis/build/status/OPCFoundation.UA-.NETStandard-Samples?branchName=master)](https://opcfoundation.visualstudio.com/opcua-netstandard/_build/latest?definitionId=12&branchName=master)

## Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

## Debugging the Opc.Ua.Core Nuget packages

Since Nuget version 1.4.363.107 there is support for symbol snupkg packages on Nuget.Org and github source link. 

- See [devblog](https://devblogs.microsoft.com/nuget/improved-package-debugging-experience-with-the-nuget-org-symbol-server/) for more information on how to setup the debug symbol server.
- Support for [Sourcelink](https://docs.microsoft.com/en-us/dotnet/standard/library-guidance/sourcelink) for integrated source code debugging experience.

<a name="certificates"/>

## Self signed certificates for the sample applications

All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store.

### Windows .Net applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\CertificateStores** with a root folder which is specified by the `SpecialFolder` variable **%CommonApplicationData%**. On Windows 7/8/8.1/10 this is usually the invisible folder **C:\ProgramData**. 

Note: Since the sample applications in the UA-.Net repository use the same storage and application names as UA-.NetStandard, but create only certificates with hostname `localhost`, it is recommended to delete all existing certificates in **MachineDefault** to recreate proper certificates for all sample applications when moving to the UA-.NetStandard repository. 

### Windows UWP applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). 

The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\CertificateStores** in the **LocalState** folder of the installed universal windows package. Deleting the application state also deletes the certificate stores.

### .Net Standard Console applications on Windows, Linux, iOS etc.
The self signed certificates are stored in a folder called **OPC Foundation/CertificateStores/MachineDefault** with a root folder which is specified by the `SpecialFolder` variable **%LocalApplicationData%** or in a **X509Store** called **CurrentUser\\My**, depending on the configuration. For best cross platform support the personal store **CurrentUser\\My** was chosen to support all platforms with the same configuration. Some platforms, like macOS, do not support arbitrary certificate stores.

The *trusted*, *issuer* and *rejected* stores remain in a shared folder called **OPC Foundation\CertificateStores** with a root folder specified by the `SpecialFolder` variable **%LocalApplicationData%**. Depending on the target platform, this folder maps to a hidden locations under the user home directory.

## Local Discovery Server
By default all sample applications are configured to register with a Local Discovery Server (LDS). A reference implementation of a LDS for Windows can be downloaded [here](https://opcfoundation.org/developer-tools/developer-kits-unified-architecture/local-discovery-server-lds). To setup trust with the LDS the certificates need to be exchanged or registration will fail.

## How to build and run the samples in Visual Studio on Windows

1. Open the UA Sample Applications.sln solution file using Visual Studio 2017.  
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console samples on Windows, Linux and iOS
This section describes how to run the **NetCoreConsoleClient** and **NetCoreConsoleServer** sample applications.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. As of today .Net Standard 2.0 is required.

### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore UA Sample Applications.sln`. This command calls into NuGet to restore the tree of dependencies.

### Start the server 
1. Open a command prompt. 
2. Navigate to the folder **Samples/NetCoreConsoleServer**. 
3. To run the server sample type `dotnet run --project NetCoreConsoleServer.csproj -a`. 
    - The server is now running and waiting for connections. 
    - The `-a` flag allows to auto accept unknown certificates and should only be used to simplify testing.

### Start the client 
1. Open a command prompt 
2. Navigate to the folder **Samples/NetCoreConsoleClient**. 
3. To run the sample type `dotnet run --project NetCoreConsoleClient.csproj -a` to connect to the OPC UA console sample server running on the same host. 
    - The `-a` flag allows to auto accept unknown certificates and should only be used to simplify testing. 
    - To connect to another OPC UA server specify the server as first argument and type e.g. `dotnet run --project NetCoreConsoleClient.csproj -a opc.tcp://myserver:51210/UA/SampleServer`. 
4. If not using the `-a` auto accept option, on first connection, or after certificates were renewed, the server may have refused the client certificate. Check the server and client folder **%LocalApplicationData%/OPC Foundation/CertificateStores/RejectedCertificates** for rejected certificates. To approve a certificate copy it to the **%LocalApplicationData%/OPC Foundation/CertificateStores/UA Applications** folder.
5. Retry step 3 to connect using a secure connection.

## How to build and run the OPC UA COM Server Wrapper (Removed as of 12/2024)

## How to build and run the OPC UA Aggregation Client and Server
- Please refer to the OPC Foundation UA .Net Standard Library [Aggregation Client and Server](Workshop/Aggregation/README.md) for a detailed description how to run the aggregation client and server.

## How to build and run the OPC UA Xamarin Client (Removed as of 12/2024)

## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
