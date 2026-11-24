# AI log

Significant AI use on SiteSense. Entry format and rules: `.claude/skills/ai-process/SKILL.md`.

## 2026-11-22 — SS-1: Docker compose and health check
- Goal: start API, Keycloak and Redis with `docker compose up`, validate options at start, expose `/health`.
- Prompt (summary): implement issue SS-1 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `StoreOptions` and validator, `Program.cs` wiring, tests, Dockerfile, `docker-compose.yml`, `.env.example`, docs (Haiku and Opus).
- Corrected or discarded: the review added a non-root owner for the store volume, an API health check in compose, a unique write probe and a longer Keycloak start period. The owner chose Q70–Q72 (admin defaults, liveness-only health, no compose job in CI).

## 2026-11-24 — SS-4: Structured logs with correlation id
- Goal: JSON console logs, and a correlation id that is read from or added to `X-Correlation-Id`, sits in the log scope, and can travel to the ingest queue and SignalR events (NF-03).
- Prompt (summary): implement issue SS-4 with the plan / validate / approve / implement / verify workflow.
- AI produced: plan (Opus), plan review (Sonnet), `Diagnostics/Correlation.cs`, `Diagnostics/Correlated.cs`, `Program.cs` wiring, 4 tests (Haiku), docs and Q75–Q76 (Opus), verify run (Haiku).
- Corrected or discarded: the review removed setting `TraceIdentifier` (ProblemDetails uses the Activity id) and added the known limit that hosting lines stay outside the scope. The owner chose the `Diagnostics/` folder and the `Correlated<T>` carrier. The Q numbers moved twice to avoid clashes with SS-1 and SS-3.
