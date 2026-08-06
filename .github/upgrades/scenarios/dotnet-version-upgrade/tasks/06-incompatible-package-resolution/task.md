# 06-incompatible-package-resolution: Resolve deferred incompatible package in ConsoleAggregationServer

Address the incompatible package flagged by `NuGet.0001` in `ConsoleAggregationServer` (deferred during tier upgrades to preserve buildability). Research a target-compatible replacement or a supported newer version, update the reference, and adapt consuming code. If no replacement exists, document the blocker and options.

**Done when**: `ConsoleAggregationServer` builds on its target framework with a supported package (or a documented, agreed workaround is in place); no deferred stubs remain.
