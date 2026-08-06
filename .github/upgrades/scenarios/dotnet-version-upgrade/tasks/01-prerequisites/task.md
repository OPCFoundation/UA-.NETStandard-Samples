# 01-prerequisites: Verify toolchain and target framework readiness

Confirm the .NET 10 SDK is installed and usable, and that no `global.json` in the repo pins an incompatible SDK version. Verify the target `net10.0-windows` is valid for WinForms desktop projects (requires the Windows Desktop workload) and that Visual Studio can restore/build the solution in its current state before any changes are made. Establish a clean baseline build of the current .NET Framework solution so regressions introduced by the upgrade are attributable.

**Done when**: .NET 10 SDK validated; any `global.json` confirmed compatible or updated; current solution restores successfully; baseline recorded.
