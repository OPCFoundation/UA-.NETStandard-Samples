# Task 03 — Foundation Libraries: Progress Details

## Summary
Retargeted all Tier-1/Tier-3 shared (foundation) libraries from `net48` to `net10.0-windows` and validated each individually. Bottom-up ordering means consumer apps (tiers 04/05, still `net48`) now report expected `NU1201` incompatibility — this is approved/expected and resolved when those tiers are retargeted.

## Projects Retargeted & Validated
| Project | Result |
|---------|--------|
| Workshop\DataTypes\Common\DataTypes Library.csproj | ✅ Built on net10.0-windows |
| Samples\ClientControls.Net4\UA Client Controls.csproj | ✅ Built (fixed CS0104 X509AuthorityKeyIdentifierExtension ambiguity via alias) |
| Samples\ServerControls.Net4\UA Server Controls.csproj | ✅ Built |
| Workshop\Common\Quickstart Library.csproj | ✅ Built |
| Samples\Controls.Net4\UA Sample Controls.csproj | ✅ Compiles (fixed CS0234 — removed dead WCF usings) |
| Samples\GDS\ClientControls\GlobalDiscoveryClientControls.csproj | ✅ Built (removed System.ServiceModel + other legacy refs) |
| Samples\Opc.Ua.Sample\Opc.Ua.Sample.csproj | ✅ No-op (already multi-targets via $(LibTargetFrameworks) incl. net10.0) |

## Key Changes
- **WFO1000 analyzer** (option c): Added `dotnet_diagnostic.WFO1000.severity = warning` to `.editorconfig` (solution-wide) so WinForms designer serialization diagnostics don't block the build.
- **CertificatePropertiesListCtrl.cs**: Added alias `using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;` to disambiguate from the new .NET 10 `System.Security.Cryptography.X509Certificates` type (CS0104).
- **UA Sample Controls session dialogs** (CreateSecureChannelDlg.cs, EndpointViewDlg.cs, ReadHistoryDlg.cs, SecuritySettingsDlg.cs): Removed unused WCF usings (`System.ServiceModel`, `System.IdentityModel.Claims`, `System.ServiceModel.Security`, `System.ServiceModel.Channels`) — these were dead imports; actual channel logic uses OPC UA `ITransportChannel`/`UaChannelBase`. Fixes CS0234.
- **GlobalDiscoveryClientControls.csproj**: Removed legacy `System.ServiceModel`, `System.Data.DataSetExtensions`, `Microsoft.CSharp`, `System.Xml.Serialization` framework references; retargeted to net10.0-windows.
- Removed obsolete framework references, bootstrapper packages, and `<RequiredTargetFramework>` metadata from all retargeted `.csproj` files.

## Expected/Deferred
- `NU1201` on consumer apps (tier 04/05) — expected under bottom-up strategy; resolved when those tiers retarget.
- Package versions kept per-project; CPM deferred to task 07.
