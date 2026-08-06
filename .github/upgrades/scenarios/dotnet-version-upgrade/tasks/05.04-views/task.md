# 05.04-views: Retarget Views Client & Server

## Objective
Retarget Workshop Views Client & Server to net10.0-windows.

## Projects
- Workshop\Views\Client\Views Client.csproj
- Workshop\Views\Server\Views Server.csproj

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages; add Windows Compatibility Pack if needed.
4. Fix API breaking changes inline.
5. Build; fix all errors and warnings.

## Done when
Both projects build on net10.0-windows.
