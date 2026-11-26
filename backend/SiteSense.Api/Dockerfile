# Build context: repository root.
# Build the admin UI first; its output becomes the API's wwwroot.
FROM node:22.21.1-alpine AS ui
WORKDIR /ui
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.102 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY backend/SiteSense.Domain/SiteSense.Domain.csproj backend/SiteSense.Domain/
COPY backend/SiteSense.Api/SiteSense.Api.csproj backend/SiteSense.Api/
RUN dotnet restore backend/SiteSense.Api/SiteSense.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/SiteSense.Api/SiteSense.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.2
WORKDIR /app
# Pre-create the store root so a new named volume inherits the non-root owner.
RUN mkdir -p /var/lib/sitesense/store && chown $APP_UID:$APP_UID /var/lib/sitesense/store
COPY --from=build /app .
# Static files the API serves at / (SPA).
COPY --from=ui /ui/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SiteSense.Api.dll"]
