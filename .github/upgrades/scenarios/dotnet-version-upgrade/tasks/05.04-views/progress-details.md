# Task 05.04 — Views Client & Server: Progress Details

## Summary
Retargeted Workshop Views Client & Server from `net48` to `net10.0-windows`. Removed a stray `win7-x64` RID block in the server and fixed two obsolete-API warnings.

## Projects
| Project | Result |
|---------|--------|
| Workshop\Views\Server\Views Server.csproj | ✅ Built (0 warnings) |
| Workshop\Views\Client\Views Client.csproj | ✅ Built (0 project-local warnings) |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.Runtime.Serialization`, `System.ServiceModel`, `System.ServiceProcess`).
- Removed `<BootstrapperPackage>` items.
- Removed obsolete config-level `<UseVSHostingProcess>` and missing `AllRules.ruleset` `<CodeAnalysisRuleSet>` (eliminated MSB3884).
- Server: removed the trailing `win7-x64` `RuntimeIdentifier`/`BaseNuGetRuntimeIdentifier` PropertyGroup (invalid on .NET 10, caused NETSDK1083).

## Source changes
- `ViewsNodeManager.cs`: replaced two `FindPredefinedNode(NodeId, typeof(NodeState))` calls with the generic `FindPredefinedNode<NodeState>(NodeId)` overload (fixed CS0618).

## Notes
- Remaining warnings during the Client build all originate from the already-migrated `UA Client Controls` dependency (WFO1000/CAxxxx analyzer warnings), not from Views Client. WFO1000 is intentionally warning-level per the Key Decisions Log.
- Package references already at net10-compatible versions from task 02.
