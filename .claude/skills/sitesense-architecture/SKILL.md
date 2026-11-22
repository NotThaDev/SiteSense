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

**Decided (Q66):** two server projects. Domain is a separate project so the compiler keeps it pure. Everything else lives in Api, by feature folder.

| Layer | Project | Contains | May depend on | Must not depend on |
|---|---|---|---|---|
| Domain | `backend/SiteSense.Domain` | Core types, geometry, pedestrian graph and routing, GPS filters, area assignment, area and alarm rules | Nothing: no package and no project reference | ASP.NET, Hangfire, SignalR, ML.NET, the store, file IO, HTTP, a clock, logging |
| Api | `backend/SiteSense.Api` | Composition root and feature folders (below) | Domain | Nothing (top layer) |
| Apps | `simulator/SiteSense.Simulator`, demo client | Device simulator, demo client | HTTP and SignalR only | Server internals |
| Tests | `tests/SiteSense.Tests` | All server tests, in `Domain/` and `Api/` subfolders | Domain, Api | Nothing else |

- `SiteSense.Domain.csproj` has no `PackageReference` and no `ProjectReference`. Never add one.
- The domain never reads a clock or does IO. Time arrives as a value from Api. The domain returns results or events. It never publishes anything itself.
- Storage is behind interfaces (`IDocumentStore<T>`), defined and implemented in `Api/Store/`.
- Realtime: features publish through a small `IRealtimePublisher` interface in `Api/Realtime/`. Only `Api/Realtime/` touches `IHubContext`.
- Apps may share contract types only if the contract is a separate, dependency-free project.
- Check every new `ProjectReference` against this table.

### Api feature folders

| Folder | Holds |
|---|---|
| `Store/` | JSON document store: `IDocumentStore<T>`, its implementation, file IO |
| `Auth/` | Authentication wiring and authorization policies |
| `Sites/` | Sites and map import: endpoints and services |
| `Tracking/` | Position ingest, tracks, area occupancy: endpoints and services |
| `Navigation/` | Routing and evacuation endpoints; they call the graph and routing code in Domain |
| `Anomalies/` | ML.NET anomaly detectors behind an interface |
| `Realtime/` | SignalR hub, `IRealtimePublisher` and its implementation |

- A folder is created by the first story that puts code in it. A new feature folder updates this table in the same PR.

### Background jobs

**Decided (Q66):** no `Jobs/` folder. Each Hangfire job lives in the feature it serves.

- Job class: in the feature folder, named `<What>Job.cs`. Examples: `Anomalies/AnomalyScanJob.cs`, `Tracking/SilentDeviceJob.cs`, `Tracking/RetentionJob.cs`.
- The job class is only the trigger. It calls the feature's own service; the logic lives in that service, not in the job.
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
| Endpoints, request and response DTOs | Api, in the feature folder |
| Use-case services (ingest, navigation, alerts, tracking) | Api, in the feature folder |
| Auth wiring, policies | `Api/Auth/`, rules in [security](../sitesense-security/SKILL.md) |
| SignalR hub and publisher | `Api/Realtime/` |
| Hangfire job classes (`<What>Job.cs`, trigger only) | The feature folder they serve; recurring schedules in `Program.cs` (see Background jobs) |
| Geometry, graph, routing, filters, area assignment | Domain |
| Business rules and state rules | Domain |
| Anomaly detectors (ML.NET) | `Api/Anomalies/`, behind an interface |
| JSON document store, file IO, store interfaces | `Api/Store/` |
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

1. Define request and response records. No domain types on the wire.
2. Validate the request at the boundary. Rules: [coding](../dotnet-coding/SKILL.md).
3. Add the authorization policy for the route. Rules: [security](../sitesense-security/SKILL.md).
4. Call one application service. No business logic in the endpoint.
5. Map errors to ProblemDetails. Rules: [coding](../dotnet-coding/SKILL.md).
6. If the endpoint changes a stored document, use the version check (JDB-03). Expose it with `ETag` and `If-Match`. A mismatch returns **412** (JDB-10).
7. If the endpoint writes data, a job, or an alert, make it idempotent: the same input twice gives one result (TRK-01, JOB-02, EVT-02).
8. Add OpenAPI metadata.
9. Add tests: one integration test and one tenant-isolation test. Rules: [testing](../sitesense-testing/SKILL.md).
10. Add an entry to the `.http` file that tries the API.

## 7. Add a store document type

1. Define a record with `Id`, `Version` and `TenantId`.
2. Register the collection with the generic store (JDB-01). Do not write per-entity storage code.
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
