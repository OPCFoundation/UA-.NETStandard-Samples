# Task 05.05 — PerfTest Client & Server: Progress Details

## Summary
Retargeted Workshop PerfTest Client & Server from `net48` to `net10.0-windows`. No source changes required.

## Projects
| Project | Result |
|---------|--------|
| Workshop\PerfTest\Server\PerfTest Server.csproj | ✅ Built (0 warnings) |
| Workshop\PerfTest\Client\PerfTest Client.csproj | ✅ Built (0 project-local warnings) |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.Runtime.Serialization`, `System.ServiceModel`, `System.ServiceProcess`).
- Removed `<BootstrapperPackage>` items.
- Removed obsolete config-level `<UseVSHostingProcess>` and missing `AllRules.ruleset` `<CodeAnalysisRuleSet>`.
- Server: removed the trailing `win7-x64` RID PropertyGroup (invalid on .NET 10).

## Notes
- No API breaking changes surfaced; no Windows Compatibility Pack needed.
- Remaining Client-build warnings all originate from the already-migrated `UA Client Controls` dependency, not PerfTest.
- Package references already at net10-compatible versions from task 02.
