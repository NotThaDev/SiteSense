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

## 2026-11-25 — SS-7: Authorization policies
- Goal: named policies `CanManage`, `CanSeeIndividuals`, `CanSeeAggregates`, `CanWritePositions`; no role checks in endpoint code.
- Prompt (summary): implement SS-7 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), policy registration, probe controller and per-role tests (Haiku), docs (Opus).
- Corrected or discarded: the Sonnet review added an explicit authenticated requirement, exact scope matching with look-alike and array tests, 401 rows, and a source scan that fails when it finds no files. An endpoint-metadata test was not taken (fallback policy already blocks anonymous callers).

## 2026-11-25 — SS-14: Sites API
- Goal: `POST /api/v1/sites` with the site JSON (201, Admin), 409 on a duplicate `siteId`, 404 for another tenant's site, `GET /api/v1/sites/{siteId}` with ETag.
- Prompt (summary): implement SS-14 with the plan / validate / approve / implement / verify workflow, stacked on SS-7 and SS-43.
- AI produced: plan (Opus), plan review (Sonnet), `Site` model, DTOs, `SitesController`, `.http` file and tests (Haiku), docs (Opus).
- Corrected or discarded: the Sonnet review added the invalid-id guard (the store throws on bad names → 500), action names without `Async` for `CreatedAtAction`, the shared test collection with unique site ids, and more 400 rows. The owner chose Q81 (GET for all roles) and Q82 (full validation).
