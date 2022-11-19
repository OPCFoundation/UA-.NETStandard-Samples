#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["ConsoleAggregationServer/ConsoleAggregationServer.csproj", "ConsoleAggregationServer/"]
RUN dotnet restore "ConsoleAggregationServer/ConsoleAggregationServer.csproj"
COPY . .
WORKDIR "/src/ConsoleAggregationServer"
RUN dotnet build "ConsoleAggregationServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ConsoleAggregationServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 62530
ENTRYPOINT ["dotnet", "ConsoleAggregationServer.dll"]
