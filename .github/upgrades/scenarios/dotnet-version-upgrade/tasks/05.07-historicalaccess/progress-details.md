# Task 05.07 — HistoricalAccess Client/Server/Tester: Progress Details

## Summary
Retargeted Workshop HistoricalAccess Server, Client, and Aggregate Tester from `net48` to `net10.0-windows`.

## Projects
| Project | Result |
|---------|--------|
| Workshop\HistoricalAccess\Server\HistoricalAccess Server.csproj | ✅ Built (0 warnings) |
| Workshop\HistoricalAccess\Client\HistoricalAccess Client.csproj | ✅ Built (0 project-local warnings) |
| Workshop\HistoricalAccess\Tester\Aggregate Tester.csproj | ✅ Built (0 project-local warnings) |

## Changes (all csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.Runtime.Serialization`, `System.ServiceModel`, `System.Xml.Linq`, `System.Data.DataSetExtensions`).
- Removed obsolete `<UseVSHostingProcess>` and missing `AllRules.ruleset` `<CodeAnalysisRuleSet>`.
- Server: removed the trailing `win7-x64` RID/`BaseNuGetRuntimeIdentifier` block.

## Source changes
- `Client\ReadHistoryDlg.cs`: removed unused `System.ServiceModel`, `System.ServiceModel.Security`, and `System.ServiceModel.Channels` using directives (WCF unavailable on .NET 10; namespaces were not actually used → fixed CS0234).
- `Tester\MainForm.cs`: `buffer2.Append(buffer.ToString())` → `buffer2.Append(buffer)` (fixed CA1830, strongly-typed StringBuilder overload).

## Notes
- Remaining build warnings all originate from the already-migrated `UA Client Controls` dependency, not HistoricalAccess.
- Package references already at net10-compatible versions from task 02.
