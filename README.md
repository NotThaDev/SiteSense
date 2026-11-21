<p align="center">
  <img src="docs/assets/logo.svg" width="112" alt="SiteSense logo">
</p>

<h1 align="center">SiteSense</h1>

<p align="center">
  Multi-tenant backend for GPS safety tracking on large outdoor sites.
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

## Tech stack

| Area | Technology |
|---|---|
| Runtime | .NET 10 (LTS), C#, ASP.NET Core |
| Real time | ASP.NET Core SignalR |
| Background jobs | Hangfire |
| AI | ML.NET (`Microsoft.ML`, `Microsoft.ML.TimeSeries`) |
| Geometry | NetTopologySuite, ProjNet |
| Identity | OIDC / OAuth2 with JWT; Keycloak in a container (proposed, Q03) |
| Storage | Custom JSON document store on the file system |
| Infrastructure | Docker Compose |
| Tests | xUnit, `WebApplicationFactory`, `FakeTimeProvider` |

Routing, filtering and area-assignment algorithms are written in-house, not taken from ready-made libraries.

## Status

Work in progress. The first step sets up the AI-assisted process: see [`CLAUDE.md`](CLAUDE.md) and the skills in [`.claude/skills/`](.claude/skills/).
Run instructions, test credentials and benchmark results will be added here as the code lands.
