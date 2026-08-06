# Task 04.01 — Reference Apps: Progress Details

## Summary
Retargeted `Reference Client` and `Reference Server` executables from `net48` to `net10.0-windows`. Both build successfully.

## Projects
| Project | Result |
|---------|--------|
| Samples\ReferenceClient\Reference Client.csproj | ✅ Built |
| Samples\ReferenceServer\Reference Server.csproj | ✅ Built |

## Key Changes
- Set `<TargetFramework>net10.0-windows</TargetFramework>`, kept `<OutputType>WinExe</OutputType>` and `<UseWindowsForms>true</UseWindowsForms>`.
- Removed ClickOnce/publish/bootstrapper properties and `<BootstrapperPackage>` items (.NET Framework 3.5 SP1).
- **Reference Server**: removed legacy `<Reference Include="System.IdentityModel" />` and `System.ServiceModel` framework references.
- **Both**: removed obsolete `<RuntimeIdentifier>win7-x64</RuntimeIdentifier>` / `<BaseNuGetRuntimeIdentifier>` block — `win7-x64` is unrecognized on .NET 10 (NETSDK1083).

## Notes
- Remaining `NU1201` errors in a full build are expected bottom-up consumer breakage from still-net48 sibling apps (tiers 04.02/04.03/05); not relevant to these two projects, which build cleanly in isolation.
