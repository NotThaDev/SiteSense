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

## 2026-11-24 — SS-4: Structured logs with correlation id
- Goal: JSON console logs, and a correlation id that is read from or added to `X-Correlation-Id`, sits in the log scope, and can travel to the ingest queue and SignalR events (NF-03).
- Prompt (summary): implement issue SS-4 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `Diagnostics/Correlation.cs`, `Diagnostics/Correlated.cs`, `Program.cs` wiring, 4 tests (Haiku), docs and Q77–Q78 (Opus), verify run (Haiku).
- Corrected or discarded: the review removed setting `TraceIdentifier` (ProblemDetails uses the Activity id) and added the known limit that hosting lines stay outside the scope. The owner chose the `Diagnostics/` folder and the `Correlated<T>` carrier. The Q numbers moved twice to avoid clashes with SS-1 and SS-3.
