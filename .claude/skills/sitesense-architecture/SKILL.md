---
name: sitesense-architecture
description: >-
  Solution structure for SiteSense: layers, allowed dependencies, where each concern goes, fixed folders, and step-by-step recipes to add an endpoint or a store document type. Use when creating a project or folder, deciding where new code goes, adding an endpoint or a store document type, or changing the structure.
---

# SiteSense architecture

> **Status: provisional.** The project list is provisional until the structure review is closed (Q01 is decided). Update this file in the same PR that changes the structure.

Related: [context](../sitesense-context/SKILL.md) · [security](../sitesense-security/SKILL.md) · [coding](../dotnet-coding/SKILL.md) · [testing](../sitesense-testing/SKILL.md) · [AI process](../ai-process/SKILL.md) · [delivery](../delivery-workflow/SKILL.md)

## 1. Process model

**Decided (Q01):** everything runs in one program.

| Item | Rule |
|---|---|
| Store owner | One process owns the document store, through a single store service registered as a singleton. Nothing else reads or writes the files. |
| API process | Hosts ingest, the Hangfire server and SignalR. |
| Worker | Not a separate program. The "worker" (NF-01) is the background processing hosted in the API process: hosted background services for ingest plus the Hangfire server for jobs. Both use the same store singleton. |
| Simulator and demo client | Separate apps. They talk to the API over HTTP and SignalR only. |

## 2. Layers and dependency rules

**Decided (Q66):** two server projects. Domain is a separate project so the compiler keeps it pure. Everything else lives in Api, in the standard type-based folders.

| Layer | Project | Contains | May depend on | Must not depend on |
|---|---|---|---|---|
| Domain | `backend/SiteSense.Domain` | Pure algorithms and geometric primitives only: `Coordinate`, `Polygon`, geometry (point in area, distance), the pedestrian graph and route finder, GPS filters | Nothing: no package and no project reference | ASP.NET, Hangfire, SignalR, ML.NET, the store, persistence, file IO, HTTP, a clock, logging |
| Api | `backend/SiteSense.Api` | Composition root and the Api folders (below) | Domain | Nothing (top layer) |
| Apps | `simulator/SiteSense.Simulator`, demo client | Device simulator, demo client | HTTP and SignalR only | Server internals |
| Tests | `tests/SiteSense.Tests` | All server tests, in `Domain/` and `Api/` subfolders | Domain, Api | Nothing else |

- `SiteSense.Domain.csproj` has no `PackageReference` and no `ProjectReference`. Never add one.
- The domain never reads a clock or does IO. Time arrives as a value from Api. The domain returns results or events. It never publishes anything itself.
- Persistence: Active Record over one generic store (Q67). See "Models and persistence" below.
- Realtime: services publish through a small `IRealtimePublisher` interface in `Api/Realtime/`. Only `Api/Realtime/` touches `IHubContext`.
- Apps may share contract types only if the contract is a separate, dependency-free project.
- Check every new `ProjectReference` against this table.

### Api folders

**Decided (Q66):** the standard type-based ASP.NET Core layout, not feature folders.

| Folder | Holds |
|---|---|
| `Controllers/` | One controller per area, e.g. `SitesController`, `TrackingController`, `NavigationController`, `AuthController` |
| `Models/` | Persisted entities (`Site.cs`, `Device.cs`, ...) with their static persistence methods, and request and response DTOs. Algorithms and geometric primitives stay in `SiteSense.Domain`. |
| `Services/` | Only flows that span several entities or need an external engine: `PositionIngest` (filter, area, realtime), `AnomalyDetector` (ML.NET), navigation and evacuation. No per-entity service. |
| `Store/` | The single generic JSON document store and the static `Context` that holds it, file IO. It takes the place of a `DbContext` (no EF). |
| `Realtime/` | SignalR hub, `IRealtimePublisher` and `SignalRPublisher` |
| `BackgroundJobs/` | Hangfire job classes (see Background jobs) |
| `Auth/` | Authentication and authorization setup and policies |

- No `Helpers/` folder. Shared code goes in Domain, `Store/` or `Realtime/`.
- A folder is created by the first story that puts code in it. A new folder updates this table in the same PR.

### Models and persistence

**Decided (Q67):** Active Record static model methods over a static store context.

- Each persisted entity in `Models/` carries `Id`, `TenantId` and `Version`, like an EF entity.
- It exposes static async methods for its own persistence and single-entity rules, e.g. `Site.CreateAsync(tenantId, site, ct)`, `Site.GetAsync(tenantId, id, ct)`.
- These methods call the single generic store through the static context: `Context.Store.SaveAsync(tenantId, site, ct)`.
- `Context.Store` is a singleton, set once at startup in `Program.cs`. One process (Q01), so HTTP requests, Hangfire jobs and SignalR share the same instance.
- Models use the Domain primitives, e.g. `Site.Boundary` is a Domain `Polygon`.
- Controllers call model methods directly. A flow that spans several entities or needs an external engine goes through a service in `Services/`.
- Tests set `Context.Store` to a store on a temp folder. Rules: [testing](../sitesense-testing/SKILL.md).

Tenant safety:

- Every model persistence method takes `tenantId` as an explicit parameter. It never reads it from an ambient or HTTP context. The controller takes it from the token claim (rules: [security](../sitesense-security/SKILL.md)) and passes it on.
- The store rejects any read or write whose `tenantId` does not match the document.

### Background jobs

- Job class: in `BackgroundJobs/`, named `<What>Job.cs`. Examples: `AnomalyScanJob.cs`, `SilentDeviceJob.cs`, `RetentionJob.cs`.
- The job class is only the trigger. It calls a service in `Services/` or a model method; the logic lives there, not in the job.
- Every recurring schedule is registered in one place, `Program.cs`, one line per job, so all schedules read together.

### Projects

| Project | Kind | References |
|---|---|---|
| `backend/SiteSense.Domain` | Class library | none |
| `backend/SiteSense.Api` | ASP.NET Core web app | Domain |
| `simulator/SiteSense.Simulator` | Console app | none |
| `tests/SiteSense.Tests` | xUnit test project | Domain, Api |

- The demo client gets its own project in its own story.
- Solution file: `SiteSense.slnx` in the repository root.

## 3. Placement table

| Concern | Place |
|---|---|
| Endpoints | `Api/Controllers/`, one controller per area |
| Request and response DTOs | `Api/Models/` |
| Persisted entities and their single-entity rules | `Api/Models/`, static methods (see Models and persistence) |
| Multi-entity flows and external engines (ingest, navigation, evacuation, anomaly detection) | `Api/Services/` |
| Auth wiring, policies | `Api/Auth/`, rules in [security](../sitesense-security/SKILL.md) |
| SignalR hub and publisher | `Api/Realtime/` |
| Hangfire job classes (`<What>Job.cs`, trigger only) | `Api/BackgroundJobs/`; recurring schedules in `Program.cs` (see Background jobs) |
| Geometric primitives, geometry, graph, route finder, GPS filters | Domain |
| Anomaly detectors (ML.NET) | `Api/Services/` |
| Generic JSON document store, static `Context`, file IO | `Api/Store/` |
| Options classes and startup validation | Api, rules in [coding](../dotnet-coding/SKILL.md) |
| Simulator | Separate console app |
| Demo client | Separate app |
| Tests | `tests/SiteSense.Tests/`, in `Domain/` or `Api/` |

## 4. Fixed folders

| Folder | Holds |
|---|---|
| `backend/` | Backend projects: `SiteSense.Domain`, `SiteSense.Api` (see Projects) |
| `simulator/` | Device simulator, a separate app: `SiteSense.Simulator` |
| `tests/` | One xUnit test project: `tests/SiteSense.Tests` |
| `.github/workflows/` | CI: restore, build and test on every PR |
| `data/` | Input only, versioned: site JSON, map GeoJSON, download and convert scripts. The API never writes here. |
| `docs/adr/` | Architecture decision records |
| `docs/analysis/` | Analysis notes |
| `docs/architecture.md` | Solution structure page linked from the README |
| `.claude/skills/` | Skills for AI agents |

- Docs files (ADR, assumptions, known issues, data sources, AI log) and their placement: see [AI process](../ai-process/SKILL.md).
- Root files and their content: see [delivery](../delivery-workflow/SKILL.md).
- Do not add a folder that is not in this table without updating this file.

### Store location (runtime data)

The document store is never inside `data/` and never in git.

| Where | Path | How it is set |
|---|---|---|
| Option | `Store:RootPath` | Read from configuration and validated on start (NF-04). |
| Local run | `.store/` in the repo root | Default in `appsettings.json`; `.store/` is in `.gitignore`. |
| Docker | `/var/lib/sitesense/store` on a named volume | Overridden by the `Store__RootPath` environment variable in `docker-compose.yml`, so data survives restarts (NF-05). |

Inside the root, one subfolder per tenant (JDB-07).

## 5. Host concerns

- **Health checks** and **options validation on start**: required (NF-04). Details in [coding](../dotnet-coding/SKILL.md).
- **Graceful shutdown**: no accepted position is lost on restart (NF-05). Stop accepting, drain queued writes, flush the store, then exit.
- Secrets: none in the repository (NF-04). Rules in [coding](../dotnet-coding/SKILL.md).

## 6. Add an endpoint

1. Add the action to the area's controller in `Api/Controllers/` (a new area gets a new controller).
2. Define request and response records in `Api/Models/`. No entities or Domain types on the wire.
3. Validate the request at the boundary. Rules: [coding](../dotnet-coding/SKILL.md).
4. Add the authorization policy for the route. Rules: [security](../sitesense-security/SKILL.md).
5. Call one model static method (single entity) or one service in `Api/Services/` (multi-entity flow), passing `tenantId` explicitly. No business logic in the controller.
6. Map errors to ProblemDetails. Rules: [coding](../dotnet-coding/SKILL.md).
7. If the endpoint changes a stored document, use the version check (JDB-03). Expose it with `ETag` and `If-Match`. A mismatch returns **412** (JDB-10).
8. If the endpoint writes data, a job, or an alert, make it idempotent: the same input twice gives one result (TRK-01, JOB-02, EVT-02).
9. Add OpenAPI metadata.
10. Add tests: one integration test and one tenant-isolation test. Rules: [testing](../sitesense-testing/SKILL.md).
11. Add an entry to the `.http` file that tries the API.

## 7. Add a store document type

1. Add the entity in `Api/Models/` with `Id`, `Version` and `TenantId`.
2. Add its static persistence methods (`CreateAsync`, `GetAsync`, ...), each taking `tenantId` explicitly and calling `Context.Store` (JDB-01). Do not write per-entity storage code in the store.
3. Writes are atomic: write to a temp file, flush, then rename over the target (JDB-02).
4. Updates check the version. A stale version is rejected (JDB-03).
5. Thread safety inside the process: one lock or immutable snapshots (JDB-04).
6. Add a secondary index if a filtered query needs it. Example: tenant, device, timestamp (JDB-05).
7. Tenant directory: one folder per tenant (JDB-07, SHOULD).
8. Paging: cursor, not offset (JDB-08, SHOULD).
9. Write-heavy data (positions): the storage strategy is in an ADR under `docs/adr/` (JDB-06).
10. Add tests: concurrency and crash recovery. Rules: [testing](../sitesense-testing/SKILL.md).

## 8. Keep this file current

- Same PR: any change to layers, projects, folders or these recipes updates this file.
- Remove the status banner only after the structure review closes.
