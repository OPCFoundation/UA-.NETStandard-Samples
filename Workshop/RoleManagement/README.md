# Role Management Quickstart (OPC UA Part 18)

A server/client pair which demonstrates **OPC 10000-18, Role-Based Security**: who a Session
is — and what it connected with — which Roles that earns it, what those Roles are allowed to
do with a node, what the channel decides regardless of them, and how an administrator changes
all of that while the server is running.

| Project | What it is |
|---------|------------|
| [Server](Server) | A `StandardServer` with six protected nodes and a Role configuration |
| [Client](Client) | A Windows Forms client which signs in as different accounts, manages the RoleSet and watches the audit trail |

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
| *(nobody)* | `ConfigureAdmin` — earned by a **certificate**, not by an account |

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

### 1b. …and so does a certificate, on one endpoint

`ConfigureAdmin` is the Role of the sample which belongs to a machine rather than to a
person. Its configuration uses two more parts of Part 18:

* an identity mapping rule of criteria type **`X509Subject`** whose criteria is the subject
  name of the application instance certificate the sample **client** creates for itself. Any
  Session that client opens holds the Role, anonymous or signed in, and no Session from any
  other client does — however it signs in.
* an **`Endpoints` filter** (§4.4.1), which is evaluated *before* any identity rule, so the
  Role is refused on the unsecured endpoint. On an unsecured channel there is no client
  certificate to judge in the first place, which makes the two halves consistent.

> The criteria is matched against the **application instance certificate of the client** —
> the one it sends in `CreateSession` — not against a user certificate. Part 18 §4.4.3 reads
> either way; this is what the stack does, and it is also the only certificate a Session
> opened with an anonymous or a user name token has.
>
> The criteria string is a normalised subject: `Name="Value"` pairs separated by slashes, in
> the order CN, O, OU, DC, L, S, C. The sample writes it out in
> `RoleManagementServer.WorkstationCertificateSubject`, with the host name in the `DC`
> because that is what the stack substitutes for `DC=localhost`. Client and server therefore
> have to run on the same machine, which is what a Quickstart does.

`CustomConfiguration` is left `false`. It is the flag which lets a Role with an **empty**
`Identities` list be granted at all (§4.4.1): revoke the `X509Subject` rule of
`ConfigureAdmin`, set the flag with the client's *Toggle CustomConfiguration* button, and
the endpoint filter becomes the whole of the configuration — every Session on the encrypted
endpoint holds the Role.

### 2. Roles decide what a node allows

`RoleManagementNodeManager.Configure` writes a `RolePermissions` attribute onto every node of
the model. The master node manager checks it before a service touches the node, so no handler
in the node manager checks anything itself.

| Node | Anonymous | Observer | Operator | Engineer | Supervisor | ConfigureAdmin | SecurityAdmin |
|------|-----------|----------|----------|----------|------------|----------------|---------------|
| `Machine` | Browse | Browse | Browse | Browse | Browse | Browse | Browse |
| `Temperature` | Browse | Browse, Read | Browse, Read | Browse, Read | Browse, Read | — | Browse |
| `SetPoint` | Browse | Browse, Read | Browse, Read, **Write** | Browse, Read, **Write** | Browse, Read | — | Browse |
| `Calibration` | — | — | Browse | Browse, Read, **Write** | Browse, Read | — | — |
| `MaintenanceNote` | — | — | Browse, Read | Browse, Read | Browse, Read, **Write** | — | — |
| `ServiceCode` | — | — | — | Browse, Read | — | Browse, Read, **Write** | — |
| `Reset` (Method) | Browse | Browse | Browse, **Call** | Browse, **Call** | Browse | — | Browse |

Every row also carries `ReadRolePermissions`, so a client can read the `UserRolePermissions`
attribute and tell the user what it may do before it tries.

Two things are worth watching in the client:

* **Browse is a permission.** An Observer browsing the machine finds three children, an
  Engineer finds five. `Calibration` and `MaintenanceNote` are simply not in the address
  space an Observer sees.
* **Call is not Write.** An Observer sees the `Reset` Method and is refused the call with
  `BadUserAccessDenied`.

### 2b. …and the channel decides separately

`AccessRestrictions` (Part 3 §5.2.11) is the other half of the access story, and the master
node manager checks it right after the role permissions. It says nothing about who the
Session is: no Role talks its way past one.

| Node | Restriction | What an Operator or Engineer sees on the unsecured endpoint |
|------|-------------|-------------------------------------------------------------|
| `Calibration` | `EncryptionRequired` | the node is there; reading or writing its value answers `BadSecurityModeInsufficient` |
| `MaintenanceNote` | `EncryptionRequired`, `ApplyRestrictionsToBrowse` | as above, and browsing the node itself is refused too |

The distinction the client makes visible is between the two refusals.
`BadUserAccessDenied` means *sign in as somebody else*; `BadSecurityModeInsufficient` means
*reconnect with security*. The client's **Access restrictions** column shows the attribute
where it can read it — which is nowhere on an unsecured channel, because reading any
attribute other than the Value is checked against the restrictions too.

> A server which wants a whole namespace behind an encrypted channel sets
> `DefaultAccessRestrictions` on its `NamespaceMetadata` node instead of repeating the
> attribute per node. This sample sets each node explicitly because the point here is to see
> which node carries what.
>
> `ApplyRestrictionsToBrowse` does **not** take the node out of the reference list of its
> parent in 2.0.0-preview.4: the per-reference filter of a Browse applies role permissions
> only, so `MaintenanceNote` is still listed below the machine on an unencrypted channel and
> only a Browse of the node itself is refused.

### 3. An administrator changes it at runtime

The stack binds the standard `RoleSet` object below `Server/ServerCapabilities` to the same
Role manager, so the whole Part 18 §4.2/§4.4 API is available over OPC UA:

* `RoleSet.AddRole` / `RemoveRole` — create and delete a Role of the server's own
* `<Role>.AddIdentity` / `RemoveIdentity` — grant and revoke a Role for a user name, or for
  the certificate of a client application: the drop down beside the criteria box picks
  `UserName`, `Thumbprint` or `X509Subject`, and fills the box with what *this* client would
  present for the last two
* `<Role>.CustomConfiguration` — the *Toggle CustomConfiguration* button writes the Property
* `<Role>.AddApplication`, `AddEndpoint`, and the `…Exclude` properties

All of them are gated by `RoleAuthorizationGate.CheckAdmin`, which answers
`BadSecurityModeInsufficient` on an unencrypted channel and `BadUserAccessDenied` for a
Session which does not hold `SecurityAdmin`. The client leaves the buttons enabled for every
account on purpose, because seeing those two refusals is half the point.

The other half is Part 18 §4.4.1: **the Roles of an active Session are re-evaluated when the
configuration of a Role changes.** Connect one client as `guest` and try to write the set
point — refused. Connect a second one as `secadmin`, select `Operator` and grant it to
`guest`. The first client can now write, without reconnecting.

### 4. Every change is audited

Part 18 §4.4 asks a server to audit each change to its role configuration, and the stack
reports a `RoleMappingRuleChangedAuditEventType` from the RoleSet binding for every one of
those Methods — but only when the server sets `AuditingEnabled`, which this sample now does.
The client subscribes to `AuditEventType` on the Server object and lists what arrives in its
third panel.

> **The panel stays empty against 2.0.0-preview.4.** Measured: `Server.Auditing` reads
> `true`, `AddIdentity` answers `Good`, and a subscription on the same Server object does
> receive a `GeneralModelChangeEvent` — so neither the configuration of the sample nor the
> event path is the problem, and no audit event of any type reaches a subscriber. The
> tier 1.5 fixture is written the right way round and recorded as a known issue, so it turns
> into a failure asking for the note to be removed the moment the stack delivers them.

## Running it

```bash
dotnet run --project "Workshop/RoleManagement/Server/RoleManagement Server.csproj"
```

```bash
dotnet run --project "Workshop/RoleManagement/Client/RoleManagement Client.csproj"
```

In the client, pick an account in **Sign in as**, then **Server → Connect**. The upper list is
the machine as that Session sees it, the middle list is the RoleSet, and the lower list is the
audit trail. Reconnect as a different account to see the first two change — and reconnect with
the **Use Security** box cleared to see what the channel decides rather than the account:
`Calibration` and `MaintenanceNote` stop giving up their values, and `ServiceCode` disappears
because the `ConfigureAdmin` Role this workstation earns from its certificate is only granted
on the encrypted endpoints.

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
* `IRoleManager.AddEndpoint` refuses an entry whose `EndpointUrl` is empty, although Part 18
  §4.4.2 says a field left at its default value is ignored during the comparison — so a rule
  which constrains the security mode alone matches everything but cannot be stored. The
  server copies the endpoint descriptions it actually advertises instead, which is why that
  part of the Role configuration runs in `OnServerStarted` rather than in
  `CreateRoleManager`: the URLs are not known before the start, and the comparison is an
  exact string match.
* `ApplyRestrictionsToBrowse` covers a Browse of the restricted node itself, and a
  `TranslateBrowsePathsToNodeIds` which starts there, but not the reference to it in its
  parent's browse result: that per-reference filter applies role permissions only.
* No audit event reaches a subscriber in 2.0.0-preview.4 — see §4 above.

## What this sample does not cover

Part 18 is larger than one sample. The `Role`, `GroupId`, `Application` and
`TrustedApplication` identity criteria, the `Applications` filter of a Role, the §5 user
management model, event and history permissions, namespace-level default permissions, and a
persistent `IRoleManager` are all outside it. They are listed, with what each would take,
in [#836](https://github.com/OPCFoundation/UA-.NETStandard-Samples/issues/836).

## Tests

`Tests/SampleNodeManagers.Tests/RoleManagementNodeManagerTests.cs` (tier 1.5) drives all of
the above over a real session, one fixture per behaviour:

```bash
dotnet test Tests/SampleNodeManagers.Tests --filter "FullyQualifiedName~RoleManagementNodeManagerTests"
```

`Tests/SampleClients.Tests/RoleManagementClientTests.cs` (tier 2) drives the Windows Forms
client itself, including the fixture which holds the server's hard-coded `X509Subject`
criteria to the certificate the client's own configuration file produces:

```bash
dotnet test Tests/SampleClients.Tests --filter "FullyQualifiedName~RoleManagementClientTests"
```

See [docs/TESTING.md](../../docs/TESTING.md) for the tiers.
