# Upgrade Options — UA Samples

Assessment: ~40 projects, all on .NET Framework 4.8 (legacy csproj), ~30 WinForms apps + shared control/library projects; SDK-style conversion required, 1 incompatible package, framework API breaking changes flagged, WinForms/System.Drawing throughout.

## Strategy

### Upgrade Strategy
.NET Framework → modern .NET across multiple projects with a real dependency graph (shared libraries consumed by clients/servers), so tier-by-tier upgrade with validation is required. This selection is fixed for multi-project Framework migrations.

| Value | Description |
|-------|-------------|
| **Bottom-Up** (selected) | Upgrade leaf-node libraries first (e.g., Quickstart/DataTypes libraries, control libraries), then work upward to the client/server apps tier by tier, validating each tier. Fixed for this solution. |

## Project Structure

### Project Approach
Projects are class libraries and WinForms executables with no ASP.NET/System.Web; since all consumers migrate together in one effort, libraries can retarget directly without a multi-targeting transition window.

| Value | Description |
|-------|-------------|
| **In-place** (selected) | Retarget each library/app directly to `net10.0-windows`. Clean, no multi-targeting overhead — appropriate because all consumers upgrade together. |
| Multi-targeting | Add the new TFM alongside `net48` so libraries serve both Framework and modern consumers during a longer transition. Adds `#if` and dependency-graph overhead. |

### Package Management
Solution has many projects, is non-SDK-style, and crosses the .NET Framework → modern boundary — introducing CPM now would create `VersionOverride` churn on top of the SDK conversion.

| Value | Description |
|-------|-------------|
| **Per-Project (defer CPM to post-migration)** (selected) | Each project keeps its own package versions during the migration; a CPM setup recommendation is added to the final cleanup phase once all projects are SDK-style and on one TFM. |
| Central Package Management (CPM) | Create `Directory.Packages.props` now and centralize versions. Best when all projects are already SDK-style and modern — not the case here. |

## Compatibility

### Unsupported Packages
The assessment flags one incompatible package (NuGet.0001, in `ConsoleAggregationServer`) with no drop-in compatible version for the target TFM; with a Bottom-Up strategy, per-tier buildability is preferred.

| Value | Description |
|-------|-------------|
| **Defer Resolution** (selected) | Make the project build without the incompatible package (condition/remove it), then create a follow-up task to find a real replacement, preserving tier buildability. |
| Resolve Inline | Research and replace the incompatible package within the same task. Reasonable given the small count, but interrupts tier flow. |
| Compatibility Mode | Keep the .NET Framework reference and suppress NU1701. Only safe for transitive dependencies not called directly. |

### Unsupported API Handling
Binary/source/behavioral API changes (Api.0001/0002/0003) are flagged broadly, but most WinForms/BCL changes across these framework versions are mechanical renames/namespace moves.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Resolve every API change in the same task, including complex ones. No deferred stubs to clean up later. |
| Defer Complex Changes | Apply simple fixes inline, stub complex ones, and create resolution subtasks. Better only when there are many (>5) complex changes per tier. |

### Windows Native APIs
The projects use `System.Windows.Forms`, `System.Drawing` (GDI+), and likely Registry/P-Invoke; these apps are inherently Windows desktop apps with no cross-platform requirement.

| Value | Description |
|-------|-------------|
| **Windows Compatibility Pack** (selected) | Add `Microsoft.Windows.Compatibility` where non-desktop Windows APIs (Registry, WMI, etc.) are used. Apps stay Windows-only (expected for WinForms). WinForms/System.Drawing themselves come from the Windows Desktop SDK via `UseWindowsForms`. |
| No Compatibility Pack | Windows API build errors surface immediately and must be replaced with cross-platform alternatives. Unnecessary — these are desktop-only apps. |

## Modernization

### Assembly Binding Redirects
Projects use `AutoGenerateBindingRedirects` (Visual Studio auto-generated boilerplate), not hand-authored redirects; SDK-style modern .NET does not need them.

| Value | Description |
|-------|-------------|
| **Remove Binding Redirects** (selected) | Remove all redirects during project cleanup — .NET Core resolves assemblies without them. |
| Document and Review Before Removing | Produce a report of redirects first. Warranted only when redirects are hand-authored or span major version jumps. |

### Nullable Reference Types
Target `net10.0-windows` supports NRTs, but the solution spans many projects and a large legacy codebase — enabling now would flood the migration with warnings.

| Value | Description |
|-------|-------------|
| **Leave Disabled** (selected) | Do not enable nullable during the upgrade. Adopt separately as a focused effort afterward. |
| Enable Nullable Reference Types | Add `<Nullable>enable</Nullable>` now. Compile-time null safety, but generates many warnings across this large codebase. |
