# 05.11-empty: Retarget Empty Client & Server

## Objective
Retarget Workshop Empty Client & Server to net10.0-windows.

## Projects
- Workshop\Empty\Client\Empty Client.csproj
- Workshop\Empty\Server\Empty Server.csproj

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages; add Windows Compatibility Pack if needed.
4. Fix API breaking changes inline.
5. Build; fix all errors and warnings.

## Done when
Both projects build on net10.0-windows.
