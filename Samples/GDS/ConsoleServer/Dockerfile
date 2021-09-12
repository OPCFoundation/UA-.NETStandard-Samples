FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base

COPY ./publish /publish
WORKDIR /publish

ENTRYPOINT ["dotnet", "NetCoreGlobalDiscoveryServer.dll"]
