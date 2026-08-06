# .NET Version Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0
- **Scope**: All WinForms projects (Samples + Workshop) — OutputType=WinExe / System.Windows.Forms references

## Source Control
- **Source Branch**: master
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: Bottom-Up

### Project Structure
- Project Approach: In-place
- Package Management: Per-Project (defer CPM to post-migration)

### Compatibility
- Unsupported Packages: Defer Resolution
- Unsupported API Handling: Fix Inline
- Windows Native APIs: Windows Compatibility Pack

### Modernization
- Assembly Binding Redirects: Remove Binding Redirects
- Nullable Reference Types: Leave Disabled

## Strategy
**Selected**: Bottom-Up (Dependency-First)
**Rationale**: ~40-project .NET Framework → .NET 10 migration with a real dependency graph; shared libraries (control libs, Quickstart/DataTypes libraries, Opc.Ua.Sample) are consumed by ~30 WinForms client/server apps, so each layer must be upgraded and validated before its consumers.

### Execution Constraints
- Strict tier ordering: Tier 1 (foundation libraries) must build and validate before Tier 2 (applications) starts.
- SDK-style conversion is a separate task from TFM upgrade — never merge them; conversion stays on `net48`.
- WinForms projects retarget to `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`; executables keep `<OutputType>WinExe</OutputType>`.
- Between-tier validation: after each tier, confirm higher (not-yet-upgraded) tiers still build on .NET Framework.
- Package versions stay per-project during migration; add CPM only in final cleanup (07-final-validation).
- Fix API breaking changes inline; defer only the flagged incompatible package (ConsoleAggregationServer) to task 06.
- Large tier tasks (04, 05) may be broken into per-app/per-feature subtasks at execution time.

## Key Decisions Log
- **UserAuthentication WIF/WCF security stub-out** (task 05.12): UserAuthentication Client & Server depend on `System.IdentityModel` WCF/WIF security token types (WSSecurityTokenSerializer, Kerberos*SecurityToken, SecurityTokenResolver, UserNameSecurityToken, X509CertificateValidator) and `WindowsImpersonationContext`, none of which have a .NET 10 port or Windows Compatibility Pack equivalent. User chose **Option C**: keep projects on `net10.0-windows`, conditional-compile/stub the unsupported Kerberos/WS-Security/impersonation paths so they compile (throw `NotSupportedException` / `PlatformNotSupportedException`), keeping username/password + certificate auth functional. Approved by user.
- **GDS Server EF6 EDMX → EF Core** (task 04.03): GlobalDiscoveryServer.csproj uses EF6 EDMX Model-First (gdsdb.edmx, usersdb.edmx, System.Data.Entity). EDMX is unsupported on .NET 10. User chose to **migrate to EF Core** (option A) rather than EF6 code-first or defer. Convert both EDMX models to EF Core DbContext + entity classes.
- **WFO1000 WinForms analyzer** (2025 retarget): Set severity to warning at `targets.props` level (option c) rather than per-property `[DesignerSerializationVisibility]` attributes or per-project NoWarn. Approved by user.
- **NU1201 bottom-up breakage**: Validate tier-3 libraries individually (not full-solution) until consumer apps are retargeted in tiers 04/05. Expected/normal for bottom-up ordering.
