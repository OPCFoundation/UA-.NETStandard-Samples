# Opc.Ua.Samples.WinForms

The dependency injection plumbing of the Windows Forms samples: how a form or a control
gets the window it opens.

Before this library, a sample created its dialogs itself and handed them what they needed:

```csharp
string endpointUrl = new DiscoverServerDlg().ShowDialog(Configuration, hostName, m_telemetry);
```

The dependencies of a dialog were therefore the business of whoever opened it, which is why
`ITelemetryContext` and `ApplicationConfiguration` were threaded through the arguments of
almost every `ShowDialog` in the repository. Now the dialog says what it needs and the
container supplies it:

```csharp
// DiscoverServerDlg(ITelemetryContext telemetry)
string endpointUrl = Windows.Create<DiscoverServerDlg>().ShowDialog(Configuration, hostName);
```

## The pieces

| Type | What it is |
|---|---|
| `IWindowFactory` | `Create<TWindow>(params object[] arguments)` - creates a window from the container. What the caller passes is matched to the constructor by type; the rest is resolved as services. |
| `IWindowFactoryConsumer` | A form or control which is handed the factory. |
| `SampleForm`, `SampleUserControl` | The base classes of the sample windows and controls. They expose the factory as `Windows`. |
| `WindowServices` | Hands the factory down a control tree (`AttachWindows`) and finds it again from anywhere in it (`FindWindows`, `RequireWindows`). |
| `SampleApplicationMessageDlg` | The message box the stack asks its questions through, registered rather than installed by hand in every `Program.cs`. |
| `AddSampleWindows()` | The registration. `SampleWinFormsHost` calls it, so a sample does not. |

## Why controls are handed the factory instead of taking it

A `Form` is created by the container, so it takes what it needs through its constructor. A
`UserControl` on that form is not: the Windows Forms designer writes `new SomeCtrl()` into
the generated code, and there is no hook to change that without giving up the designer. So
the factory is pushed down the control tree once, when the container creates the window:

```
IWindowFactory.Create<MainForm>()
  -> MainForm ctor runs InitializeComponent(), which creates the designer's controls
  -> WindowServices.AttachWindows(mainForm, factory) walks the tree and sets Windows
```

A control which is created and parented later - a page added at run time - finds the factory
by walking up to its form instead, which is what the `Windows` property falls back to. A
control which is on neither says so:

> 'ConnectServerCtrl' has no window factory: it was not created by the container and is not
> on a window which was.

That is the one new failure mode of this design, and it is the reason `Windows` throws
rather than returning null: a missing factory is a wiring mistake, and it should read like
one. Tests which assemble a form out of single controls hand them a factory of their own -
see `TestWindowFactory` in [`Tests/Samples.Tests.WinForms`](../../Tests/Samples.Tests.WinForms).

## What `Create` does

`ActivatorUtilities.CreateInstance`, and then `AttachWindows` on the result. A window is a
transient with a lifetime of its own, so it is *created* rather than resolved; nothing has to
be registered per dialog. Two consequences worth knowing:

- **Ambiguous constructors.** `ActivatorUtilities` picks the constructor with the most
  parameters it can satisfy. A window with two constructors the container could both satisfy
  has to say which one is meant with `[ActivatorUtilitiesConstructor]` - `ServerForm` does.
- **Per-instance arguments are positional.** `Windows.Create<ViewApplicationRecordsDialog>(m_gds)`
  passes the client the dialog is about; the telemetry comes from the container.

## Which assemblies use it

`Opc.Ua.ClientControls`, `Opc.Ua.SampleControls`, `Opc.Ua.ServerControls`, the global
discovery client controls, and every sample and Workshop application above them.
