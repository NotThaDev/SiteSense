<p align="center">
  <img src="docs/assets/logo.svg" width="112" alt="SiteSense logo">
</p>

<h1 align="center">SiteSense</h1>

<p align="center">
  Multi-tenant application for GPS safety tracking on large outdoor sites.
</p>

## Overview

Companies (tenants) run large outdoor sites such as plants, construction sites and logistics yards.
Workers who opt in carry a GPS device that reports its position at regular intervals.
SiteSense is the backend that turns those positions into safety information:

- **Site maps.** Imports a site map (areas, walking paths, assembly points, entrances) as GeoJSON, validates it and builds the walking-path graph.
- **Position tracking.** Receives position batches from device gateways. It drops duplicates and positions outside the site, reorders late data, filters out implausible jumps and GPS noise, and assigns each device to an area.
- **Pedestrian routing.** Computes the fastest walking route between two points, avoiding restricted and hazardous areas. In an evacuation it routes to the assembly point that is nearest in walking time.
- **Anomaly detection.** Uses ML.NET time-series models to spot unusual crowding in an area.
- **Events and alerts.** Tracks area occupancy, raises alerts (e.g. entering a hazardous area) and pushes them in real time.
- **Isolation and privacy.** Tenants are fully isolated. Devices are pseudonymous, and viewers see only aggregated data.

Domain data lives in a custom document store built on JSON files: atomic writes, optimistic concurrency and secondary indexes, with no database.

## Structure

```text
backend/
  SiteSense.Domain     pure algorithms: geometry, route finder, GPS filters
  SiteSense.Api        host: Controllers/, Models/ (entities with static persistence
                       methods), Dtos/ (request and response records),
                       Services/ (multi-entity flows, ML.NET),
                       Store/ (generic JSON store), Realtime/ (SignalR),
                       BackgroundJobs/ (Hangfire), Auth/,
                       Helpers/ (SiteSenseContext: store and clock)
simulator/
  SiteSense.Simulator  device simulator (console), a separate app
frontend/              demo client (reserved, added in its own story)
tests/
  SiteSense.Tests      all server tests (xUnit)
```

Build and test from the repository root: `dotnet build`, `dotnet test`. Full page: [`docs/architecture.md`](docs/architecture.md).

### Run with Docker

```bash
docker compose up
```

| Service | URL | Notes |
|---|---|---|
| API | `http://localhost:8080` | `GET /health` returns 200 |
| Keycloak | `http://localhost:8081` | admin console, test login `admin` / `admin` |
| Redis | internal only | Hangfire storage, append-only on a volume |

Everything runs on plain HTTP, Keycloak included, so the API reaches it the same way from a container or from `dotnet run`. Details: [`docs/architecture.md`](docs/architecture.md#run-with-docker).

The `sitesense` realm is imported at start: 6 test users (`admin-a`, `safety-a`, `viewer-a`, `admin-b`, `safety-b`, `viewer-b`, password `Password1!`) and the gateways `gateway-a` / `gateway-b`. The issuer is in [`verify.env`](verify.env). Get a token:

```bash
curl -s -d grant_type=password -d client_id=sitesense-verify -d username=admin-a -d 'password=Password1!' http://localhost:8081/realms/sitesense/protocol/openid-connect/token
```

Details: [`docs/architecture.md`](docs/architecture.md#identity-realm).

No setup needed: every variable has a default. To override one, copy `.env.example` to `.env`. Full page: [`docs/architecture.md`](docs/architecture.md#run-with-docker).

### Run locally

1. Create your local config from the committed template (`appsettings*.json` files are not in git):

   ```bash
   cp backend/SiteSense.Api/appsettings.example.json backend/SiteSense.Api/appsettings.json
   ```

2. Run the API: `dotnet run --project backend/SiteSense.Api`. The store goes to `backend/SiteSense.Api/.store/` (`Store:RootPath`, gitignored).
3. Read the API: the OpenAPI document is at `/openapi/v1.json` and the Scalar UI at `/scalar/v1`.

## Tech stack

<p align="center">
  <img src="https://img.shields.io/badge/.NET%2010-161b22?style=for-the-badge&logo=dotnet&logoColor=9780E5" alt=".NET 10">
  <img src="https://img.shields.io/badge/C%23-161b22?style=for-the-badge&logo=dotnet&logoColor=C77DD2" alt="C#">
  <img src="https://img.shields.io/badge/ASP.NET%20Core-161b22?style=for-the-badge&logo=dotnet&logoColor=9780E5" alt="ASP.NET Core">
  <img src="https://img.shields.io/badge/SignalR-161b22?style=for-the-badge&logo=dotnet&logoColor=9780E5" alt="SignalR">
  <img src="https://img.shields.io/badge/ML.NET-161b22?style=for-the-badge&logo=dotnet&logoColor=9780E5" alt="ML.NET">
  <img src="https://img.shields.io/badge/Hangfire-161b22?style=for-the-badge" alt="Hangfire">
  <img src="https://img.shields.io/badge/Redis%20%28Hangfire%20storage%29-161b22?style=for-the-badge&logo=redis&logoColor=FF4438" alt="Redis (Hangfire storage)">
  <img src="https://img.shields.io/badge/NetTopologySuite-161b22?style=for-the-badge" alt="NetTopologySuite">
  <img src="https://img.shields.io/badge/ProjNet-161b22?style=for-the-badge" alt="ProjNet">
  <img src="https://img.shields.io/badge/Keycloak-161b22?style=for-the-badge&logo=keycloak&logoColor=white" alt="Keycloak">
  <img src="https://img.shields.io/badge/Docker%20Compose-161b22?style=for-the-badge&logo=docker&logoColor=2496ED" alt="Docker Compose">
  <img src="https://img.shields.io/badge/JSON%20document%20store-161b22?style=for-the-badge&logo=json&logoColor=F7DF1E" alt="JSON document store">
  <img src="https://img.shields.io/badge/xUnit-161b22?style=for-the-badge" alt="xUnit">
</p>

Identity: OIDC / OAuth2 with JWT, issued by Keycloak. Hangfire stores its jobs in Redis through an open-source community storage package (the official Redis storage is commercial). Redis runs with append-only persistence on a volume, so queued and scheduled jobs survive a restart; it never holds domain data. Full rationale: [`ASSUMPTIONS.md`](ASSUMPTIONS.md) (Q08). Tests use xUnit, `WebApplicationFactory` and `FakeTimeProvider`.
Routing, filtering and area-assignment algorithms are written in-house, not taken from ready-made libraries.

