---
name: sitesense-stack
description: >-
  Stack definition for SiteSense: runtime, packages, versions, forbidden libraries,
  Hangfire storage choice, identity provider and the rule for adding a new dependency.
  Use when choosing a package, adding a NuGet reference, editing a .csproj or
  Directory.Packages.props, touching docker-compose.yml, or asking which library to use.
---

# SiteSense stack

Architecture and folder layout: `../sitesense-architecture/SKILL.md`.
Coding rules: `../dotnet-coding/SKILL.md`. Test tools: `../sitesense-testing/SKILL.md`.

## 1. Stack table

| Area | Choice | Package(s) | Version | Docs |
|---|---|---|---|---|
| Runtime | .NET 10 (LTS), C# | SDK / runtime | .NET 10 (LTS) | https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview |
| Web API | ASP.NET Core, Minimal API or controllers (one style for the whole solution) | ASP.NET Core (framework reference) | pin at scaffold | https://learn.microsoft.com/aspnet/core/ |
| Real time | ASP.NET Core SignalR | framework reference | pin at scaffold | https://learn.microsoft.com/aspnet/core/signalr/introduction |
| Background jobs | Hangfire | `Hangfire.Core`, `Hangfire.AspNetCore` | pin at scaffold | https://docs.hangfire.io/ |
| Hangfire storage | **Proposed (Q08, open):** `Hangfire.InMemory` (see §4) | `Hangfire.InMemory` | pin at scaffold | https://docs.hangfire.io/ |
| Anomaly detection (AI) | ML.NET | `Microsoft.ML`, `Microsoft.ML.TimeSeries` | pin at scaffold | https://learn.microsoft.com/dotnet/machine-learning/tutorials/phone-calls-anomaly-detection |
| Geometry primitives | NetTopologySuite | `NetTopologySuite` | pin at scaffold | https://github.com/NetTopologySuite/NetTopologySuite |
| Projections | ProjNet | `ProjNet` (GeoAPI port) | pin at scaffold | https://github.com/NetTopologySuite/ProjNet4GeoAPI |
| Identity provider | **Proposed (Q03, open):** Keycloak in a container | Keycloak image | pin at scaffold (image tag) | https://www.keycloak.org/documentation |
| Containers | Docker Compose | `docker-compose.yml` + Dockerfiles | pin at scaffold | https://docs.docker.com/compose/ |
| Tests | xUnit, time testing, HTTP integration | `xunit`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.Mvc.Testing` | pin at scaffold | see `../sitesense-testing/SKILL.md` |

Rules for this table:

- Only `.NET 10 (LTS)` is a fixed version here. Every other row reads `pin at scaffold` until the solution is created.
- When the solution is scaffolded, replace `pin at scaffold` with the exact version and update this file in the same PR.
- A row is not a licence to add the package. Add it only when a feature needs it.

## 2. Version policy

| Rule | Status |
|---|---|
| Exact versions only. No floating versions (`*`, `1.*`) and no open ranges. | Rule |
| Versions live in one place: central package management in `Directory.Packages.props`. | **Proposed:** central package management |
| Keycloak runs from an image pinned by tag. Never `latest`. | **Proposed:** pinned tag |
| Upgrading a package is its own PR with a short note on what changed. | Rule |

## 3. Forbidden

| Forbidden | Why | Instead |
|---|---|---|
| Any database for domain data (SQL, NoSQL, LiteDB, SQLite, EF Core, and similar) | Domain data lives only in the JSON store. | Document store in `sitesense-architecture` (JSON files). |
| Ready-made routing or graph libraries (for example QuickGraph or routing engines) | We write the algorithms. | Our own graph and Dijkstra or A* code. |
| Ready-made map-matching, Kalman or filter libraries | We write the algorithms. | Our own snapping, filters and hysteresis. |
| NetTopologySuite graph or routing features | NTS is for primitives only. | `intersects`, `distance`, `contains` and similar. |
| Any ML library other than ML.NET for anomaly detection | ML.NET is required. | `Microsoft.ML` and `Microsoft.ML.TimeSeries`. |

**Proposed:** the NTS reading above (primitives only, no graph or routing features). Accept or change.

Exception: Hangfire's own storage may use a database if §4 chooses one. It never holds domain data.

## 4. Hangfire storage

The storage choice is open. It must be justified in the README.

**Proposed (Q08, open):** `Hangfire.InMemory`.

| Point | Reason |
|---|---|
| Recurring jobs | Re-registered at startup. The schedule lives in code, not in storage. |
| Lost runs | Jobs are idempotent (JOB-02), so a run lost on restart is harmless. |
| Extra containers | None. |
| Alternative | SQLite on a Docker volume, if jobs must survive restarts. Requires a README note. |

## 5. Identity provider

**Proposed (Q03, open):** Keycloak in a container.

- The realm export is loaded at startup (IDN-04). It lives in the repo.
- Allowed alternative: OpenIddict inside the API. Switching is an ADR, not a silent change.
- Roles, clients and token claims: `../sitesense-security/SKILL.md`.

## 6. New dependency rule

Every new package needs all of the following:

1. Justify it under `Dependencies:` in What it touches (see `../clear-description/SKILL.md`):
   - what it replaces;
   - why the BCL or an existing package does not cover it;
   - its licence.
2. The row in §1 of this file, updated in the same PR. A new package with no row is not merged.
3. A version pinned as in §2.
4. No package from the forbidden list in §3.

If the dependency is a new area (not a new package in an existing area), record the decision as an ADR: `../ai-process/SKILL.md`.

## 7. Docker Compose

- `docker-compose.yml` and the Dockerfiles live in the repo root (layout: `../sitesense-architecture/SKILL.md`).
- `.env.example` lists every variable the compose file reads.
- Images are pinned by tag (§2).

## 8. Checklist for a stack change

- [ ] §1 row updated, or no row needed because the package is in an existing area
- [ ] Version pinned, not floating
- [ ] Not in §3
- [ ] Justified under `Dependencies:` in What it touches (see `../clear-description/SKILL.md`)
- [ ] Hangfire or identity change: the README note and the ADR are updated (`../ai-process/SKILL.md`)
