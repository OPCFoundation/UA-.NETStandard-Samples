# 05.01-boiler: Retarget Boiler Client & Server

## Objective
Retarget Workshop Boiler Client & Server to net10.0-windows.

## Projects
- Workshop\Boiler\Client\Boiler Client.csproj
- Workshop\Boiler\Server\Boiler Server.csproj

## Steps
1. Set TargetFramework=net10.0-windows, OutputType=WinExe, UseWindowsForms=true.
2. Remove framework <Reference>s and binding redirects.
3. Update packages to net10-compatible versions; add Microsoft.Windows.Compatibility if Registry/WMI/P-Invoke used.
4. Fix API breaking changes inline (mirror patterns from Samples tier: Font/type ambiguities, dead WCF usings).
5. Build each project; fix all errors and warnings.

## Done when
Both projects build on net10.0-windows; no removed-framework references remain.
