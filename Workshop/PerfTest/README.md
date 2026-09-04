# PerfTest

A server which exposes a large block of continuously changing registers, and a client
which subscribes to them and measures how fast the updates arrive.

Because the client is the one sample which creates monitored items in bulk, it is also
where the **unbounded monitored item mode** of the V2 subscription engine is on show.

## Running a test

1. Start the **PerfTest Server**.
2. Start the **PerfTest Client** and set the three fields above the log:

   | Field | Meaning |
   |---|---|
   | `Sampling Rate (ms)` | The publishing interval of the subscription. |
   | `Item Count` | How many registers to monitor. |
   | `Items / Partition` | The upper bound of monitored items per server-side subscription. `0` leaves the bound to the engine. |

3. Connect. The test starts with the session and the counters fill on a one second
   timer; `Stop` ends it without closing the session.

## Unbounded monitored items

OPC UA Part 4 §5.13.2 lets a server cap how many monitored items one subscription may
hold (`MaxMonitoredItemsPerSubscription`). The V2 subscription engine hides that cap:
one *logical* subscription spreads its items over as many server-side **partition**
subscriptions as it needs, and still presents a single monitored-item collection.
`TryAdd`, `TryRemove` and the lookups search every partition, so nothing above the
engine has to know which partition owns which item.

Two read-only fields report what happened:

| Field | Meaning |
|---|---|
| `Partitions` | How many server-side subscriptions carry the items. |
| (next to it) | The item updates each partition delivered, as `serverId:count`. |

A single partition is the common case and the fast path - the wrapper short-circuits
to the one subscription and takes no extra locks. Set `Items / Partition` to something
well below `Item Count` (25 against 100, say) to force the split on a server whose real
limit is far higher, and watch the partition count rise and the updates arrive from
each of them.

### The reactive fallback

Leaving `Items / Partition` at `0` is the setting a real client would use: the engine
watches every `CreateMonitoredItems` response and, on the first
`Bad_TooManyMonitoredItems`, marks that partition no-grow so later items fan out to a
new one. That handles a server whose actual limit is lower than the capability it
advertises, and one whose limit changes between sessions.

### What the sample sets

```csharp
new SubscriptionOptions {
    // ...
    MaxMonitoredItemsPerPartition = bound > 0 ? (uint)bound : null,
    DisableUnboundedItemMode = false,
}
```

`DisableUnboundedItemMode = true` binds the wrapper to one server-side subscription
again, so items beyond the server cap fail per item with
`Bad_TooManyMonitoredItems` the way they did before the V2 engine. The model exposes
it as `PerfTestClientModel.DisableUnboundedItemMode` for a client which wants the old
behaviour.

`SecondaryPartitionIdleTimeout` (30 s by default) decides how long an empty secondary
partition survives before it is deleted; the primary is never deleted while the
logical subscription lives, so its server-side identifier stays stable.

### Affinity

Items which have to end up in the *same* partition - because `SetTriggering` is scoped
to one server-side subscription (Part 4 §5.13.6) - are pinned with
`MonitoredItemOptions.Affinity`. Items sharing a non-null affinity tag are guaranteed
to share a partition; once that partition is full the next `TryAdd` with the tag
returns `false` rather than splitting the group. Triggering itself is shown in the
subscription dialogs of the [UA Sample Client](../../Samples/Client.Net4).

`PerfTestClientModel.AffinityGroupSize` puts every *n* consecutive items into one
group, which is enough to see the contract work - and to see the trap in it.

**A group is not placed as a unit.** The items arrive one at a time and the *first* item
of a group decides which partition the group is pinned to, so a group which starts near
the end of a partition loses its tail: the partition fills up, and the remaining items
of that group cannot go anywhere else. 100 items in groups of 10 into partitions of 25
places 85 and refuses 15; the same items into partitions of **20** place cleanly,
because two whole groups fill a partition exactly.

So `MaxMonitoredItemsPerPartition` should be a multiple of the largest affinity group,
and no group may be larger than a partition. The client logs how many items were
refused, which is what `TryAdd` returning `false` means here.

## Where the code is

The window only reads the fields and shows the counters. The measuring is
[`Tester`](Client/Model/Tester.cs), which is the notification handler of the
subscription it creates: it keeps its counters under a lock because the engine calls
it on a publish worker, and it tags each update with the `PartitionServerId` the
notification carries. [`PerfTestClientModel`](Client/Model/PerfTestClientModel.cs) owns
its lifetime and hands the counters out through `ReadStatistics` and `ReadPartitions`.

## See also

- `Docs/Subscriptions.md#unbounded-monitored-items` in the
  [UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard) repository.
