# Opc.Ua.Samples.Client

The half of a sample client which talks OPC UA and knows nothing about the window. Every
Workshop client is split in two:

| Part | Where | Knows about |
|---|---|---|
| **Model** `Model/<Sample>ClientModel.cs` | the client project, namespace `<Root>.Model` | the OPC UA stack, this library, the generated client stubs of the sample |
| **Window** `MainForm.cs` and its dialogs | the client project | the model, the shared controls of `ClientControls.Net4` |

A model never references Windows Forms. That is what lets
[`Tests/SampleClientModels.Tests`](../../Tests/SampleClientModels.Tests) drive it against the
sample server without a window, a message loop or an STA thread - and what keeps the OPC UA
logic of a sample readable on its own. `ModelContractTests` enforces the rule by reflection
over every `Model` namespace.

## What a sample looks like

The window owns the shared connect control and hands the session it opens to the model:

```csharp
public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
{
    InitializeComponent();
    ConnectServerCTRL.Configuration = configuration;
    ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62567/Quickstarts/BoilerServer";

    // created on the thread of the window, so the model raises its events on this thread
    m_model = new BoilerClientModel(telemetry);
    m_model.ValueChanged += Model_ValueChanged;
    m_model.Error += Model_Error;
}

private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
{
    ISession session = ConnectServerCTRL.Session;

    if (session == null)
    {
        await m_model.DetachAsync();
        BoilerCB.Enabled = false;
        return;
    }

    await m_model.AttachAsync(session);          // the model finds the boilers here
    BoilerCB.Items.AddRange(m_model.Boilers);
    BoilerCB.Enabled = true;
}

private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
{
    ClientUtils.WaitForTeardown(m_model.DetachAsync);   // subscriptions first
    ConnectServerCTRL.Disconnect();                      // then the session
}

private void Model_ValueChanged(object sender, BoilerValueChangedEventArgs e)
{
    if (IsDisposed) return;                              // already on the UI thread
    m_displays[e.Variable].Text = e.Value.WrappedValue.ToString();
}
```

The model does the OPC UA work and reports through properties, methods and events:

```csharp
public sealed class BoilerClientModel : SampleClientModel
{
    public IReadOnlyList<BoilerInfo> Boilers { get; }
    public event EventHandler<BoilerValueChangedEventArgs> ValueChanged;

    public async Task SelectBoilerAsync(BoilerInfo boiler, CancellationToken ct = default) { ... }

    protected override async Task OnAttachedAsync(CancellationToken ct)   // browse the boilers
    protected override async Task OnDetachingAsync()                       // delete the subscription
}
```

The Boiler client is the reference implementation; the Empty client is the template.

## The contract of `SampleClientModel`

- **Lifecycle.** `AttachAsync(session)` sets the session, runs `OnAttachedAsync` and raises
  `ConnectionChanged(Attached)`. If the setup throws, the session is released again and the
  exception reaches the caller. `DetachAsync()` runs `OnDetachingAsync`, clears the session
  and raises `Detached`; it is idempotent and **never throws** - the connect control disposes
  its session before it reports the disconnect, so a detach regularly runs against a dead
  session, and a subscription which cannot be deleted on a server that is gone is logged, not
  shown. Detaching runs **before** the window closes the session: closing a session which
  still carries a subscription waits for the publish pipeline to drain.
- **Reconnect.** `NotifyReconnectStarting()` and `NotifyReconnectCompletedAsync()` are what the
  window forwards the events of the connect control to. A managed session keeps its identity
  across a reconnect, and so do the subscriptions of the V2 engine, so most models override
  neither hook; one which caches something the server may have changed re-reads it in
  `OnReconnectCompletedAsync`.
- **Threading.** Methods may be called from any thread and complete on any thread; the model
  awaits with `ConfigureAwait(false)` throughout. Events are raised on the
  `SynchronizationContext` captured in the constructor, the way `Progress<T>` does it - a
  window creates its model on the UI thread and receives every event there; a headless test
  creates it without a context and receives events inline on the publish worker. Events are
  posted, never sent: a window may be blocking its thread in `WaitForTeardown` while it
  closes, and a synchronous send from the teardown would deadlock against that. A posted
  event can land after the window closed, so every handler starts with `if (IsDisposed)`.
- **Errors.** A method throws and the window reports the exception as it always has. A
  failure on a background path (a pump streaming notifications) has no caller to throw to and
  is logged and raised through `Error` instead.

## What else is here

- `SampleSession`: the UI-free helpers which used to live in `ClientUtils` (browse, browse
  path translation, event decoding, bounded session close, V2 subscription creation).
  `ClientUtils` keeps forwarders under the old names.
- `SubscriptionPump`: owns the token and the tasks of `await foreach` enumerations over a
  streaming subscription, so `StopAsync` can wait for them and nothing is raised after a
  detach.
- `SerialNotificationPump<T>`: a channel with one consumer, for a callback subscription whose
  per-notification processing has to await. The engine's callback only posts; the consumer
  does the awaits in arrival order and is never entered twice. This is the fix for the
  reentrancy the AlarmCondition client used to have.
- `SubscriptionCallbacks`, `MonitoredItemEntry`: the V2 engine adapter and the per-item
  handle the models keep.
- `OperationResult`: what a status bar shows - what was attempted and what the server
  answered.
- `SampleSessionFactory`: opens the managed session the connect control opens, for callers
  without a window (the model tests).
- `NodeSetExport`: browses a connected server and writes its address space to a NodeSet2 XML
  file, which is what the `Export NodeSet2...` commands of the Reference Client and the UA
  Sample Client run. `NodeSetExportSettings` decides how far the browse reaches and whether
  the OPC UA namespace comes along; the stack does the writing.
- `ReverseConnectListener`: the client half of a reverse connection - listens on the client
  endpoints of the configuration and hands out the connections servers open to it, so a
  session can be created on a socket the server opened. Used by
  `Server > Reverse Connect` of the [Reference Client](../ReferenceClient/README.md).

## Testing a model

A fixture derives from `ClientModelFixtureBase<TModel>`, names its sample and creates its
model; the base starts the sample server once, opens a managed session per test and disposes
the model before the session. Tests attach, call the model, and wait on an `EventSink<T>`:

```csharp
[Test]
public async Task SelectingABoilerDeliversAllFourVariables(CancellationToken ct)
{
    var values = new EventSink<BoilerValueChangedEventArgs>();
    Model.ValueChanged += values.Handle;

    await AttachAsync(ct);
    await Model.SelectBoilerAsync(Model.Boilers[0], ct);

    await values.WaitForAsync(v => v.Variable == BoilerVariable.DrumLevel, "no drum level arrived", timeout, ct);
}
```

One trap: the generated model types of a sample (`Namespaces`, `ObjectIds`, ...) are compiled
into both its client and its server assembly, and the test project references both. Naming
one of them in a test is a `CS0433`; use browse paths or the constants a model exposes.
