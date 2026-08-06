# 02.01-level0-foundation: Convert Level 0 foundation libraries to SDK-style

## Objective
Convert the Level 0 (foundation, no project dependencies) legacy projects to SDK-style format, remaining on `net48`. Structural change only — no TFM change, no API changes.

## Scope (convert sequentially, bottom-up)
- Samples/ClientControls.Net4/UA Client Controls.csproj
- Samples/ServerControls.Net4/UA Server Controls.csproj
- Workshop/DataTypes/Common/DataTypes Library.csproj

(Excluded — already SDK-style: Opc.Ua.Sample, NetCoreGlobalDiscoveryServer)

## Steps
1. For each project, call `convert_project_to_sdk_style` ONE AT A TIME (shared MSBuild global state — never parallel).
2. Migrate any packages.config → PackageReference.
3. Preserve WinForms items: .resx/Designer DependentUpon, ApplicationIcon, embedded .uanodes resources.
4. Handle AssemblyInfo.cs (set GenerateAssemblyInfo=false or remove duplicated attributes).
5. Build each project after conversion; then build the solution to confirm no regressions.

## Done when
- All three Level 0 libraries are SDK-style, still target `net48`, and build successfully with no functional change.
- No leftover packages.config in converted projects.
