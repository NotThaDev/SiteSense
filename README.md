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
| Redis | `127.0.0.1:6379` (loopback only) | Hangfire storage, append-only on a volume |

Everything runs on plain HTTP, Keycloak included, so the API reaches it the same way from a container or from `dotnet run`. Details: [`docs/architecture.md`](docs/architecture.md#run-with-docker).

The `sitesense` realm is imported at start: 6 test users (`admin-a`, `safety-a`, `viewer-a`, `admin-b`, `safety-b`, `viewer-b`, password `Password1!`) and the gateways `gateway-a` / `gateway-b`. The issuer is in [`verify.env`](verify.env). Get a token:

```bash
curl -s -d grant_type=password -d client_id=sitesense-verify -d username=admin-a -d 'password=Password1!' http://localhost:8081/realms/sitesense/protocol/openid-connect/token
```

Gateway token (client credentials, scope `positions:write`):

```bash
curl -s -d grant_type=client_credentials -d client_id=gateway-a -d client_secret=gateway-secret-a http://localhost:8081/realms/sitesense/protocol/openid-connect/token
```

Details: [`docs/architecture.md`](docs/architecture.md#identity-realm).

No setup needed: every variable has a default. To override one, copy `.env.example` to `.env`. Full page: [`docs/architecture.md`](docs/architecture.md#run-with-docker).

### Run locally

1. Create your local config from the committed template (`appsettings*.json` files are not in git):

   ```bash
   cp backend/SiteSense.Api/appsettings.example.json backend/SiteSense.Api/appsettings.json
   ```

2. Start Redis and Keycloak (`docker compose up redis keycloak keycloak-init`), then run the API: `dotnet run --project backend/SiteSense.Api`. The store goes to `backend/SiteSense.Api/.store/` (`Store:RootPath`, gitignored).
3. Read the API: the OpenAPI document is at `/openapi/v1.json` and the Scalar UI at `/scalar/v1`.

## Simulator

Console device simulator: moves test devices along the site paths and sends their positions to the API as the gateway.

Run on the host, with `docker compose up` running:

```bash
dotnet run --project simulator/SiteSense.Simulator -- --devices 5 --duration-s 30
```

Run in Docker (plain `docker compose up` does not start it):

```bash
docker compose --profile sim up simulator
```

Setup runs by default: it creates the site, uploads the map and registers the devices with the test admin. `--no-setup` turns it off. Options: [`docs/simulator.md`](docs/simulator.md).

## Admin UI

A small React UI (sites, map upload and Leaflet map view on OpenStreetMap tiles, devices), a separate `ui` container (nginx) that serves the build and proxies the API, so the browser sees one origin. Log in goes through Keycloak.

- Docker: `docker compose up --build`, open <http://localhost:3000/>, log in as `admin-a` / `Password1!` (other test users: see above). After pulling this change, run once `docker compose up -d --force-recreate keycloak` so the realm gets the `sitesense-ui` client.
- Dev mode: `docker compose up -d keycloak`, run the API with `dotnet watch --project backend/SiteSense.Api` (port 5073), then `cd frontend && npm install && npm run dev` and open <http://localhost:5173/>. The Vite proxy targets `http://localhost:5073`; set `API_URL=http://localhost:8080` to use the API in Docker instead.

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

Identity: OIDC / OAuth2 with JWT, issued by Keycloak. Hangfire stores its jobs in Redis through an open-source community storage package (the official Redis storage is commercial). Redis runs with append-only persistence on a volume, so queued and scheduled jobs survive a restart; it never holds domain data. Full rationale: [`ASSUMPTIONS.md`](ASSUMPTIONS.md) (Q08).

**Why Redis for Hangfire.** Hangfire keeps its schedules, queues and retries in Redis, never in the JSON store. Redis writes every command to its append-only file on a volume, so a restart or a recreated container loses no queued or scheduled job, and a failed job is retried after the restart. `Hangfire.InMemory` would lose them on every restart; the official Redis storage needs a commercial licence; a relational database only for jobs is heavier than one Redis container. Periodic checks run every minute per site with the minute as a job argument, so a retry produces no duplicate alert or event (JOB-02). Tests use the in-memory storage, since CI has no Redis. Tests use xUnit, `WebApplicationFactory` and `FakeTimeProvider`.
Routing, filtering and area-assignment algorithms are written in-house, not taken from ready-made libraries.

