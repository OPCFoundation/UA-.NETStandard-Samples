# Testing the samples

This repository ships ~40 sample applications, most of them WinForms. The goal of the
test suite is **not** code coverage. The goal is a single, boring question, answered
automatically for every sample:

> Does this sample still start, connect, and do the one thing it exists to demonstrate?

Anything beyond that is out of scope. A sample that starts, serves its address space,
and answers a browse/read is considered working.

## The three tiers

| Tier | What it proves | Needs a network? | Needs a desktop? | Runtime |
|------|----------------|------------------|------------------|---------|
| 0 — Configuration | Every `*.Config.xml` parses and validates; client URLs match their server's endpoint; ports don't collide | no | no | seconds |
| 1 — Server smoke | Every sample server starts headless and answers a real OPC UA session (browse, read, sample-specific check) | localhost | no | ~1 min |
| 2 — Client smoke | Every WinForms client builds its main form, connects to its own sample server, and completes its post-connect logic | localhost | yes | ~2-3 min |

Tier 0 and Tier 1 run anywhere, including Linux agents for Tier 0. Tier 2 is Windows-only and
is tagged `[Category("RequiresDesktop")]` so it can be excluded where there is no window
station. CI runs all three: the `Test Samples` job is on a Windows agent.

### Why this works at all

Three properties of the samples make headless testing cheap:

1. **Server `Main` methods are UI-free until the last line.** Every sample server does
   `LoadApplicationConfigurationAsync` -> `CheckApplicationInstanceCertificatesAsync` ->
   `application.StartAsync(new XServer(...))` and only *then* calls `Application.Run(serverForm)`.
   The server class is `public` and derives from `StandardServer`, so a test starts the real
   server object with the real config file and never touches WinForms.
2. **Every WinForms client uses the same control under the same name.** All client
   `MainForm.Designer.cs` files declare `private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;`,
   and that control exposes `ConnectAsync()`, `Session` and `ConnectComplete`. One reflection
   helper drives every client. No UI automation, no changes to the samples.
3. **Endpoints are hard-coded and paired.** The client URL sits in `MainForm.cs`, the server
   endpoint in `*.Config.xml`. That pairing is machine-checkable, which is Tier 0's job.

## Layout

```
Tests/
  Directory.Build.props        # relaxes the repo-wide "AnalysisMode=all" for test code
  Samples.Tests.Common/        # shared helpers, and the sample catalog that drives every tier
  Samples.Servers.Hosting/     # references every sample server, knows how each creates its server
  Samples.Tests.WinForms/      # STA message loop runner, dialog watchdog, form reflection helpers
  SampleConfiguration.Tests/   # Tier 0. No project references, no network.
  SampleServers.Tests/         # Tier 1. Starts the servers from Samples.Servers.Hosting.
  SampleClients.Tests/         # Tier 2. References the sample *client* projects.
```

`Samples.Tests.Common` targets `net10.0` and stays free of WinForms, so Tier 0 runs anywhere.
Everything that touches a sample assembly targets `net10.0-windows`, because the samples are
WinForms executables - even though the tests never show a window.

Generated model types such as `Quickstarts.Boiler.Constants` are compiled into *both* the
Boiler client and the Boiler server assembly, so a project referencing both has two copies of
them. That is harmless as long as the tests only name types which exist once - `MainForm`,
`BoilerServer` - and it is why Tier 2 can start a server in process instead of having to spawn
one. Should a test ever need one of the duplicated types, it will fail to compile with
`CS0433`; the fix is an `Aliases` attribute on the project reference, not a redesign.

Test framework is **NUnit** (matching UA-.NETStandard). Fixtures which start servers are
`[NonParallelizable]`: the samples bind fixed, sometimes overlapping ports.

## The sample catalog

`Samples.Tests.Common/SampleCatalog.cs` holds one entry per sample - project paths, config
files and the expected endpoint URL. **Adding a new sample means adding one row.** All three
tiers iterate that table, so a sample that is not in the catalog is not tested; Tier 0
additionally globs the repo for `*.Config.xml` so new configs cannot be silently forgotten.

Two factory tables pair those entries with the sample code: `SampleServerFactories`
(`Samples.Servers.Hosting`) knows how each sample creates its server, `SampleClientFactories`
(`SampleClients.Tests`) how each creates its main form. Both are written out rather than
resolved by reflection on purpose: renaming or removing a sample then breaks the build of the
tests, which is the earliest and clearest moment to notice. A sample without a factory has to
be listed as a known gap, so it cannot drop out unnoticed.

## Practical notes

**Certificates.** Sample configs store their PKI under `%CommonApplicationData%\OPC Foundation\pki`.
Tests redirect every store to a per-run temp directory and set
`AutoAcceptUntrustedCertificates` on both sides, so a test run neither depends on nor pollutes
machine state.

**Configuration loading.** Tests load config files by explicit path rather than through
`ConfigSectionName`. Under a test host the entry assembly is `testhost.exe`, so the
`<app>.exe.config` lookup the samples rely on cannot resolve.

**Modal dialogs are the enemy.** Sample clients funnel errors into a modal `ExceptionDlg`,
which in CI hangs forever. The Tier 2 harness runs a watchdog that scans `Application.OpenForms`,
captures any dialog's text, closes it, and fails the test with that message. This turns silent
UI popups into readable failures and is the single most valuable piece of the harness.

**Ports.** Sample servers bind fixed ports and some overlap (`ReferenceServer` and the WinForms
`AggregationServer` both use 62541). The tests therefore keep the ports the samples ship with -
that is part of what is being tested - and run one sample at a time (`[NonParallelizable]`).

The consequence is machine wide: **only one test run at a time**. Two runs in parallel fail
with "address already in use", and a second git worktree of this repository is not isolation -
the ports are the same. The same applies to a sample you left running by hand.

## Running

```bash
dotnet test Tests/SampleConfiguration.Tests
dotnet test Tests/SampleServers.Tests
dotnet test Tests/SampleClients.Tests
```

```bash
dotnet test "UA Samples.slnx" --filter "TestCategory!=RequiresDesktop"
```

CI runs all three tiers in the `Test Samples` stage of `azure-pipelines.yml`
(`.azurepipelines/test.yml`). The job runs on a Windows agent and filters nothing out, so
the WinForms client tests run there too. The `--filter` above is for running the suite where
there is no window station, a Linux machine or a container.

## What Tier 0 checks today

143 test cases, under a second, no network:

- every `*.Config.xml` in the repository loads and validates, and declares an application
  name, uri, type and security configuration
- every server configuration declares at least one base address
- every catalog entry points at files that exist
- every configuration file belongs to a catalog entry, so a new sample cannot slip in untested
- every server listens on the endpoint the catalog claims
- every client with a hard coded url connects to an endpoint its own server offers
- no two servers share a port unless the sharing is listed and explained
- and one self check: a file that is not an application configuration has to be rejected,
  so the checks above cannot pass vacuously

## What Tier 1 checks today

89 test cases, about 25 seconds. Fifteen of them start a sample server in process, from the
sample's own configuration file, and connect to it with a plain OPC UA client:

- the server comes up on the endpoint the catalog claims
- a session can be opened, and the server reports `ServerState.Running`
- browsing the Objects folder returns the nodes of the sample - Boiler answers with
  `Boiler #1` and `Boiler #2`, Methods with `My Process`, Views with `Plant`
- the sample registered its own namespace, which means its node manager loaded

Only the opc.tcp endpoints are exercised; https base addresses are stripped in memory before
the server starts, because they need their own bindings and would double the ports a test run
occupies.

All 14 servers pass. The first run of this tier found four that did not, all of them samples
which had not caught up with the value types the 2.0 stack introduced (`ArrayOf<T>`,
`DateTimeUtc`, `NodeId` as a struct, the `Variant.From` overloads); they were fixed rather
than parked:

| Sample | What was wrong |
|--------|----------------|
| `Workshop/DataAccess/Server` | `QuickstartNodeManager` unboxed the absent `DataType` attribute of an Object node straight into `NodeId`, now a struct, so every browse answered `BadUnexpectedError` |
| `Workshop/HistoricalAccess/Server` | the archive stored `DateTimeUtc` timestamps in `DataTable` columns typed `DateTime` |
| `Workshop/HistoricalEvents/Server` | `QuickstartNodeManager` passed a null default value to `Variant.From` through a `dynamic`, and a null has no type for the binder to resolve an overload from |
| `Samples/Opc.Ua.Sample` | `TestDataSystem` cast the boxed random arrays to `T[]` where the stack now hands out `ArrayOf<T>` - in all eleven array cases |

### Configuration drift

`ConfigurationExtensionTests` is the regression cover for the way these samples actually rot.
The OPC UA configuration decoder **ignores what it does not recognize**: a renamed or retyped
configuration property does not fail, it silently loses its value, and the sample breaks later
and somewhere else. That is precisely how both GDS samples came to declare a certificate group
with a `<CertificateType>` element long after the stack had renamed it to `<CertificateTypes>`,
and answered *"Please specify at least one valid Certificate Type"* at startup.

The test walks the element tree of every configuration file against the classes which decode
it - `ApplicationConfiguration` for the file itself, and for each `<Extensions>` entry the
class whose name matches the extension element - and reports every element that has no member
behind it. Member lookup follows the name a serialization attribute gives a member, because
that is what the decoder matches on. It lives with tier 1 rather than tier 0 because the
extension classes belong to the sample assemblies.

It found three dead elements, all now removed from the sample configurations:

| Element | Why it was dead |
|---------|-----------------|
| `GlobalDiscoveryServerConfiguration/ShutdownDelay` | belongs to `ServerConfiguration`, where both GDS configurations already have it; inside the extension it did nothing |
| `ServerConfiguration/MinMetadataSamplingInterval` | no such member on `ServerConfiguration` in the 2.0 stack - it was in 19 configurations |
| `SecurityPolicies/ServerSecurityPolicy/SecurityLevel` | the security level is computed by `ServerSecurityPolicy.CalculateSecurityLevel`, not configured |

There is no allow list. Every element of every sample configuration now maps onto a member of
the class which reads it, and anything that stops doing so fails the build.

### The console samples

Three samples build their host in `Main` and block there, so they are started as the
processes they really are (`ConsoleSampleProcess`), which also covers the entry point and the
configuration lookup that the in process tests bypass. The test waits for the line the sample
prints when it is up, then talks to it:

- **LDS** (`Samples/LDS/ConsoleServer`) - answers `FindServers` and reports itself. Note it
  binds the well known discovery port **4840**, so a real local discovery server on the
  machine will make this test fail.
- **GDS** (`Samples/GDS/ConsoleServer`) - serves its address space, and is asked to shut down
  through the `quit` command it documents.
- The **aggregation** server is started in process instead, because the console project
  compiles the very same sources and only its configuration differs.

Two limits worth knowing:

- The console samples run with the machine's own PKI under `%CommonApplicationData%` and
  `%LocalApplicationData%`, not with a temporary one - a process started from a test reads
  the configuration file the sample ships. They therefore create their certificates and,
  for the GDS, its JSON databases, where running the sample by hand would.
- The aggregation sample is only tested as far as "it starts and serves". Its configuration
  aggregates external servers (a UA CTT server on 65300 and others), and the entry pointing
  at this repository's reference server is commented out, so there is nothing to aggregate
  in an unattended run without changing the sample.

### Known issues

`s_knownIssues` in `SampleServerTests` (and the same list in `SampleClientTests`) reports a
listed sample as **ignored** rather than failed, and fails the moment it starts working, so
an entry cannot rot. Both lists are used sparingly: a sample that is broken is worth fixing,
not parking. Both lists are currently empty.

## What Tier 2 checks today

16 test cases, about 50 seconds, Windows only. For each WinForms sample client the test
starts its sample server in process, then on a dedicated STA thread with a running message
loop - but without ever showing a window:

- builds the client's real `MainForm` from the client's own configuration file
- reaches the shared `ConnectServerCtrl` through its designer field and connects it to the
  server the sample ships with
- asserts the session is connected and that the control kept it
- waits two seconds so the sample's `async void` ConnectComplete handler can run, and for
  the samples that have one, asserts the control it enables afterwards is enabled - which is
  the proof that the sample's own logic ran, not just the shared control
- disconnects and asserts the session was released

The **dialog watchdog** is what makes this safe: the sample clients report errors through a
modal `ExceptionDlg`, which in an unattended run would wait forever for a click. A timer on
the UI thread closes any modal form, keeps its text, and the harness fails the test with it.
`WatchdogTurnsAModalDialogIntoAFailure` proves the watchdog itself works.

The fixture is `[Category("RequiresDesktop")]` because it needs a window station. CI runs it:
the `Test Samples` job is on a Windows agent and filters nothing out. The category is there so
the suite can still be run where no window station exists - `--filter "TestCategory!=RequiresDesktop"`.

All 16 cases pass. Two samples did not, and both were fixed rather than parked.

The **AlarmCondition client** connected and then filled a modal dialog with a
`NullReferenceException` followed by `An item with the same key has already been added.
Key: i=9764`, `i=10751` and `i=10060` - the alarm types its own server raises. Both messages
come from one defect: its event notification handler is an `async void` which awaits in the
middle of its work, so the message loop delivers the next event into a second run of the
handler while the first one is suspended. The events arrive in a burst, because the sample
sends a `ConditionRefresh` as soon as it is connected. Two places did not survive that:

| Where | What was wrong |
|-------|----------------|
| `Workshop/AlarmCondition/Client/MainForm.cs` | `MonitoredItem_NotificationAsync` added the `ListViewItem` for a condition to `ConditionsLV` and only assigned `item.Tag = condition` after `await NodeCache.FindAsync`. A reentering handler walked the list, found the entry with an empty `Tag`, cast it to `ConditionState` and dereferenced `null`. The cache lookup now happens before the list is touched, so nothing is awaited between finding or creating an entry and completing it |
| `Workshop/AlarmCondition/Client/FormUtils.cs` | `ConstructEventAsync` filled its event type mapping with `Dictionary.Add` after an awaited supertype browse. Every event of an unmapped type that arrived while that browse was running started its own browse, and all but the first threw on the duplicate key. `TryAdd` is the fix, and is what the shared `Samples/ClientControls.Net4/ClientUtils.cs` copy of this method already did - it was hardened in `f3417859` and the private copy in this sample was missed |

The first run of this tier found the other sample, and it is
exactly the kind the watchdog exists for: the **HistoricalEvents client** connected, then its
post connect logic read the event history and failed with `BadContinuationPointInvalid`,
which the sample reported in a modal dialog. Without the watchdog the test would simply have
hung. Two defects, both in the sample's own server, were behind it:

| Where | What was wrong |
|-------|----------------|
| `Workshop/HistoricalEvents/Server/HistoricalEventsNodeManager.cs` | `HistoryReadEvents` treated any non null `HistoryReadValueId.ContinuationPoint` as a continuation point to restore. `ContinuationPoint` is a `ByteString` now and a freshly created `HistoryReadValueId` carries an *empty* one rather than a null one, so the very first history read of a session looked like a continuation of a request the server had never issued and answered `BadContinuationPointInvalid`. An empty continuation point has to be read as "no continuation point", which is what `Samples/Opc.Ua.Sample/TestData/TestDataNodeManager.cs` already does |
| `Workshop/HistoricalEvents/Server/ReportGenerator.cs` | the `DataView` row filter it builds wrote its `#...#` date literals with `DateTime.ToString()`, so in the current culture. The `System.Data` expression parser reads them with the invariant culture, so on a machine that is not formatting dates the invariant way - a German Windows, for instance - the history read threw `FormatException` and the client saw `BadUnexpectedError` |

The known issue list in `SampleClientTests` is therefore empty again.

The same unguarded continuation point check existed three times in
`Workshop/HistoricalAccess/Server/HistoricalAccessNodeManager.cs` - in `HistoryReadRawModified`,
`HistoryReadProcessed` and `HistoryReadAtTime` - and carries the same guard now. It is latent
there: the HistoricalAccess client does not read history from its ConnectComplete handler, so
no test reaches it, which is why it is fixed by inspection rather than by a failing test.

## Status / roadmap

- [x] Phase 1 - shared helpers, Tier 0 configuration tests, CI test stage
- [x] Phase 2 - Tier 1 server smoke tests (12 Workshop servers + Reference + Sample server)
- [x] Phase 3 - Tier 2 client smoke tests (no separate host process was needed)
- [x] Phase 4 - LDS, the console GDS and the aggregation server

Two further issues, found while writing this plan and caught by Tier 0:

- `Workshop/Aggregation/Server/Quickstarts.AggregationServer.Config.xml` binds port **62541**,
  which the reference server sample already uses, so those two samples cannot run side by
  side. The console variant of the same server uses 62530 and is the one the tests start.
- `testclientserver.sh` builds `Samples/NetCoreConsoleServer` and `Samples/NetCoreConsoleClient`,
  which no longer exist in this repository. Tier 1 replaces it.
