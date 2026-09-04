# Runtime NodeSets Quickstart

A server/client pair for the case every other sample in this repository skips: **a vendor
sent a NodeSet2 document and it has to be served as it is** — no model design, no source
generator, no node manager class — and it has to be possible to replace it while the
server is running.

| Project | What it is |
|---------|------------|
| [Server](Server) | A server whose entire address space is two NodeSet2 XML files it reads at start up |
| [Client](Client) | A Windows Forms client which loads, reloads and removes one of them over OPC UA |

Endpoint: `opc.tcp://localhost:62579/Quickstarts/RuntimeNodeSetsServer`.

The upstream references for the SDK side are
[RuntimeNodeSets.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/RuntimeNodeSets.md)
and [NodeManagers.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/NodeManagers.md#reload-modes).

## No generated code anywhere

Every other server sample here either source-generates its model from a `ModelDesign.xml`
or hand-builds `NodeState` objects. This one does neither. `Server/NodeSets` holds three
XML documents and the C# knows two things about them: a file name and a namespace URI.

| Document | Namespace | What it is |
|---|---|---|
| `ModelControl.NodeSet2.xml` | `.../RuntimeNodeSets/Control/` | `ModelControl` with the `Load`, `Reload` and `Remove` Methods |
| `ConveyorLine.Rev1.NodeSet2.xml` | `.../RuntimeNodeSets/Line/` | The vendor model: a conveyor type, a `ConveyorState` enumeration, one conveyor |
| `ConveyorLine.Rev2.NodeSet2.xml` | the same | The vendor model after the vendor added `Throughput` and a second conveyor |

Both revisions carry the **same `ModelUri`**, which is what makes loading one over the
other a reload of one namespace rather than the addition of a second model. The client
compiles nothing of either: it finds `ModelControl`, the Methods and the conveyors by
browse path in those two namespaces, which is exactly the position a client is in when it
meets a model it was not built against.

## The two ways in, and why this sample uses both

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

## Known gap on 2.0.0-preview.4

**A Method imported from a NodeSet2 document loses its typed `InputArguments`.** The
importer materializes the `InputArguments` Property as an untyped `PropertyState` child
and never assigns it to `MethodState.InputArguments`, which stays `null`. A client reads
the Property and sees the arguments the document declares — but `Call` validates against
the typed Property, finds none, and answers `BadTooManyArguments` for every call which
carries an argument.

`RuntimeNodeSetController.BindInputArguments` repairs it in the `Configure` hook:
`NodeState.CreateChild` with `createOrReplace` creates the typed Property and assigns it
(`PropertyState<T>` is abstract and cannot be constructed directly), and the declared
arguments are decoded out of the imported child before that child is dropped. The method
returns without doing anything as soon as the SDK materializes the typed Property itself,
so it can be deleted rather than maintained.

Anyone serving Methods out of a vendor NodeSet2 on this preview needs the same six lines.
Raised upstream as
[UA-.NETStandard#4422](https://github.com/OPCFoundation/UA-.NETStandard/issues/4422), which
is the symptom
[#1056](https://github.com/OPCFoundation/UA-.NETStandard/issues/1056) first reported in
2022 for a hand-rolled `UANodeSet.Import`.

## Running it

```bash
dotnet run --project "Workshop/RuntimeNodeSets/Server/RuntimeNodeSets Server.csproj"
```

```bash
dotnet run --project "Workshop/RuntimeNodeSets/Client/RuntimeNodeSets Client.csproj"
```

**Server → Connect.** The list shows the vendor model as it is published right now, and
the line above it says which revision that is and which generation of the registration is
serving it. Then:

1. Press **Watch the speed** and wait for a value to arrive.
2. Pick `Rev2`, pick a reload mode, press **Reload**. `Conveyor2` and the `Throughput`
   variables appear, the generation counter moves on, and the notification list shows what
   that mode did to the MonitoredItem.
3. Press **Remove**. The model is gone from the address space; the session is not.
4. Pick `Rev1` and press **Load**. It is back, under the same NodeIds.

The status bar reports the status code of every call, because the refusals are worth
seeing: a `Load` over a published model is `BadInvalidState`, and a revision the server
has no document for is `BadInvalidArgument`.

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

## What this sample does not cover

Loading a document from anywhere but a file (`RuntimeNodeSetSource.FromStream` takes a
stream factory, for a document which arrives over HTTP or out of a database), several
vendor models side by side, `ConfigureAsync` and the `IAsyncDisposable` a generation can
own (for a simulation loop which has to be torn down when its generation is retired), or
persisting across a restart which model was published.

For the other half of the runtime-model story - turning a DataType which arrived this way
into an XSD, BSD or JSON Schema - see [DataTypes](../DataTypes/README.md#schemas-at-run-time).

## Tests

`Tests/SampleNodeManagers.Tests/RuntimeNodeSetsNodeManagerTests.cs` (tier 1.5) drives the
server over a real session, and
`Tests/SampleClientModels.Tests/RuntimeNodeSetsClientModelTests.cs` (tier 1.7) drives the
client model without its window:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~RuntimeNodeSetsNodeManagerTests"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers.
