# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0.102 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY simulator/SiteSense.Simulator/SiteSense.Simulator.csproj simulator/SiteSense.Simulator/
RUN dotnet restore simulator/SiteSense.Simulator/SiteSense.Simulator.csproj
COPY simulator/SiteSense.Simulator/ simulator/SiteSense.Simulator/
RUN dotnet publish simulator/SiteSense.Simulator/SiteSense.Simulator.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0.2
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "SiteSense.Simulator.dll"]
