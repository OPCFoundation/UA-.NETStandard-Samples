# 04-sample-applications: Retarget Samples WinForms apps to net10.0-windows

Upgrade the Samples-tier executables that depend on the foundation libraries: `Reference Client`, `Reference Server`, `UA Sample Client`, `UA Sample Server`, `UA Sample Controls` consumers, and the GDS `GlobalDiscoveryClient` / `GlobalDiscoveryServer`. Retarget to `net10.0-windows`, set `<OutputType>WinExe</OutputType>` and `<UseWindowsForms>true</UseWindowsForms>`, remove framework `<Reference>`s and binding redirects, update packages, add the Windows Compatibility Pack where needed, and fix API breaking changes inline. This tier depends on tier 1 being complete. Large task — expect execution-time breakdown into per-app or per-feature subtasks.

**Done when**: All Samples applications build and launch on `net10.0-windows`; tests pass; no references to removed framework assemblies remain.
