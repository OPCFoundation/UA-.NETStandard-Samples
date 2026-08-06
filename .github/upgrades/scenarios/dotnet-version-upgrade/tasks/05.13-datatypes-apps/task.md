# 05.13-datatypes-apps: Retarget DataTypes Client & Server

## Objective
Retarget Workshop DataTypes Client & Server apps to net10.0-windows (DataTypes Library already done in task 03).

## Projects
- Workshop\DataTypes\Client\DataTypes Client.csproj
- Workshop\DataTypes\Server\DataTypes Server.csproj

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages; add Windows Compatibility Pack if needed.
4. Fix API breaking changes inline.
5. Build; fix all errors and warnings.

## Done when
Both projects build on net10.0-windows.
