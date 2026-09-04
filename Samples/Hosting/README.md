# Opc.Ua.Samples.Hosting

The bootstrap the sample and workshop applications share: dependency injection, the .NET
Generic Host, and file based logging. It exists so that ~30 sample entry points do not each
repeat the same twenty lines of configuration loading, certificate handling and telemetry
plumbing.

## What a sample looks like

A Windows Forms server registers its composition root - the server class, its configuration
XML file and the node managers the server is made of - and shows the running server in the
shared server form:

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
    return services.AddSampleServer<BoilerServer>(
        configurationFile ?? "BoilerServer.Config.xml",
        server => server.AddNodeManager<BoilerNodeManagerFactory>(),
        configure);
}
```

`AddSampleServer` hands the configuration file to the hosted server of the stack
(`services.AddOpcUa().AddServer(configurationFile)` under the hood) and the callback to the
server builder of the stack, which is where the node managers are registered:
`AddNodeManager<TFactory>()` makes the container create the factory and the hosted server hand
it to the server before the server starts. A server class registers nothing itself - no
`AddNodeManager` in a constructor, no `CreateMasterNodeManagerAsync` override.

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

A console server builds its own host on the same registration:

```csharp
HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

builder.Logging.AddSampleConsole();
builder.Services.AddAggregationServer(configure: RejectUntrustedCertificatesLoudly);

await builder.Build().RunAsync();
```

## The server path: the hosted server of the stack

`AddSampleServer<TServer>(configurationFile, configureServer)` - and the overload taking a
server factory, for the servers which need databases - registers the server through the
dependency injection surface of the stack: `services.AddOpcUa().AddServer(configurationFile)`
loads the configuration XML document of the sample and owns the application instance, the
certificate check and the server lifetime, and `configureServer` receives the
`IOpcUaServerBuilder` of the stack to register the node managers with. Every setting in the
file applies exactly as on the classic `ApplicationInstance` path. On top of the stack the
sample registration adds what the samples rely on:

1. the hosted server starts the server instance of the *container* (through
   `IOpcUaServerFactory`), so the main form resolves the same running server -
   also published as the shared `StandardServer`, which `ServerForm.Create` takes,
2. the loaded `ApplicationConfiguration` is captured (through the loaded-configuration
   callback of the stack) and registered for the forms,
3. the log file the configuration names is attached the moment the file has been read,
4. a readiness gate (`SampleServerReadyHostedService`, driven by an `IServerStartupTask`)
   holds the start of the host until the server is listening, or fails it with the original
   error - the hosted server of the stack is a `BackgroundService`, which by itself starts in
   the background.

The optional `configure` callback runs right after the file has been read and before
certificates are checked, for the settings a configuration file cannot express, such as a
certificate validation callback.

### Node managers which depend on the configuration

The hosted server of the stack collects the registered node manager factories before it reads
the configuration file, so a factory type registered with `AddNodeManager<TFactory>()` cannot
take the `ApplicationConfiguration` in its constructor. Two samples need the configuration to
know what to serve, and show the two ways around that:

- the file transfer server's `FileTransferNodeManagerFactory` takes the container and reads the
  configuration the first time the server asks it for its namespace or for the node manager -
  both happen with the configuration loaded;
- the aggregation server needs *one factory per configured endpoint*, so its composition root
  uses the server-factory overload of `AddSampleServer`: the factory runs with the configuration
  loaded, right before the hosted server starts the server, and adds the factories to the server
  it creates. The reverse connect manager the node managers share is a hosted service registered
  after the server, so it starts once the server listens.

The GDS servers take the middle road: their `ManagedApplications` and onboarding registrar node
managers are registered as factory types whose stores come from the container, and the sample
GDS server creates the node managers of every registered factory next to the GDS one (the GDS
base server of the stack builds its master node manager without looking at registered
factories).

## The client path: an eager `ApplicationInstance`

`AddSampleApplication(...)` registers `SampleApplication`, and through it the
`ApplicationInstance` and the `ApplicationConfiguration` of the sample, plus one
`IHostedService` which owns the startup sequence:

1. read the configuration file named by `ConfigurationFile`,
2. apply `ConfigureConfiguration`, for settings the configuration file cannot express,
3. attach the log file the configuration names,
4. make sure the application instance certificate is usable,
5. start a server registered with the classic `AddSampleServer<T>()` (no file), with the node
   managers registered through `AddSampleNodeManager<TFactory>()` - the same registration the
   server builder of the stack makes for `AddNodeManager<TFactory>()` - handed to it first,
   and stop it on shutdown.

The client samples stay on this path on purpose: their forms take the loaded
`ApplicationConfiguration` in their constructors, and the client controls create their
sessions from it directly. The `AddClient(configurationFile)` surface of the stack loads the
document lazily on the first session connect, which is after the forms already exist. The
handful of samples whose user interface is built around the `ApplicationInstance` itself -
the UA sample client and server, the GDS applications - keep this path for their servers
too.

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
