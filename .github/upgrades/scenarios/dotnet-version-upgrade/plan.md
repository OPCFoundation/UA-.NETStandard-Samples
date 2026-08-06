# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade the WinForms sample and workshop projects (and the shared libraries they depend on) from .NET Framework 4.8 to `net10.0-windows`.
**Scope**: Large solution — ~40 legacy (non-SDK) projects: shared control/class libraries plus ~30 WinForms client/server executables. Bottom-Up strategy, in-place retargeting, per-project package versions during migration.

### Selected Strategy
**Bottom-Up (Dependency-First)** — Upgrade from leaf-node libraries to root applications, tier by tier, validating each tier.
**Rationale**: Multi-project .NET Framework → modern .NET migration with a real dependency graph; shared libraries are consumed by the client/server apps, so each layer must be upgraded and validated before its consumers.

### Dependency Graph (tiers)
```
Tier 2 (apps): Samples apps (Reference/Sample/GDS Client & Server, Sample Controls)
               Workshop apps (Boiler, DataAccess, AlarmCondition, Views, PerfTest,
               Aggregation, HistoricalAccess/Events, Methods, SimpleEvents, Empty,
               UserAuthentication, DataTypes Client/Server)
                 ↓ depend on
Tier 1 (libs): UA Client Controls, UA Server Controls, UA Sample Controls,
               GlobalDiscoveryClientControls, Quickstart Library,
               DataTypes Library, Opc.Ua.Sample
```

## Tasks

### 01-prerequisites: Verify toolchain and target framework readiness

Confirm the .NET 10 SDK is installed and usable, and that no `global.json` in the repo pins an incompatible SDK version. Verify the target `net10.0-windows` is valid for WinForms desktop projects (requires the Windows Desktop workload) and that Visual Studio can restore/build the solution in its current state before any changes are made. Establish a clean baseline build of the current .NET Framework solution so regressions introduced by the upgrade are attributable.

**Done when**: .NET 10 SDK validated; any `global.json` confirmed compatible or updated; current solution restores successfully; baseline recorded.

### 02-sdk-style-conversion: Convert legacy csproj files to SDK-style (on net48)

Convert all non-SDK-style projects (identified by `Project.0001` in the assessment) from the legacy MSBuild format to SDK-style, **while remaining on `net48`**. This is a structural change only — no TFM change and no API changes in this task. Conversion includes migrating any `packages.config` to `PackageReference`, removing explicit `Compile`/boilerplate includes, preserving WinForms-specific items (`.resx`/Designer `DependentUpon`, `ApplicationIcon`, embedded `.uanodes` resources), and handling `AssemblyInfo.cs` (disable `GenerateAssemblyInfo` or remove duplicated attributes). Projects already SDK-style (`Opc.Ua.Sample`, `NetCoreGlobalDiscoveryServer`) are excluded.

**Done when**: All targeted projects are SDK-style, still target `net48`, and the full solution builds successfully on .NET Framework with no functional change.

### 03-foundation-libraries: Retarget shared libraries to net10.0-windows

Upgrade the leaf-tier shared libraries that the applications depend on: `UA Client Controls`, `UA Server Controls`, `UA Sample Controls`, `GlobalDiscoveryClientControls`, `Quickstart Library`, `DataTypes Library`, and `Opc.Ua.Sample`. Retarget each to `net10.0-windows`, add `<UseWindowsForms>true</UseWindowsForms>` for the WinForms control libraries, remove obsolete `<Reference>` framework assemblies and binding redirects, update NuGet packages to target-compatible versions (per `NuGet.0002`), add `Microsoft.Windows.Compatibility` where non-desktop Windows APIs (Registry/WMI/P-Invoke) are used, and fix flagged API breaking changes (`Api.0001/0002/0003`) inline. Research starting points: inventory `System.Windows.Forms`/`System.Drawing` usage and any Registry/P-Invoke in these libraries.

**Done when**: All foundation libraries build on `net10.0-windows`; their tests pass; the still-Framework application projects continue to build against the upgraded libraries (or are ready to move in the next tier).

### 04-sample-applications: Retarget Samples WinForms apps to net10.0-windows

Upgrade the Samples-tier executables that depend on the foundation libraries: `Reference Client`, `Reference Server`, `UA Sample Client`, `UA Sample Server`, `UA Sample Controls` consumers, and the GDS `GlobalDiscoveryClient` / `GlobalDiscoveryServer`. Retarget to `net10.0-windows`, set `<OutputType>WinExe</OutputType>` and `<UseWindowsForms>true</UseWindowsForms>`, remove framework `<Reference>`s and binding redirects, update packages, add the Windows Compatibility Pack where needed, and fix API breaking changes inline. This tier depends on tier 1 being complete. Large task — expect execution-time breakdown into per-app or per-feature subtasks.

**Done when**: All Samples applications build and launch on `net10.0-windows`; tests pass; no references to removed framework assemblies remain.

### 05-workshop-applications: Retarget Workshop WinForms apps to net10.0-windows

Upgrade the Workshop-tier client/server executables that depend on `Quickstart Library` / `DataTypes Library`: Boiler, DataAccess, AlarmCondition, Views, PerfTest, Aggregation (Client & Server), HistoricalAccess (Client/Server/Tester), HistoricalEvents, Methods, SimpleEvents, Empty, UserAuthentication, and DataTypes (Client & Server). Same mechanics as the Samples tier: retarget to `net10.0-windows`, `WinExe` + `UseWindowsForms`, remove framework references/redirects, update packages, add compatibility pack where needed, fix API breaking changes inline. Large task — expect execution-time breakdown by workshop feature group.

**Done when**: All Workshop applications build and launch on `net10.0-windows`; tests pass; solution builds end-to-end.

### 06-incompatible-package-resolution: Resolve deferred incompatible package in ConsoleAggregationServer

Address the incompatible package flagged by `NuGet.0001` in `ConsoleAggregationServer` (deferred during tier upgrades to preserve buildability). Research a target-compatible replacement or a supported newer version, update the reference, and adapt consuming code. If no replacement exists, document the blocker and options.

**Done when**: `ConsoleAggregationServer` builds on its target framework with a supported package (or a documented, agreed workaround is in place); no deferred stubs remain.

### 07-final-validation: Full-solution validation and deferred recommendations

Build the entire solution on the new targets, run the full test suite, and confirm no projects remain on .NET Framework within scope. Document the deferred **Central Package Management** recommendation (all projects are now SDK-style on a single TFM — CPM can be added cleanly without `VersionOverride` friction) and any follow-ups (e.g., enabling nullable reference types as a separate effort).

**Done when**: Full solution builds with no errors and no warnings in modified projects; all tests pass; deferred CPM and nullable recommendations recorded.
