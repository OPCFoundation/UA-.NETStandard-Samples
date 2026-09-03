# Alias Names Quickstart (OPC UA Part 17)

A server/client pair which demonstrates **OPC 10000-17, Alias Names**: how a server publishes a
searchable index of the names people actually use for its signals, and how a client turns one of
those names into the NodeId it stands for.

| Project | What it is |
|---------|------------|
| [Server](Server) | A `StandardServer` with a plant laid out by structure, and the same signals indexed by tag name |
| [Client](Client) | A Windows Forms client which browses the plant, searches the index, and edits it |

Endpoints: `opc.tcp://localhost:62577/Quickstarts/AliasNamesServer` and
`https://localhost:62574/Quickstarts/AliasNamesServer`.

## The problem Part 17 solves

The address space of the sample is organized the way engineering documentation organizes a
plant — by where a signal physically is:

```
Objects/Plant/Reactor/TemperatureMeasurement
Objects/Plant/Boiler/SteamPressure
```

The people who operate that plant do not call those signals anything of the sort. To them, and
to the historian, the MES recipe and the alarm list, they are `TIC101_PV` and `PIC201_PV`.

Both names are right, and neither can be dropped. Renaming the nodes would break every client
which addresses the plant by structure, and a client which knows only `TIC101_PV` **cannot
browse for it** — the name is nowhere in the address space. Part 17 resolves this by publishing
a second, searchable index beside the address space: alias names, grouped into categories,
each pointing at one or more nodes through an `AliasFor` reference.

`ModelDesign.xml` and [`PlantTags.cs`](Server/PlantTags.cs) are deliberately unrelated files.
That separation is the sample.

## What the sample shows

### 1. Two ways to publish an index

Part 17 §9 offers a choice, and the sample server does both so the client can put them side by
side.

| | Standard categories | Application-defined categories |
|---|---|---|
| Where | `TagVariables` (`i=23479`), fixed by the specification | `PlantTags` and its sub-categories, in a namespace of the server's own |
| Server does | seeds a store, registers it with `IAliasNameStoreRegistry` | hands a store to an `AliasNameNodeManager` |
| Client needs to know | nothing — `AliasNameClient.OpenStandardTagVariables` knows the NodeId | the namespace uri and identifier, or it browses for them |
| Browsable | only after the server materializes the store — the NodeSet ships the category, not its aliases | yes — real `AliasNameCategoryType` nodes |
| Methods | `FindAlias` from the NodeSet; the optional ones once materialized | `FindAlias`, `FindAliasVerbose`, `AddAliasesToCategory`, `DeleteAliasesFromCategory`, `LastChange` |
| Nesting | — | `PlantTags/Reactor` and `PlantTags/Boiler` |

Registering a store with the server registry is the whole of the work for the standard case:
the `DiagnosticsNodeManager` already binds `TagVariables.FindAlias`, so the standard node starts
answering from that one call. The sample goes one step further and calls
`MaterializeRegisteredAliasNameNodesAsync` from its own `ConfigurationNodeManager`
([`AliasNamesConfigurationNodeManager`](Server/AliasNamesServer.cs)), which creates what the
NodeSet leaves out: the optional Methods the category's `AliasNameCapabilities` declare, and one
browsable `AliasNameType` node per alias (§6.2, what the OPC Foundation CTT browses for).

### 2. A name resolves to a node, and a node back to a name

The upper list of the client is the plant found by **browsing**. Its last column is the tag
name each node answers to — which the client did not browse for, because the name is not in the
address space. It asked an `AliasNameResolver` to map the node back.

The lower half runs the search the other way, which is the reason to use Part 17 at all: given
a pattern over names, which nodes are meant?

| Pattern | Answers |
|---------|---------|
| `%` | every tag of the selected category |
| `%_PV` | the measured values: `TIC101_PV`, `PIC102_PV`, `PIC201_PV`, `FIC202_PV` |
| `TIC101%` | the two tags of one instrument: `TIC101_PV`, `TIC101_SP` |

The **Resolves to** and **Value of that node** columns are the point: the search answered with
a usable address, and reading it returns live data.

### 3. Nesting narrows a search

`PlantTags/Reactor` serves the four reactor tags, `PlantTags/Boiler` the three boiler tags, and
the `PlantTags` root serves all seven — a category is searched together with everything below
it. A client which knows how the plant is divided narrows the search; one which does not asks
the root.

### 4. A tag list is configuration, not a constant

`AddAliasesToCategory` and `DeleteAliasesFromCategory` (§6.3.4/§6.3.5) change the index while
the server runs. Select a node in the upper list, type a name, and add it — the search finds it
straight away, and `LastChange` advances.

The two buttons are left enabled for every account on purpose, because the refusal is as much
part of the sample as the success. Part 17 leaves the authorization of these Methods to the
server, and the stack takes the strict reading: they need the **`SecurityAdmin` Role on a
`SignAndEncrypt` channel** and answer `BadUserAccessDenied` otherwise. Sign in as `secadmin`
(the password is the user name) to see them work, and as **Anonymous** to see them refused.
Searching needs no privilege at all — a tag list is not a secret.

### 5. `FindAliasVerbose` says where an entry came from

The optional Method of §6.3.3, which only the application-defined categories expose. Its two
extra columns are filled only by the **Find verbose** button: the category an entry was found
in — which matters once a search covers a tree — and the server uri of a target that lives in
another server. It deliberately does *not* carry the concrete reference type of the association.

## Running it

```bash
dotnet run --project "Workshop/AliasNames/Server/AliasNames Server.csproj"
```

```bash
dotnet run --project "Workshop/AliasNames/Client/AliasNames Client.csproj"
```

In the client, **Server → Connect**. Change the **Category** drop-down to compare what the
standard and the application-defined categories can do, and the **Pattern** box to narrow the
search. To try the mutation Methods, pick `secadmin` under **Sign in as** before connecting.

## Notes for implementers

* **A category descriptor has to carry the NodeId the category will really have**, which means
  the namespace index the server assigned. `AliasNameNodeManager` skips — with a warning rather
  than claiming another manager's ids — any descriptor whose NodeId lies outside the namespace
  it owns. The sample therefore seeds its store in the node manager factory's `Create`, where
  `server.NamespaceUris` is available, rather than in the server's constructor.
* **Give the descriptors a BrowseName in a namespace the server owns.** The store stamps that
  namespace onto every alias name it reports, and namespace zero is reserved for
  OPC-Foundation-defined names. Part 17 clients compare alias names ignoring the namespace, so
  this is invisible to them, but it keeps the server honest.
* **Aliases are stored as `ExpandedNodeId` carrying a namespace uri**, not as `NodeId` with an
  index. That is what Part 17 §7.2 puts on the wire, and it survives a server restart which
  renumbers the namespace table.
* **A store has to be registered before the address space is built** if its categories are to
  be materialized. `FindAlias` is bound late — the binder asks the registry at every call, so a
  store registered at any time answers it — but the nodes of §6.2 are created once, while the
  address space is built. The sample therefore registers its standard store in
  `CreateMainNodeManagerFactory`, the first hook which sees the running server, rather than in
  `OnServerStarted`.
* **Materialized nodes are a snapshot.** A tag added through `AddAliasesToCategory` afterwards
  is found by `FindAlias` and advances `LastChange`, but gets no `AliasNameType` node until the
  server restarts. The browse view and the search results diverge until then.
* `AliasNameClient` maps the Part 17 status codes onto ordinary .NET exceptions —
  `BadUserAccessDenied` becomes `UnauthorizedAccessException` and `BadNotSupported` becomes
  `NotSupportedException` — so the refusals this sample is about arrive as exceptions rather
  than as status codes, and the client catches them to put in its status bar.
* The `AliasNameResolver` is left in its default `Manual` refresh mode. The automatic modes
  (`AutoOnLastChangePolling`, `AutoOnLastChangeMonitoredItem`) are worth having in a long-lived
  client whose server's tag list changes underneath it, but they cost a poll or a subscription,
  and this form re-reads on every refresh anyway.

## What this sample does not cover

Part 17 is larger than one sample. The well-known `Topics` category (§9.4) and its
`PublishedDataSetType` targets, aliases which point into **another** server through a
`ServerUri`, a custom `IAliasNameStore` over a database or an MES, the `ReferenceTypeFilter`
argument of `FindAlias`, and the whole of **Annex D** — the PubSub schema which distributes
`LastChange` notifications between servers, along with the `AliasNamePublisher` and
`AliasNamePubSubRefreshStrategy` the SDK ships for it — are all outside it.

## Tests

`Tests/SampleNodeManagers.Tests/AliasNamesNodeManagerTests.cs` (tier 1.5) drives all of the
above over a real session:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~AliasNamesNodeManagerTests"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers.
