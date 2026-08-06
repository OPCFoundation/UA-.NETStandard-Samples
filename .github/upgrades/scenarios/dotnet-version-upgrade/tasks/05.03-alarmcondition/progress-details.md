# Task 05.03 — AlarmCondition Client & Server: Progress Details

## Summary
Retargeted Workshop AlarmCondition Client & Server from `net48` to `net10.0-windows`. No source changes required.

## Projects
| Project | Result |
|---------|--------|
| Workshop\AlarmCondition\Server\AlarmCondition Server.csproj | ✅ Built |
| Workshop\AlarmCondition\Client\AlarmCondition Client.csproj | ✅ Built |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s and all `<BootstrapperPackage>` items.
- Server: removed the `win7-x64` RID/`BaseNuGetRuntimeIdentifier` block.
- Client: preserved the `<Compile Remove="EventFieldDefinition.cs" />` glob-exclusion item.

## Notes
- No API breaking changes surfaced; no Windows Compatibility Pack needed.
