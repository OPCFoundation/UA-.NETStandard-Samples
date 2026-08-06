# 05.06-aggregation: Retarget Aggregation Client & Server (exclude Console)

## Objective
Retarget Workshop Aggregation Client & Server to net10.0-windows. EXCLUDE ConsoleAggregationServer (deferred to task 06 due to NuGet.0001 incompatible package).

## Projects
- Workshop\Aggregation\Client\Aggregation Client.csproj
- Workshop\Aggregation\Server\Aggregation Server.csproj

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages; add Windows Compatibility Pack if needed.
4. Fix API breaking changes inline.
5. Build; fix all errors and warnings.
6. Do NOT touch ConsoleAggregationServer.

## Done when
Aggregation Client & Server build on net10.0-windows; ConsoleAggregationServer untouched.
