# Task 05.08 — HistoricalEvents Client & Server: Progress Details

## Summary
Retargeted Workshop HistoricalEvents Client & Server from `net48` to `net10.0-windows`. No source changes required.

## Projects
| Project | Result |
|---------|--------|
| Workshop\HistoricalEvents\Server\HistoricalEvents Server.csproj | ✅ Built |
| Workshop\HistoricalEvents\Client\HistoricalEvents Client.csproj | ✅ Built |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.Runtime.Serialization`, `System.ServiceModel`, `System.ServiceProcess`).
- Removed obsolete `<UseVSHostingProcess>` and missing `AllRules.ruleset` `<CodeAnalysisRuleSet>`.
- Server: removed the trailing `win7-x64` RID/`BaseNuGetRuntimeIdentifier` block.

## Notes
- No API breaking changes surfaced; no Windows Compatibility Pack needed.
- All remaining build warnings originate from the already-migrated `UA Client/Server Controls` dependency, not from HistoricalEvents.
- Package references already at net10-compatible versions from task 02.
