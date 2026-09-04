# Opc.Ua.Samples.Hosting

The bootstrap the sample and workshop applications share: dependency injection, the .NET
Generic Host, and file based logging. It exists so that ~30 sample entry points do not each
repeat the same twenty lines of configuration loading, certificate handling and telemetry
plumbing.

## What a sample looks like

A Windows Forms server registers its composition root - its configuration XML file and the
node managers the server is made of - and shows the running server in the shared server form:

```csharp
[STAThread]
static void Main(string[] args)
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

    SampleWinFormsHost.Run(
        args,
        services => services.AddBoilerServer(),
        ServerForm.Create,
        ExceptionDlg.Show);
}
```

The composition root lives next to the entry point (`BoilerServerHosting.cs`), so the tests
host the sample through exactly the registration its `Main` uses:

```csharp
public static IServiceCollection AddBoilerServer(
    this IServiceCollection services,
    string configurationFile = null,
    Action<ApplicationConfiguration> configure = null)
{
    return services.AddSampleServer(
        configurationFile ?? "BoilerServer.Config.xml",
        server => server.AddNodeManager<BoilerNodeManagerFactory>(),
        configure);
}
```

That is the whole server side of a sample. `AddSampleServer` hands the configuration file to
the hosted server of the stack (`services.AddOpcUa().AddServer(configurationFile)` under the
hood) and the callback to the server builder of the stack, `IOpcUaServerBuilder`, which is
where a sample says what its server is made of: `AddNodeManager<TFactory>()` makes the
container create the factory and the hosted server hand it to the server before the server
starts. No sample has a server class: the server is the `SampleServer` of this library, the
`DependencyInjectionStandardServer` of the stack, which resolves its role manager, session
and subscription managers and the other hooks of `StandardServer` from the container.

Everything a server class used to override is a registration on the same builder:

| Used to be an override of        | Is now                                                                 |
|----------------------------------|------------------------------------------------------------------------|
| `CreateRoleManager` (identities) | `server.ConfigureRoles(roles => ...)` of the stack                     |
| `OnServerStarted` (authenticators) | `server.AddIdentityAuthenticator(...)` - the stack's `<T>()` overload, or the factory overload of this library for the callback based authenticators of the stack |
| `OnServerStarted` / `OnNodeManagerStarted` (anything else) | `server.AddStartupTask<T>()`, an `IServerStartupTask` of the stack, run once with the live server |
| `LoadServerProperties`           | derived from the configuration by `SampleServer` - application name, product URI, assembly build |
| `AddNodeManager(new Factory(...))` in a constructor | `server.AddNodeManager(factory)` for an instance, `server.AddNodeManagers(provider => ...)` for factories which need the loaded configuration |

A Windows Forms client loads the same kind of file eagerly, because its main form takes the
`ApplicationConfiguration` in its constructor:

```csharp
SampleWinFormsHost.Run<MainForm>(
    args,
    services => services
        .AddSampleApplication(options => {
            options.ApplicationType = ApplicationType.Client;
            options.ConfigurationFile = "BoilerClient.Config.xml";
        }),
    ExceptionDlg.Show);
```

A sample whose user interface is built around the `ApplicationInstance` itself - the UA sample
server, the UA sample client which is a server as well, the GDS server - registers its
application first and runs its server on it, with the same builder callback:

```csharp
services
    .AddSampleApplication(options => {
        options.ApplicationType = ApplicationType.Server;
        options.ConfigurationFile = "Opc.Ua.SampleServer.Config.xml";
    })
    .AddSampleServer(server => server.AddUaSampleServer());
```

A console server builds its own host on the same registration:

```csharp
HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

builder.Logging.AddSampleConsole();
builder.Services.AddAggregationServer(configure: RejectUntrustedCertificatesLoudly);

await builder.Build().RunAsync();
```

## The server path: the hosted server of the stack

`AddSampleServer(configurationFile, configureServer)` registers the server through the
dependency injection surface of the stack: `services.AddOpcUa().AddServer(configurationFile)`
loads the configuration XML document of the sample and owns the application instance, the
certificate check and the server lifetime, and `configureServer` receives the
`IOpcUaServerBuilder` of the stack. Every setting in the file applies exactly as on the classic
`ApplicationInstance` path. `AddSampleServer(configureServer)` - no file - does the same on the
application instance registered by `AddSampleApplication`, which the hosted server of the stack
takes through its `IOpcUaApplicationConfigurationProvider` seam. On top of the stack the sample
registration adds what the samples rely on:

1. the hosted server starts the server instance of the *container* (through
   `IOpcUaServerFactory`), so the main form resolves the same running server -
   also published as the shared `StandardServer`, which `ServerForm.Create` takes,
2. the loaded `ApplicationConfiguration` is captured (through the loaded-configuration
   callback of the stack) and registered for the forms,
3. the log file the configuration names is attached the moment the file has been read,
4. a readiness gate (`SampleServerReadyHostedService`, driven by an `IServerStartupTask`
   registered after the parts of the sample) holds the start of the host until the server is
   listening and the startup tasks of the sample have run, or fails it with the original
   error - the hosted server of the stack is a `BackgroundService`, which by itself starts in
   the background.

The optional `configure` callback runs right after the file has been read and before
certificates are checked, for the settings a configuration file cannot express, such as a
certificate validation callback.

### The `AddSampleServer<TServer>` overloads

For the rare server which has to override a hook of `StandardServer` the stack offers no
registration for. Two samples use them, for two different reasons:

- the reference server runs the `ReferenceServer` class of the quickstart library of the
  stack, which is a server class by design;
- the alias names server materializes the alias nodes of the standard `TagVariables` category
  in a configuration node manager of its own, which needs the store registered before the
  address space is built - earlier than the `AddAliasNameStore` registration of the stack makes
  it. Its `AliasNamesServer` derives from `SampleServer` and overrides that one hook; roles,
  authenticator and node managers are registered on the builder like everywhere else.

A server the container cannot create - the GDS server takes databases and certificate groups -
is registered by the sample before the call (`services.AddSingleton(CreateServer)`), and the
`TryAddSingleton<TServer>()` of the library then leaves it alone.

### Node managers which depend on the configuration

The hosted server of the stack collects the registered node manager factories before it reads
the configuration file, so a factory type registered with `AddNodeManager<TFactory>()` cannot
take the `ApplicationConfiguration` in its constructor. Two samples need the configuration to
know what to serve, and show the two ways around that:

- the file transfer server's `FileTransferNodeManagerFactory` takes the container and reads the
  configuration the first time the server asks it for its namespace or for the node manager -
  both happen with the configuration loaded;
- the aggregation server needs *one factory per configured endpoint*, so its composition root
  uses `AddNodeManagers(provider => ...)`: the callback runs with the configuration loaded,
  right before the hosted server starts the server, and the factories it returns are added
  next to the registered ones. The reverse connect manager the node managers share is a hosted
  service registered after the server, so it starts once the server listens.

The GDS servers register their `ManagedApplications` and onboarding registrar node managers as
factory types whose stores come from the container, and the sample GDS server creates the node
managers of every registered factory next to the GDS one (the GDS base server of the stack
builds its master node manager without looking at registered factories).

### What the stack does not offer yet

The library papers over a few seams the hosted server of the stack lacks on this SDK version;
each is one small class here and should move into the stack
([UA-.NETStandard#4410](https://github.com/OPCFoundation/UA-.NETStandard/issues/4410)):

- the product description (`ServerProperties`) can only come from an override, hence
  `SampleServer.LoadServerProperties`;
- `AddIdentityAuthenticator` has no factory overload, so the callback based
  `UserNamePasswordAuthenticator` and `X509Authenticator` of the stack cannot be registered
  through it - `SampleServerBuilderExtensions.AddIdentityAuthenticator(factory)` fills in;
- there is no builder method for `IServerStartupTask`, nor an instance overload of
  `AddNodeManager`, nor a registration for node managers created after the configuration load;
- alias stores registered with `AddAliasNameStore` are not materialized as nodes;
- `DependencyInjectionStandardServer` does not reverse connect to clients (`ReverseConnectServer`
  is a separate class), so the aggregation and UA sample servers no longer do either - their
  configurations had no reverse connect clients enabled.

## The client path: an eager `ApplicationInstance`

`AddSampleApplication(...)` registers `SampleApplication`, and through it the
`ApplicationInstance` and the `ApplicationConfiguration` of the sample, plus one
`IHostedService` which reads the configuration when the host starts:

1. read the configuration file named by `ConfigurationFile`,
2. apply `ConfigureConfiguration`, for settings the configuration file cannot express,
3. attach the log file the configuration names,
4. make sure the application instance certificate is usable.

The client samples stay on this path on purpose: their forms take the loaded
`ApplicationConfiguration` in their constructors, and the client controls create their
sessions from it directly. The `AddClient(configurationFile)` surface of the stack loads the
document lazily on the first session connect, which is after the forms already exist.

`ITelemetryContext` comes from `services.AddOpcUa()` - the `ServiceProviderTelemetryContext` of
the stack, which resolves the `ILoggerFactory` of the host. No sample declares a telemetry
context of its own.

## Logging

The samples log to the file their configuration names in `TraceConfiguration/OutputFilePath`,
which is what the pre 2.0 samples did through `SerilogTraceLogger`. A console logger is of no
use to a Windows Forms sample - there is no console to write to - so console output is only
added by the samples which really are console applications.

`TraceMasks` selects the level: anything beyond the masks which map to `Information` and above
turns the file logger verbose. `DeleteOnLoad` replaces the file on every start.

Because the file to log to is only known once the configuration has been read, while the
loggers of the stack are created before that, the static Serilog logger is built once with a
placeholder sink that the file is attached to later. Loggers handed out in between write to the
file as well.

The samples which have no application configuration - the aggregate tester, the local discovery
server - name their log file themselves with `AddSampleLogFile(...)`.

## Windows Forms

`SampleWinFormsHost.Run` starts the message loop first and then runs the asynchronous startup on
the user interface thread through the Windows Forms synchronization context. That keeps the
thread a single threaded apartment - which the common dialogs, drag and drop and the clipboard
need - without a blocking wait in the entry point of the sample.

The main form is created by the container, either by type
(`ActivatorUtilities.CreateInstance<TMainForm>`) or through a factory - `ServerForm.Create`
for the server samples, whose shared form takes the running `StandardServer` and the loaded
configuration.

Because the form is built by the container, a sample which needs a client of the stack lets the
stack register it rather than constructing it. The Global Discovery Client is the one that does:
`AddGlobalDiscoveryClient()` registers an `ISessionFactory` and then calls the library's own
`AddOpcUa().AddGdsClient().AddLocalDiscoveryServerClient()`, and `MainForm` takes the
`GlobalDiscoveryServerClient`, the `ServerPushConfigurationClient` and the
`LocalDiscoveryServerClient` as constructor parameters. Which session those clients open is then
one registration rather than three `new` expressions in a form constructor - and the container,
not the form, owns their lifetime.

## The client models

What a Windows Forms client does *after* it is connected - browse, subscribe, call, watch -
does not live in its form either. Every Workshop client keeps that in a `Model/<Sample>ClientModel`
which knows nothing about the window, built on the base class and helpers of
[`Samples/Client.Common`](../Client.Common/README.md). The form hands the session of the shared
connect control to the model and renders what the model reports; the model tier of the tests
drives the model without a window.

The model comes from the container, like everything else the form takes. Each client has a
composition root next to its entry point - `Workshop/<Sample>/Client/<Sample>ClientHosting.cs`,
the counterpart of the `AddXServer()` of the server half:

```csharp
public static IServiceCollection AddBoilerClient(this IServiceCollection services)
{
    services.TryAddSingleton<BoilerClientModel>();
    return services;
}
```

```csharp
SampleWinFormsHost.Run<MainForm>(
    args,
    services => services
        .AddSampleApplication(options => { ... })
        .AddBoilerClient(),
    ExceptionDlg.Show);
```

and `MainForm` takes the `BoilerClientModel` as a constructor parameter. The timing matters
and is what makes a singleton right here: the container creates the model while it is creating
the form, on the thread which owns the message loop, and a model captures the
`SynchronizationContext` of the thread it was constructed on to raise its events. So a model
resolved for a window reports to that window's thread, and the window's handlers touch its
controls directly.

Tier 2 of the tests goes through the same registration rather than calling the constructor:
see [`docs/TESTING.md`](../../docs/TESTING.md).
