# Opc.Ua.Samples.Hosting

The bootstrap the sample and workshop applications share: dependency injection, the .NET
Generic Host, and file based logging. It exists so that ~30 sample entry points do not each
repeat the same twenty lines of configuration loading, certificate handling and telemetry
plumbing.

## What a sample looks like

A Windows Forms server:

```csharp
[STAThread]
static void Main(string[] args)
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

    SampleWinFormsHost.Run<ServerForm>(
        args,
        services => services
            .AddSampleApplication(options => {
                options.ApplicationType = ApplicationType.Server;
                options.ConfigSectionName = "BoilerServer";
            })
            .AddSampleServer<BoilerServer>(),
        ExceptionDlg.Show);
}
```

A Windows Forms client is the same without `AddSampleServer<T>()`, and a console sample builds
its own host:

```csharp
HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

builder.Logging.AddConsole();
builder.Services
    .AddSampleApplication(options => { /* ... */ })
    .AddSampleServer<AggregationServer>();

await builder.Build().RunAsync();
```

## What it does

`AddSampleApplication(...)` registers `SampleApplication`, and through it the
`ApplicationInstance` and the `ApplicationConfiguration` of the sample, plus one
`IHostedService` which owns the startup sequence:

1. read the configuration named by `ConfigSectionName`,
2. apply `ConfigureConfiguration`, for settings the configuration file cannot express,
3. attach the log file the configuration names,
4. make sure the application instance certificate is usable,
5. start the server the sample registered with `AddSampleServer<T>()`, and stop it on shutdown.

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
(`ActivatorUtilities.CreateInstance<TMainForm>`) or through a factory, for the samples whose
forms take arguments the container cannot know.

## Why not `AddServer(options => ...)`

The 2.0 dependency injection surface can describe a server entirely in code with
`AddOpcUa().AddServer(o => ...)`, and the hosted service it registers then builds the
`ApplicationConfiguration` from those options. The samples keep their `*.Config.xml` files
instead: the files are part of what the samples demonstrate, the tests load them by path, and
`OpcUaServerOptions` in `2.0.0-preview.2` has no way to hand a pre-loaded configuration to the
hosted server. Everything else - the container, the generic host, the hosted lifetime, the
dependency injection telemetry - is the surface the libraries ship.
