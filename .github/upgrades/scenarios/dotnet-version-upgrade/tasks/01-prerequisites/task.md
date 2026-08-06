# 01-prerequisites: Verify toolchain and target framework readiness

Confirm the .NET 10 SDK is installed and usable, and that no `global.json` in the repo pins an incompatible SDK version. Verify the target `net10.0-windows` is valid for WinForms desktop projects (requires the Windows Desktop workload) and that Visual Studio can restore/build the solution in its current state before any changes are made. Establish a clean baseline build of the current .NET Framework solution so regressions introduced by the upgrade are attributable.

**Done when**: .NET 10 SDK validated; any `global.json` confirmed compatible or updated; current solution restores successfully; baseline recorded.

## Research Findings

### Environment
- IDE: Visual Studio Community 2026 (18.8.2), Windows, PowerShell.
- Solution: `UA Samples.slnx` — ~40 legacy .NET Framework 4.8 projects (WinForms apps + shared libraries).
- Git branch: `upgrade-dotnet-10` (clean baseline).

### Validation Results
- **.NET 10 SDK**: `validate_dotnet_sdk_installation(net10.0)` → "Compatible SDK found".
- **global.json**: none found in repo → nothing pins an incompatible SDK; no changes needed.
- **Baseline build**: full solution build via IDE → **Build successful** on current .NET Framework 4.8 state.

### Notes for downstream tasks
- Target for WinForms projects will be `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>` (Windows Desktop SDK). No `global.json` to maintain.
- No toolchain blockers — safe to proceed to SDK-style conversion (task 02).

### Decisions Made
- No `global.json` created — repo intentionally floats to the latest installed SDK, which satisfies net10.0.
