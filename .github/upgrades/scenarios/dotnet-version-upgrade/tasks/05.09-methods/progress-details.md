# Task 05.09 — Methods Client & Server: Progress Details

## Summary
Retargeted Workshop Methods Client & Server from `net48` to `net10.0-windows`. No source changes required.

## Projects
| Project | Result |
|---------|--------|
| Workshop\Methods\Server\Methods Server.csproj | ✅ Built (0 warnings) |
| Workshop\Methods\Client\Methods Client.csproj | ✅ Built (0 project-local warnings) |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.Runtime.Serialization`, `System.ServiceModel`, `System.ServiceProcess`).
- Removed obsolete `<UseVSHostingProcess>` and missing `AllRules.ruleset` `<CodeAnalysisRuleSet>`.
- Server: removed the trailing `win7-x64` RID/`BaseNuGetRuntimeIdentifier` block.

## Notes
- No API breaking changes surfaced; no Windows Compatibility Pack needed.
- Client's remaining warnings all originate from the already-migrated `UA Client Controls` dependency.
- Package references already at net10-compatible versions from task 02.
