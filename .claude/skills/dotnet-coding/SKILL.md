---
name: dotnet-coding
description: >-
  C# and .NET coding rules for SiteSense: language settings, naming, async and cancellation, time through TimeProvider, ProblemDetails errors, validation, options, logging, secrets, ML.NET concurrency, the Ponytail minimal-change rules, and which docs sources to query. Use when writing or reviewing C# code, adding a DTO, handling an error or a configuration value, adding logging, touching secrets or credentials, using ML.NET, or looking up a library API.
---

# .NET coding rules

Related: [architecture](../sitesense-architecture/SKILL.md) · [security](../sitesense-security/SKILL.md) · [stack](../sitesense-stack/SKILL.md) · [testing](../sitesense-testing/SKILL.md) · [context](../sitesense-context/SKILL.md)

## 1. Language and project settings

| Rule | Status |
|---|---|
| `<Nullable>enable</Nullable>` in every project | Required |
| `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | **Proposed:** warnings as errors. Accept or change. |
| File-scoped namespaces | Required |
| Classes are `sealed` unless built for inheritance | **Proposed:** sealed by default. Accept or change. |
| Records for DTOs and value objects | Required |
| Immutable types where possible (`init`, readonly) | Required |

## 2. Naming

| Item | Style | Example |
|---|---|---|
| Types, methods, properties, constants | PascalCase | `AreaAssigner`, `GetTrackAsync` |
| Locals and parameters | camelCase | `deviceId` |
| Private fields | `_` + camelCase | `_store` |
| Async methods | `Async` suffix | `SaveBatchAsync` |
| Interfaces | `I` prefix | `IDocumentStore<T>` |

## 3. Async and cancellation

- Every async method takes a `CancellationToken` as its last parameter and passes it on.
- No `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`.
- No `async void`, except event handlers.

## 4. Time

- Inject `TimeProvider`. Pass it through constructors.
- Forbidden: `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `Task.Delay` without the provider, `Stopwatch` for business time.
- Reason: the test clock must drive all time-dependent logic (see [testing](../sitesense-testing/SKILL.md)).
- Timestamps at the boundary: `long` milliseconds UTC. Convert to `DateTimeOffset` inside the domain only if needed.

## 5. Errors

- All errors are **ProblemDetails** (RFC 9457). Set up with `AddProblemDetails` and one exception handler.
- Never leak stack traces, SQL, file paths or secrets in a response.
- Status codes:

| Case | Status | Body |
|---|---|---|
| Malformed or invalid input, other than the cases below | 400 | `ValidationProblemDetails` **Proposed:** 400 for other input errors. Accept or change. |

Contract-fixed codes and shapes (422, 412, 404): `../sitesense-context/SKILL.md`.

- Shapes and codes from the contract: [context](../sitesense-context/SKILL.md). Do not restate them here.

## 6. Validation

- Validate at trust boundaries only: request DTOs, map import, position batches, config values.
- Domain code assumes valid input. Do not add checks inside the domain for input that the boundary already checked.
- Reject invalid input before any side effect.

## 7. Options and configuration

- Strongly typed options class per section. Bind with the options pattern.
- `ValidateOnStart()` plus data annotations or a custom validator. The app must fail at start on bad config (NF-04).
- No magic numbers for tunables: filters, detector windows, thresholds, retention. They live in options.

## 8. Logging

- `ILogger<T>` with message templates. No string interpolation in log calls.

  ```csharp
  _logger.LogInformation("Accepted {Count} positions for {TenantId}", count, tenantId);
  ```

- Correlation id in the log scope. It flows: API request → processing → SignalR event (NF-03).
- Never log secrets, tokens or personal data. Rules: [security](../sitesense-security/SKILL.md).

## 9. Secrets and credentials

- No secrets in code, in `appsettings*.json`, or in any committed file.
- Read secrets from environment variables. Map them into options.
- The only credentials allowed in the repository are the test credentials in the spec §7.1 (NF-04).
- Other files that hold test identities: [security](../sitesense-security/SKILL.md) lists them and links here.

## 10. Concurrency

- Shared state is thread-safe. Prefer immutable snapshots over locks.
- Process/store ownership: see `../sitesense-architecture/SKILL.md` (Proposed, Q01).
- Lock scope: keep it short. Never await inside a lock.

## 11. ML.NET (AI-03)

| Rule | Detail |
|---|---|
| Create once | `MLContext`, pipelines and prediction engines are created once, not per request. |
| Engine is not thread-safe | `PredictionEngine<TIn, TOut>` is not thread-safe. Use `PredictionEnginePool` (needs `Microsoft.Extensions.ML`) or one engine per thread, or a lock. |
| Parameters | Windows, thresholds and confidence are options. Their values are justified in an ADR under `docs/adr/`. |
| Tests | Use fixed seeds and a small fixed data set (see [testing](../sitesense-testing/SKILL.md)). |

## 12. Ponytail: minimal change

1. Smallest complete change. Solve the full requirement. Do not cut scope.
2. Reuse before writing. Look for an existing helper, service or pattern in the repo first.
3. No speculative abstractions, options or extension points. Add them when a second real use exists.
4. Platform or library feature before custom code.
5. Deletion beats addition.
6. Comments only for the **why** that the code cannot show. One line.
7. Known limit of a shortcut: a comment in this form. Example:

   ```csharp
   // shortcut: whole-file rewrite per save, no batching; upgrade when a file exceeds 50 MB
   ```

8. Never cut: validation at trust boundaries, error handling that prevents data loss, security, the test clock.

## 13. Library docs: where to look

Look up the docs before you write code that uses a library API. Prefer the docs over memory for APIs and version behavior.

- Microsoft Learn MCP: .NET, ASP.NET Core, SignalR, ML.NET, xUnit. Context7: Hangfire, NetTopologySuite, ProjNet, Keycloak, anything else.
- Microsoft Learn MCP: search first, then fetch the page.
- Context7: resolve the library id, then query it.
- No result in those: try another docs MCP, then a web search.
