# AI log

Significant AI use on SiteSense. Entry format and rules: `.claude/skills/ai-process/SKILL.md`.

## 2026-11-22 — SS-1: Docker compose and health check
- Goal: start API, Keycloak and Redis with `docker compose up`, validate options at start, expose `/health`.
- Prompt (summary): implement issue SS-1 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `StoreOptions` and validator, `Program.cs` wiring, tests, Dockerfile, `docker-compose.yml`, `.env.example`, docs (Haiku and Opus).
- Corrected or discarded: the review added a non-root owner for the store volume, an API health check in compose, a unique write probe and a longer Keycloak start period. The owner chose Q70–Q72 (admin defaults, liveness-only health, no compose job in CI).

## 2026-11-24 — SS-9: Generic store with atomic writes
- Goal: one generic, crash-safe document store with a lock per document and one folder per tenant.
- Prompt (summary): implement SS-9 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `IDocument`, `IDocumentStore`, `FileDocumentStore`, `SiteSenseContext`, crash writer, unit and crash tests (Haiku), docs (Opus).
- Corrected or discarded: the review added reading the stored version under the lock, `\z` in the name check, and a separate crash-writer class to avoid a `Program` clash. The owner chose Q04 (one file per document, no history) and Q75 (two persistence interfaces) and Q76 (case-sensitive ids, uppercase encoded in file names, after the owner flagged the macOS collision).

## 2026-11-24 — SS-10: Optimistic concurrency and ETag
- Goal: reject a save based on a stale version; expose it as ETag / If-Match with 412.
- Prompt (summary): implement SS-10 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), version check, 412 mapping, probe controller and tests (Haiku), docs (Opus).
- Corrected or discarded: the Sonnet review grew the plan (428/400 paths, ETag helper, folder choice); the owner cut it to the minimal version ("check the version before saving, if it differs return an error"). The owner chose Q45 (ETag on every document, If-Match required, expected version from `doc.Version`).

## 2026-11-25 — SS-43: Tenants from configuration
- Goal: configured tenants with per-tenant retention and silence threshold, validated at startup; unknown `tenant_id` → 403 after SS-6.
- Prompt (summary): implement SS-43 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), tenant options, validator, `SiteSenseContext.Tenants`, config and tests (Haiku), docs (Opus).
- Corrected or discarded: the review moved tenant validation before the store is created and fixed test names. The owner chose Q14 (configuration only, 30 days / 10 minutes) and discarded a single seed file with a realm generator: tenant ids stay in config and realm, kept in sync by hand.

## 2026-11-25 — SS-6: JWT validation and tenant context
- Goal: validate Keycloak tokens, take the tenant from `tenant_id`, map realm roles, return 404 for another tenant's resource.
- Prompt (summary): implement SS-6 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), auth setup, `/api/v1/me`, signed-token tests (Haiku), docs (Opus).
- Corrected or discarded: the Sonnet review fixed the Authority vs MetadataAddress branch, `Configure` instead of `PostConfigure` for the test key, a 30 s clock skew and the `tenant_id` format check. The owner removed non-risks from the plan, asked how keys are cached (handler default, no extra code) and chose Q77–Q80.

## 2026-11-25 — SS-18: Real site map from OpenStreetMap
- Goal: a real site map in the SiteSense map format, rebuilt by a reproducible script, as the real-world input for map import (SS-15).
- Prompt (summary): implement SS-18 with the plan / validate / approve / implement / verify workflow; the owner picks the site.
- AI produced: candidate sites from OSM counts (Opus), plan (Opus), plan review (Sonnet), `data/build_map.py`, extract, `map.geojson`, `site.json`, `data/README.md`, `DATA_SOURCES.md` (Sonnet), independent output check (Haiku).
- Corrected or discarded: the review added the walkable-graph connectivity check, snapping assembly points only to walkable segments, T-touch and collinear joins, no joins across layers or bridges, and rounding before any geometry. The owner chose Catania (Q50), Python, a saved extract and no CI check.

## 2026-11-25 — SS-7: Authorization policies
- Goal: named policies `CanManage`, `CanSeeIndividuals`, `CanSeeAggregates`, `CanWritePositions`; no role checks in endpoint code.
- Prompt (summary): implement SS-7 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), policy registration, probe controller and per-role tests (Haiku), docs (Opus).
- Corrected or discarded: the Sonnet review added an explicit authenticated requirement, exact scope matching with look-alike and array tests, 401 rows, and a source scan that fails when it finds no files. An endpoint-metadata test was not taken (fallback policy already blocks anonymous callers).

## 2026-11-25 — SS-14: Sites API
- Goal: `POST /api/v1/sites` with the site JSON (201, Admin; 409 on a duplicate `siteId`), `PUT /api/v1/sites/{siteId}` with `If-Match` (200 / 412), 404 for another tenant's site, `GET /api/v1/sites/{siteId}` with ETag.
- Prompt (summary): implement SS-14 with the plan / validate / approve / implement / verify workflow, stacked on SS-7 and SS-43.
- AI produced: plan (Opus), plan review (Sonnet), `Site` model, DTOs, `SitesController`, `.http` file and tests (Haiku), docs (Opus).
- Corrected or discarded: the Sonnet review added the invalid-id guard (the store throws on bad names → 500), action names without `Async` for `CreatedAtAction`, the shared test collection with unique site ids, and more 400 rows. The owner chose Q81 (GET for all roles) and Q82 (full validation), and settled Q43 after one iteration: POST creates (409), PUT updates with the version check.

## 2026-11-25 — SS-15: Map validation and saving (domain)
- Goal: read a GeoJSON site map, list every problem as `{ elementId, reason }`, save a valid map with a `mapVersion`.
- Prompt (summary): implement SS-15 with the plan / validate / approve / implement / verify workflow, domain PR first.
- AI produced: plan (Opus), plan review (Sonnet), `PlanarGeometry`, `SiteMap`, `MapValidator`, tests (Haiku), docs (Opus).
- Corrected or discarded: the review added read guards for coordinates and Features (a bad number would have crashed the projection), the version rule, stable error order and inline test maps. The owner chose Q17 (forward only), Q40 (`null` / `features[i]`) and Q41 (extended checks).

## 2026-11-26 — SS-8: Gateway authentication
- Goal: only gateway tokens (scope `positions:write`) may call `POST /api/v1/positions`; user tokens get 403.
- Prompt (summary): implement SS-8 with the plan / validate / approve / implement / verify workflow, reusing the SS-7 policy.
- AI produced: plan (Opus), plan review (Sonnet), guarded 501 route and tests (Haiku), docs (Opus).
- Corrected or discarded: the review added ProblemDetails asserts, the tenant-b and role+scope cases, and moved tenant checks to SS-22. The owner chose Q12 (202, drop and count), a 501 stub instead of a test-only probe, and kept ingestion out of this story.

## 2026-11-26 — SS-2: Test mode and simulated clock
- Goal: `SITESENSE_TEST_MODE` gates `/api/v1/test/*` (404 when off, Admin when on); `PUT /api/v1/test/clock` sets the clock read through `SiteSenseContext.Time`.
- Prompt (summary): implement SS-2 with the plan / validate / approve / implement / verify workflow; drain and jobs endpoints are later stories.
- AI produced: plan (Opus), plan review (Sonnet), gate middleware, `TestMode` policy, `FrozenTimeProvider`, `TestController`, tests and docs (Opus; small change, written directly).
- Corrected or discarded: the Sonnet review moved the flag from a static to a local (each test host re-runs `Program.cs`), asked for segment matching (no match on `/api/v1/testing`), a `[Range]` DTO, a flag pinned off in the test factory and a startup warning. Hiding the endpoint from OpenAPI when off was not taken. The owner chose Q38 (global, frozen) and 404 for every caller when off.

## 2026-11-26 — SS-44: Device registration
- Goal: let an Admin register the devices of a site, so that batch ingest (SS-22) can find each device's site from `tenant + deviceId`.
- Prompt (summary): implement SS-44 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `Device` model, DTOs, `DevicesController`, docs (Haiku), tests (Sonnet).
- Corrected or discarded: the review found that `KNOWN_ISSUES.md` did not exist yet, that the PRV-01 test needed the test store root, and that the GET policy options must map to existing policies. The owner chose Q13 (unique per tenant, because devices send no `siteId`), 409 on any reuse, a GET endpoint, and Q85 (`CanManage`).

## 2026-11-26 — SS-12: Position log
- Goal: an append-only log for positions with no lost acknowledged record and fast one-hour reads, next to the document store.
- Prompt (summary): plan SS-12 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `FileAppendLog`, `Position`, tests (Haiku), ADR 0001, docs (Opus).
- Corrected or discarded: the review made reads use newline-terminated lines only, added input checks and load tests. The owner changed day files to hour files and chose one lock per device log.

## 2026-11-26 — SS-22: Batch ingest
- Goal: idempotent, durable `POST /api/v1/positions`.
- Prompt (summary): plan per the planning skill, Sonnet validation, Haiku implementation in two stacked PRs.
- AI produced: plan `docs/plans/SS-22-batch-ingest.md`, `PlanarGeometry.Locate`, `FileAppendLog.AppendNewAsync`, `PositionIngest` service, DTOs, controller, tests (dedupe, torn line, concurrency, crash and resend, perimeter, foreign device, 5,000 batch).
- Corrected or discarded: the validator found that a torn-but-parsable last line and the `501` tests were missed; the first `AppendNewAsync` truncated the torn tail, replaced so a whole record without its newline counts as stored and gets its newline (keeps Q83); the point-in-polygon helper was unified with SS-17 by the owner's decision (one `Locate` returning `OnBoundary`, each caller decides).

## 2026-11-26 — SS-17: Route graph
- Goal: build an in-memory pedestrian graph from the site map, in metres, ready for routing (SS-19, SS-20, SS-21).
- Prompt (summary): implement SS-17 with the plan / validate / approve / implement / verify workflow; no routing in this story.
- AI produced: plan and ADR 0001 (Opus), plan review (Sonnet), Domain graph builder, segment/polygon test, map adapter and tests (Haiku).
- Corrected or discarded: the Sonnet review asked for an exact segment/polygon rule, strict tolerance, no self-loops, connector edge cases and the ADR file name fixed by the ai-process skill. The point-in-polygon test is shared with SS-22 (one `PlanarGeometry.Locate`) instead of a second copy. The owner chose the recommended options for Q18, Q19, Q21, Q25, Q86 and Q87.
