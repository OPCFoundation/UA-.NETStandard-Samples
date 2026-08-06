# 02.02-level1: Convert Level 1 libraries and apps to SDK-style

## Objective
Convert the Level 1 legacy projects (depend only on Level 0) to SDK-style, remaining on `net48`. Structural change only.

## Scope (convert sequentially)
Libraries: Workshop/Common/Quickstart Library.csproj, Samples/GDS/ClientControls/GlobalDiscoveryClientControls.csproj, Samples/Controls.Net4/UA Sample Controls.csproj, Workshop/HistoricalAccess/Tester/Aggregate Tester.csproj
Apps: Aggregation Client/Server, Boiler Client/Server, DataTypes Client/Server, Empty Client/Server, GlobalDiscoveryServer, HistoricalAccess Client/Server, Methods Client/Server, PerfTest Client/Server, Reference Client/Server, SimpleEvents Client/Server, UserAuthentication Server, Views Client/Server

## Steps
1. Convert each project with `convert_project_to_sdk_style` ONE AT A TIME.
2. Migrate packages.config → PackageReference; preserve WinForms items (.resx/Designer DependentUpon, ApplicationIcon, .uanodes); handle AssemblyInfo duplication.
3. Build after each conversion; build solution after the tier.

## Done when
- All Level 1 projects are SDK-style on `net48` and the solution builds with no functional change.
