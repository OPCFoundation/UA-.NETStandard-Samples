# 02-sdk-style-conversion: Convert legacy csproj files to SDK-style (on net48)

Convert all non-SDK-style projects (identified by `Project.0001` in the assessment) from the legacy MSBuild format to SDK-style, **while remaining on `net48`**. This is a structural change only — no TFM change and no API changes in this task. Conversion includes migrating any `packages.config` to `PackageReference`, removing explicit `Compile`/boilerplate includes, preserving WinForms-specific items (`.resx`/Designer `DependentUpon`, `ApplicationIcon`, embedded `.uanodes` resources), and handling `AssemblyInfo.cs` (disable `GenerateAssemblyInfo` or remove duplicated attributes). Projects already SDK-style (`Opc.Ua.Sample`, `NetCoreGlobalDiscoveryServer`) are excluded.

**Done when**: All targeted projects are SDK-style, still target `net48`, and the full solution builds successfully on .NET Framework with no functional change.

## Research Findings

### Scope
~38 legacy projects need conversion. Already SDK-style (excluded): `Opc.Ua.Sample`, `NetCoreGlobalDiscoveryServer`.

### Dependency tiers (from assessment get_projects_graph)
- **Level 0 (foundation libs)**: UA Client Controls, UA Server Controls, DataTypes Library, (ConsoleAggregationServer — top-level, incompatible pkg, convert with its tier)
- **Level 1 (libs + apps)**: Quickstart Library, GlobalDiscoveryClientControls, UA Sample Controls, Aggregate Tester, plus most workshop/sample client & server apps
- **Level 2 (apps depending on L0+L1)**: AlarmCondition, DataAccess, GlobalDiscoveryClient, HistoricalEvents, UA Sample Client/Server, UserAuthentication Client

### Decomposition Decision
38 projects >> 4-project threshold → **decompose by dependency tier** per execution.md Section 1. Convert bottom-up: Level 0 → Level 1 → Level 2, building after each tier. `convert_project_to_sdk_style` must run **sequentially** (shared MSBuild global state).

### Constraints
- Structural change only — TFM stays `net48`, no API changes.
- Preserve WinForms items (.resx/Designer DependentUpon, ApplicationIcon, embedded .uanodes), migrate packages.config → PackageReference, handle AssemblyInfo duplication.
