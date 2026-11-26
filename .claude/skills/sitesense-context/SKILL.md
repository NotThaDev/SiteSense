---
name: sitesense-context
description: >-
  SiteSense problem, domain glossary, data flow, data formats, API contract summary,
  units, priorities and evaluation criteria. Use when starting any SiteSense task, when
  a term or field name is unclear, or before designing an endpoint, a map import, a
  position pipeline or an event.
---

# SiteSense context

## Problem in brief

Companies (tenants) run large outdoor sites. Opted-in workers carry GPS devices.
The backend must:

1. import the site map (areas, walking paths, assembly points, entrances);
2. receive GPS positions, filter noise, place them on the map;
3. route pedestrians, including evacuation to the nearest assembly point;
4. detect anomalies with ML.NET (crowding, sudden immobility);
5. raise and push events in real time;
6. isolate tenants and protect worker privacy.

## Data flow

| Step | What happens | Requirements |
|---|---|---|
| 1 | Admin creates a site and uploads a GeoJSON map. Map is validated, converted to a local metric projection (MAP-02), and a path graph is built. | MAP-01, MAP-02, MAP-03 |
| 2 | Gateway client (client credentials) posts position batches. | IDN-05, API-02 |
| 3 | Ingest: dedupe by device + timestamp, order by timestamp, drop positions outside the perimeter, drop implausible jumps, smooth noise. | TRK-01, TRK-02, TRK-03, PRV-02 |
| 4 | Tracking: assign areas with hysteresis at borders, update occupancy, raise intrusion alerts. | TRK-04, EVT-01, EVT-02 |
| 5 | Background jobs (Hangfire), e.g. every minute per site: anomaly detection, silent devices, retention. Jobs are idempotent. | AI-01, AI-02, JOB-01, JOB-02, JOB-04 |
| 6 | Persist to the JSON store first, then emit SignalR events with a per-tenant sequence number. Clients catch up via `GET /api/v1/events?after={seq}`. | RTM-01, RTM-03, RTM-05 |

**Rule (RTM-03):** events are emitted only after the state is persisted.
Never emit an event for state that is not yet saved.

## Glossary

| Term | Meaning |
|---|---|
| Tenant | A client company. All data belongs to one tenant. |
| Site | An outdoor site of a tenant. Has one map and navigation settings. |
| SiteMap | The site map: areas, paths, assembly points, entrances. Imported as GeoJSON. |
| Area | A polygon of the map. Has `areaType` (below). |
| Path | A walkable line between vertices. Has surface, steps, slope, width. |
| AssemblyPoint | A point on a path where people gather in an evacuation. Has capacity. |
| Entrance | A point on a path where people enter the site. |
| Device | A pseudonymous GPS device, assigned to a worker. Has no personal data. |
| Position | One GPS fix from a device: time, lat/lon, accuracy. |
| Track | The filtered sequence of one device's positions, placed on the map. |
| Alert | A relevant event to notify. Types below. |
| Event | A recorded notification with its per-tenant sequence number. |

Area `areaType` values: `perimeter`, `work`, `rest`, `parking`, `restricted`, `hazard`.

Alert `type` values: `AreaIntrusion`, `ZoneAnomaly`, `SuddenImmobility`, `DeviceSilent`.
Some types depend on requirements that may not be implemented; they may be absent.

## Formats

Summary:

| Format | Key rules |
|---|---|
| Site JSON | Fields: `siteId`, `name`, `timeZone` (IANA), `mapFile`, `navigation` with `walkingSpeedMps`, `unpavedSpeedFactor`, `stepsSpeedFactor`, `connectToleranceM`, `maxSnapDistanceM`. |
| Map GeoJSON | RFC 7946 `FeatureCollection`, WGS84, order `[lon, lat]`. Element type in property `kind`: `area` (Polygon), `path` (LineString), `assemblyPoint` (Point), `entrance` (Point). Required props: `area`: `id`, `name`, `areaType` (`perimeter`, `work`, `rest`, `parking`, `restricted`, `hazard`), optional `capacity`; `path`: `id`, `surface` (`paved`, `unpaved`), `steps` (bool), `slopePercent`, `widthM`; `assemblyPoint`: `id`, `name`, `capacity`; `entrance`: `id`, `name`. |
| Map guarantees | Test maps guarantee: one perimeter containing all areas, no overlapping non-perimeter areas, valid geometry. Do not spend effort re-checking these. |
| Map checks we must do | Required props present; perimeter present and single; assembly points lie on a path (entrances: guaranteed by the input maps). Errors go back as 422 (see contract). |
| Path rule | A segment (between two consecutive vertices) that crosses a `restricted` or `hazard` area is not walkable. |
| Position batch | `deviceId`, `positions[{timestampMs, lat, lon, accuracyM}]`. Unordered, duplicates possible, up to 5000 items, may be hours old. Timestamp decides order. |

## Contract

All endpoints need a Bearer token. Errors are ProblemDetails. Who may call which endpoint: see `../sitesense-security/SKILL.md`.

| Method + path | Success | Error shape |
|---|---|---|
| `POST /api/v1/sites` (site JSON) | 201 `{ "siteId" }` | ProblemDetails |
| `PUT /api/v1/sites/{siteId}/map` (GeoJSON) | 200 `{ "mapVersion" }` | 422 with `errors: [{ "elementId", "reason" }]` |
| `POST /api/v1/sites/{siteId}/devices` `{ "deviceId" }` | 201 `{ "deviceId", "siteId" }` | ProblemDetails; 409 if the id is already registered in the tenant (Q13) |
| `GET /api/v1/sites/{siteId}/devices/{deviceId}` (extension, not in the contract) | 200 `{ "deviceId", "siteId" }` | ProblemDetails |
| `DELETE /api/v1/sites/{siteId}/devices/{deviceId}` + `If-Match` (extension, not in the contract) | 204 | ProblemDetails; 412 on a missing or stale `If-Match` |
| `POST /api/v1/positions` (batch) | 202 | ProblemDetails |
| `POST /api/v1/sites/{siteId}/routes` `{ "from", "to" }` | 200 `{ "geometry", "distanceM", "durationS" }` (`geometry` = GeoJSON LineString) | 422 with `reason`: `UNREACHABLE` or `START_TOO_FAR` |
| `POST /api/v1/sites/{siteId}/evacuation-route` `{ "from" }` | 200 as routes, plus `assemblyPointId` | 422 with `reason` as routes |
| `GET /api/v1/sites/{siteId}/occupancy` | 200 `{ "atMs", "areas": [{ "areaId", "count" }] }` | ProblemDetails |
| `GET /api/v1/devices/{deviceId}/track?fromMs=&toMs=` | 200 `{ "points": [{ "timestampMs", "lat", "lon", "areaId" }] }`, filtered, sorted by time | ProblemDetails |
| `GET /api/v1/sites/{siteId}/alerts?fromMs=&toMs=&type=` | 200 `{ "items": [{ "alertId", "type", "eventTimeMs", "deviceId", "areaId", "late" }] }` | ProblemDetails |
| `PUT /api/v1/test/clock` `{ "nowMs" }` | Sets simulated time. Success code not defined (2xx). | 404 when test mode is off |
| `POST /api/v1/test/jobs/{jobName}/run` | Runs the job now, responds when done. `jobName`: `anomaly-detection`, `silent-devices`, `retention`. Success code not defined (2xx). | 404 when test mode is off |
| `POST /api/v1/test/drain` | Responds when all positions received so far are processed (filters, areas, events). Success code not defined (2xx). | 404 when test mode is off |

Test mode is on only when `SITESENSE_TEST_MODE=true`.

**Occupancy rule (5 minutes):** a device counts in an area if its last valid
position is in that area and is less than 5 minutes before the simulated current time.
Occupancy is computed at the simulated time, not at wall-clock time.

**Contract rules**
- Field names are exact. Extra fields are allowed.
- Coordinates in request and response bodies are `{ "lat", "lon" }` objects.
- Map files use `[lon, lat]` (GeoJSON). Do not mix the two orders.

## Units

| Quantity | Unit |
|---|---|
| Distance, tolerance, accuracy | metres (m) |
| Speed | metres per second (m/s) |
| Time instants | milliseconds since Unix epoch, UTC (`long`) |
| Geometry math | local metric projection (MAP-02) |

All time-dependent logic uses the injected clock, so the test clock drives it.
How to inject it: `../dotnet-coding/SKILL.md`.

## Priorities and evaluation

- Priorities: **MUST** first. **SHOULD** and **BONUS** only when MUST is solid.
- Evaluation gives more weight to:
  - routing and tracking on hard cases (edge cases, border flapping, out-of-order data);
  - anomaly detection: effectiveness and written rationale;
  - correctness under stress: concurrency, crash, duplicate and late data;
  - design quality and rationale;
  - exact formats and contract, checked with test data.
- A small, solid, well-argued subset beats a broad, fragile one. Quantity of features counts least.

## Requirement ID prefixes

| Prefix | Area |
|---|---|
| IDN | Identity and multi-tenancy |
| JDB | JSON document store |
| MAP | Site map import and geometry |
| NAV | Navigation and routing |
| TRK | Position reception and tracking |
| AI | Anomaly detection with ML.NET |
| EVT | Events and alerts |
| PRV | Privacy |
| JOB | Background jobs |
| RTM | Real-time SignalR |
| SIM | Device simulator |
| API | HTTP API |
| NF | Non-functional |

Cite requirement IDs in PR descriptions and commit bodies.

## Decisions and assumptions

- Open points: `ASSUMPTIONS.md` (repo root).
- Decisions: ADRs in `docs/adr/`.
- Rules for writing them: `../ai-process/SKILL.md`.
- Do not copy their content here. Link only.
