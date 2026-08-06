# 02-sdk-style-conversion: Convert legacy csproj files to SDK-style (on net48)

Convert all non-SDK-style projects (identified by `Project.0001` in the assessment) from the legacy MSBuild format to SDK-style, **while remaining on `net48`**. This is a structural change only — no TFM change and no API changes in this task. Conversion includes migrating any `packages.config` to `PackageReference`, removing explicit `Compile`/boilerplate includes, preserving WinForms-specific items (`.resx`/Designer `DependentUpon`, `ApplicationIcon`, embedded `.uanodes` resources), and handling `AssemblyInfo.cs` (disable `GenerateAssemblyInfo` or remove duplicated attributes). Projects already SDK-style (`Opc.Ua.Sample`, `NetCoreGlobalDiscoveryServer`) are excluded.

**Done when**: All targeted projects are SDK-style, still target `net48`, and the full solution builds successfully on .NET Framework with no functional change.
