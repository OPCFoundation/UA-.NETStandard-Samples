# 02.03-level2: Convert Level 2 apps to SDK-style and validate full solution

## Objective
Convert the remaining Level 2 legacy projects (depend on Level 0+1) to SDK-style on `net48`, then validate the entire solution.

## Scope (convert sequentially)
- AlarmCondition Client/Server, DataAccess Client/Server, GlobalDiscoveryClient, HistoricalEvents Client/Server, UA Sample Client, UA Sample Server, UserAuthentication Client
- ConsoleAggregationServer (top-level; convert here — incompatible package resolution is deferred to task 06, keep it building)

## Steps
1. Convert each project with `convert_project_to_sdk_style` ONE AT A TIME.
2. Migrate packages.config → PackageReference; preserve WinForms items; handle AssemblyInfo duplication.
3. Build after each conversion.
4. Full solution build to confirm the whole conversion is behavior-preserving on .NET Framework.

## Done when
- All targeted projects are SDK-style on `net48`.
- Full solution builds successfully with no functional change; no leftover packages.config.
