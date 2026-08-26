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

Tier 0 and Tier 1 run anywhere, including Linux agents for Tier 0. Tier 2 is Windows-only
and is tagged `[Category("RequiresDesktop")]` so it can be excluded if an agent's window
station misbehaves.

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
  Samples.Tests.Common/        # shared helpers, and the sample catalog that drives every tier   (exists)
  SampleConfiguration.Tests/   # Tier 0. No project references, no network.                      (exists)
  SampleServers.Tests/         # Tier 1. References the sample *server* projects.                (exists)
  Samples.TestHost/            # console app: starts any sample server headless, prints "READY <url>"  (phase 3)
  Samples.Tests.WinForms/      # STA message loop runner and the modal dialog watchdog           (phase 3)
  SampleClients.Tests/         # Tier 2. References the sample *client* projects.                (phase 3)
```

`Samples.Tests.Common` targets `net10.0` and stays free of WinForms, so Tier 0 runs anywhere;
the WinForms helpers live in their own `net10.0-windows` library from phase 3 on.

The client/server split is load-bearing, not cosmetic. Generated model types such as
`Quickstarts.Boiler.Constants` are compiled into *both* the Boiler client and the Boiler
server assembly, so one test project referencing both would fail with `CS0433`. Splitting by
role avoids it, and `Samples.TestHost` is how the client tests get a server without
referencing one.

Test framework is **NUnit** (matching UA-.NETStandard). Test projects target `net10.0-windows`
except Tier 0, which is portable. The assembly is `[NonParallelizable]`: sample servers use
fixed, sometimes overlapping ports.

## The sample catalog

`Samples.Tests.Common/SampleCatalog.cs` holds one entry per sample - project paths, config
files and the expected endpoint URL. **Adding a new sample means adding one row.** All three
tiers iterate that table, so a sample that is not in the catalog is not tested; Tier 0
additionally globs the repo for `*.Config.xml` so new configs cannot be silently forgotten.

Tier 1 pairs each entry with the way the sample creates its server, in
`SampleServers.Tests/SampleServerFactories.cs`. Those factories are written out rather than
resolved by reflection on purpose: renaming or removing a sample server then breaks the build
of the tests, which is the earliest and clearest moment to notice. A server sample without a
factory has to be listed as a known gap, so it cannot drop out unnoticed.

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

## Running

```bash
dotnet test Tests/SampleConfiguration.Tests
dotnet test Tests/SampleServers.Tests
```

```bash
dotnet test "UA Samples.slnx" --filter "TestCategory!=RequiresDesktop"
```

CI runs the same thing in the `Test Samples` stage of `azure-pipelines.yml`
(`.azurepipelines/test.yml`), excluding `RequiresDesktop` until Tier 2 is proven stable on
hosted agents.

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

16 test cases, about 15 seconds. Each one starts the real sample server in process, from the
sample's own configuration file, and connects to it with a plain OPC UA client:

- the server comes up on the endpoint the catalog claims
- a session can be opened, and the server reports `ServerState.Running`
- browsing the Objects folder returns the nodes of the sample - Boiler answers with
  `Boiler #1` and `Boiler #2`, Methods with `My Process`, Views with `Plant`
- the sample registered its own namespace, which means its node manager loaded

Only the opc.tcp endpoints are exercised; https base addresses are stripped in memory before
the server starts, because they need their own bindings and would double the ports a test run
occupies.

### Known issues

Four samples do not survive this today. They are listed in `s_knownIssues` in
`SampleServerTests`, which reports them as **ignored** rather than failed - and fails the
moment one of them starts working, so nobody has to remember to remove the entry.

| Sample | Symptom |
|--------|---------|
| `Workshop/DataAccess/Server` | starts and accepts a session, but browsing the Objects folder answers `BadUnexpectedError` |
| `Workshop/HistoricalAccess/Server` | fails to start: the archive stores a `DateTimeUtc` in a `DataTable` column typed `DateTime` |
| `Workshop/HistoricalEvents/Server` | fails to start: a `dynamic` call to `Variant.From` is ambiguous between `From(MatrixOf<Variant>)` and `From(string)` |
| `Samples/Opc.Ua.Sample` | fails to start: an `ArrayOf<SByte>` is cast to `SByte[]` |

All four are the samples not having caught up with the value types the 2.0 stack introduced
(`ArrayOf<T>`, `DateTimeUtc`, the `Variant.From` overloads).

## Status / roadmap

- [x] Phase 1 - shared helpers, Tier 0 configuration tests, CI test stage
- [x] Phase 2 - Tier 1 server smoke tests (12 Workshop servers + Reference + Sample server)
- [ ] Phase 3 - `Samples.TestHost` + Tier 2 client smoke tests
- [ ] Phase 4 - GDS, Aggregation (needs two servers), LDS

Two further issues, found while writing this plan and caught by Tier 0:

- `Workshop/Aggregation/Server/Quickstarts.AggregationServer.Config.xml` listens on port
  **62541** while aggregating a downstream ReferenceServer that also lives on **62541**. That
  sample cannot run as configured. The console variant correctly uses 62530.
- `testclientserver.sh` builds `Samples/NetCoreConsoleServer` and `Samples/NetCoreConsoleClient`,
  which no longer exist in this repository. Tier 1 replaces it.
