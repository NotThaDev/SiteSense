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
