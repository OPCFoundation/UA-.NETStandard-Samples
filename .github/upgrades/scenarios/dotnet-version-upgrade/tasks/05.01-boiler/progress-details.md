# Task 05.01 — Boiler Client & Server: Progress Details

## Summary
Retargeted Workshop Boiler Client & Server from `net48` to `net10.0-windows`. No source changes required.

## Projects
| Project | Result |
|---------|--------|
| Workshop\Boiler\Server\Boiler Server.csproj | ✅ Built |
| Workshop\Boiler\Client\Boiler Client.csproj | ✅ Built |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.IdentityModel`, `System.Runtime.Serialization`, `System.Security`, `System.ServiceModel`, `System.ServiceProcess`).
- Removed `<BootstrapperPackage>` items.
- Server: removed the `win7-x64` RID/`BaseNuGetRuntimeIdentifier` block (invalid on .NET 10).

## Notes
- No API breaking changes surfaced; no Windows Compatibility Pack needed.
- Package references already at net10-compatible versions from task 02.
