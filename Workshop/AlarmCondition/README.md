# Alarms and Conditions Quickstart (OPC UA Part 9)

A server/client pair which demonstrates **OPC 10000-9, Alarms & Conditions**: how a server
turns the alarms of an underlying system into UA Conditions, how those events travel from a
source up to the areas which declare it, and what an operator can do with an alarm once it
arrives.

| Project | What it is |
|---------|------------|
| [Server](Server) | A hand-written `FluentNodeManagerBase` which maps a simulated plant onto Areas, Sources and Conditions |
| [Client](Client) | A Windows Forms alarm viewer which subscribes to conditions and calls the Part 9 Methods on them |

Endpoint: `opc.tcp://localhost:62544/Quickstarts/AlarmConditionServer`.

The stack side of everything below is documented in
[AlarmsAndConditions.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AlarmsAndConditions.md).

## The address space

The server reads an area tree out of its configuration and hangs sources under it. Areas are
**notifiers**, wired to the Server object and to each other with `HasNotifier`; sources are
attached to their areas with `HasEventSource`. That wiring is what makes an event raised by a
tank arrive at a client which subscribed to an area three levels above it, and a source listed
under two areas is one node reachable from both.

Each source carries:

| Node | Type | What it is |
|------|------|------------|
| `Red` / `Yellow` / `Green` (Colours), `Gold` / `Silver` / `Bronze` (Metals) | alarm | the conditions of the source |
| `OnlineState` | `DialogConditionType` | asks an operator to put the source online or offline |
| `Alarms` | `AlarmGroupType` | every alarm of the source, as `AlarmGroupMember` references |
| `AlarmMetrics` | `AlarmMetricsType` | how many alarms the source has and how often they activate |
| `MaintenanceMode` | writable `Boolean` | suppresses every alarm of the source while it is set |

The source type decides which alarms exist and how they behave: a **Colours** source has
`Red` (a deviation alarm), `Yellow` (a non exclusive level alarm) and `Green` (a trip alarm);
a **Metals** source has `Gold`, `Silver` and `Bronze` in the same roles.

## What the sample shows

### 1. The states an alarm can be in

Every alarm carries the optional Part 9 states and the Methods which drive them:
`SuppressedState`, `SilenceState`, `OutOfServiceState`, `ShelvingState`, plus `Silence`,
`Suppress`/`Unsuppress`, `RemoveFromService`/`PlaceInService` and `GetGroupMemberships`.
Creating the optional nodes is all the sample does — `AlarmConditionState` wires a handler
onto each of those Methods in `OnAfterCreate`, and each handler validates the transition,
applies it and raises the audit event the specification asks for.

The sample routes every one of them into its `UnderlyingSystem` through the **veto delegates**
(`OnSilenceRequested`, `OnSuppressRequested`, `OnOutOfServiceRequested`, `OnResetRequested`),
so that the simulated plant stays the single owner of the alarm state and the stack's own
transition merely agrees with it.

### 2. Latching, and the veto which guards it

Only the **trip alarm** of a source latches: it gets a `LatchedState` and a `Reset` Method.
Activating it sets the latch; deactivating it does not clear it, and a latched alarm stays
`Retain = true`, so a client which connects later still sees it in a condition refresh
(Part 9 §4.8).

`Reset` is refused by the stack until the alarm is inactive, acknowledged and confirmed, and
refused by the sample's `OnResetRequested` delegate while the source is offline — an operator
cannot clear a trip which the plant cannot confirm is gone. Both refusals arrive at the client
as a status code rather than as a silently ignored call.

### 3. Re-alarming

Every alarm has a `ReAlarmTime` of fifteen seconds. Part 9 leaves the schedule to the
application — the stack only offers `ProcessReAlarm` — so the sample keeps a deadline per
alarm and drives the reminders from the same one second cycle which runs its simulation. A
reminder withdraws the acknowledgement, lifts the silence and counts up `ReAlarmRepeatCount`,
which is the number a client watches to see that an alarm has been asking for a while.

### 4. Audible alarms and silencing

`AudibleEnabled` is set and `AudibleSound` carries a short wave file the server builds itself,
so a client has something to play. Silencing stops the annunciation; the next activation and
the next re-alarm lift the silence again, which is what `UpdateAudibleState` and
`SetActiveState` do on the server.

### 5. Two suppression patterns

`AlarmSuppressionEngine` owns both, and the node manager owns one engine for the whole server.

* **AlarmSuppressionGroup** — every source registers its `Alarms` group with
  `MaintenanceMode` as the suppression source. Writing `true` suppresses every alarm of that
  source; writing `false` releases them.
* **FirstInGroup** — the **Metals** sources register their trip alarm as the leader of their
  group. While `Bronze` is active, `Gold` and `Silver` are suppressed, so an operator sees the
  trip rather than the consequences it drags along.

Only the metal sources run the second pattern on purpose: the trip alarm of this simulation is
active most of the time, and a server which ran the pattern everywhere would leave a client
with the default *hide suppressed conditions* filter looking at an almost empty list. Half of
the sources showing every alarm and half showing only the leader is what makes the difference
visible. `FirstInGroupFlag`, which says nothing about suppression, is set on the trip alarm of
every source.

A single `SuppressedState` carries every cause an alarm can have — the plant, an operator's
`Suppress` call, the maintenance flag and the group leader — so `SourceState` records which
cause the engine just decided on and re-applies the union to the members. Clearing one cause
therefore does not clear a suppression another cause still asks for.

### 6. Alarm metrics

`AlarmRateTracker` counts activations into a sliding one minute window, and the simulation
cycle copies `CurrentAlarmRate`, `MaximumAlarmRate`, `AlarmCount` and `MaximumReAlarmCount`
into the `AlarmMetrics` object of the source.

## Using the client

Connect and the list fills with the conditions of the whole server. The **Flags** column shows
the Part 9 states which are set — `Active, Latched, Silenced, Suppressed, OutOfService,
Unacked, Unconfirmed` — so the effect of every Method below is visible in the row it acted on.

Everything under **Conditions** goes through one `AlarmClient`, which is obtained from the
session and delegates each call to the source generated proxy of the type that declares the
Method. The form never names a Method NodeId:

| Menu item | What it calls |
|-----------|---------------|
| Enable / Disable / Add Comment... | `ConditionType` |
| Acknowledge... / Confirm... | `AcknowledgeableConditionType` |
| Respond... | `DialogConditionType`, on the `OnlineState` dialog of a source |
| Shelving | `ShelvedStateMachineType`, called with the ConditionId (Part 9 §5.8.10.4) |
| Silence | `AlarmConditionType.Silence` |
| Suppression → Suppress... / Unsuppress... | `Suppress2` / `Unsuppress2` when a comment is given, `Suppress` / `Unsuppress` otherwise |
| Suppression → Remove From Service... / Place In Service... | `RemoveFromService` / `PlaceInService` |
| Reset... | `Reset`, on a latched trip alarm |
| Group Memberships... | `GetGroupMemberships`, and shows the groups it names |
| Toggle Maintenance Mode | writes the `MaintenanceMode` flag of the source the condition came from |

A call the server refuses shows its status code in the **Comment** column of the row, which is
where `BadInvalidState` from a `Reset` on an alarm that is still active turns up.

### Things worth trying

1. **Latching.** Select the `Green` alarm of a Colours source, *Reset...* it while it is
   active — refused with `BadInvalidState`. *Acknowledge...*, then *Confirm...*: the alarm
   goes inactive but stays in the list, still flagged `Latched`. Now *Reset...* it.
2. **The veto.** Answer the `OnlineState` dialog of that source with *Offline*, then try
   *Reset...* again — refused with `BadUserAccessDenied` and the reason why. The dialog arms
   itself again after every answer, so *Online* puts the source back.
3. **Group suppression.** Select any condition of a source and *Toggle Maintenance Mode*, then
   press *Refresh*. The alarms of that source are gone from the list, because the form filters
   suppressed conditions out. Toggle it back and refresh again.
4. **First in group.** Watch a Metals source and press *Refresh* while `Bronze` is active:
   `Gold` and `Silver` are missing from the list for the same reason.
5. **Re-alarming.** Leave an alarm unacknowledged and watch its message; every fifteen seconds
   it says it was re-alarmed and its acknowledgement is withdrawn again.

## Running it

```bash
dotnet run --project "Workshop/AlarmCondition/Server/AlarmCondition Server.csproj"
```

```bash
dotnet run --project "Workshop/AlarmCondition/Client/AlarmCondition Client.csproj"
```

## Known gaps

* **The audit window stays empty.** Every Part 9 Method raises the audit event the
  specification asks for, and the server configuration now turns auditing on
  (`AuditingEnabled`), which a server needs before it forwards an audit event at all. Even
  so, no `AuditUpdateMethodEventType` reaches a subscriber, so *View → Audit Events...*
  opens a window which never fills. The window itself works — it starts its streaming
  subscription and tears it down again with the window.

## Notes for implementers

* **Set the reference type after `Create`, not before.** Creating a node from its type model
  copies the reference type of the template over whatever the caller set, and a child whose
  reference type ends up null is not a hierarchical child of anything — a browse of the parent
  simply does not return it. `SourceState.AttachToSource` sets it afterwards, which is what
  makes the conditions, the group and the metrics of a source browsable at all.
* **`SetLimitState` re-applies the active state.** `ExclusiveLimitAlarmState.SetLimitState`
  ends in `SetActiveState`, and activating an alarm lifts its silence and sets its latch. The
  states which the underlying system owns therefore have to be pushed *after* the limit
  handling, not before it.
* **`ReportStateChange` does nothing unless `AutoReportStateChanges` is set.** The Part 9
  Method handlers of the stack call it, so a server which does not set the flag — like this
  one, whose events come from its underlying system — has to report the resulting event
  itself. This sample does that by routing each Method into the underlying system, which
  reports the change back through the path the simulation already uses.
* **Only push a state which actually changed.** `SetActiveState` is not idempotent: calling it
  with `true` again lifts the silence and re-stamps the transition time, so an alarm which
  merely got a comment would lose an operator's silence.
* **`MaximumReAlarmCount` is optional** in `AlarmMetricsType`, so the type model does not
  materialize it on every server.
