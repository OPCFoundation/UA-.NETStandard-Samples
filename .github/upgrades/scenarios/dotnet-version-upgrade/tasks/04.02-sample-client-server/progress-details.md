# Task 04.02 — UA Sample Client & Server: Progress Details

## Summary
Retargeted `UA Sample Client` and `UA Sample Server` executables from `net48` to `net10.0-windows`. Both build successfully.

## Projects
| Project | Result |
|---------|--------|
| Samples\Client.Net4\UA Sample Client.csproj | ✅ Built |
| Samples\Server.Net4\UA Sample Server.csproj | ✅ Built |

## Key Changes
- Set `<TargetFramework>net10.0-windows</TargetFramework>`, kept `<OutputType>WinExe</OutputType>` and `<UseWindowsForms>true</UseWindowsForms>`.
- Removed legacy framework `<Reference>`s: `System.Configuration`, `System.Configuration.Install`, `System.Core` (RequiredTargetFramework), `System.IdentityModel`, `System.ServiceModel`, `System.ServiceProcess`.
- Removed obsolete `<RuntimeIdentifier>win7-x64</RuntimeIdentifier>` block (NETSDK1083 on .NET 10).
- Removed `<UseVSHostingProcess>` property.

## Inline API Fix (in dependency UA Sample Controls)
- **Samples\Controls.Net4\ClientForm.cs**: Removed spurious `using static System.Net.Mime.MediaTypeNames;` which caused CS0104 `Font` ambiguity against `System.Drawing.Font` on .NET 10. The static import was unused.

## Notes
- Remaining `NU1201` errors in a full build are expected bottom-up consumer breakage from still-net48 sibling apps (tier 04.03/05); both target projects build cleanly in isolation.
