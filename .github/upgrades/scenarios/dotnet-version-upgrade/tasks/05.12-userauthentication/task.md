# 05.12-userauthentication: Retarget UserAuthentication Client & Server

## Objective
Retarget Workshop UserAuthentication Client & Server to net10.0-windows.

## Projects
- Workshop\UserAuthentication\Client\UserAuthentication Client.csproj
- Workshop\UserAuthentication\Server\UserAuthentication Server.csproj

## Notes
- UserAuthentication Client references Aggregate Tester (05.07) — ensure that is retargeted first.

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages; add Windows Compatibility Pack if needed.
4. Fix API breaking changes inline.
5. Build; fix all errors and warnings.

## Done when
Both projects build on net10.0-windows.
