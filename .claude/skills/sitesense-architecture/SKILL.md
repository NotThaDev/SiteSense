---
name: sitesense-architecture
description: >-
  Solution structure for SiteSense: layers, allowed dependencies, where each concern goes, fixed folders, and step-by-step recipes to add an endpoint or a store document type. Use when creating a project or folder, deciding where new code goes, adding an endpoint or a store document type, or changing the structure.
---

# SiteSense architecture

> **Status: provisional.** The project list is provisional until the structure review is closed (Q01 is decided). Update this file in the same PR that changes the structure.

Related: [context](../sitesense-context/SKILL.md) · [security](../sitesense-security/SKILL.md) · [stack](../sitesense-stack/SKILL.md) · [coding](../dotnet-coding/SKILL.md) · [testing](../sitesense-testing/SKILL.md) · [AI process](../ai-process/SKILL.md) · [delivery](../delivery-workflow/SKILL.md)

## 1. Process model

**Decided (Q01):** everything runs in one program.

| Item | Rule |
|---|---|
| Store owner | One process owns the document store, through a single store service registered as a singleton. Nothing else reads or writes the files. |
| API process | Hosts ingest, the Hangfire server and SignalR. |
| Worker | Not a separate program. The "worker" (NF-01) is the background processing hosted in the API process: hosted background services for ingest plus the Hangfire server for jobs. Both use the same store singleton. |
| Simulator and demo client | Separate apps. They talk to the API over HTTP and SignalR only. |

## 2. Layers and dependency rules

| Layer | Contains | May depend on | Must not depend on |
|---|---|---|---|
| Domain | Entities, geometry, graph and routing, filters, area assignment, rules | Nothing outside the domain | ASP.NET, Hangfire, SignalR, the store, file IO |
| Application (core) | Use-case services, store and detector interfaces | Domain | Infrastructure, host |
| Infrastructure | JSON document store, file IO, ML.NET detector implementations | Application, Domain | Host |
| Host (API) | Composition root: DI, auth, endpoints, SignalR hub, Hangfire registration, options | All layers | Nothing (top layer) |
| Apps | Simulator, demo client | HTTP and SignalR only | Server internals |
| Tests | Tests for each project | The project under test | Nothing else |

- Dependencies point inward. Check every new `ProjectReference` against this table.
- Storage is behind interfaces (`IDocumentStore<T>`). Core defines them. Infrastructure implements them.
- Apps may share contract types only if the contract is a separate, dependency-free project.
- Tests mirror the project they test: `tests/<Project>.Tests/`.

## 3. Placement table

| Concern | Layer |
|---|---|
| Endpoints, request and response DTOs | Host (API) |
| Auth wiring, policies | Host (API), rules in [security](../sitesense-security/SKILL.md) |
| SignalR hub and publisher | Host (API) |
| Hangfire job registration and thin job wrappers | Host; each wrapper calls an application service |
| Use-case services (ingest, navigation, alerts, tracking) | Application (core) |
| Geometry, graph, routing, filters, area assignment | Domain |
| Business rules and state rules | Domain |
| Anomaly detectors (ML.NET) | Own module behind an interface in Infrastructure |
| JSON document store, file IO | Infrastructure |
| Store and detector interfaces | Application (core) |
| Options classes and startup validation | Host (API), rules in [coding](../dotnet-coding/SKILL.md) |
| Simulator | Separate console app |
| Demo client | Separate app |
| Tests | `tests/`, mirroring the project |

## 4. Fixed folders

| Folder | Holds |
|---|---|
| `data/` | Site JSON, map GeoJSON, download and convert scripts |
| `docs/adr/` | Architecture decision records |
| `docs/analysis/` | Analysis notes |
| `.claude/skills/` | Skills for AI agents |

- Docs files (ADR, assumptions, known issues, data sources, AI log) and their placement: see [AI process](../ai-process/SKILL.md).
- Root files and their content: see [delivery](../delivery-workflow/SKILL.md).
- Do not add a folder that is not in this table without updating this file.

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
