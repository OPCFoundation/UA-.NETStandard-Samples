FROM mcr.microsoft.com/dotnet/runtime:6.0-bullseye-slim AS base

COPY ./publish /publish
WORKDIR /publish

ENTRYPOINT ["dotnet", "NetCoreGlobalDiscoveryServer.dll"]
