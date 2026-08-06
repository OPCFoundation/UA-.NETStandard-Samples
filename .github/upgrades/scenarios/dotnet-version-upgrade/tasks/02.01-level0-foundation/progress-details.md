## Files Modified
- Samples/ClientControls.Net4/UA Client Controls.csproj (converted to SDK-style; fixed NETSDK1022 duplicate Compile items)
- Samples/ClientControls.Net4/packages.config (removed by conversion)
- Samples/ServerControls.Net4/UA Server Controls.csproj (converted to SDK-style)
- Samples/ServerControls.Net4/packages.config (removed by conversion)
- Workshop/DataTypes/Common/DataTypes Library.csproj (converted to SDK-style)
- Workshop/DataTypes/Common/packages.config (removed by conversion, if present)

## Build Result
- Errors: 0
- Warnings: 0
- Projects built: UA Client Controls, UA Server Controls, DataTypes Library (each built individually — all successful)

## Test Result
- Tests run: 0 (structural conversion, no behavior change; validation is via build)

## Changes Summary
- Converted the three Level 0 foundation libraries from legacy csproj to SDK-style format using `convert_project_to_sdk_style` (sequentially).
- TFM unchanged (`net48`); WinForms enabled via `UseWindowsForms`; no package version drift.
- Fixed one conversion side-effect in UA Client Controls: 6 `Common (OLD)` files were explicitly `<Compile Include>`'d and also picked up by default globbing → NETSDK1022. Changed those to `<Compile Update>` to preserve `SubType` metadata without duplicating.

## Issues Encountered
- NETSDK1022 (duplicate Compile items) in UA Client Controls after conversion — resolved by converting the 6 duplicate `Include` items to `Update`. No other issues.
