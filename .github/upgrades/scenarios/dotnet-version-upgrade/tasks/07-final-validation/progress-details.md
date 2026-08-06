# Task 07 — Final Validation & Deferred Recommendations

## Objective
Build the entire solution on the new targets, run the full test suite, confirm no in-scope projects remain on .NET Framework, and document deferred recommendations (CPM, nullable) and follow-ups.

## Result: ✅ PASS

## Full-Solution Build
- **Command**: `dotnet build "UA Samples.slnx" --no-incremental`
- **Outcome**: **Build succeeded — 0 Errors**
- **Warnings**: ~152 (accepted baseline — see below)
- All previously-blocking `NU1201` compatibility errors resolved once `05.13-datatypes-apps` retargeted DataTypes Client/Server to `net10.0-windows`.

> Note: an *incremental* build reports fewer warnings because cached projects don't re-emit diagnostics. A `--no-incremental` clean build is the authoritative source for the true warning baseline.

## No .NET Framework Projects Remain (in scope)
- Repository-wide search for `net48` / `net4x` target frameworks in scope returned **no in-scope WinForms app/library projects still on .NET Framework**.
- All in-scope WinForms projects target `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`; executables keep `<OutputType>WinExe</OutputType>`.
- `targets.props` retains `net48` only in the conditional multi-target lists for library cross-compat, not as an in-scope app target.

## Test Suite
- **No test projects exist** in the solution (no `Microsoft.NET.Test.Sdk`, xUnit, NUnit, or MSTest references found).
- Validation is therefore **build-based only**. No tests to execute.

## Warning Cleanup Performed in This Task
| Change | Warnings removed |
|--------|------------------|
| Removed orphaned `<CodeAnalysisRuleSet>AllRules.ruleset</CodeAnalysisRuleSet>` from 12 project files | 24 × MSB3884 |
| Removed redundant `System.Configuration.ConfigurationManager` PackageReference (GlobalDiscoveryServer) | NU1510 |
| Removed redundant `System.Net.Http` PackageReference (UA Sample Client) | NU1510 |

Files modified:
- `Samples/ClientControls.Net4/UA Client Controls.csproj`
- `Samples/Controls.Net4/UA Sample Controls.csproj`
- `Samples/GDS/Client/GlobalDiscoveryClient.csproj`
- `Samples/GDS/ClientControls/GlobalDiscoveryClientControls.csproj`
- `Samples/GDS/Server/GlobalDiscoveryServer.csproj`
- `Samples/Client.Net4/UA Sample Client.csproj`
- `Workshop/AlarmCondition/Client/AlarmCondition Client.csproj`
- `Workshop/AlarmCondition/Server/AlarmCondition Server.csproj`
- `Workshop/Boiler/Client/Boiler Client.csproj`
- `Workshop/Boiler/Server/Boiler Server.csproj`
- `Workshop/Common/Quickstart Library.csproj`
- `Workshop/DataAccess/Client/DataAccess Client.csproj`
- `Workshop/DataAccess/Server/DataAccess Server.csproj`
- `Workshop/DataTypes/Common/DataTypes Library.csproj`

## Accepted Warning Baseline (~152)
Pre-existing, repo-wide analyzer diagnostics driven by the intentionally aggressive
`AnalysisMode=all` / `AnalysisLevel=preview-all` config in `targets.props`. **Not migration regressions** — the solution builds with 0 errors.

| Approx. Count | Code | Nature | Disposition |
|------|------|--------|-------------|
| WFO1000 | WinForms serialization | Accepted policy — surfaced as warning (not error) via `CodeAnalysisTreatWarningsAsErrors=false`, not suppressed | Accepted |
| SYSLIB0057 | `X509Certificate2` ctor obsolete → `X509CertificateLoader` | Suggested modernization | Deferred |
| CS0618 / CS0672 | Obsolete API usage | Pre-existing | Deferred |
| CA1845 / CA1864 / CA1865 / CA1866 | Roslyn perf suggestions | Style/perf | Deferred |
| SYSLIB0027 / SYSLIB0060 | Obsolete crypto/API | Suggested modernization | Deferred |
| CS8073 / WFDEV005 | Misc analyzer | Pre-existing | Deferred |

User approved (**Option A**) accepting these as the baseline rather than reopening completed tier tasks.

## WFO1000 Policy Confirmation
Decision (Key Decisions Log) = set WFO1000 to *warning* severity at `targets.props` level (option c).
Satisfied by existing `targets.props`:
```xml
<CodeAnalysisTreatWarningsAsErrors>false</CodeAnalysisTreatWarningsAsErrors>
<AnalysisLevel>preview-all</AnalysisLevel>
<AnalysisMode>all</AnalysisMode>
```
WFO1000 surfaces as a warning (not error), and is neither `NoWarn`-suppressed nor rewritten as per-property attributes — exactly option (c).

## Deferred Recommendations (Follow-ups)
1. **Central Package Management (CPM)** — deferred per upgrade options. Introduce `Directory.Packages.props` to centralize the per-project package versions used during migration.
2. **Nullable Reference Types** — left disabled per upgrade options. Recommend a separate opt-in NRT modernization effort.
3. **Analyzer warning burn-down** — optionally reduce the ~152 baseline warnings (SYSLIB/CA/CS) in a dedicated cleanup pass, or dial back `AnalysisMode=all` if the aggressive profile is not desired.

## Commits
- Warning cleanup committed: `00327705` — "Task 07: clean up orphaned AllRules.ruleset (MSB3884) and redundant package refs (NU1510)"
