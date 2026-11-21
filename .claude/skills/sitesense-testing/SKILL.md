---
name: sitesense-testing
description: >-
  Test strategy for SiteSense: which tests each area needs (NF-02), the test tools, crash and concurrency test recipes, determinism rules, test naming, and the commands to run tests and the benchmark. Use when writing or reviewing tests, planning the tests for a story, adding a crash or concurrency test, or running tests or the benchmark.
---

# Test strategy

Related: [architecture](../sitesense-architecture/SKILL.md) · [coding](../dotnet-coding/SKILL.md) · [security](../sitesense-security/SKILL.md) · [stack](../sitesense-stack/SKILL.md) · [delivery](../delivery-workflow/SKILL.md)

## 1. Required tests (NF-02)

Every area below needs at least the listed tests. Each story PR lists the tests it adds.

| Area | Required tests | Requirement IDs | Story |
|---|---|---|---|
| Map | Import of a valid map. Paths joined at a shared vertex. Paths joined by endpoints within tolerance. At least one invalid file gives 422 with `elementId` and `reason`. | MAP-01, MAP-03 | SS-x, TBD when the backlog is approved |
| Navigation | Restricted and hazard areas are avoided. Nearest assembly point is chosen by time, not distance. Unreachable and too-far start return their error codes. | NAV-02, NAV-04 | SS-x, TBD when the backlog is approved |
| Tracking | Duplicates recorded once. Out-of-order positions handled. Implausible jump dropped. Noisy area border does not flap. | TRK-01, TRK-02, TRK-03, TRK-04 | SS-x, TBD when the backlog is approved |
| AI | One anomalous scenario is detected. One normal scenario is not flagged. | AI-01 | SS-x, TBD when the backlog is approved |
| Store | Concurrent writers with version conflict. Consistency after a crash. | JDB-02, JDB-03, JDB-04 | SS-x, TBD when the backlog is approved |
| API | At least one end-to-end integration test. At least one tenant-isolation test (other tenant gets 404). | NF-02, IDN-02 | SS-x, TBD when the backlog is approved |

- Requirement IDs and status codes come from the spec §5 and §8. Do not copy the wording.
- Error codes (`elementId`, `reason`, `START_TOO_FAR`, `UNREACHABLE`) and their shapes: [context](../sitesense-context/SKILL.md).

## 2. Tools

| Need | Tool |
|---|---|
| Test framework | xUnit |
| Fake clock | `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` |
| HTTP integration | `WebApplicationFactory<Program>` |
| Auth in tests | A test auth handler that issues fake tokens with `tenant_id`, roles and scope claims |
| Assertions | xUnit `Assert`, unless [stack](../sitesense-stack/SKILL.md) says otherwise |

- **Proposed:** unit and integration tests use a test auth handler; Keycloak runs only with docker compose.
- Use `FakeTimeProvider`. Time rule: `../dotnet-coding/SKILL.md`.

## 3. Store crash test

1. Start a child process. It writes documents in a loop.
2. Kill the child with `Process.Kill()` at a point chosen from a fixed seed. Repeat N times (N fixed in the test).
3. Open the store in the test process.
4. Assert: every document parses, and each document version is consistent with its content.
5. Assert: no temp files are left as live documents.

## 4. Store concurrency test

1. Create one document at version 1.
2. Start several writers. Each reads version 1 and writes an update with `If-Match` version 1.
3. Assert: exactly one write succeeds. The others get a version conflict (412 at the API, conflict error in the store).
4. Assert: the stored version is 2 and the content equals the winning write.

## 5. Determinism

| Rule | Detail |
|---|---|
| Time | `FakeTimeProvider`. Advance it in the test. |
| Randomness | Fixed seed. Log the seed when a test fails. |
| Sleeps | None. Wait on a condition or a signal, with a timeout. |
| Test data | Fixed files under `tests/<Project>.Tests/TestData/` or a small fixture in `data/`. |
| Order | Tests do not depend on each other or on execution order. |
| Clock and IO | Use temp folders per test. Clean them up. |

## 6. Naming and structure

- Test name: `Method_Condition_Result`. Example: `Assign_PositionInsideRestrictedArea_ReturnsRestrictedArea`.
- One behavior per test.
- Arrange, Act, Assert, with a blank line between parts.
- Test project layout: `../sitesense-architecture/SKILL.md`.

## 7. Commands

Commands are placeholders until the solution exists. Update this section in the PR that creates the solution.

| Task | Command |
|---|---|
| Run all tests | `dotnet test` (from the solution root) |
| Run one area | `dotnet test --filter "FullyQualifiedName~Tracking"` |
| Benchmark (JDB-09) | **TBD:** command. Results go to `docs/RESULTS.md`. |

- JDB-09 (SHOULD): 100 positions per second for 10 minutes without losing any position. A device trace for one hour loads in under 200 ms. Provide a repeatable benchmark (spec §5.2).

## 8. Definition of done

- Tests and build pass, and the story's required tests exist. Full list: [delivery](../delivery-workflow/SKILL.md).
- Frontend or endpoint changes: the feature is checked in a browser or through the API. Rule: [delivery](../delivery-workflow/SKILL.md).
