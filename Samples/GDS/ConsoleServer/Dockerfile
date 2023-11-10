FROM mcr.microsoft.com/dotnet/sdk:6.0-bookworm-slim AS base

ADD . /gds/src
WORKDIR /gds

RUN dotnet publish src/NetCoreGlobalDiscoveryServer.csproj -p:AppTargetFramework=net6.0 -o /gds

RUN cp /gds/src/entrypoint.sh /gds/entrypoint.sh

ENTRYPOINT ["bash", "entrypoint.sh"]