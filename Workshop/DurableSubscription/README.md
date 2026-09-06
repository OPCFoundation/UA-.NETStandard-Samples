# Durable Subscriptions and Transfer Quickstart (OPC UA Part 4)

A server/client pair which demonstrates **durable subscriptions** and
**`TransferSubscriptions`**: how a server keeps a subscription and its queued
notifications alive while its client is gone, and how a client takes that subscription
back into a new session and recovers the values it missed.

| Project | What it is |
|---------|------------|
| [Server](Server) | A `ReferenceServer` with durable subscriptions turned on in its configuration |
| [Client](Client) | A Windows Forms client which creates a durable subscription, persists it, restarts itself, and transfers the subscription back |

Endpoints: `opc.tcp://localhost:62581/Quickstarts/DurableSubscriptionServer` and
`https://localhost:62580/Quickstarts/DurableSubscriptionServer`.

## The problem durable subscriptions solve

An ordinary subscription lives and dies with its session. When the session goes away —
the client crashes, the network drops long enough for the session to time out, or the
client is simply stopped and restarted — the server tears the subscription down and
throws away everything it had queued. A client that comes back has to create the
subscription again from scratch, and **the values produced while it was gone are lost**.

For a client that has to account for every value — a historian, a recorder, a gateway
that forwards data on — that gap is unacceptable. OPC UA Part 4 answers it with two
features that work together:

* a **durable subscription** (`SetSubscriptionDurable`, Part 4 §5.13.9) asks the server
  to keep the subscription, and its per-item queues, alive without the client for hours
  rather than the seconds an ordinary lifetime allows; and
* **`TransferSubscriptions`** (Part 4 §5.13.7) moves an existing subscription into a new
  session, so a client that reconnects — or a *different* client — can take it over and
  drain the values the server queued in the meantime.

## What the sample shows

The four client use cases below are the ones the OPC Foundation `DurableSubscription`
and `TransferSubscription` documents describe. All four reduce to the same two calls of
the V2 subscription engine (`Opc.Ua.Client.Subscriptions`) — `ISubscription.SetAsDurableAsync`
to make a subscription durable and `ISubscriptionManager.LoadAsync` with
`transferSubscriptions: true` (which drives `GetMonitoredItems` and `TransferSubscriptions`
for you) to take it over.

### 1. Transfer from an active session

A subscription owned by one live session is transferred into another. The server never
lost it; the transfer just re-homes it. This is the plain `TransferSubscriptions` call,
the building block the other three cases rest on.

### 2. Transfer from a closed session with `DeleteSubscriptionsOnClose = false`

By default a session deletes its subscriptions when it closes. Set
`Session.DeleteSubscriptionsOnClose = false` first and the close leaves the subscription
behind on the server, still queuing notifications, for the next session to transfer
back. The client model sets this flag on every session it opens.

### 3. Transfer from persisted storage across a client restart

This is the case the sample drives end to end. The client:

1. creates a durable subscription and calls `ISubscription.SetAsDurableAsync`;
2. on **Save & Restart**, snapshots the subscription to disk with
   `ISubscriptionManager.SaveAsync`, closes the session without deleting the subscription,
   and calls `Application.Restart()`;
3. on the next start, opens a fresh session, calls `ISubscriptionManager.LoadAsync` with
   `transferSubscriptions: true` — which rebuilds the subscription on the session from the
   file *and* transfers it back from the server — and the values the server queued while the
   process was down arrive marked **recovered**, before the live values continue.

The persisted subscription outlives the process, which is what separates this from
case 2.

### 4. Transfer after a failed reconnect

When a session drops, the SDK first tries to reactivate it. If that fails, the
reconnect handler falls back to creating a new session and transferring the durable
subscription into it — the same `TransferSubscriptions` call, reached automatically
instead of by hand. A durable subscription is what makes this fallback able to recover
the gap rather than start clean.

## The server side

Turning durable subscriptions on is a server-side decision, and the server a client
transfers from has to provide two things Part 4 leaves to the implementation:

* an **`IMonitoredItemQueueFactory`** whose queues can hold a durable subscription's
  backlog — far larger than an ordinary in-memory queue and, in a production server,
  backed by storage that survives a server restart; and
* an **`ISubscriptionStore`** which persists the subscriptions across a restart of the
  *server*.

The [`ReferenceServer`](Server) this sample reuses already wires up the
`DurableMonitoredItemQueueFactory` and `SubscriptionStore` that the
`Quickstarts.Servers` package ships (through its `CreateMonitoredItemQueueFactory` and
`CreateSubscriptionStore` overrides), so the only thing the sample server adds is the
configuration that turns the feature on:

```xml
<ServerConfiguration>
  <DurableSubscriptionsEnabled>true</DurableSubscriptionsEnabled>
  <MaxDurableNotificationQueueSize>100000</MaxDurableNotificationQueueSize>
  <MaxDurableEventQueueSize>100000</MaxDurableEventQueueSize>
  <MaxDurableSubscriptionLifetimeInHours>24</MaxDurableSubscriptionLifetimeInHours>
</ServerConfiguration>
```

Without `DurableSubscriptionsEnabled`, `SetSubscriptionDurable` returns `false` and the
subscription stays ordinary — the client reports as much in its status bar.

## Running it

Start the server:

```bash
dotnet run --project "Workshop/DurableSubscription/Server/DurableSubscription Server.csproj"
```

Start the client:

```bash
dotnet run --project "Workshop/DurableSubscription/Client/DurableSubscription Client.csproj"
```

In the client:

1. **Connect** to the server.
2. **Create Durable Subscription** — the list starts filling with the server's current
   time, one value a second, each marked *live*.
3. **Save & Restart** — the client persists the subscription and restarts itself. When
   it comes back it reconnects on its own, transfers the subscription back, and the
   values queued while it was gone appear at the top marked *recovered* (highlighted),
   followed by the live values again.
4. **Disconnect + Delete** ends the durable subscription on the server for good.

The subscription watches `Server/ServerStatus/CurrentTime`, which changes on its own
without any configuration, so the gap the recovery fills is simply the wall-clock time
the client was down.

## Notes for implementers

* **The subscription is made durable *after* it is created.** `SetAsDurableAsync`
  is a call on an existing subscription; the server may revise the requested lifetime
  down to `MaxDurableSubscriptionLifetimeInHours`, and the client uses the revised value.
* **`DeleteSubscriptionsOnClose` has to be cleared on the session, not the
  subscription**, and before the session closes. The client model sets it on every
  session it opens so that a close, expected or not, always leaves the subscription
  behind.
* **`SaveAsync`/`LoadAsync` persist the client's view of the subscription, not the
  server's data.** The queued notifications live on the server; the file only carries what
  the new session needs to re-create the subscription and ask for the transfer. The
  `transferSubscriptions: true` argument of `LoadAsync` is what turns the loaded
  subscription into a real `TransferSubscriptions` call.
* **Recovered values are ordinary notifications.** They arrive through the same
  subscription `DataChangeCallback` as live ones; the sample tells them apart by their
  `SourceTimestamp` — a value sampled before the transfer was queued while the client
  was gone, a later one is live. Those timestamps span the downtime, which is how you
  can see that nothing was dropped.
* **The sample connects without security** (`SecurityPolicy` `None`) to stay
  self-contained, so the client and server do not have to trust each other's certificate.
  A real durable-subscription client would use a secure endpoint; the transfer logic is
  identical.
* **The client uses the V2 subscription engine** (`Opc.Ua.Client.Subscriptions`:
  `ISubscription`, `ISubscriptionManager`, `MonitoredItemOptions`) on a managed session,
  the same engine the other client samples in this repository use. `SetAsDurableAsync`,
  the save/load helpers and transfer-on-load all live there.

## What this sample does not cover

Event queues (the sample monitors a data value, not events), a client that transfers a
subscription created by a *different* client, securing the endpoint, and a server with a
custom `IMonitoredItemQueueFactory`/`ISubscriptionStore` over a database rather than the
`Quickstarts.Servers` defaults are all outside it.
