# Node Management Quickstart (OPC UA Part 4 §5.8)

A server/client pair which demonstrates the **NodeManagement service set**: a client creates
and deletes nodes and references in the running server's address space, and the server decides
how far it may go.

| Project | What it is |
|---------|------------|
| [Server](Server) | A `StandardServer` whose node manager opts in to NodeManagement |
| [Client](Client) | A Windows Forms client which builds the server's address space with the four services |

Endpoints: `opc.tcp://localhost:62575/Quickstarts/NodeManagementServer` and
`https://localhost:62574/Quickstarts/NodeManagementServer`.

The upstream reference for the SDK side is
[NodeManagement.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/NodeManagement.md).

## The four services

| Service | What it does here |
|---------|-------------------|
| `AddNodes` | Creates an object or a variable below `Plant/Devices` |
| `DeleteNodes` | Removes one of those again, with the references that point at it |
| `AddReferences` | Makes an existing node reachable from `Plant/Commissioned` as well |
| `DeleteReferences` | Removes that reference — and leaves the node where it was |

The distinction the last two rows make is the point of having a group folder at all:
`AddReferences` does not copy and does not move. After it, the same node is browsable under
both folders, and it is the same node under each.

## What the server has to do

**Almost nothing, and then several things.** The SDK implements all four services in
`StandardServer`, dispatches them per item in `MasterNodeManager`, emits the audit events, and
implements the actual work in `AsyncCustomNodeManager`. A node manager opts in with one
property:

```csharp
public override bool AllowNodeManagement => true;
```

That is the whole of the opt-in, and it is
[`NodeManagementNodeManager`](Server/NodeManagementNodeManager.cs). The rest of that file is
what the SDK cannot decide for a server, and a sample which only flipped the property would
suggest there is nothing to decide:

* **Where clients may build.** `AddNodeAsync` refuses any parent outside `Devices` with
  `BadParentNodeIdInvalid`. Below `Devices` it accepts any depth, so a client can add a device
  and then give that device variables of its own.
* **What clients may not remove.** `DeleteNodeAsync` refuses every node the model shipped with
  `BadUserAccessDenied`. A server which opts in and protects nothing can be emptied by its
  first client — `DeleteNodes` on `Plant` would take the folders and the counter with it.
* **What a server-assigned NodeId looks like.** `New` — the `INodeIdFactory` hook — builds a
  string identifier from the browse name, so `Pump1` becomes `ns=2;s=Pump1-1`. A client which
  wants a particular NodeId sends `RequestedNewNodeId` instead.
* **State derived from an address space the server does not control.** `DeviceCount` is
  updated from both methods, and is the only reason either of them does anything after
  delegating to the base implementation.

## What the client has to get right

### The browse name carries the namespace

When a client leaves `RequestedNewNodeId` empty, the server routes the item to a node manager
**by the namespace index of the `BrowseName`**. A `QualifiedName` built from a bare string is
in namespace zero, which is the standard address space, whose node manager has not opted in —
so the request comes back `BadUserAccessDenied` rather than landing in the wrong folder. The
client keeps the index it looked up at connect time and puts it on every browse name it sends.

### Attributes are how a client says what it is creating

A variable added without `NodeAttributes` is a `BaseDataType` with no value and read-only
access. The client sends `VariableAttributes` with `DataType`, `ValueRank`, `AccessLevel`,
`UserAccessLevel` and `Value`, and — the easy mistake — a `SpecifiedAttributes` mask which
names all of them. Only the attributes in the mask are applied; a field left out of it is
silently ignored.

### `DeleteTargetReferences`

`DeleteNodes` removes the node. The reference its parent holds to it is removed only when the
item sets `DeleteTargetReferences`, and a browse of a parent which kept one reports a child
that cannot be read.

### The address space can change under you

This is the situation the service set creates: two clients connected to one server, either of
them free to add and delete. The server answers it the way Part 5 §9.32 prescribes, and the
client subscribes:

* The node manager calls `EnableModelChangeTrackingFor` on its three folders. That attaches a
  `NodeVersion` property to each — which is why a browse of `Devices` shows one — and makes
  the folder eligible to appear in a `GeneralModelChangeEvent`.
* `AddNodes` and `DeleteNodes` then raise that event by themselves. What it names is the
  **folder**, not the new node: Part 5 §9.32.2 only reports a node which carries a
  `NodeVersion`, and a node a client just created has none.
* The client subscribes to `GeneralModelChangeEventType` on the Server object and browses
  again whenever one arrives. Open two copies of the client and add a node in one of them.

## Error codes worth trying

Every button reports the status code the server answered into the status bar rather than into
a dialog, because the refusals are half of what there is to see.

| Try this | Answer |
|----------|--------|
| Add a node whose name a sibling already has | `BadBrowseNameDuplicated` |
| Add a node with a `RequestedNewNodeId` which exists | `BadNodeIdExists` |
| Select `Commissioned`, then *Add object* | `BadParentNodeIdInvalid` — the sample's own rule |
| Select `Devices`, then *Delete selected* | `BadUserAccessDenied` — the model is not deletable |
| *Try it on a standard node* | `BadUserAccessDenied` — the core node manager never opted in |
| Reference the same node into the group twice | `BadDuplicateReferenceNotAllowed` |

The last two are worth separating. `BadUserAccessDenied` is the status the service set defines
for "this server does not allow this operation", and the opt-in that decides it is **per node
manager**, not per server: the same request is accepted for `Plant` and refused for
`ServerCapabilities`, because a different node manager owns each.

## Running it

```bash
dotnet run --project "Workshop/NodeManagement/Server/NodeManagement Server.csproj"
```

```bash
dotnet run --project "Workshop/NodeManagement/Client/NodeManagement Client.csproj"
```

**Server → Connect**, then type a name and press *Add object*. The upper list is the plant:
everything below `Devices` was put there by a client. Select the new object and press *Add
variable* to give it one, or *Reference the selected node* to make it reachable from the group
in the lower list as well.

## Notes for implementers

* The server declares `MaxNodesPerNodeManagement` in its
  [configuration](Server/Quickstarts.NodeManagementServer.Config.xml). That is the operation
  limit which bounds all four services; a request with more items is refused with
  `BadTooManyOperations` before any item is dispatched. Leaving the block out means *no limit*,
  which is not the same as advertising support.
* `AuditingEnabled` is on, so the `AuditAddNodesEventType`, `AuditDeleteNodesEventType`,
  `AuditAddReferencesEventType` and `AuditDeleteReferencesEventType` the four services raise
  reach a client which subscribes to the Server object.
* An inverse edge is mirrored by the dispatcher only when the target is owned by a **different**
  node manager. Source and target are in the same one here, so *Reference the selected node*
  adds the forward edge alone, and dropping it again sets `DeleteBidirectional` to false. A
  client which wants both directions inside one node manager sends two items.

## What this sample does not cover

The `Method`, `View`, `ObjectType`, `VariableType`, `ReferenceType` and `DataType` node
classes: `AsyncCustomNodeManager` implements `Object` and `Variable` and answers
`BadNodeClassInvalid` for the rest, so a server which wants them overrides `AddNodeAsync`.
Nor cross-node-manager parents and targets, node management gated by Part 18 `RolePermissions`
(`AddNode`, `DeleteNode`, `AddReference`, `RemoveReference` are permissions like `Read` and
`Write` — see [RoleManagement](../RoleManagement/README.md)), namespace-level
`DefaultRolePermissions`, or persisting an address space clients built across a restart.

## Tests

`Tests/SampleNodeManagers.Tests/NodeManagementNodeManagerTests.cs` (tier 1.5) drives all of
the above over a real session, and
`Tests/SampleClients.Tests/NodeManagementClientTests.cs` (tier 2) drives the four services
through the form:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~NodeManagementNodeManagerTests"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers.
