
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
5. [OPC Classic adapter for OPC UA](ComIOP/README.md).
6. OPC UA [Global Discovery Client and Global Discovery Server](Samples/GDS/README.md).
7. OPC UA [Xamarin Client](Samples/XamarinClient/readme.md).
8. OPC UA [Quickstart Samples](Workshop).
9. The Core UA stack and SDK has been tested with Mono 5.4 to add support for the [Xamarin Client](Samples/XamarinClient/readme.md) and the Mono console application samples.

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

## How to build and run the OPC UA COM Server Wrapper
- Please refer to the OPC Foundation UA .Net Standard Library [COM Server Wrapper](ComIOP/README.md) for a detailed description how to run the OPC COM wrapper.

## How to build and run the OPC UA Aggregation Client and Server
- Please refer to the OPC Foundation UA .Net Standard Library [Aggregation Client and Server](Workshop/Aggregation/README.md) for a detailed description how to run the aggregation client and server.

## How to build and run the OPC UA Xamarin Client
- Please refer to the OPC UA [Xamarin Client](Samples/XamarinClient/readme.md) for a detailed description how to run the UA Xamarin Client on UWP, Android and iOS.

## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
