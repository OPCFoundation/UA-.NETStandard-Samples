# UA .NET Standard Console Local Discovery Server (LDS / LDS-ME)

## Introduction
This sample is a minimal .NET console host for the managed OPC UA Local Discovery Server shipped in the NuGet
package [`OPCFoundation.NetStandard.Opc.Ua.Lds.Server`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/src/Opc.Ua.Lds.Server).
It implements the Local Discovery Server (LDS) and the Local Discovery Server with Multicast Extension (LDS-ME) as
specified in OPC UA Part 12, and is a managed, cross-platform replacement for the legacy C++ LDS.

The LDS answers `FindServers`, `FindServersOnNetwork`, `GetEndpoints`, `RegisterServer` and `RegisterServer2`. When
multicast is enabled it re-publishes registered endpoints over mDNS (LDS-ME) so other nodes on the local network can
discover them.

## How it is hosted
The server is wired up with the Generic Host and the OPC UA dependency-injection extensions:

```csharp
builder.Services
    .AddOpcUa()
    .AddLdsServer(options =>
    {
        options.ApplicationName = "Local Discovery Server";
        options.ApplicationUri = "urn:localhost:UA:LocalDiscoveryServer";
        options.ProductUri = "uri:opcfoundation.org:LocalDiscoveryServer";
        options.EndpointUrls.Add("opc.tcp://localhost:4840");
        options.EnableMulticast = true;                 // LDS-ME / mDNS
        options.AutoAcceptUntrustedCertificates = true; // sample convenience only
    })
    .AddOpcTcpTransport();
```

The canonical LDS discovery endpoint per OPC 10000-12 is `opc.tcp://<host>:4840`.

## How to build and run
1. Open a command prompt and navigate to **Samples/LDS/ConsoleServer**.
2. Run `dotnet run`.
3. The LDS listens on `opc.tcp://localhost:4840`. Press `Ctrl-C` to exit.

> `AutoAcceptUntrustedCertificates` is enabled purely for convenience so servers can register without pre-provisioning
> trust. Do **not** enable it in production; exchange the application instance certificates instead.

## Using it together with the GDS
Run this LDS side by side with the sample **NetCoreGlobalDiscoveryServer**:
- Point the GDS `RegistrationEndpoint` at this LDS so the GDS registers itself and becomes visible on the network.
- Add this LDS discovery URL to the GDS `<LdsScannerConfiguration>` extension so the GDS `scan` command can pull the
  servers this LDS has discovered and stage them for approval.

See [Samples/GDS/README.md](../../GDS/README.md) — section *Integrating the v2 Local Discovery Server (LDS) library
with the GDS* — for the full flow.
