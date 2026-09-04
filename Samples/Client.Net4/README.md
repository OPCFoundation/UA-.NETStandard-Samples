# UA Sample Client

A Windows Forms client which drives the OPC UA service sets through the dialogs of the
[UA Sample Controls](../Controls.Net4) library: sessions, browsing, read and write,
history, and the subscription and monitored item services. It runs on a
`ManagedSession` and the V2 subscription engine.

This document covers the two parts a user tends to go looking for.

## Set Triggering

Triggering (OPC UA Part 4 §5.13.5) links a *triggering* monitored item to *triggered*
ones. When the triggering item fires, the queued notifications of the triggered items
are reported in the same publish even though their monitoring mode is `Sampling`,
which would otherwise suppress them - the "sample many, report on demand" pattern.

In the subscription window, select one item in the monitored item grid and choose
`Set Triggering...` from its context menu. The dialog lists every other item of the
subscription with a check box; checking one links it, unchecking one removes the link.
The `Triggered By` column of the grid shows, per item, which items make it report.

The relationship is **N:M**: a triggered item can be linked to several triggering
items, and the dialog only edits the links of the item it was opened for, so the links
another item holds are left alone.

### The two halves of the API

The menu item uses both, because a subscription dialog needs both:

- **Imperative** - `ISubscription.SetTriggeringAsync(triggeringItem, linksToAdd,
  linksToRemove, ct)` for items which already exist on the server. This is the only
  path that returns a status code per link, which is what lets the dialog report that
  the server refused one (`Bad_MonitoredItemIdInvalid`, per Part 4 §5.13.5.4).
- **Declarative** - `MonitoredItemOptions.TriggeredByNames` for items the wizard
  staged but the engine has not created yet. There is no server-side item to link, so
  the intent is written into the options the item will be created with; the engine
  issues the `SetTriggering` itself once both ends exist.

The engine keeps the desired state per item and replays it whenever it has to: after a
`TransferSubscriptions`, after a recreate, and after a reconnect. Nothing in the sample
re-issues `SetTriggering` by hand.

### Partitions

`SetTriggering` is scoped to one server-side subscription (Part 4 §5.13.6). A
subscription which grew past the per-subscription cap is spread over partitions by the
V2 engine, and items in different partitions cannot be linked - the call throws and the
sample reports that the items have to be created with the same
`MonitoredItemOptions.Affinity` to be kept together. The
[PerfTest client](../../Workshop/PerfTest) is where partitioning and affinity are shown
under load.

## Export NodeSet2

Right-click a connected session in the session tree and choose `Export NodeSet2...` to
write the address space of the server to a NodeSet2 XML file. The export walks the
hierarchy below the `Objects` folder and leaves the OPC UA namespace out, so the file
holds the model of the server; the same reader a server loads its model with reads it
back.

The [Reference Client](../ReferenceClient) offers the same export under
`Server > Export NodeSet2...`, and there it starts at the node selected in the browse
tree, so a single machine can be taken out of a large address space. Its
[README](../ReferenceClient/README.md) describes the options.

## See also

- `Docs/Subscriptions.md` and `Docs/NodeSetExport.md` in the
  [UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard) repository.
