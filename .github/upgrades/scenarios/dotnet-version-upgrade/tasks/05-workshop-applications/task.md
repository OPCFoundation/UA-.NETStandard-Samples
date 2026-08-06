# 05-workshop-applications: Retarget Workshop WinForms apps to net10.0-windows

Upgrade the Workshop-tier client/server executables that depend on `Quickstart Library` / `DataTypes Library`: Boiler, DataAccess, AlarmCondition, Views, PerfTest, Aggregation (Client & Server), HistoricalAccess (Client/Server/Tester), HistoricalEvents, Methods, SimpleEvents, Empty, UserAuthentication, and DataTypes (Client & Server). Same mechanics as the Samples tier: retarget to `net10.0-windows`, `WinExe` + `UseWindowsForms`, remove framework references/redirects, update packages, add compatibility pack where needed, fix API breaking changes inline. Large task — expect execution-time breakdown by workshop feature group.

**Done when**: All Workshop applications build and launch on `net10.0-windows`; tests pass; solution builds end-to-end.
