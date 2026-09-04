# OPC UA Reference Client

A Windows Forms client which connects to any OPC UA server and browses its address
space. It pairs with the [Reference Server](../ReferenceServer), and its two menus
beyond `Connect` are what this document is about.

## Server > Export NodeSet2...

Writes the address space of the connected server to a NodeSet2 XML file.

The export starts at the node selected in the browse tree, so a single machine can be
taken out of a large address space; with nothing selected it starts at the `Objects`
folder. The nodes of the OPC UA namespace are left out - every consumer of a NodeSet2
file already has the standard address space - so what the file holds is the model of
the server.

The file is a plain NodeSet2 document: the same reader a server loads its own model
with reads it back, which is what makes an export useful for documenting a server,
diffing two versions of one, or replaying a foreign address space against a sample
server.

The same command sits on the session of the [UA Sample Client](../Client.Net4), under
`Export NodeSet2...` in the context menu of the session tree.

The browse and the writing are in
[`NodeSetExport`](../Client.Common/NodeSetExport.cs), which has no user interface in
it; the stack does the conversion in `CoreClientUtils.ExportNodesToNodeSet2`.

```csharp
NodeSetExportResult result = await NodeSetExport.ExportToFileAsync(
    session,
    "ReferenceServer.NodeSet2.xml",
    new NodeSetExportSettings {
        StartNodeId = ObjectIds.ObjectsFolder,
        NodeOptions = NodeSetExportOptions.Complete,
    });
```

`NodeSetExportSettings` decides how far the browse reaches (`StartNodeId`,
`FetchTree`, `MaxNodeCount`) and whether the standard namespace comes along
(`ExcludeStandardNamespace`); `NodeSetExportOptions` decides what is written per node -
`Default` leaves the values out for a schema-sized file, `Complete` writes them.

## Server > Reverse Connect

Waits for a server to connect out to this client, and opens the session on the
connection the server offered.

This is the case a firewall in front of the server forces: the client can be reached
from the server but not the other way round, so the server dials the client and offers
the open socket with a `ReverseHello` message (OPC UA Part 6 §7.1.3). Nothing in this
path opens a socket to the server.

To try it:

1. Start the **Reference Server**. Its configuration dials
   `opc.tcp://localhost:65301` every 15 seconds.
2. Start the **Reference Client**. Leave the endpoint URL of the reference server in
   the connect control - a wait is always for one named server, and this is the URL
   the incoming `ReverseHello` is matched against.
3. Choose `Server > Reverse Connect`. The status line reports that the client is
   listening; the menu item becomes `Stop Reverse Connect`.
4. Within one connect interval the server calls, and the browse tree fills exactly as
   it would after an ordinary connect.

A secure reverse connection costs more than one `ReverseHello`: the first connection
is spent on `GetEndpoints`, which fetches the server certificate and closes the
channel, so the client waits for the server to call again before it opens the session.
That is why the connect can take two connect intervals, and why
`ReverseConnectListener.ConnectAsync` loops.

### Configuration

The client listens on the endpoints its `ClientConfiguration` names:

```xml
<ClientConfiguration>
  <ReverseConnect>
    <ClientEndpoints>
      <ClientEndpoint>
        <EndpointUrl>opc.tcp://localhost:65301</EndpointUrl>
      </ClientEndpoint>
    </ClientEndpoints>
    <HoldTime>15000</HoldTime>
    <WaitTimeout>20000</WaitTimeout>
  </ReverseConnect>
</ClientConfiguration>
```

and the server dials the clients its `ServerConfiguration` names:

```xml
<ServerConfiguration>
  <ReverseConnect>
    <Clients>
      <ReverseConnectClient>
        <EndpointUrl>opc.tcp://localhost:65301</EndpointUrl>
        <MaxSessionCount>0</MaxSessionCount>
        <Enabled>true</Enabled>
      </ReverseConnectClient>
    </Clients>
    <ConnectInterval>15000</ConnectInterval>
    <ConnectTimeout>30000</ConnectTimeout>
    <RejectTimeout>60000</RejectTimeout>
  </ReverseConnect>
</ServerConfiguration>
```

Port 65301 belongs to this pair. The [aggregation sample](../../Workshop/Aggregation)
uses 65300 for the connections *it* accepts from the servers it aggregates, so the two
can run side by side.

While no client listens, the server keeps offering a connection every
`ConnectInterval` - the specification asks a server to maintain an available socket to
each configured client. Remove the `ReverseConnectClient` entry, or set `Enabled` to
`false`, to stop a server nobody calls from retrying.

One listener serves several servers: register or wait once per server, keyed by its
endpoint URL and, preferably, its server URI. That is what the aggregation server does
in the other direction - it is a client to the servers it aggregates, and reaches them
over the same `ReverseConnectManager`.

The listener half is
[`ReverseConnectListener`](../Client.Common/ReverseConnectListener.cs), which also has
no user interface in it.

## See also

- `Docs/ReverseConnect.md` and `Docs/NodeSetExport.md` in the
  [UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard) repository.
- [OPC UA Part 6, 7.1.3](https://reference.opcfoundation.org/specs/OPC-10000-6/v1.05.07/7.1.3)
  for the reverse connect handshake.
