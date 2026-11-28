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

## 2026-11-26 — SS-31: Hangfire and idempotent jobs
- Goal: periodic per-site jobs on Hangfire with Redis storage, no duplicates on retry, and the test run endpoint.
- Prompt (summary): plan SS-31 with the plan / validate / approve / implement / verify workflow; no real jobs, only the mechanism.
- AI produced: plan (Opus), plan review (Sonnet), `ISiteJob`, `SiteJobRunner`, Hangfire wiring, test endpoint, tests (Haiku), docs (Opus).
- Corrected or discarded: the review showed that a Hangfire storage cannot be swapped in tests (now chosen by `Hangfire:Storage`), that store ids allow no dots (parts joined with `_`), and asked for a concurrent-run test. The owner chose option A for all seven open questions.

## 2026-11-26 — SS-17: Route graph
- Goal: build an in-memory pedestrian graph from the site map, in metres, ready for routing (SS-19, SS-20, SS-21).
- Prompt (summary): implement SS-17 with the plan / validate / approve / implement / verify workflow; no routing in this story.
- AI produced: plan and ADR 0001 (Opus), plan review (Sonnet), Domain graph builder, segment/polygon test, map adapter and tests (Haiku).
- Corrected or discarded: the Sonnet review asked for an exact segment/polygon rule, strict tolerance, no self-loops, connector edge cases and the ADR file name fixed by the ai-process skill. The point-in-polygon test is shared with SS-22 (one `PlanarGeometry.Locate`) instead of a second copy. The owner chose the recommended options for Q18, Q19, Q21, Q25, Q86 and Q87.

## 2026-11-27 — SS-39: Device simulator
- Goal: a console app that walks N devices along the site paths and sends realistic position batches as the gateway, to test tracking and realtime.
- Prompt (summary): implement SS-39 from a plan written by the main session, without a plan PR (owner's choice); Sonnet validation, Haiku implementation, Sonnet verification.
- AI produced: plan (Opus), plan review (Sonnet), `PathGraph`, `SimulatedDevice`, `ApiClient`, `SimulatorOptions`, `SimulatorApp`, Dockerfile, compose service under the `sim` profile, tests and docs (Haiku), end-to-end check on an isolated compose stack (Sonnet).
- Corrected or discarded: the repo check found 11 path vertices outside the perimeter, so the graph keeps only vertices inside it (Q93). The review found that `.dockerignore` excluded `simulator/`, that a top-level `Program` would clash with the Api's `Program` in the tests, and asked for time through `TimeProvider`, secrets from env vars and tests of the retry rules.

## 2026-11-27 — SS-32: Position retention
- Goal: delete position log hour files older than each tenant's `RetentionDays`, hourly and from the test endpoint.
- Prompt (summary): plan SS-32 with the plan / validate / approve / implement / verify workflow; ask the owner on every open point.
- AI produced: plan, `DeleteBeforeAsync`, `ITenantJob`, `RetentionJob` and wiring (Opus), plan review (Sonnet), store, crash and API tests (Haiku).
- Corrected or discarded: the Sonnet review asked for the exact append lock on delete, where `RetentionDays` is read, no hourly schedule on the test host, a cutoff exactly on the hour and an append-during-delete test. The owner chose the recommended options for Q48, Q95 and Q96.

## 2026-11-27 — SS-23 ordered processing and drain
- Goal: process each device's positions in timestamp order, replay late ones, and add `POST /api/v1/test/drain`.
- Prompt (summary): plan with the owner's decisions on Q06, Q39, Q100–Q103; Sonnet review of the plan; Haiku and Sonnet subagents for models, processor, wiring, tests and records.
- AI produced: plan file, `DeviceProcessing`, `TrackPoint`, `PositionProcessor`, ingest marks, drain endpoint, unit and API tests, these records.
- Corrected or discarded: the plan review found a lost-batch race (a run could clear a marker before its positions were written): fixed with a second mark after the append. It also replaced "a few workers" with one worker and a per-device state machine, and made drain answer 500 after a failed run.

## 2026-11-26 — SS-46: Admin UI
- Goal: a minimal React + shadcn/ui admin UI (sites, map upload, devices) that logs in through the API.
- Prompt (summary): minimal React + shadcn admin UI, login through the API (BFF).
- AI produced: plan, backend (sites list, BFF auth, CSRF, static files, tests), frontend.
- Corrected or discarded: SPA PKCE was discarded for the BFF at user review. The Sonnet plan validation found the missing Keycloak scopeMappings/webOrigins, the SPA fallback masking API 404s and the missing `.dockerignore`. The review found the CSRF check skipped on any non-Bearer `Authorization` header; fixed with a test.

## 2026-11-27 — SS-42: Delivery documentation
- Goal: check the repository against the delivery list and fill the gaps in the documentation.
- Prompt (summary): "what is missing for SS-41 and SS-42", then "prepare everything for delivery".
- AI produced: the gap list (missing endpoints, missing NF-02 tests, ADR number clash), ADR 0004 (anomaly detector parameters, Proposed), the KNOWN_ISSUES "Delivery" section, and the rename of the position-storage ADR to 0003.
- Corrected or discarded: the memory index still said "ZIP via `git archive`", which drops `.git` (the delivery ZIP must include it). It was corrected to a fresh `git clone`. In ADR 0004 the AI claimed that "trial series" had been used to choose the SR-CNN threshold. No trials were run, so the claim was removed and the threshold is marked as not tuned. The ADR stays Proposed because the detector is not implemented.

## 2026-11-28 — SS-24: Plausibility and noise filter
- Goal: keep GPS jumps out of the track and smooth the noise, with a replay-safe filter (TRK-03, Q26).
- Prompt (summary): implement the SS-24 plan (`TrackingOptions`, `TrackFilter`, tombstones, processor wiring, tests) without commit or push.
- AI produced: `TrackingOptions` and validator, `TrackFilter`, `Dropped` tombstones in `TrackPoint`, processor wiring, unit and API tests (Haiku).
- Corrected or discarded: `NewPositionsAsync` keeps the raw-log revision check instead of folding all revisions, because a fold would republish a point kept under an older revision; only kept points are published.

## 2026-11-28 — SS-25: Area with hysteresis
- Goal: give every kept track point an area with hysteresis, so GPS noise on a border does not flip the area (TRK-04, Q27, Q28, Q119).
- Prompt (summary): implement the SS-25 plan (`SiteAreas`, `AreaAssigner`, options, processor wiring, tests, records) without commit or push.
- AI produced: `SiteAreas`, `AreaAssigner`, the two options and their validation, processor wiring, unit and API tests, and the Q27, Q28, Q119 records (Haiku subagent).
- Corrected or discarded: none in the code. The plan's "inside by m" uses the boundary tolerance of `PlanarGeometry.Locate`, so it means inside and more than m from every edge.

## 2026-11-28 — SS-28: Alarm core and AreaIntrusion
- Goal: one entry point for alarms (`AlarmManager`), an alerts endpoint and page, and the `AreaIntrusion` alarm raised from position processing.
- Prompt (summary): plan SS-28 with the plan / validate / approve / implement / verify workflow, two stacked PRs (core, then the detector).
- AI produced: plan, `Alert`, `AlarmManager`, alerts endpoint, Alarms page, shared area projection, `AreaIntrusionDetector`, tests, these records.
- Corrected or discarded: the detector runs before the device state is saved (an exception keeps the dirty marker) instead of after, so a crash cannot lose an alarm for good.
