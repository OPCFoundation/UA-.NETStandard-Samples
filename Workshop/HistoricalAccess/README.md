# Historical Access Quickstart (OPC UA Part 11)

A server/client pair which demonstrates **OPC 10000-11, Historical Access**: how a server puts
a store of past values behind the history services, and how a client reads, aggregates,
annotates and rewrites what is in it.

| Project | What it is |
|---------|------------|
| [Server](Server) | A `StandardServer` whose address space is a folder of archive items backed by text files |
| [Client](Client) | A Windows Forms client which reads and writes the history of any variable |
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
`Annotations` property to the variable it hangs on, and the audit events an update raises — is
the dispatcher's, not the sample's.

What the sample writes is one class,
[`ArchiveHistorianProvider`](Server/UnderlyingSystem/ArchiveHistorianProvider.cs), and one line
which registers it:

```csharp
Server.UseHistorian()
    .UseProvider(m_historian)
    .RegisterForNamespace(Namespaces.HistoricalAccess);
```

That line has to run in `CreateAddressSpaceAsync`. After every node manager has built its
address space the server reconciles what each variable advertises against the providers which
are registered, and clears `Historizing` and the history access bits from any variable no
provider will answer for.

### The capability interfaces

A provider implements the umbrella `IHistorianProvider` plus whichever of the narrow interfaces
its store can honour. The dispatcher type-tests the provider at run time and answers
`BadHistoryOperationUnsupported` for an operation the resolved provider does not implement, so
there is nothing to register and no base class full of `NotImplementedException`.

| Interface | What this sample does with it |
|---|---|
| `IHistorianDataProvider` | Raw reads, and insert / replace / update / delete-raw / delete-at-time. |
| `IHistorianModifiedProvider` | The modified-history table of an archive item: prior versions plus who changed them, when and how. |
| `IHistorianAtTimeProvider` | Implemented rather than left to the framework, so that the per item `Stepped` flag decides how a value between two samples is arrived at. |
| `IHistorianProcessedProvider` | Implemented rather than left to the framework, so that the aggregate configuration recorded in an archive file is what the aggregate is computed with. |
| `IHistorianAnnotationProvider` | Read / insert / replace / update / delete of annotations, keyed by their annotation time. |
| `IHistorianTransactionalProvider` | A batch of values applied to an archive item as a whole. |
| `IHistorianBulkInsertProvider` | A batch of values spread over several items, with the lock taken and each item reloaded once. |

The last two are offers rather than obligations, and this SDK version takes up neither: nothing
calls the atomic path yet, and the bulk path is reached only from the automatic value-capture
pipeline, which this sample does not use because its archive is filled from files rather than
from live values. They are implemented anyway, because what a store has to do to honour them is
the part worth showing.

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

### Capabilities

`HistoryServerCapabilities` is not written by the sample. Once the address space exists the
diagnostics node manager asks every registered provider for its capabilities and rolls the
answers up into that node. `GetCapabilitiesAsync(NodeId.Null, …)` is the roll-up question —
"what does this provider support in general" — and a real node id is asked when the framework
wants the `HistoricalDataConfigurationType` companion of that one variable.

## What the client shows

The client hosts the shared
[`HistoryDataListView`](../../Samples/ClientControls.Net4/Common/Client/HistoryDataListView.cs)
control, which drives everything through `Opc.Ua.Client.Historian.HistoryClient`:

```csharp
HistoryClient historian = session.Historian();
```

The client hands out the answer of a read as an `IAsyncEnumerable<DataValue>` which spans the
whole time range: it issues the requests, carries the continuation point of one into the next,
and releases the one still open when the caller stops pulling. The control walks that sequence
a page at a time, so **Go**, **Next** and **Stop** keep meaning what they always did — and
**Stop** abandoning the sequence is exactly what releases the continuation point the server is
holding.

| Control | Client call |
|---|---|
| Read → Raw | `ReadRawAsync` |
| Read → At Time | `ReadAtTimeAsync` |
| Read → Processed | `ReadProcessedAsync` |
| Insert / Replace / Insert-Replace | `InsertAsync` / `ReplaceAsync` / `UpdateAsync` |
| Delete Raw, Delete Modified | `DeleteRawAsync` |
| Delete At Time | `DeleteAtTimeAsync` |
| Annotate selected values | `WriteAnnotationAsync` |
| *Detect limits* | `ReadRawAsync` for one value at each edge of the archive |
| — | `GetServerCapabilitiesAsync` on connect, for the page size and whether to offer annotations |

Two operations stay on the plain service call, and the control says why where it does it:

* **Read → Modified.** `HistoryClient.ReadModifiedAsync` yields the values of an answer, and
  the `ModificationInfo` beside them — what was done to a value, when and by whom — is the
  whole point of reading modified history.
* **Remove.** The history client offers the two deletes of Part 11 rather than the `Remove`
  form of an update.

## Running it

```bash
dotnet run --project "Workshop/HistoricalAccess/Server/HistoricalAccess Server.csproj"
```

```bash
dotnet run --project "Workshop/HistoricalAccess/Client/HistoricalAccess Client.csproj"
```

In the client, **Server → Connect**, then **Aggregates → Select Variable** and pick something
under `Data/Sample` or `Data/Dynamic`. *Detect limits* fills the time range from the archive.
Then choose a read type and press **Go**.

The `Data/Sample` items are static and are the ones the
[Tester](Tester) replays the Part 13 aggregate test vectors against; the `Data/Dynamic` items
generate new samples while the server runs.

## The event half

[HistoricalEvents](../HistoricalEvents) is the same model applied to events rather than values.
Its [`WellReportHistorianProvider`](../HistoricalEvents/Server/WellReportHistorianProvider.cs)
implements `IHistorianEventProvider` over a table of well test reports, and its node manager —
which is source generated — needs no history override either.

Two things are worth reading it for:

* **Records.** An event reaches a provider flattened: its fields keyed by the browse path which
  addresses them, segments joined by a slash. A read builds that dictionary by asking the event
  for exactly the fields the request refers to; an update decodes one back into a row.
* **Where clauses are evaluated twice.** The provider evaluates the filter against the full
  event, which is what keeps the requested number of events per page a count of *matching*
  events rather than of candidates; the framework evaluates it again against the record. A
  record therefore has to carry every field the where clause reads as well as every field the
  select clauses ask for, or the second pass would discard what the first one kept.

Only the data half of `HistoryServerCapabilities` is rolled up from the registered providers,
so that server sets the event flags of that node itself once its address space is up.

## Tests

Tier 1.5 drives both samples over a real session:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~HistoricalAccessNodeManagerTests"
```

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~HistoricalEventsNodeManagerTests"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers, and
[HistoricalAccess.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/HistoricalAccess.md)
in the stack repository for the reference documentation of the provider model.
