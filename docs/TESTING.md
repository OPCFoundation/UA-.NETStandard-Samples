# Testing the samples

This repository ships ~40 sample applications, most of them WinForms. The goal of the
test suite is **not** code coverage. The goal is a single, boring question, answered
automatically for every sample:

> Does this sample still start, connect, and do the one thing it exists to demonstrate?

Anything beyond that is out of scope. A sample that starts, serves its address space,
and answers a browse/read is considered working.

## The four tiers

| Tier | What it proves | Needs a network? | Needs a desktop? | Runtime |
|------|----------------|------------------|------------------|---------|
| 0 — Configuration | Every `*.Config.xml` parses and validates; client URLs match their server's endpoint; ports don't collide | no | no | seconds |
| 1 — Server smoke | Every sample server starts headless and answers a real OPC UA session (browse, read, sample-specific check) | localhost | no | ~1 min |
| 1.5 — Node managers | Every sample node manager still does the thing it was written to demonstrate | localhost | no | ~1.5 min |
| 2 — Client smoke | Every WinForms client builds its main form, connects to its own sample server, and completes its post-connect logic | localhost | yes | ~2-3 min |

Tier 0 and Tier 1 run anywhere, including Linux agents for Tier 0. Tier 2 is Windows-only and
is tagged `[Category("RequiresDesktop")]` so it can be excluded where there is no window
station. CI runs all of them: the `Test Samples` job is on a Windows agent.

Tier 1.5 is the odd one out and is worth explaining. The other three ask whether a sample
works; this one asks whether it still does what it *means*. It exists because the sample node
managers are due to be migrated, and a rewrite of a node manager is exactly the kind of change
which leaves a server starting, browsing and reading perfectly while quietly dropping the
behaviour the sample was written to show. Tier 1 would not notice any of that.

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
  SampleNodeManagers.Tests/    # Tier 1.5. One fixture per node manager, over a real session.
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
dotnet test Tests/SampleNodeManagers.Tests
dotnet test Tests/SampleClients.Tests
```

```bash
dotnet test "UA Samples.slnx" --filter "TestCategory!=RequiresDesktop"
```

One fixture of Tier 1.5 while working on it:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~MethodsNodeManagerTests"
```

CI runs every tier in the `Test Samples` stage of `azure-pipelines.yml`
(`.azurepipelines/test.yml`). The job runs on a Windows agent and filters nothing out, so
the WinForms client tests run there too. The `--filter` above is for running the suite where
there is no window station, a Linux machine or a container. The pipeline globs
`Tests/**/*.Tests.csproj`, so a new tier needs no pipeline change.

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

  The GDS test creates that certificate itself, before starting the sample, through the same
  `ApplicationInstance` the sample uses (`SampleCertificates`), so a machine which has never
  run the sample behaves like one which has. A store which already holds a certificate is
  left alone, whatever the stack thinks of it: on a developer machine it was often created
  for a different host name, and repairing it is not a test's business.

  This is what uncovered the key size defect below. If your machine still has a 1024 bit GDS
  certificate from before that fix, delete
  `%LocalApplicationData%/OPC Foundation/GDS/pki/own` and let the sample create a new one -
  the stack will not replace an existing certificate on its own.
- The aggregation sample is only tested as far as "it starts and serves". Its configuration
  aggregates external servers (a UA CTT server on 65300 and others), and the entry pointing
  at this repository's reference server is commented out, so there is nothing to aggregate
  in an unattended run without changing the sample.

### The GDS key size

Worth recording, because it took a build agent to find and a developer machine actively hid
it: the three GDS configurations declared `<MinimumCertificateKeySize>1024</MinimumCertificateKeySize>`,
and the stack uses that value as the key size when it creates the application instance
certificate. On any machine without an older certificate the GDS therefore built itself a
**1024 bit** certificate, which no security policy it offers will accept - `Basic256Sha256`
and both AES policies all require at least 2048. Every endpoint answered
`BadCertificatePolicyCheckFailed`, so the sample could not be connected to at all.

It passed on a developer machine only because the store there still held a 2048 bit
certificate from before the value was lowered. All three configurations now say 2048, which
is what every other sample in the repository uses.

### Known issues

`s_knownIssues` in `SampleServerTests` (and the same list in `SampleClientTests`) reports a
listed sample as **ignored** rather than failed, and fails the moment it starts working, so
an entry cannot rot. Both lists are used sparingly: a sample that is broken is worth fixing,
not parking. Both lists are currently empty.

## What Tier 1.5 checks today

83 test cases across 16 fixtures, about 1.5 minutes. One fixture per node manager, each
starting its sample server once and driving it through an ordinary OPC UA session.

**Everything is observed through the services a client would use.** No test reaches into a
node manager object, because that is precisely the part which is going to be replaced. Nodes
are resolved by browse path rather than by node id where the sample does not fix the id, and
namespace *indexes* are never written down - only namespace uris, looked up per run. A
migration is allowed to renumber namespaces and to restructure its internals; it is not
allowed to change what a client sees.

What each fixture pins down, in one line:

| Node manager | The behaviour under test |
|---|---|
| Empty | The hand-built trigger, its 2x2 matrix property, and the sample's own reference type in both directions |
| Boiler (Workshop) | The boiler from the node set and the one built in code; the simulation counts one below 100 and the other below 20 |
| DataTypes | Custom structures with both encodings, an instance from a second node set carrying a value of a type from the first |
| Views | The same node browsed through two views shows two different sets of children |
| SimpleEvents | The custom event type, its declared fields, both severities, and the cycle counter advancing |
| Methods | Argument metadata, the two argument-validation refusals, the ramp, and replacing a running process |
| UserAuthentication | UserAccessLevel computed per session, the write refused for anonymous, an unknown user refused a session |
| PerfTest | The register/offset arithmetic in the node id, nodes synthesized on demand, bounds refused |
| DataAccess | The segment tree, blocks browsable down to their tags, one block reachable through two paths |
| AlarmCondition | The configured area tree, areas as notifiers of the server, alarms travelling from source to area |
| HistoricalAccess | Raw reads, continuation points, read-at-time, aggregates, inserting into the history, and which items are still being collected |
| HistoricalEvents | The well tree, event history with continuation points, and the two refusals the sample declares |
| Aggregation | The proxy root published for the configured downstream server |
| TestData | Static write round trip, simulated values while monitored, and which single variable is archived |
| MemoryBuffer | Tags synthesized from node ids, and the three creation refusals the custom monitored item makes |
| Boiler (sample server) | Display names renamed after the unit, and the state machine started by the node manager itself |

### Recorded issues

Three expectations are written the way the sample is *meant* to behave and reported as
**ignored** because they do not hold today, through `KnownIssue.RecordAsync`. Like `s_knownIssues`
in Tier 1, an entry fails the moment it starts passing, so it cannot rot - and that has already
happened twice while these tests were being written, both times because the expectation was
wrong about the harness rather than about the sample.

They are not asserted the other way round on purpose: recording the broken behaviour as
expected would ask the migration to preserve it.

- **SimpleEvents** - events arrive with the sample's own fields (`CycleId`, `CurrentStep`,
  `Steps`) empty. This is no longer the node manager building the event wrongly, which it was:
  the event is now created from its type model, so the fields exist, carry their browse names
  and hold their values, and asking the event object itself to resolve `2:CycleId` through the
  very method the server uses to apply an event filter returns the value. The server accepts
  the select clauses - the filter result for the monitored item is empty - and then delivers a
  null for each of them. Whatever drops them sits below the sample; the standard fields of the
  same event, selected the same way, arrive normally.
- **HistoricalAccess** - two. A read at a recorded point in time returns a bad value, and what
  a dynamic item reports to a subscriber does not turn up in its history. Both are worth
  looking at together with one observation: a raw read whose range starts *before* the first
  archived value returns nothing rather than the values inside the range, and the archive is
  searched with a binary search over a view sorted by source timestamp. Deleting a value which
  *is* in the archive answers `BadUnexpectedError` and leaves the item refusing every later
  read, so that one is described here rather than tested - a test for it would take the rest
  of the fixture down with it.
- **Aggregation** - the server publishes its proxy root and then answers `BadNotConnected` to
  every browse of it. The refusal is deliberate rather than an error: the node manager hands
  out a downstream session only once its type cache is loaded and its status node reads Good,
  and both are set by the metadata update it schedules five seconds after start. That update
  never finishes - the proxy root still carries its placeholder name `Root` rather than the
  name of the downstream server, and renaming it is the first thing the update does. The
  fixture holds an ordinary session to that same downstream server and reads from it, which is
  asserted separately so this cannot be blamed on the downstream server being absent.
  Everything the sample exists for is behind that browse.

One further test, `AuthenticatedUserMayWriteAndTheLogFileAppears`, is skipped unless
`OPCUA_SAMPLES_TEST_USER` and `OPCUA_SAMPLES_TEST_PASSWORD` name a real local Windows account.
The UserAuthentication server verifies passwords with `LogonUser`, so there is no way to
authenticate without one. The two refusal paths need no account and run everywhere, including
CI.

### Waiting without sleeping

Most of these node managers are driven by a timer, so most of these tests have to wait for
something. None of them sleep for a guessed duration: `Poll.UntilAsync` retries a probe until
a condition holds, `DataChangeCapture` collects notifications from a subscription, and
`EventCapture` waits for an event matching a predicate. Each reports what it last saw when it
gives up, which is the difference between a failure that explains itself and one that does not.
Nothing assumes it has seen the *first* event of a run either: the simulations have been going
since the server started.

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
- [x] Phase 5 - Tier 1.5 node manager tests, ahead of migrating the node managers

Writing Tier 1.5 found four defects which are fixed rather than recorded:

| Where | What was wrong |
|-------|----------------|
| `Workshop/Methods/Server/MethodsNodeManager.cs` | The `Start` method could not be called. Its `InputArguments` and `OutputArguments` were declared with a hand-written `IVariantBuilder` whose `WithValue` stored the arguments through `Variant.FromStructure` and whose `GetValue` could not read that back, so it returned an empty array. Reading the property over a session worked - the encoding is fine - but the server read the declaration back to validate a call, saw zero declared arguments, and answered `BadTooManyArguments` to any call carrying any. Both properties now use the SDK's own `StructureBuilder<Argument>`, which is what a structure property is meant to be built with; the local builder is gone |
| `Workshop/DataAccess/Server/Model/BlockState.cs` | Browsing a block returned no references at all, so a client could not discover its tags - which is most of what the sample demonstrates. A block is built for the duration of an operation and never lives in the address space, so nothing populates a browser for it with its children; `SegmentState` already does this for itself and `BlockState` did not. It also had no `TypeDefinitionId`, so it did not even report what kind of object it was |
| `Workshop/HistoricalAccess/Server/UnderlyingSystem/UnderlyingSystem.cs` | Nothing written to the history was ever stored. Every operation was handed a freshly constructed archive item which loaded its own copy of the data from the resource it came from, so a write went into a copy that was then thrown away and the next read loaded the file again. Both halves reported success. Archive items are now kept, which is also what lets the simulation's appends survive |
| `Workshop/SimpleEvents/Server/SimpleEventsNodeManager.cs` | The events were built as empty shells: a freshly constructed event has none of the fields its type declares, and the code filled them in with `SetChildValue`, which only writes a field that is already there. The event is now created from its type model first, so the fields exist and carry their browse names. This is not enough to make them reach a client - see the recorded issue above - but it is the half of it which belongs to the sample |

A stale duplicate was removed at the same time: `Workshop/DataAccess/Server/Namespaces.cs`
declared `Quickstarts.EmptyServer.Namespaces` inside the DataAccess assembly, so any project
referencing both that and the Empty server - such as a test project which hosts several
samples - failed to compile with `CS0433`. The real constant lives in
`Workshop/DataAccess/Server/Model/Namespaces.cs` and nothing referenced the copy.

Two further issues, found while writing this plan and caught by Tier 0:

- `Workshop/Aggregation/Server/Quickstarts.AggregationServer.Config.xml` binds port **62541**,
  which the reference server sample already uses, so those two samples cannot run side by
  side. The console variant of the same server uses 62530 and is the one the tests start.
- `testclientserver.sh` builds `Samples/NetCoreConsoleServer` and `Samples/NetCoreConsoleClient`,
  which no longer exist in this repository. Tier 1 replaces it.
