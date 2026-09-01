# Role Management Quickstart (OPC UA Part 18)

A server/client pair which demonstrates **OPC 10000-18, Role-Based Security**: who a Session
is, which Roles that earns it, what those Roles are allowed to do with a node, and how an
administrator changes all of that while the server is running.

| Project | What it is |
|---------|------------|
| [Server](Server) | A `StandardServer` with five protected nodes and a Role configuration |
| [Client](Client) | A Windows Forms client which signs in as different accounts and manages the RoleSet |

Endpoints: `opc.tcp://localhost:62573/Quickstarts/RoleManagementServer` and
`https://localhost:62572/Quickstarts/RoleManagementServer`.

## What the sample shows

### 1. Identities earn Roles

The server keeps six demonstration accounts. The password of each is its own user name.

| Account | Role it is granted |
|---------|--------------------|
| `observer1` | `Observer` |
| `operator1` | `Operator` |
| `engineer1` | `Engineer` |
| `supervisor1` | `Supervisor` |
| `secadmin` | `SecurityAdmin` |
| `guest` | none beyond `AuthenticatedUser` |

`RoleManagementServer.CreateRoleManager` adds one Part 18 §4.4.3 identity mapping rule per
account (`CriteriaType = UserName`, `Criteria = <account>`) to the default
`Opc.Ua.Server.RoleManager`, which already carries the nine well known Roles of Part 3 §4.9.2.
Nothing else is needed: when a Session activates, the session manager asks the Role manager
which Roles the authenticated identity earns and wraps the identity in a `RoleBasedIdentity`
that carries them.

Authentication is separate and much simpler — `AuthenticateUserNameAsync` only checks the
password. Which Role that identity is worth is not its business.

> Part 18 §4.3 gives the `Anonymous` Role both the `Anonymous` and the `AuthenticatedUser`
> criteria, so **every** Session holds it. A node which names `Anonymous` in its permissions
> is saying "anyone who got this far", and a signed-in Session holds `AuthenticatedUser` and
> its own Role on top of that.

### 2. Roles decide what a node allows

`RoleManagementNodeManager.Configure` writes a `RolePermissions` attribute onto every node of
the model. The master node manager checks it before a service touches the node, so no handler
in the node manager checks anything itself.

| Node | Anonymous | Observer | Operator | Engineer | Supervisor | SecurityAdmin |
|------|-----------|----------|----------|----------|------------|---------------|
| `Machine` | Browse | Browse | Browse | Browse | Browse | Browse |
| `Temperature` | Browse | Browse, Read | Browse, Read | Browse, Read | Browse, Read | Browse |
| `SetPoint` | Browse | Browse, Read | Browse, Read, **Write** | Browse, Read, **Write** | Browse, Read | Browse |
| `Calibration` | — | — | Browse | Browse, Read, **Write** | Browse, Read | — |
| `MaintenanceNote` | — | — | Browse, Read | Browse, Read | Browse, Read, **Write** | — |
| `Reset` (Method) | Browse | Browse | Browse, **Call** | Browse, **Call** | Browse | Browse |

Every row also carries `ReadRolePermissions`, so a client can read the `UserRolePermissions`
attribute and tell the user what it may do before it tries.

Two things are worth watching in the client:

* **Browse is a permission.** An Observer browsing the machine finds three children, an
  Engineer finds five. `Calibration` and `MaintenanceNote` are simply not in the address
  space an Observer sees.
* **Call is not Write.** An Observer sees the `Reset` Method and is refused the call with
  `BadUserAccessDenied`.

### 3. An administrator changes it at runtime

The stack binds the standard `RoleSet` object below `Server/ServerCapabilities` to the same
Role manager, so the whole Part 18 §4.2/§4.4 API is available over OPC UA:

* `RoleSet.AddRole` / `RemoveRole` — create and delete a Role of the server's own
* `<Role>.AddIdentity` / `RemoveIdentity` — grant and revoke a Role for a user
* `<Role>.AddApplication`, `AddEndpoint`, and the `…Exclude` properties

All of them are gated by `RoleAuthorizationGate.CheckAdmin`, which answers
`BadSecurityModeInsufficient` on an unencrypted channel and `BadUserAccessDenied` for a
Session which does not hold `SecurityAdmin`. The client leaves the buttons enabled for every
account on purpose, because seeing those two refusals is half the point.

The other half is Part 18 §4.4.1: **the Roles of an active Session are re-evaluated when the
configuration of a Role changes.** Connect one client as `guest` and try to write the set
point — refused. Connect a second one as `secadmin`, select `Operator` and grant it to
`guest`. The first client can now write, without reconnecting.

## Running it

```bash
dotnet run --project "Workshop/RoleManagement/Server/RoleManagement Server.csproj"
```

```bash
dotnet run --project "Workshop/RoleManagement/Client/RoleManagement Client.csproj"
```

In the client, pick an account in **Sign in as**, then **Server → Connect**. The upper list is
the machine as that Session sees it; the lower list is the RoleSet. Reconnect as a different
account to see both change.

## Notes for implementers

* The sample keeps to the **well known** Roles for its node permissions. Those nine Roles are
  part of the standard address space, so each has a `RoleType` node below the `RoleSet` that a
  client can browse and manage. A Role created on the Role manager during startup would be
  honoured for access control but would have **no node**: the stack materializes a node only
  for a Role created through the `AddRole` Method. Creating one that way is what the client's
  *Add role* button demonstrates.
  ([UA-.NETStandard#4361](https://github.com/OPCFoundation/UA-.NETStandard/issues/4361))
* `AddRole` allocates the node id of a new Role by counting up from 1 in the namespace it is
  given, without checking whether that node id is already taken. Passing an empty namespace
  URI — which is what the client and the tests do — puts the Role in the namespace the server
  reserves for dynamic Roles instead of into a namespace which already holds a model, where it
  would replace the node with identifier 1.
  ([UA-.NETStandard#4361](https://github.com/OPCFoundation/UA-.NETStandard/issues/4361))
* The standard address space reserves the `RoleType` nodes themselves for the `SecurityAdmin`
  Role, so an ordinary Session cannot browse to `AddIdentity` at all. The client shows
  *(not visible to this session)* in the identities column rather than an error.

## What this sample does not cover

Part 18 is larger than one sample. The identity criteria other than `UserName`, the
`Applications` and `Endpoints` filters of a Role, the §5 user management model, event and
history permissions, `AccessRestrictions`, namespace-level default permissions, the audit
event the stack already raises for every role change, and a persistent `IRoleManager` are all
outside it. They are listed, with what each would take,
in [#836](https://github.com/OPCFoundation/UA-.NETStandard-Samples/issues/836).

## Tests

`Tests/SampleNodeManagers.Tests/RoleManagementNodeManagerTests.cs` (tier 1.5) drives all of
the above over a real session, one fixture per behaviour:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~RoleManagementNodeManagerTests"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers.
