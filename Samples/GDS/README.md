# UA .Net Standard Global Discovery Server and Client

## Introduction
This Global Discovery Server and Client implement the `Global Discovery and Certificate Management Server` profile as specified in the OPC Unified Architecture Specification Part 12: Discovery Release 1.03.

The Solution is split into these projects:
- **GlobalDiscoveryServer:** Global Discovery Server for .Net (Windows) that uses Entity Framework Core with a SQL server as registration and certificate database.
- **GlobalDiscoveryServerLibrary:** Common Global Discovery Server classes for .Net 4.6 and .Net Standard.
- **NetCoreGlobalDiscoveryServer:** Global Discovery Server for .Net Standard with Json database implementation to demonstrate the abstracted database registration and certificate authority interface. (The gdsdb.json is not a secure database and should only be used for testing).
- **GlobalDiscoveryClient:** Global Discovery Client for .Net 4.6. with Windows forms user interface.
- **GlobalDiscoveryClientControls:** Global Discovery Client reusable controls for .Net 4.6.
- **GlobalDiscoveryClientLibrary:** Common Global Discovery Client classes for .Net 4.6 and .Net Standard.
- **GlobalDiscoveryClientTest:** Unit tests for .Net Standard Global Discovery client and server libraries.

## How to build and run the Windows OPC UA Global Discovery Server
1. Open the solution **UA Global Discovery Server.sln** with VisualStudio.
2. Choose the project `GlobalDiscoveryServer` in the Solution Explorer and set it with a right click as `Startup Project`.
3. The server uses [Entity Framework Core](https://learn.microsoft.com/ef/core/) and requires a SQL server. By default the server connects to the data source `Data Source=(localdb)\MSSQLLocalDB`, the SQL Server Express LocalDB instance that is installed with Visual Studio. The default location for the database files is the user home directory. To use a different SQL server (for example a full SQL Server instance or another LocalDB instance name) modify the `gdsdbEntities` and `usersdbEntities` connection strings in the `app.config` file.
4. Hit `Ctrl-F5` to build and execute the sample.
5. The server loads and initializes all [Certificates](#certificates).
6. On the first start the server automatically creates the `gdsdb` and `usersdb` databases (via Entity Framework Core `EnsureCreated`) and seeds the required tables and default data from the embedded scripts `\DB\gdsdb.edmx.sql` and `\DB\usersdb.edmx.sql`. No manual database creation or script execution is required. The account used by the connection string must have permission to create databases on the target SQL server.
7. The server is now running and waiting for the connection of a GDS client. 

## How to build and run the console OPC UA Global Discovery Server on Windows, Linux and iOS
This section describes how to run the **NetCoreGlobalDiscoveryServer**.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. 

### Start the server 
1. Open a command prompt.
2. Now navigate to the folder **SampleApplications/Samples/GDS/NetCoreGlobalDiscoveryServer**.
3. Execute `dotnet restore`. This command calls into NuGet to restore the tree of dependencies. In latest .Net versions this command is optional.
4. To run the server type `dotnet run`. 
5. The server loads and initializes all [Certificates](#certificates).
6. The server is now running and waiting for the connection of a GDS client. 

## Integrating the v2 Local Discovery Server (LDS) library with the GDS
Starting with UA .NET Standard v2 the stack ships a managed Local Discovery Server as the NuGet package
[`OPCFoundation.NetStandard.Opc.Ua.Lds.Server`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/src/Opc.Ua.Lds.Server).
It implements the Local Discovery Server (LDS) and the Local Discovery Server with Multicast Extension (LDS-ME) as
specified in OPC UA Part 12, and is a managed, cross-platform replacement for the legacy C++ LDS.

### Roles of the LDS and the GDS
The LDS and the GDS are complementary discovery components and are meant to run side by side:
- The **LDS / LDS-ME** is a *local* directory. Servers on the same host register themselves with the LDS through
  `RegisterServer` / `RegisterServer2`, and the LDS-ME re-publishes those endpoints over mDNS so that other nodes on
  the local network can find them. Clients call `FindServers` and `FindServersOnNetwork` on the LDS to enumerate what
  is currently reachable. The LDS keeps only volatile, TTL-based records and performs **no** certificate management.
- The **GDS** is a *global*, persistent directory and certificate authority. It stores approved
  `ApplicationRecordDataType` entries in its database and issues/manages certificates and trust lists.

Integrating the two lets the GDS reuse the network view that the LDS-ME already maintains, instead of every server
having to register with the GDS explicitly. This is the feature tracked in
[issue #329 – *GDS autopopulate from LDSes*](https://github.com/OPCFoundation/UA-.NETStandard-Samples/issues/329):
the GDS periodically scans one or more LDSes for servers on the network and, **after an administrator approves them**,
adds records to its own database.

### Relevant LDS library API
The package exposes the pieces you need to host an LDS and to read what it has discovered:
- **`Opc.Ua.Lds.Server.LdsServer`** – a `DiscoveryServerBase` subclass that answers `FindServers`,
  `FindServersOnNetwork`, `GetEndpoints`, `RegisterServer` and `RegisterServer2`. Host it from a small executable the
  same way the sample GDS hosts `GlobalDiscoverySampleServer`.
- **`Opc.Ua.Lds.Server.IRegisteredServerStore`** – the backing store for explicit registrations and mDNS-observed
  network records. Useful members:
  - `IReadOnlyList<ServerOnNetworkRecord> SnapshotNetworkRecords()` – all currently known network records.
  - `(IList<ServerOnNetwork> records, DateTime lastCounterResetTime) ListOnNetwork(startingRecordId, maxRecordsToReturn, serverCapabilityFilter)` – the same paginated view returned by `FindServersOnNetwork`.
  - `IList<ApplicationDescription> Find(serverUriFilter, requestedLocaleIds)` – translated application descriptions for the active registrations.

### How the GDS pulls records from an LDS
From the GDS side the LDS is just another discovery endpoint, so the scan can be driven entirely with the standard
UA client discovery services — no direct reference to the LDS assembly is required:

1. **List the servers the LDS knows about.** Call `FindServersOnNetwork` on each configured LDS discovery URL
   (default `opc.tcp://<lds-host>:4840`). Each returned `ServerOnNetwork` record carries a `RecordId`,
   `ServerName`, `DiscoveryUrl` and the advertised `ServerCapabilities`. Page through the results using the
   `startingRecordId` / `maxRecordsToReturn` arguments exactly as the sample
   `ViewServersOnNetworkDialog` does against the GDS.
2. **Resolve full application information.** For every candidate `DiscoveryUrl`, call `FindServers` (and optionally
   `GetEndpoints`) to obtain the complete `ApplicationDescription` (application URI, product URI, application type,
   discovery URLs, and — from the endpoints — the application instance certificate).
3. **Stage the candidates for approval.** Do **not** register discovered servers automatically. Present them to a
   user holding the **`DiscoveryAdmin`** role (see [GDS Users](#gds-users)); auto-populating the GDS without review
   would let any server on the network insert itself into the global directory.
4. **Register approved applications.** Map each approved `ApplicationDescription` to an `ApplicationRecordDataType`
   and persist it through the GDS applications database, e.g. `ApplicationsDatabaseBase.RegisterApplication(...)`
   (implemented by `SqlApplicationsDatabase` for the Windows/SQL server and by `JsonApplicationsDatabase` for the
   .NET console server). Once registered, the application can request a CA-signed certificate and trust list through
   the normal GDS pull workflow.

This scan-and-stage flow is implemented in the **NetCoreGlobalDiscoveryServer** sample. Once the console GDS is
running, type `scan` at the prompt to enumerate the configured LDSes, review each discovered server, and register the
ones you approve (`help` lists the available commands). The scanner lives in
[`Samples/GDS/ConsoleServer/LdsGdsScanner.cs`](ConsoleServer/LdsGdsScanner.cs) and the list of LDS discovery URLs it
scans is read from the `<LdsScannerConfiguration>` extension in `Opc.Ua.GlobalDiscoveryServer.Config.xml` (see
[Configuration](#configuration) below). A companion LDS host you can scan against ships as the
[**ConsoleLds**](../LDS/ConsoleServer) sample.

The following sketch shows the scan-and-stage step (the approval gate and the final `RegisterApplication` call are
left to the host application):

```csharp
// 'ldsUrl' is a configured LDS discovery URL, e.g. "opc.tcp://lds-host:4840".
// 'discovery' is an Opc.Ua.DiscoveryClient created for that URL.
var candidates = new List<ServerOnNetwork>();
uint startingRecordId = 0;

while (true)
{
    // FindServersOnNetwork mirrors IRegisteredServerStore.ListOnNetwork on the LDS side.
    var onNetwork = await discovery.FindServersOnNetworkAsync(
        startingRecordId,
        maxRecordsToReturn: 100,
        serverCapabilityFilter: null);

    if (onNetwork.Servers.Count == 0)
    {
        break;
    }

    candidates.AddRange(onNetwork.Servers);
    startingRecordId = onNetwork.Servers.Max(s => s.RecordId) + 1;
}

foreach (var server in candidates)
{
    // Resolve the full ApplicationDescription(s) behind each discovery URL.
    using var serverDiscovery = DiscoveryClient.Create(new Uri(server.DiscoveryUrl));
    ApplicationDescriptionCollection apps = await serverDiscovery.FindServersAsync(null);

    // -> hand 'apps' to a DiscoveryAdmin for approval, then map the approved
    //    entries to ApplicationRecordDataType and call RegisterApplication on
    //    the GDS applications database.
}
```

### Configuration
Two related settings already exist in the GDS config (`Opc.Ua.GlobalDiscoveryServer.Config.xml`, under
`ServerConfiguration`):
- **`RegistrationEndpoint`** – the discovery endpoint the GDS *registers itself* with on start-up. In the sample it
  points at `opc.tcp://localhost:4840`, i.e. a local LDS. Point this at your LDS so the GDS becomes visible through it.
- **`MultiCastDnsEnabled`** – set to `true` to let the GDS announce itself over mDNS through an LDS-ME.

The scan direction (the GDS *reading* servers from one or more LDSes) is not represented by an existing element, so
the sample adds its own `<LdsScannerConfiguration>` extension element with a list of LDS discovery URLs, which the
`scan` command reads when driving the scan above (see
[`Samples/GDS/ConsoleServer/LdsScannerConfiguration.cs`](ConsoleServer/LdsScannerConfiguration.cs)).
The default LDS discovery URL is `opc.tcp://<lds-host>:4840`. In every case make sure the GDS trusts each LDS
application certificate (copy it from the GDS **rejected** store to **trusted/certs**, see
[GDS Certificate stores](#gds-certificate-stores)) so the discovery calls can use a secure channel.

## GDS Users
The sample GDS servers only implement the username/password authentication. The following combinations can be used to connect to the servers:
- **DiscoveryAdmin**
  - PW: **demo**
  - This Role grants rights to register, update and unregister any OPC UA Application.
  - see spec (Roles and Privileges)[https://reference.opcfoundation.org/GDS/v105/docs/6.2]
- **CertificateAuthorityAdmin**
  - PW: **demo**
  - This Role grants rights to request or revoke any Certificate, update any TrustList or assign CertificateGroups to OPC UA Applications.
  - see spec (Roles and Privileges Part 2)[https://reference.opcfoundation.org/GDS/v105/docs/7.2]
- **System Administrator:** 
  - Username: **sysadmin**, PW: **demo**
  - This user is defined for server push management and has the ability to access the server configuration nodes of the GDS server to update the server certificate and the trust lists. Server push configuration management is not a requirement for a GDS server and only supported here to demonstrate the functionality.
  - Roles: CertificateAuthorityAdmin, DiscoveryAdmin, SecurityAdmin, ConfigureAdmin 
*Deprecated*
- **GDS Administrator:** 
  - Username: **appadmin**, PW: **demo**
  - This user has the ability to register and unregister applications and to issue new certificates. It should be used by the GDS Client application to connect.
- **GDS User:**
  - Username: **appuser**, PW: **demo**
  - This user has only a limited ability to search for applications.

## Certificates
The global discovery server creates the CA certificates for all configured certificate groups on the first start.

By default, a global discovery server accepts any incoming secure connection with an authenticated user ([GDS Users](#gds-users)). 

The console server certificates are stored in **%LocalApplicationData%/OPC Foundation/GDS/PKI** while the Windows .Net 4.6 server stores the certificates in **%CommonApplicationData%\OPC Foundation\GDS\PKI**. 
**%CommonApplicationData%** maps to the path set by the environment variable **ProgramData** on Windows. 

On Linux and macOS **%LocalApplicationData%** maps to **~/root/.local/share**.

On Windows **%LocalApplicationData%** maps to **%USERPROFILE%\AppData\Local**. 

### GDS Certificate stores
Under **PKI**, the following stores contain certificates under **certs**, CRLs under **crl** or private keys under **private**.
- **own** contains the GDS public certificate and private key.
- **rejected** contains the rejected client certificates. To trust a client certificate, copy the rejected certificate to the **trusted/certs** folder.
- **trusted** contains *trusted* client and CAs certificates and CRLs.
- **issuers** contains CAs certificates and CRLs needed for validation of certificate chains.

### GDS CA Certificate stores
Under **PKI**, the following stores contain certificates under **certs**, CRLs under **crl** or private keys under **private**.
- **authorities** contains the public certificates, CRLs and private keys of the CA authorities.
- **applications** contains the public certificates of all applications registered with the GDS.
- **PKI/CA** contains folders for all supported certificate groups. At this point only the `DefaultApplicationGroup` **default** is supported.
  - **PKI/CA/default** contains the *issuer* and *trusted* list for the default application group. Each store contains the CA certificates and CRLs.

### Customize the GDS CA Certificates
To customize the CA certificate search for `<SubjectName>CN=IOP-2017 CA, O=OPC Foundation</SubjectName>` and enter your new subject. Then search the code and the configuration files for `SomeCompany` and enter your company name as appropriate.

## How to build and run the Windows OPC UA Global Discovery Client
1. Open the solution **UA Global Discovery Server.sln** with VisualStudio.
2. Choose the project `GlobalDiscoveryClient` in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `Ctrl-F5` to build and execute the sample.
4. Press the `Registration` button to connect to a running GDS. Use the `GDS Administrator` credentials in [GDS Users](#gds-users) to connect and to be able to register applications and to issue certificates.
5. Select the appropriate `Registration Type`: [Client or Server Pull Management](#client-or-server-pull-management) or [Server Push Management](#server-push-management) and proceed with the registration.

### Client or Server Pull Management
1. Always `Clear` registration form to start a new or to update an existing registration.
2. Register the application in one of the described ways under [Pull Registration](#pull-registration).
3. Press the `Certificate` button. Inspect an existing certificate in the form. To issue a CA signed certificate press `Request New` certificate which triggers either a certificate signing request or a new keypair request, whichever is more appropriate. After a short while the new CA signed certificates are issued and the GDS client may ask to override existing certificates.
4. Press the `Trust List` button. Inspect the existing trusted and issuer list of the application. To add the CA certificate and the CRL to the trusted list press the `Merge with GDS` button.

### Pull Registration

#### Manually enter the registration for an application
1. In this case the entries in the `Client -` or `Server - Pull management` form must be filled in. Some fields are *ignored* if the application type is Client, some fields are *optional*.
- Application ID: The unique identifier *assigned by the GDS* to the application.
- Application Name: The default name of the Application.
- Application URI: The URI for the Application. This URI is also stored in the application certificate extensions.
- Product URI: A globally unique URI for the product associated with the Application. This URI is assigned by the vendor of the Application.
- Discovery URLs: The list of discovery URLs for a *Server Application*.
- Server Capabilities: The list of *Server* capability identifiers for the Application.
- To use an existing store with or without existing public/private key:
  - Certificate Store Path: local X509 store (CurrentUser\UA_MachineDefault) or directory store.
  - Certificate Subject Name: The certificate distinguished name.
- or the path to new or existing public/private key pair:
  - Certificate Public Key Path: A DER encoded certificate with a public key.
  - Certificate Private Key Path: A PFX or PEM encoded private key.
- Trust List Store Path: *optional* to copy the GDS CA public certificate to the trusted store.
- Issuer List Store Path: *optional* to copy the GDS CA public certificate to the issuer store.
- Domains: Enter the domain names to be added to the certificate extension as hostnames or IP addresses.
2. `Register` the application or `Apply Changes`. 
3. The `Application ID` should display a proper NodeId after registration.
4. `Save` the configuration for future use.
5. Continue with `Certificate`and `Trust List`management.

#### Load the existing certificate to register an application
The manual registration is simplified if there is already an existing certificate available, with or without private key.
1. Select `Client`or `Server - Pull Management`.
2. Load existing certificate in the `Certificate Public Key Path` field.
3. Fill in remaining fields.
4. Continue with step 2 in the previous section.

#### Load registration from a sample configuration file
The GDS client can fill in the full information from a UA .Net Standard application configuration. However, for legacy .Net applications Windows certificate stores are not permitted.
1. `Load` configuration, for example chose **UA-.NETStandard\SampleApplications\Samples\Client.Net4\Opc.Ua.SampleClient.Config.xml**
2. The registration type is `Server - Pull management`, because the UA Sample Client is also a server.
2. `Register` the UA Sample Client. The `Application ID` should now contain a valid NodeId.
3. Press the `Certificate` button and `Request New` certificate. After a short while the UA Sample Server/Client certificate is updated with a CA signed application certificate.
4. Press the `Trust List` button to add the CA certificate and CRL with `Merge with GDS` to the application trusted store.
5. UA Sample Client is now ready to use and trust the GDS issued and CA signed certificates.

![UA Sample Server Registration](doc/UASampleServerRegistration.png "UA Sample Client Registration")

### Server Push Management
Push configuration requires server configuration node support and a session with the managed server. 
1. Select `Server - Push Management`
2. Press `Pick Server` to connect to the managed server. Special system administrator credentials might be necessary to access the server configuration nodes - see [GDS Users](#gds-users).
3. Fill the remaining registration fields which can not be extracted from the server endpoint information.
2. `Register` the application or `Apply Changes`. 
3. The `Application ID` should display a proper NodeId after registration.
6. Press the `Server Status` and then the `green arrow` connect button to inspect the status. Being connected is mandatory to remote manage the server in the next steps.
3. Press the `Certificate` button. Inspect an existing certificate in the form. To issue a CA signed certificate press `Request New` certificate, which triggers a certificate signing request. After a short while the new CA signed certificates is updated on the server directly. After the update, the GDS client user might be asked to `Apply Changes` in the `Server Status` form.
8. Press the `Trust List` button. `Reload`the trust list from the managed server. Manage the certificates and `Merge with GDS` to add the GDS CA certificate to the trust list. `Push To Server` to save the updated trust list on the server.
8. Press the `Server Status` button and then press `Apply Changes` to update the security settings on the server. After a regular certificate update the managed server may require a reboot or at least closes all sessions and requires a reconnect. Press the `green arrow` connect button to reconnect to the server using the new certificate.  
9. Press the `Certificate` button and inspect the new CA signed certificate to verify the new certificate is being used for the new session.


