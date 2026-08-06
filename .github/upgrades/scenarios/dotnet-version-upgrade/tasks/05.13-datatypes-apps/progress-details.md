# 05.13-datatypes-apps — Progress Details

## Files Modified
- `Workshop\DataTypes\Client\DataTypes Client.csproj`
- `Workshop\DataTypes\Server\DataTypes Server.csproj`
- `Workshop\DataTypes\Server\DataTypesServerConfiguration.cs`

## Build Result
| Project | Target | Build |
|---|---|---|
| `Workshop\DataTypes\Server\DataTypes Server.csproj` | `net10.0-windows` | ✅ Succeeded — 0 errors, 0 project warnings |
| `Workshop\DataTypes\Client\DataTypes Client.csproj` | `net10.0-windows` | ✅ Succeeded — 0 errors, 0 project warnings |

> Remaining warnings are dependency-originated only: the Client's 94 warnings come from
> referenced libraries (UA Client Controls, DataTypes Library, OPC UA packages), and the
> single MSB3884 (`AllRules.ruleset` not found) originates from `DataTypes Library.csproj`.
> No warnings originate from the DataTypes Client/Server projects themselves.

## Changes Summary
Retargeted the Workshop DataTypes Client & Server executables from `net48` to
`net10.0-windows` (the DataTypes Library was already migrated in an earlier tier task).

### Both projects
- `TargetFramework` `net48` → `net10.0-windows`; kept `OutputType=WinExe` and `UseWindowsForms=true`.
- Removed ClickOnce/publish/bootstrapper properties and `<BootstrapperPackage>` items.
- Removed `ImportWindowsDesktopTargets`, `UseVSHostingProcess`, `CodeAnalysisRuleSet`, and
  per-config `OutputPath` blocks.
- Removed legacy framework `<Reference>` items (`System.ComponentModel.DataAnnotations`,
  `System.Core`, `System.ServiceProcess`, `System.IdentityModel`, `System.Runtime.Serialization`,
  `System.ServiceModel`).

### Server-specific
- Removed the `win7-x64` `RuntimeIdentifier`/`BaseNuGetRuntimeIdentifier` block.
- Removed the unused `using System.ServiceModel;` from `DataTypesServerConfiguration.cs`
  (the config only uses `System.Runtime.Serialization` DataContract attributes).

## Verification of "Done when"
- ✅ Both projects build on `net10.0-windows` with 0 errors and 0 project-originated warnings.

## Issues Encountered
- The server referenced `System.ServiceModel`/`System.IdentityModel` framework assemblies, but
  the only source usage was an unused `using System.ServiceModel;` — removed it; no WCF/WIF APIs
  were actually consumed, so no stubbing was needed.
