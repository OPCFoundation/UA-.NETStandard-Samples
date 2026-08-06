# 04.01-reference-apps: Retarget Reference Client & Reference Server

## Objective
Retarget `Samples\ReferenceClient\Reference Client.csproj` and `Samples\ReferenceServer\Reference Server.csproj` from net48 to net10.0-windows.

## Scope
- Reference Client.csproj
- Reference Server.csproj

## Steps
1. Set `<TargetFramework>net10.0-windows</TargetFramework>`, keep `<OutputType>WinExe</OutputType>`, add `<UseWindowsForms>true</UseWindowsForms>`.
2. Remove legacy framework `<Reference>`s, `<RequiredTargetFramework>` metadata, bootstrapper packages, binding redirects.
3. Fix API breaking changes inline; add Windows Compatibility Pack if needed.
4. Build each project; fix all warnings.

## Done when
Both projects build on net10.0-windows with no removed-framework references.
