# 05.07-historicalaccess: Retarget HistoricalAccess Client/Server/Tester

## Objective
Retarget Workshop HistoricalAccess Client, Server, and Aggregate Tester to net10.0-windows.

## Projects
- Workshop\HistoricalAccess\Client\HistoricalAccess Client.csproj
- Workshop\HistoricalAccess\Server\HistoricalAccess Server.csproj
- Workshop\HistoricalAccess\Tester\Aggregate Tester.csproj

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages; add Windows Compatibility Pack if needed.
4. Fix API breaking changes inline.
5. Build; fix all errors and warnings.

## Done when
All three projects build on net10.0-windows.
