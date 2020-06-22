
# Official OPC UA .Net Standard Samples from the OPC Foundation

## Overview

Sample Servers and Clients, including all required controls, for .NET Framework 4.6.2, .NET Core 2.0 and UWP 
Integrates the offical OPC UA [NuGet](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) package containing the OPC UA reference implementation targeting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/standard/net-standard).

.Net Standard allows you develop apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. 
Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

## For more information and license terms, see [here](http://opcfoundation.github.io/UA-.NETStandard).

## Features included
1. Sample Servers and Clients, including all required controls, for .Net 4.6, .NetCore 2.0 and UWP.
2. Sessions (including UI support in the samples).
3. Subscriptions (including UI support in the samples).
4. OPC UA [Aggregation Server](Workshop/Aggregation/README.md).
5. [OPC Classic adapter for OPC UA](ComIOP/README.md).
6. OPC UA [Global Discovery Client and Global Discovery Server](Samples/GDS/README.md).
7. OPC UA [Xamarin Client](Samples/XamarinClient/readme.md).
8. OPC UA [Quickstart Samples](Workshop).
9. The Core UA stack and SDK has been tested with Mono 5.4 to add support for the [Xamarin Client](Samples/XamarinClient/readme.md) and the Mono console application samples.

## Project Information

### General Project Info

[![Github top language](https://img.shields.io/github/languages/top/OPCFoundation/UA-.NETStandard)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github stars](https://img.shields.io/github/stars/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github forks](https://img.shields.io/github/forks/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github size](https://img.shields.io/github/repo-size/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases)
[![Nuget Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)

### Build Status

[![Travis Build Status](https://img.shields.io/travis/OPCFoundation/UA-.NETStandard/master?label=Travis)](https://travis-ci.org/OPCFoundation/UA-.NETStandard)
[![Build Status](https://img.shields.io/azure-devops/build/sysadmin0797/722e4f81-14ec-459b-aed2-c01ea07c7f3b/1/master?label=Azure%20Pipelines)](https://dev.azure.com/sysadmin0797/sysadmin/_build/latest?definitionId=1&_a=summary&repositoryFilter=1&branchFilter=2)
[![Build Status](https://img.shields.io/appveyor/build/opcfoundation-org/ua-netstandardlibrary/master?label=Appveyor)](https://ci.appveyor.com/project/opcfoundation-org/ua-netstandardlibrary)

### Code Quality

[![Test Status](https://img.shields.io/azure-devops/tests/sysadmin0797/sysadmin/1?style=plastic)](https://dev.azure.com/sysadmin0797/sysadmin/_build/latest?definitionId=1&_a=summary&repositoryFilter=1&branchFilter=2)
[![Coverage Status](https://img.shields.io/azure-devops/coverage/sysadmin0797/sysadmin/1/master?style=plastic)](https://dev.azure.com/sysadmin0797/sysadmin/_build/latest?definitionId=1&_a=summary&repositoryFilter=1&branchFilter=2)

## Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

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

## How to build and run the OPC UA COM Server Wrapper
- Please refer to the OPC Foundation UA .Net Standard Library [COM Server Wrapper](ComIOP/README.md) for a detailed description how to run the OPC COM wrapper.

## How to build and run the OPC UA Aggregation Client and Server
- Please refer to the OPC Foundation UA .Net Standard Library [Aggregation Client and Server](Workshop/Aggregation/README.md) for a detailed description how to run the aggregation client and server.

## How to build and run the OPC UA Xamarin Client
- Please refer to the OPC UA [Xamarin Client](Samples/XamarinClient/readme.md) for a detailed description how to run the UA Xamarin Client on UWP, Android and iOS.

## What happened to the OPC UA Web Telemetry sample?
The web telemetry sample was removed as there is a much more complete (and better looking!) solution now available [here](https://github.com/azure/azure-iot-connected-factory). You can try this new solution, called "Connected Factory", out [here](http://www.azureiotsuite.com).

## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
