# 03-foundation-libraries: Retarget shared libraries to net10.0-windows

Upgrade the leaf-tier shared libraries that the applications depend on: `UA Client Controls`, `UA Server Controls`, `UA Sample Controls`, `GlobalDiscoveryClientControls`, `Quickstart Library`, `DataTypes Library`, and `Opc.Ua.Sample`. Retarget each to `net10.0-windows`, add `<UseWindowsForms>true</UseWindowsForms>` for the WinForms control libraries, remove obsolete `<Reference>` framework assemblies and binding redirects, update NuGet packages to target-compatible versions (per `NuGet.0002`), add `Microsoft.Windows.Compatibility` where non-desktop Windows APIs (Registry/WMI/P-Invoke) are used, and fix flagged API breaking changes (`Api.0001/0002/0003`) inline. Research starting points: inventory `System.Windows.Forms`/`System.Drawing` usage and any Registry/P-Invoke in these libraries.

**Done when**: All foundation libraries build on `net10.0-windows`; their tests pass; the still-Framework application projects continue to build against the upgraded libraries (or are ready to move in the next tier).

## Research Findings

### Projects Affected (dependency order)
1. `Workshop/DataTypes/Common/DataTypes Library.csproj` — class lib, no WinForms UI
2. `Samples/ClientControls.Net4/UA Client Controls.csproj` — WinForms controls
3. `Samples/ServerControls.Net4/UA Server Controls.csproj` — WinForms controls
4. `Workshop/Common/Quickstart Library.csproj` — WinForms lib; uses WCF (System.ServiceModel, System.IdentityModel/.Selectors), System.ServiceProcess
5. `Samples/Controls.Net4/UA Sample Controls.csproj` — WinForms controls; System.IdentityModel, System.ServiceModel
6. `Samples/GDS/ClientControls/GlobalDiscoveryClientControls.csproj` — WinForms; refs UA Client Controls; System.ServiceModel
7. `Samples/Opc.Ua.Sample/Opc.Ua.Sample.csproj` — already multi-targeted via $(LibTargetFrameworks); no change (verify build only)

### Approach
- `<TargetFramework>net48</TargetFramework>` → `net10.0-windows` for projects 1-6.
- Keep `<UseWindowsForms>true</UseWindowsForms>` where present; DataTypes Library stays a plain class lib.
- Remove obsolete framework `<Reference>` items (System.Core, System.Runtime.Serialization, System.Xml.Linq, System.ServiceProcess, System.ComponentModel.DataAnnotations, System.Data.DataSetExtensions, Microsoft.CSharp, System.Xml.Serialization).
- Remove `<BootstrapperPackage>` and ClickOnce publish properties (obsolete).
- WCF refs have no .NET 10 framework equivalent — check actual usage; fix inline at build time.

### Packages
- All packages report ✅ Compatible. NuGet.0002 is only a recommended upgrade (Potential) — defer per per-project policy.

### API Issues
- Api.0001 counts are mostly WinForms binary-compat, auto-resolved by net10.0-windows. Fix real breaks inline at build.

### Decisions
- Opc.Ua.Sample treated as no-op (already modern TFMs).
- Iterative build-fix per project in dependency order; full solution build at end.
