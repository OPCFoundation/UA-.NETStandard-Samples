# Task 05.02 — DataAccess Client & Server: Progress Details

## Summary
Retargeted Workshop DataAccess Client & Server from `net48` to `net10.0-windows`. No source changes required.

## Projects
| Project | Result |
|---------|--------|
| Workshop\DataAccess\Server\DataAccess Server.csproj | ✅ Built |
| Workshop\DataAccess\Client\DataAccess Client.csproj | ✅ Built |

## Changes (both csproj)
- TargetFramework `net48` → `net10.0-windows`; kept `OutputType=WinExe`, `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `ImportWindowsDesktopTargets`.
- Removed legacy framework `<Reference>`s (`System.ComponentModel.DataAnnotations`, `System.Core`, `System.IdentityModel`, `System.ServiceModel`).
- Removed all `<BootstrapperPackage>` items.
- Server: removed the `win7-x64` RID/`BaseNuGetRuntimeIdentifier` block.

## Notes
- No API breaking changes surfaced; no Windows Compatibility Pack needed.
