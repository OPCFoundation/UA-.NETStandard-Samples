# Runtime NodeSets Quickstart

A server/client pair for the case every other sample in this repository skips: **a NodeSet2
document arrives after the server was written**, and it has to reach the address space
anyway.

There are two ways to do that, they solve different problems, and this sample does both:

| | The document becomes… | Used for |
|---|---|---|
| **`AddRuntimeNodeSet`** | a node manager of its own, with no compiled model behind it | a model the server has no code for at all, and which may be replaced while the server runs |
| **`builder.Import`** | an **overlay** on a model the server *does* have code for | a generated model with placeholder slots an integrator fills in |

| Project | What it is |
|---------|------------|
| [Server](Server) | A server which hosts a vendor NodeSet2 document as its own node manager **and** overlays two more onto a model it generated from `ModelDesign.xml` |
| [Client](Client) | A Windows Forms client which loads, reloads and removes the first one over OPC UA, and browses what the overlay did to the second |

Endpoint: `opc.tcp://localhost:62579/Quickstarts/RuntimeNodeSetsServer`.

The upstream references for the SDK side are
[RuntimeNodeSets.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/RuntimeNodeSets.md),
[NodeManagers.md#reload-modes](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/NodeManagers.md#reload-modes)
and
[NodeManagers.md#builderimport](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/NodeManagers.md#importing-a-nodeset2-overlay-at-runtime--builderimport).

## The five documents

`Server/NodeSets` holds five XML documents. Three of them are the runtime half, where the
C# knows two things about a document — a file name and a namespace URI — and two are the
overlay half, where the namespace they write into is one the server generated code for.

| Document | Namespace | Reaches the server through |
|---|---|---|
| `ModelControl.NodeSet2.xml` | `.../RuntimeNodeSets/Control/` | `AddRuntimeNodeSet` on the server builder |
| `ConveyorLine.Rev1.NodeSet2.xml` | `.../RuntimeNodeSets/Line/` | `INodeManagerLifecycle.AddRuntimeNodeSetAsync` |
| `ConveyorLine.Rev2.NodeSet2.xml` | the same | the same, on a reload |
| `Site.Layout.NodeSet2.xml` | `.../RuntimeNodeSets/Site/` | `INodeManagerBuilder.Import`, inside `SiteNodeManager.Configure` |
| `Site.Instrumentation.NodeSet2.xml` | the same | the same, in the same batch |

The two conveyor revisions carry the **same `ModelUri`**, which is what makes loading one
over the other a reload of one namespace rather than the addition of a second model. The
client compiles nothing of them: it finds `ModelControl`, the Methods and the conveyors by
browse path, which is exactly the position a client is in when it meets a model it was not
built against.

The two `Site.*` documents are the opposite case. They write into
`.../RuntimeNodeSets/Site/`, a namespace the server **does** have generated code for
(`Server/ModelDesign.xml`), and they name its identifiers — `ns=1;i=100` is the generated
`Site` object. That is what makes them an overlay rather than a second model.

---

# Part one — a document served as it is

Nothing in this half is generated. `Server/RuntimeNodeSetLibrary.cs` knows a file name and
a namespace URI; the SDK reads the document and materializes the nodes.

## Two registration routes, and why this half uses both

```csharp
// Server/RuntimeNodeSetsServerHosting.cs - the control model
server.AddRuntimeNodeSet(controller.ControlModelOptions());
```

```csharp
// Server/RuntimeNodeSetController.cs - the vendor model, once the server is up
m_vendor = await m_lifecycle.AddRuntimeNodeSetAsync(
    m_library.VendorOptions(RuntimeNodeSetLibrary.InitialRevision),
    callerContext: null,
    cancellationToken);
```

`AddRuntimeNodeSet` on the server builder is the whole registration for a document which
only has to be served. It reads the `Models` metadata of the file during the call, so the
namespace the document claims is in the namespace table before the server starts.

The vendor model takes the other route, and **the reason is the one thing about
`INodeManagerLifecycle` which is easy to get wrong**:

> `INodeManagerLifecycle.Registrations` lists the node managers the lifecycle itself added.
> A node manager the server was composed with is not one of them.

`ReloadAsync` and `RemoveAsync` take a `NodeManagerRegistration`, and there is no way to
obtain one for a node manager registered in the composition root. A model which is going
to be replaced therefore has to be **added through the lifecycle to begin with** — from a
startup task, which is where the controller does it. A model which only has to be served
belongs in the composition root, where its namespace is known before the first client
connects.

Raised upstream as
[UA-.NETStandard#4421](https://github.com/OPCFoundation/UA-.NETStandard/issues/4421).

## Calling the lifecycle from a Method handler

The Methods on `ModelControl` are what the client presses, and every one of them enters
the lifecycle from inside a request. A lifecycle operation waits for the requests in
flight to drain, so that is a deadlock waiting to happen and the SDK refuses it by
default. Two things make it safe here, and both are necessary:

* **A different node manager serves the request.** The Methods are on the control model,
  which is never reloaded and never removed. The model being replaced is the vendor one.
  Putting the Methods on the model they replace would deadlock.
* **The options opt in.**

  ```csharp
  new RuntimeNodeSetOptions {
      Sources = [RuntimeNodeSetSource.FromFile(path)],
      AllowLifecycleFromRequestCallback = true,   // exclude the initiating request from the drain
  }
  ```

The caller's context has to be handed on for that to work, and a Method handler gets it
from the system context it was invoked with:

```csharp
await m_lifecycle.RemoveAsync(m_vendor, context.GetOperationContext(), cancellationToken);
```

## The three reloads, and the only difference a client sees

`Reload`, `ShadowReload` and `ImmediateReload` all publish the same replacement address
space. They differ in one thing: what happens to the MonitoredItems the generation being
replaced still owns. That is why the client has a **Watch the speed** button and a list of
notifications — it is the only place the difference is visible.

| Mode | The requests in flight | The MonitoredItems of the old generation |
|---|---|---|
| `Reload` | drained first | deleted with the generation |
| `ShadowReload` | not waited for | keep being served off the retired generation until they drain |
| `ImmediateReload` | not waited for | invalidated at once with `BadNodeIdUnknown` |

Watch the speed, then reload in each of the three modes and look at what arrives in the
list. A shadow reload is what a server picks when a subscription must not be disturbed by
a model update; an immediate reload is what it picks when the old model must stop being
served this instant, and the `BadNodeIdUnknown` is deliberate.

A shadow reload can only keep an item alive because both revisions give the same node the
same NodeId. `ns=N;i=1102` is `Conveyor1/Speed` in revision 1 and in revision 2. A
document which reused a NodeId for a different node would be a modelling error the server
cannot repair.

## What the server refuses

**A reload may add nodes. It may not redefine a DataType.**

```
InvalidOperationException: DataType 'ns=4;i=1000' has an incompatible definition.
Runtime DataType definitions are immutable for the server lifetime.
```

That is what revision 2 gets if its `ConveyorState` gains a field, and it is right: a
client which decoded a value against the published definition would be reading a different
type under the same identity. The two revisions of this sample therefore declare
`ConveyorState` identically, and the comment in
[`ConveyorLine.Rev2.NodeSet2.xml`](Server/NodeSets/ConveyorLine.Rev2.NodeSet2.xml) says
so. A model which needs a different structure declares a new DataType.

Removing a model takes its nodes off the server but **leaves its namespace in the
namespace table**. Namespace indexes a client already resolved therefore stay valid, and
`Load` puts the same model back under the same indexes.

---

# Part two — a document overlaid on a generated model

The half above is for a model the server has no code for. This half is the case a vendor
actually meets more often: **the model is compiled in, and only part of it is known at
build time.** An OEM ships a `SiteType` with two station slots; which stations a
particular site has, and what is bolted onto them, arrives as a NodeSet2 document beside
the executable.

[`Server/ModelDesign.xml`](Server/ModelDesign.xml) is that model:

```
StationType : BaseObjectType        Site : SiteType   (under the Objects folder)
    Throughput  Double                  Station1 : StationType   <- a slot
    Status      String                  Station2 : StationType   <- a slot
    Reset(ClearFaults) : Accepted
```

and [`SiteNodeManager.Configure`](Server/SiteNodeManager.cs) is the whole of the overlay:

```csharp
// import first
foreach (UANodeSet document in m_library.ReadOverlayDocuments())
{
    builder.Import(document);
}

// the imported nodes resolve immediately, as the generated types
m_station1 = builder.Node<StationState>(SiteId(SiteOverlayIds.Station1)).Node;
builder.Node<ResetMethodState>(SiteId(SiteOverlayIds.Station3_Reset)).OnCall(OnResetAsync);
```

## What the two documents do to the model

| Node | Where it comes from | What happened |
|---|---|---|
| `Site/Station1` | `Site.Layout` | **replaced** the generated placeholder `ns;i=101`, which left the address space |
| `Site/Station2` | the model | untouched — no document claims that slot |
| `Site/Station3` | `Site.Layout` | **added** below `Site`, a node the manager already owns |
| `Site/Station3/Reset` | `Site.Instrumentation` | added, with a parent that only exists in the *other* document |
| `Site/Station3/Temperature` | `Site.Instrumentation` | added, and no station type declares it |

Four rules produce that table, and they are the reason this is worth a sample.

**One batch per `Configure` pass.** Every document imported during one pass is linked
exactly once, after the pass returns. A node may therefore name a parent which lives in
another document of the batch (`Station3/Reset` → `Station3`) or a node the manager
already owns (`Station3` → `Site`). Import order does not matter.

**Typed states, no reflection.** The generator emits
`QuickstartsRuntimeNodeSetsSiteNodeSetImportFactoryProvider` — one factory per model type,
each calling a concrete constructor — and makes the generated node manager implement
`INodeSetImportFactoryProvider` by delegating to it. `builder.Import(document)` with no
provider argument resolves it off the node manager. An Object or Variable is matched by
its **TypeDefinition**, a Method by its **MethodDeclarationId**:

* `Station1` and `Station3` declare `HasTypeDefinition ns=1;i=10` (`StationType`), so they
  materialize as `StationState`.
* `Station3/Reset` declares `MethodDeclarationId="ns=1;i=13"` (`StationType/Reset`), so it
  materializes as `ResetMethodState`.
* `Throughput`, `Status` and `Temperature` declare the base `BaseDataVariableType`, which
  no factory claims, so they are plain `BaseDataVariableState`s — which is also why
  `StationState.Throughput` stays `null` on an imported station.

Nothing is looked up at run time, which is what keeps the path NativeAOT-safe. The sample
proves the match rather than asserting it: `builder.Node<StationState>(...)` answers
`BadTypeMismatch` if the node is not one, so a server which failed to materialize the
typed state would not start.

**An imported instance carries exactly the children its document declares.** A factory
returns an *empty* state, so the mandatory children of `StationType` are not materialized
behind the document's back. `Station1` therefore has no `Reset`: its document declares a
`Throughput` and a `Status` and nothing else, and the `Reset` of the placeholder it
displaced went with the placeholder. `Station2`, which no document claimed, still has all
three. An imported Method is the same rule one level down — `Station3/Reset` spells out
its `InputArguments` and `OutputArguments` Properties, because a Method which does not is
a Method nobody can call.

**Placeholder replacement needs a slot, and a slot is a declaration on the *type*.** An
imported child lands in a slot when the parent's generated state declares a child of that
browse name; the displaced node and every descendant the replacement does not carry over
leave the address space, and references to it are retargeted at the replacement. This is
why `Station1` and `Station2` are declared on `SiteType` and not on the `Site` instance:

> An instance of a plain `ua:BaseObjectType` has no generated state class and therefore no
> slots. The same document would then leave the placeholder where it is and add a **second**
> `Station1` beside it — two children with one browse name, and no error anywhere.

## What the overlay refuses

**Wiring a node the import is about to displace.** The rule is *import, then wire*:

```csharp
builder.Variable<double>("Site/Station1/Throughput").OnRead(...);   // don't
builder.Import(layout);
```

The server does not start, and says why:

```
fail: Opc.Ua.Server.MasterNodeManager[201]
      Unexpected error creating address space for NodeManager =SiteNodeManager.
      Imported node '3:Throughput' replaces configured node 'ns=3;i=102'.
      Import the NodeSet before wiring that node.
      [80AF0000] (BadInvalidState)
```

`ns=3;i=102` is `Site/Station1/Throughput` **of the placeholder** — the node the browse
path led to at the moment it was wired, not the one the document brings. The builder
refuses rather than silently throwing the wiring away, and it looks for more than the
obvious: an `On*` callback on the node, a per-node hook keyed by its NodeId (history,
monitored items, node added or removed), and the same again for every node in the subtree
the placeholder is taking with it.

**Sealing a builder with an unregistered import batch.**

```
ServiceResultException: BadInvalidState
The imported NodeSet documents have not been registered. A manager which imports
NodeSets must complete the Configure pass through CompleteConfigureAsync before sealing.
```

A source-generated manager cannot reach this one — the emitted partial always completes
the batch — and this sample therefore never produces it. It is there for a hand-written
`FluentNodeManagerBase` which drives `Seal()` itself: the fix is to call
`CompleteConfigureAsync` rather than sealing directly.

## What a client can see

An imported node is an ordinary node. A Browse response never says which document it came
from, and there is no attribute to ask. What a client *can* do is compare the address
space with the type model, which is what
[`BrowseSiteModelAsync`](Client/Model/RuntimeNodeSetsClientModel.cs) does: for every node
it browses the TypeDefinition of the parent and asks whether that type declares a child of
this browse name.

Against this server the answer separates the halves — `Station3` and
`Station3/Temperature` are nodes no type accounts for, and `Station1` is missing a `Reset`
its type does declare. The client shows that in the last column of the site list.

## Running it

```bash
dotnet run --project "Workshop/RuntimeNodeSets/Server/RuntimeNodeSets Server.csproj"
```

```bash
dotnet run --project "Workshop/RuntimeNodeSets/Client/RuntimeNodeSets Client.csproj"
```

**Server → Connect.** The window is the two halves side by side. On the left, the vendor
model as it is published right now, with the line above it saying which revision that is
and which generation of the registration is serving it. On the right, the site model after
the overlay was imported into it.

The left list is the one that moves:

1. Press **Watch the speed** and wait for a value to arrive.
2. Pick `Rev2`, pick a reload mode, press **Reload**. `Conveyor2` and the `Throughput`
   variables appear, the generation counter moves on, and the notification list shows what
   that mode did to the MonitoredItem.
3. Press **Remove**. The model is gone from the address space; the session is not.
4. Pick `Rev1` and press **Load**. It is back, under the same NodeIds.

The status bar reports the status code of every call, because the refusals are worth
seeing: a `Load` over a published model is `BadInvalidState`, and a revision the server
has no document for is `BadInvalidArgument`.

The right list never moves, and that is the point of it: an overlay is imported once, at
start up, into the node manager the model was generated for. Read it downwards —
`Station1` with two children, `Station2` with three, `Station3` and `Temperature` marked
as nodes no type declares.

## Notes for implementers

* **`DefaultNamespaceUri`** is what the browse paths in a `Configure` callback resolve
  against when they carry no `ns=N;` prefix. The factory infers it when exactly one loaded
  model is a leaf, and fails at start up when the inference is ambiguous and a `Configure`
  is set. Both models here name it rather than relying on the inference.
* **Several documents in one registration.** `RuntimeNodeSetOptions.Sources` takes a
  collection, and the factory imports them in `RequiredModel` dependency order.
  A dependency no source provides is allowed - it is assumed to be already in the server;
  a cycle among the included sources fails the start up.
* **The `Configure` hook runs before the node manager is published**, and resolves its
  browse paths eagerly. A path which does not resolve throws there, at start up or at the
  moment of the reload, rather than at the first request.
* **A getter needs a value to start from.** `builder.Variable<T>(path).OnRead(...)` is not
  enough on its own: a variable whose document declares no `<Value>` starts on
  `BadWaitingForInitialData`, and a read reports that status rather than what the getter
  returns. The variables of the control model therefore carry an empty initial value.
* **The security of the Methods is left out on purpose.** Loading and removing a model is
  an administrative operation and this sample lets any anonymous session do it, because
  that is what makes it easy to try. A server which means it puts those Methods behind a
  Role on a `SignAndEncrypt` endpoint - see [RoleManagement](../RoleManagement/README.md).
* **An overlay is written against identifiers, so fix them.** `Server/ModelDesign.csv`
  pins every node of the site model to a number, because
  [`Site.Layout.NodeSet2.xml`](Server/NodeSets/Site.Layout.NodeSet2.xml) names them. The
  identifiers the documents bring with them are in
  [`SiteOverlayIds.cs`](Server/SiteOverlayIds.cs), which is the class the generator cannot
  emit: it never sees the documents. Nothing checks the two against each other at compile
  time, which is the price of a model that is only known at run time - but `Configure`
  resolves every one of them, so a mismatch fails at start up rather than at the first
  request.
* **Resolve a replaced node by the NodeId its document gives it.** Anything which walks to
  it by browse name - `builder.Variable<T>("Site/Station1/Throughput")`, or the typed
  `Site.Station1` accessor the generator emits on `ISiteNodeManagerBuilder` - finds
  whatever is in the slot *at that moment*, and during the `Configure` pass that is still
  the placeholder: the batch is linked after the pass returns. `Configure` therefore names
  `ns;i=5001`, and touching the placeholder instead is the `BadInvalidState` above.
* **Give the node manager its dependencies through a derived factory.** The generated
  `SiteNodeManagerFactory` calls the two-argument constructor. `SiteOverlayNodeManagerFactory`
  derives from it, takes the document library out of the container and calls a
  three-argument constructor declared in the hand-written partial. That is the supported
  route; the generated factory is `partial` but its `CreateAsync` cannot be overridden
  from within the same class.

## What this sample does not cover

Loading a document from anywhere but a file (`RuntimeNodeSetSource.FromStream` takes a
stream factory, for a document which arrives over HTTP or out of a database; an overlay
reaches `UANodeSet.Read` from any stream at all), several vendor models side by side,
`ConfigureAsync` and the `IAsyncDisposable` a generation can own (for a simulation loop
which has to be torn down when its generation is retired), or persisting across a restart
which model was published.

Nor **re-importing an overlay while the server runs**: `builder.Import` is a `Configure`
pass, and a `Configure` pass happens when the node manager is created. A model whose
overlay has to change while clients are connected is a model for the runtime half of this
sample, not for the overlay half.

For the other half of the runtime-model story - turning a DataType which arrived this way
into an XSD, BSD or JSON Schema - see [DataTypes](../DataTypes/README.md#schemas-at-run-time).

## Tests

| Fixture | Tier | What it drives |
|---|---|---|
| `Tests/SampleNodeManagers.Tests/RuntimeNodeSetsNodeManagerTests.cs` | 1.5 | the runtime half, over a real session |
| `Tests/SampleNodeManagers.Tests/RuntimeNodeSetsOverlayNodeManagerTests.cs` | 1.5 | the overlay half, over a real session |
| `Tests/SampleClientModels.Tests/RuntimeNodeSetsClientModelTests.cs` | 1.7 | the client model, without its window |

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~RuntimeNodeSets"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers.
