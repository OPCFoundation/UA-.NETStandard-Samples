# Historical Access Quickstart (OPC UA Part 11)

A server/client pair which demonstrates **OPC 10000-11, Historical Access**: how a server puts
a store of past values behind the history services, how the SDK keeps a history for it when
it has no store of its own, and how a client reads, aggregates, annotates, rewrites and
audits what is in it.

| Project | What it is |
|---------|------------|
| [Server](Server) | A server whose address space is a folder of archive items backed by text files, next to a folder of live variables whose history the SDK captures |
| [Client](Client) | A Windows Forms client which reads and writes the history of any variable and watches the audit trail of the server |
| [Tester](Tester) | A harness which replays the Part 13 aggregate test vectors against the server |

Endpoints: `opc.tcp://localhost:62550/Quickstarts/HistoricalAccessServer` and
`https://localhost:62549/Quickstarts/HistoricalAccessServer`.

The event half of Part 11 lives in the sibling
[HistoricalEvents](../HistoricalEvents) sample; the last section here says how the two fit
together.

## The provider model

The server implements no history service at all. `AsyncCustomNodeManager` routes every
`HistoryRead` and `HistoryUpdate` through `Opc.Ua.Server.Historian.HistorianDispatcher`, which
resolves an `IHistorianProvider` for the node the request names and calls it with a request it
has already validated and normalised. Everything between the wire and the store — continuation
points, the timestamps to return, index ranges and data encodings, the translation from the
`Annotations` property to the variable it hangs on, the gate on what a provider claims to
support, and the audit events an update raises — is the dispatcher's, not the sample's.

Two providers serve the one namespace of the sample, which is what makes the resolution order
of the dispatcher visible:

| Folder | Store | Provider | Bound how |
|---|---|---|---|
| `Sample`, `Dynamic` | text files, one per item | [`ArchiveHistorianProvider`](Server/UnderlyingSystem/ArchiveHistorianProvider.cs), written by the sample | `RegisterForNamespace`, and the `GetHistorianProvider` override of the node manager |
| `Live` | nothing of the sample's own | `InMemoryHistorianProvider`, which ships in `Opc.Ua.Server` | `RegisterAsDefault`, plus `RegisterForNode` for each variable |

```csharp
// the file archive, for every node of the namespace
m_archiveHistorian = Server.UseHistorian()
    .UseProvider(m_historian)
    .RegisterForNamespace(Namespaces.HistoricalAccess);

// the in-memory engine of the SDK, for everything nobody else claims
m_liveHistorian = Server.UseHistorian()
    .UseInMemoryProvider(new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.FromHours(1) })
    .RegisterAsDefault();
```

The dispatcher resolves a provider in a fixed order: the `GetHistorianProvider(NodeState)`
override of the node manager first — the sample answers for its own `ArchiveItemState` nodes
there and returns null for everything else — then the server-wide registry, by node id, then
by namespace, then the default. The live variables share the namespace of the archive, so
without their node binding the namespace binding would send them to the file archive; with it,
the node binding wins. Nothing in this server resolves to the default today, and that is the
point of a default: it is what a historizing variable of another node manager would get.

The registrations have to run in `CreateAddressSpaceAsync`. After every node manager has built
its address space the server reconciles what each variable advertises against the providers
which are registered, and clears `Historizing` and the history access bits from any variable no
provider will answer for.

### The capability interfaces

A provider implements the umbrella `IHistorianProvider` plus whichever of the narrow interfaces
its store can honour. The dispatcher type-tests the provider at run time and answers
`BadHistoryOperationUnsupported` for an operation the resolved provider does not implement, so
there is nothing to register and no base class full of `NotImplementedException`. What a
provider reports from `GetCapabilitiesAsync` is a gate as well as an advertisement: an
operation the capabilities do not claim is refused before the provider is reached.

| Interface | What the file archive does with it |
|---|---|
| `IHistorianDataProvider` | Raw reads, and insert / replace / update / delete-raw / delete-at-time. |
| `IHistorianModifiedProvider` | The modified-history table of an archive item: prior versions plus who changed them, when and how. |
| `IHistorianAtTimeProvider` | Implemented rather than left to the framework, so that the per item `Stepped` flag decides how a value between two samples is arrived at. |
| `IHistorianProcessedProvider` | Implemented rather than left to the framework, so that the aggregate configuration recorded in an archive file is what the aggregate is computed with. |
| `IHistorianAnnotationProvider` | Read / insert / replace / update / delete of annotations, keyed by their annotation time. |
| `IHistorianTransactionalProvider` | A batch of values applied to an archive item as a whole. The dispatcher prefers this path whenever a provider offers it, so every insert, replace and update a client sends to the archive is atomic: one value which cannot be written rolls the others back, and the update answers `BadTransactionFailed`. |
| `IHistorianBulkInsertProvider` | A batch of values spread over several items, with the lock taken and each item reloaded once. This is the path the automatic capture pipeline of the SDK flushes through; the file archive fills itself from files, so the engine of the `Live` folder is where it is exercised. |

The in-memory engine implements all of those and `IHistorianStructuredDataProvider` besides.
The `Samples/Opc.Ua.Sample` server keeps a third provider which implements `IHistorianDataProvider`
only, and its tests are where the framework fallbacks — interpolation over raw reads for
at-time, and the streaming aggregate calculator for processed reads — are what is under test.

### What an update reports back

Every update answers with a `HistorianUpdateOutcome<T>`: one status per requested entry, and
the values the operation displaced. The archive of an item is a DataSet which overwrites a row
in place, so `ArchiveItemState` copies the value out before it does and the provider hands it
on. The dispatcher writes those copies into the `OldValues` of the audit event it reports for
the update — which is how an auditor sees what a replace or a delete cost, without the
dispatcher having to read before it writes.

### Paging and resume tokens

A read returns one page and, if there is more, an opaque resume token. The framework hands that
token back as the continuation point of the next request, so it travels to the client and can
outlive the task which produced it: it must be data, never a cursor or a connection.

The archive keys its values by source timestamp, so the token of a raw read is simply the
timestamp the previous page ended at. The modified and annotation tables hold several rows per
timestamp by design — every modification of a value logs at that value's timestamp, and two
users may annotate the same instant — so their token carries the timestamp *and* how many rows
at it the pages so far returned. A token of only the timestamp would drop the rest of a group
that a page boundary lands in.

The window is half open and mirrors around the direction of the read: `[start, end)` forwards,
`(start, end]` backwards, so the sample at the far edge is the bound rather than a value.

### Errors

A provider read has no per-operation error channel, and nothing between the provider and the
transport catches an exception — one thrown there faults the whole service call for every node
in it. So every operation contains its own failures: a read answers with an empty page, an
update with a bad status per value, and the reason goes to the log.

### Capabilities and the companion object

`HistoryServerCapabilities` is not written by the sample. Once the address space exists the
diagnostics node manager asks every registered provider for its capabilities and rolls the
answers up into that node. `GetCapabilitiesAsync(NodeId.Null, …)` is the roll-up question —
"what does this provider support in general" — and a real node id is asked when the framework
wants to know about one variable.

Two places read the per node answer. The `HistoricalDataConfigurationType` companion object of
a variable — the `HA Configuration` child a client browses for the `Stepped` flag, the sampling
interval and the start of the archive — is installed by `HistorizeAsync` and populated from the
capabilities the provider reports for the node; the sample used to build it by hand, and no
longer does. And the aggregate filter of a monitored item is revised by the base class from the
same capabilities: the stepped flag, the sampling interval as the smallest processing interval,
and the aggregate configuration the node is computed with. A start time in the past is primed
from the raw history of the node before live values follow. The sample used to do both in
overrides, and no longer has to.

```csharp
await m_archiveHistorian.HistorizeAsync(
    item,
    SystemContext,
    setHistorizing: false,   // the archive file owns the Historizing flag
    autoCapture: false);     // the archive fills itself from its files
```

`Historizing` is the one attribute the builder is asked to leave alone here. Part 11 says it
means "the server is collecting" rather than "history exists": a finished recording in the
`Sample` folder says false and is still readable, an item in `Dynamic` the simulation appends
to says true, and the archive file is what decides. The live variables are historized with the
default, which sets the flag. The fluent builder of a source-generated node manager spells the
same three choices `historizing: true`, `false` and `null`.

## The live variables

The `Live` folder is the quick-start path of the SDK: nothing is written for its variables but
the variable itself.

```csharp
await m_liveHistorian.HistorizeAsync(
    variable,
    SystemContext,
    capabilities: s_liveCapabilities,
    captureOptions: new HistorianCaptureOptions { BatchTarget = 16, BatchWindow = TimeSpan.FromMilliseconds(100) });

m_liveHistorian.RegisterForNode(variable.NodeId);
```

`HistorizeAsync` registers the variable with the engine, sets its history access bits and its
`Historizing` attribute, installs the companion object from the capabilities, and — because
`autoCapture` is left on — attaches the handler which forwards every value change into the
capture sink. From then on the simulation only publishes: setting the value, the timestamp and
the status and clearing the change masks is what the pipeline listens to. The sink batches the
samples of every variable of the builder and flushes them through `IHistorianBulkInsertProvider`;
the options make it flush after sixteen samples or a tenth of a second, whichever comes first,
so a reader sees a value soon after it was published. A `Write` through the service is a state
change like any other, which is what `Setpoint` shows: a client writes it and finds the value in
its history without a `HistoryUpdate`.

| Variable | What it shows |
|---|---|
| `Temperature` | A reading the simulation publishes once a second, captured into the history. |
| `Setpoint` | A value clients write; every write is captured. |
| `LabSample` | Structured history: three readings at one instant, told apart by the key inside the value. |

`LabSample` is `IHistorianStructuredDataProvider` at work. Its data type is `KeyValuePair`, and
the engine is told to key its entries by the source timestamp *and* the `Key` of the pair:

```csharp
m_liveProvider.RegisterStructured(
    m_labSample.NodeId,
    KeyValuePairStructuredDataKeySelector.Instance,
    s_structuredCapabilities);
```

so the three readings of one sample — `pH`, `Temperature`, `Conductivity` — share the instant
it was taken at. Clients write such entries through `UpdateStructureData` rather than the data
update, and every one of insert, replace, update and *remove* addresses an entry by its key;
they read them back through the ordinary raw read, where several values share a timestamp.
The simulation records a sample every few seconds through the capture pipeline, which applies
the same key.

## What the client shows

The client hosts the shared
[`HistoryDataListView`](../../Samples/ClientControls.Net4/Common/Client/HistoryDataListView.cs)
control, which drives everything through `Opc.Ua.Client.Historian.HistoryClient`:

```csharp
HistoryClient historian = session.Historian();
```

The client hands out the answer of a read as an `IAsyncEnumerable` which spans the whole time
range: it issues the requests, carries the continuation point of one into the next, and
releases the one still open when the caller stops pulling. The control walks that sequence a
page at a time, so **Go**, **Next** and **Stop** keep meaning what they always did — and
**Stop** abandoning the sequence is exactly what releases the continuation point the server is
holding.

| Control | Client call |
|---|---|
| Read → Raw | `ReadRawAsync` |
| Read → Modified | `ReadModifiedAsync`, which yields each value with its `ModificationInfo` — what was done to it, when and by whom |
| Read → At Time | `ReadAtTimeAsync` |
| Read → Processed | `ReadProcessedAsync` |
| Insert / Replace / Insert-Replace on a plain variable | `InsertAsync` / `ReplaceAsync` / `UpdateAsync` |
| Insert / Replace / Insert-Replace / Remove on a structured variable | `UpdateStructureDataAsync` |
| Insert / Replace / Insert-Replace / Remove on the Annotations property | `WriteAnnotationsAsync`, one batch |
| Delete Raw, Delete Modified | `DeleteRawAsync` |
| Delete At Time | `DeleteAtTimeAsync` |
| Annotate selected values | `WriteAnnotationAsync` |
| *Detect limits* | `ReadRawAsync` for one value at each edge of the archive |
| — | `GetServerCapabilitiesAsync` on connect, for the page size and whether to offer annotations |

`Remove` on a plain variable stays on the plain service call: Part 11 version 1.05.07 does not
allow it for the data update, and the control shows what the server answers.

**Server → Watch Audit Events** subscribes to the `AuditHistoryUpdateEventType` events of the
server object and shows each one in the status bar: what kind of update it was, which node it
hit, how many values it wrote and how many it displaced, and who asked. The server reports them
only while `AuditingEnabled` is set in its configuration — the sample sets it — and only to a
session on an encrypted channel, which the connect control opens by default. The
[client model](Client/Model/HistoricalAccessClientModel.cs) carries all of it without the
window: the modified read, the configuration and capabilities reads, and the audit stream.

## Running it

```bash
dotnet run --project "Workshop/HistoricalAccess/Server/HistoricalAccess Server.csproj"
```

```bash
dotnet run --project "Workshop/HistoricalAccess/Client/HistoricalAccess Client.csproj"
```

In the client, **Server → Connect**, then **Aggregates → Select Variable** and pick something
under `Data/Sample`, `Data/Dynamic` or `Data/Live`. *Detect limits* fills the time range from
the archive. Then choose a read type and press **Go**.

The `Data/Sample` items are static and are the ones the
[Tester](Tester) replays the Part 13 aggregate test vectors against; the `Data/Dynamic` items
generate new samples while the server runs, and the `Data/Live` variables fill their history
from the moment the server starts.

## The event half

[HistoricalEvents](../HistoricalEvents) is the same model applied to events rather than values.
Its [`WellReportHistorianProvider`](../HistoricalEvents/Server/WellReportHistorianProvider.cs)
implements `IHistorianEventProvider` over a table of well test reports, and its node manager —
which is source generated — needs no history override either.

Three things are worth reading it for:

* **Records.** An event reaches a provider flattened: its fields keyed by the browse path which
  addresses them, segments joined by a slash, and a second time by the whole identity of the
  select clause. A read builds those by asking the event for exactly the fields the request
  refers to; an update decodes one back into a row, and a replace or a delete hands the row it
  displaced back as a record, which is what the audit event of the update carries.
* **Where clauses are evaluated twice.** The provider evaluates the filter against the full
  event, which is what keeps the requested number of events per page a count of *matching*
  events rather than of candidates; the framework evaluates it again against the record. A
  record therefore has to carry every field the where clause reads as well as every field the
  select clauses ask for, or the second pass would discard what the first one kept.
* **The client is on the history client too.** Its
  [model](../HistoricalEvents/Client/Model/HistoricalEventsClientModel.cs) reads the event
  history through `ReadEventsAsync`, pulling a page at a time off the sequence so the dialog
  keeps its Go/Next/Stop, and writes through `InsertEventsAsync`, `ReplaceEventsAsync`,
  `UpdateEventsAsync` and `DeleteEventsAsync` — with the same filter in both directions, so
  the fields of an event a read handed out are the fields a replace sends back. **Edit Field in
  Historian** in the event list is one such replace.

## Tests

Tier 1.5 drives both samples over a real session:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~HistoricalAccessNodeManagerTests"
```

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~HistoricalEventsNodeManagerTests"
```

and tier 1.7 drives both client models without their windows:

```bash
dotnet test Tests/SampleClientModels.Tests --filter "FullyQualifiedName~Historical"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers, and
[HistoricalAccess.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/HistoricalAccess.md)
in the stack repository for the reference documentation of the provider model.
