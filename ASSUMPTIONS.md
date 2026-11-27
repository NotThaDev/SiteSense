# Assumptions

Points the requirements leave open, and what we decided. Each decision is taken by the project owner before it is recorded here.

## Q01 — Are the API and the worker one process or two?
- Assumption: one process. The API process hosts the endpoints, the background processing of positions (hosted background services), the Hangfire server and SignalR. The "worker" named in NF-01 is that background processing, not a separate program. One store service, registered as a singleton, is the only code that reads or writes the store files.
- Why: the store is only required to be thread-safe within a single process (JDB-04); two processes writing the same files would need cross-process locks. One process also keeps the simulated clock, `test/drain`, on-demand job runs and SignalR in one place, with no extra infrastructure.
- Requirement IDs: NF-01, JDB-02, JDB-03, JDB-04, NF-05
- Status: Accepted

## Q03 — Which identity provider: Keycloak or OpenIddict?
- Assumption: Keycloak in a container, with the realm (tenants, users, roles, clients) imported at startup.
- Why: password grant and client credentials come from configuration, with no auth server code to write and test.
- Requirement IDs: IDN-01, IDN-04, IDN-05
- Status: Accepted

## Q08 — Which storage does Hangfire use?
- Assumption: Redis, in its own container, used through an open-source community storage package (planned: `Hangfire.Redis.StackExchange`; licence and .NET 10 support confirmed when it is added). Redis holds Hangfire jobs only (schedules, queues, retries), never domain data.
- No data loss on restart: Redis runs with append-only persistence written on every command (`appendonly yes`, `appendfsync always`) on a named volume. A restart or a recreated container keeps every queued and scheduled job.
- Why Redis: queued and scheduled jobs survive a restart, so no run is lost. Domain data stays in the JSON document store as required.
- Alternatives rejected:
  - `Hangfire.InMemory`: free and simple, but loses every queued job on restart.
  - `Hangfire.Pro.Redis` (official Redis storage): commercial licence.
  - Our own `JobStorage` for Redis: queues, distributed locks, retries and recurring jobs to write and test, on a part that adds no value to the domain.
  - `Hangfire.SqlServer` or PostgreSQL: a relational database only for jobs, heavier than Redis.
- Trade-off: `appendfsync always` makes each Redis write slower; the job load (a few runs per site per minute) is far below what that costs.
- Requirement IDs: JOB-01, JOB-02, NF-01, NF-05
- Status: Accepted

## Q09 — Does Admin also see individual positions, tracks and alerts?
- Assumption: no. Roles are separate: Admin manages sites, maps and devices; Safety sees individual data; Viewer sees aggregates only.
- Why: the requirements give individual data to Safety only and define no role hierarchy; separate roles expose personal movement data to fewer people.
- Requirement IDs: IDN-01, IDN-03, PRV-03
- Status: Accepted

## Q61 — Can a user have more than one role?
- Assumption: no. Each user has exactly one role: Admin, Safety or Viewer. There is no super user in the realm.
- Why: the requirements never combine roles and speak of a user's role in the singular (RTM-01); one role per user cannot conflict with "Viewer never sees individual data" (PRV-03). Authorization goes through policies, so allowing several roles later needs no code change.
- Requirement IDs: IDN-01, IDN-03, PRV-03, RTM-01
- Status: Accepted

## Q10 — Wrong role in the right tenant: 403 or 404?
- Assumption: `403` Forbidden. `404` stays for missing resources and for resources of another tenant.
- Why: the 404 rule exists to hide another tenant's data; inside the user's own tenant there is nothing to hide, and 403 is the standard answer when a policy is not met.
- Requirement IDs: IDN-02, IDN-03
- Status: Accepted

## Q36 — Does `AlertRaised` reach the Viewer?
- Assumption: no. Alerts are for Safety only, in real time (`AlertRaised`) and over REST, for every alert type including `ZoneAnomaly`. Admin and Viewer get `OccupancyChanged` only.
- Why: only Safety can list alerts, so a Viewer would receive alerts it can never read back or recover after a disconnect. Occupancy updates already show when an area fills up.
- Requirement IDs: RTM-01, RTM-02, PRV-03
- Status: Accepted

## Q55 — How are PRs merged if history must not be rewritten?
- Assumption: merge commits only ("Create a merge commit" on GitHub). No squash merge, no rebase merge, no force push on pushed branches, no stacked PRs. A branch is updated by merging `main` into it.
- Why: squash and rebase merges create new commits and lose the original ones; a merge commit keeps every commit as it was and records when each story entered `main`.
- Requirement IDs: delivery rules (history preserved, no squash, no rewrite)
- Status: Accepted

## Q59 — How does the API know the tenant of a gateway client?
- Assumption: the gateway access token carries a `tenant_id` claim, added by a hardcoded-claim mapper on each gateway client in the realm. The API reads the tenant from the token for users and gateways alike, never from the request.
- Why: each gateway belongs to exactly one tenant, but a client-credentials token has no user, so no tenant claim by default. One claim keeps one code path.
- Requirement IDs: IDN-02, IDN-05
- Status: Accepted

## Q60 — What happens to a position batch for an unknown or foreign device?
- Assumption: if the `deviceId` is not registered, or is registered in another tenant, the whole batch is rejected with `404` and nothing is stored.
- Why: consistent with the rule that another tenant's resources look like missing ones; a gateway cannot write data for devices outside its tenant.
- Requirement IDs: IDN-02, TRK-01, API-02
- Status: Accepted

## Q62 — May logs contain the coordinates of individual devices?
- Assumption: only at `Debug` level, which is off by default. `Information` and above log the device ID, counts and the correlation id, never coordinates. Positions outside the site perimeter are never logged with coordinates, at any level.
- Why: logs live outside role checks and retention, so everyday logs must not hold movement data; `Debug` keeps a way to diagnose the filters. Outside-perimeter positions must never be stored, and a log file is storage.
- Requirement IDs: PRV-01, PRV-02, PRV-04, NF-03
- Status: Accepted

## Q63 — How are SignalR groups organised?
- Assumption: one group per tenant, site and role, named `tenant:{tenantId}:site:{siteId}:role:{role}`. The server adds a connection to its role group when the client joins a site, after checking the site belongs to the token's tenant. Each event is sent only to the role groups allowed to see it.
- Why: site IDs are not unique across tenants, so the tenant must be part of the name; groups cannot be intersected, so the role is part of the name too. Filtering at join time means a send can never reach a role that must not see it.
- Requirement IDs: RTM-01, RTM-02, PRV-03
- Status: Accepted

## Q64 — Are compiler warnings treated as errors?
- Assumption: no. Warnings stay warnings; `Directory.Build.props` does not set `TreatWarningsAsErrors`.
- Why: a new analyzer or NuGet advisory warning must not break the build or CI with no code change. Warnings are still shown and fixed in the PR that adds them.
- Requirement IDs: none (build setting)
- Status: Accepted

## Q65 — Where are package versions declared?
- Assumption: central package management. Every version is in `Directory.Packages.props`, exact, with no floating versions or ranges; no `PackageReference` carries a `Version`.
- Why: one place to see and update every dependency, and the same version in every project.
- Requirement IDs: none (build setting)
- Status: Accepted

## Q66 — How many projects does the server have?
- Assumption: two. `SiteSense.Domain` holds pure domain code with no package or project references. `SiteSense.Api` references Domain and holds everything else in the standard type-based ASP.NET Core folders: `Controllers/`, `Models/` (persisted entities; persistence pattern in Q67), `Dtos/` (request and response records, `<Name>Request` / `<Name>Response`), `Services/`, `Store/`, `Realtime/`, `BackgroundJobs/`, `Auth/`. One test project, `tests/SiteSense.Tests`. The simulator stays a separate console app.
- Folders: `backend/` holds the backend projects (`SiteSense.Domain`, `SiteSense.Api`), `simulator/` holds the simulator, `tests/` holds the test project, and `frontend/` is reserved for the demo client (its own story). The simulator is a separate app, not part of the backend, so it does not sit next to the backend projects. The demo client is a separate app too, so it gets its own `frontend/` folder and never goes under `simulator/`.
- Api folders: type-based (`Controllers/`, `Models/`, `Services/`, ...) instead of feature folders, so the structure is familiar to reviewers. Feature folders were considered and rejected. Wire shapes sit in `Dtos/`, apart from `Models/`, so an entity is never serialized as a response by mistake (changed in SS-6 by the project owner; before, DTOs lived in `Models/`). Hangfire jobs are trigger-only `<What>Job.cs` classes in `BackgroundJobs/` that call a service or a model method; every recurring schedule is registered in `Program.cs`, one line per job.
- Why: a separate Domain project keeps the domain pure, enforced by the compiler. Separate Application and Infrastructure projects add ceremony and no value in a single-process app (Q01).
- Alternatives rejected:
  - Five projects (Domain, Application, Infrastructure, Api, Simulator) with one test project each: more projects and references to maintain, for boundaries one process does not need.
  - A single server project: nothing stops domain code from using ASP.NET, IO or the clock.
- Requirement IDs: NF-01, NF-02
- Status: Accepted

## Q67 — How do entities reach the store?
- Assumption: Active Record. Each persisted entity in `SiteSense.Api/Models/` carries `Id`, `TenantId` and `Version` and exposes static async methods for its own persistence and single-entity rules (e.g. `Site.CreateAsync(tenantId, site, ct)`). They call the single generic store through the static context `SiteSenseContext.Store` (`Api/Helpers/SiteSenseContext.cs`), a singleton set once in `Program.cs` and shared by HTTP requests, Hangfire jobs and SignalR (one process, Q01). Controllers call model methods directly; `Services/` holds only multi-entity flows and external engines (position ingest, ML.NET anomaly detection, navigation and evacuation). `SiteSense.Domain` keeps only pure algorithms and geometric primitives, which the models use.
- Clock: `SiteSenseContext` also exposes `Time`, a `TimeProvider`, set to `TimeProvider.System` in `Program.cs` and replaced by a fake in tests. Models and services read the current time only through it, never through `DateTime.Now` or `DateTimeOffset.UtcNow`. Rejected: passing a `TimeProvider` as a parameter to every method.
- Tenant safety: every persistence method takes `tenantId` as an explicit parameter, never from an ambient or HTTP context; the store rejects a read or write whose `tenantId` does not match the document.
- Why: simple and familiar (EF-style entities), chosen by the project owner.
- Alternatives rejected:
  - A per-entity service plus a store injected through the constructor: one more class per entity and more wiring, for no gain in one process.
- Known risks:
  - Static state: parallel tests would share the store. Tests that touch the store run in one xUnit collection, with `SiteSenseContext.Store` on a temp folder and `SiteSenseContext.Time` on a fake clock.
  - `tenantId` must always be passed explicitly; a missing or wrong one is caught by the store's tenant check.
- Requirement IDs: JDB-01, JDB-07, IDN-02
- Status: Accepted

## Q68 — Are appsettings files committed?
- Assumption: no. Only `backend/SiteSense.Api/appsettings.example.json` is committed, as the template. `appsettings.json` and `appsettings.*.json` are gitignored. The template is updated in the same PR as any new config key.
- Why: a real config file can never leak a secret into git, and each developer keeps local values out of the history.
- Alternatives rejected:
  - Committing `appsettings.json` without secrets: one mistaken edit puts a secret in git, and local overrides show up as diffs.
- Known cost: a manual copy step before the first run. Docker compose (SS-1) and the delivery ZIP check must also handle it: compose provides the config through environment variables or the template, and the empty-folder check must start without a hand-made `appsettings.json`.
- Requirement IDs: NF-04
- Status: Accepted

## Q69 — Where is the API description served?
- Assumption: the built-in ASP.NET Core OpenAPI document is served at `/openapi/v1.json` in every environment, and the Scalar UI at `/scalar/v1` (`/scalar` redirects there). Scalar is kept so the project owner can review the API; it may be removed later without touching the contract.
- Why: clients and reviewers can read the contract from any run, docker compose included. The document holds no secrets.
- Alternatives rejected:
  - Development only (the template default): the contract would not be visible in the compose run.
  - No UI: the raw JSON is harder to review by hand.
- Known risks:
  - Both endpoints are anonymous. When authentication lands, they need an explicit `AllowAnonymous`, or a fallback policy will block them.
- Requirement IDs: API-05
- Status: Accepted

## Q70 — How does compose get the Keycloak admin credentials?
- Assumption: compose defaults them to `admin` / `admin` (`${KC_BOOTSTRAP_ADMIN_PASSWORD:-admin}`), documented as local test credentials. `.env` can override them.
- Why: `docker compose up` must work with no manual step; the admin console is local only.
- Alternatives rejected:
  - Required values from `.env`: no default password in compose, but a copy step before the first run.
- Requirement IDs: NF-01, NF-04
- Status: Accepted

## Q71 — What does `/health` check?
- Assumption: liveness only, with the built-in ASP.NET Core health checks (`MapHealthChecks("/health")`). The store root is checked at start (`SiteSenseContext.Init`), not on every call.
- Why: no extra package; dependency checks (Redis, Keycloak) come with the stories that use them.
- Requirement IDs: NF-04
- Status: Accepted

## Q72 — Does CI run docker compose?
- Assumption: no. CI stays restore, build and test. `docker compose up` and `/health` are checked by hand in each PR that changes compose.
- Why: keeps CI fast; image pulls would add minutes to every PR.
- Requirement IDs: NF-01
- Status: Accepted

## Q73 — Where is the store on a local run?
- Assumption: `backend/SiteSense.Api/.store/`. The template keeps `"RootPath": ".store"`, a path relative to the working directory, and `dotnet run` sets that to the project folder. The folder is gitignored.
- Why: a plain relative value, no `../../` that breaks when the DLL is started from another folder.
- Alternatives rejected:
  - `../../.store` to keep the store in the repo root: wrong when the app is not started with `dotnet run`.
- Requirement IDs: NF-04, NF-05
- Status: Accepted

## Q74 — Does Keycloak use HTTPS in compose?
- Assumption: no. Keycloak runs on plain HTTP (`start-dev`), so the API reaches it the same way from a container (`http://keycloak:8081`) or from the host with `dotnet run` (`http://localhost:8081`), with no certificate to trust.
- Keycloak's default `sslRequired: external` asks for HTTPS when Docker forwards a host request from a non-private address. So:
  - the SiteSense realm import (SS-5) sets `sslRequired: none`;
  - a one-shot `keycloak-init` service sets the master realm (admin console) to `sslRequired=NONE` once Keycloak is healthy, so `docker compose up` needs no manual step.
- SS-5 also fixes the token issuer with `KC_HOSTNAME=http://localhost:8081`, so tokens carry the same issuer whether the API runs on the host or in a container. `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` keeps the jwks and token URLs on the host the caller used, so a container can still reach them at `http://keycloak:8081`.
- Why: the evaluator runs only `docker compose up`, and the backend must work against the Keycloak container both from the host and from a container.
- Alternatives rejected:
  - Self-signed certificate made by the Keycloak image: containers trust it through `SSL_CERT_DIR`, but on macOS .NET uses the Keychain, so `dotnet run` needs a manual trust step; the browser shows a warning.
  - Plain HTTP plus a documented `kcadm` command for the master realm: a manual step for the evaluator (NF-01).
  - mkcert: a manual install step for the evaluator (NF-01).
- Consequences: local and test use only. Trusting a certificate is infrastructure config, not an application feature.
- Going online: the real certificate, for the real domain, goes to one service only, an nginx reverse proxy. nginx terminates TLS and forwards each request over the internal network to the right service (Keycloak, the API, the demo client). Behind nginx the services keep plain HTTP; Keycloak is told it runs behind a proxy (`KC_PROXY_HEADERS=xforwarded`, `KC_HOSTNAME` set to the public URL), its realms go back to `sslRequired: external`, and `keycloak-init` is dropped.
- Requirement IDs: NF-01, IDN-01
- Status: Accepted

## Q04 — How are documents stored as JSON files?
- Assumption: one JSON file per document, `<root>/<tenantId>/<type>/<id>.json`. A save replaces the whole file atomically (temp file, fsync, rename) and keeps no history. Data with a history (positions, track points, events) is append-only and gets its own format in SS-12. If a later story needs old map versions, it saves each version as its own document.
- Why: documents (tenant, site, map, device, alert) are few and read or replaced whole; the requirements ask for no document history. One file per document means one write touches only that document.
- Alternatives rejected:
  - One file per type (e.g. `devices.json`): every save rewrites all documents of the type and serializes all writers.
  - Keeping every version (`<id>.v<n>.json`): more files and a cleanup job, for no requirement.
- Requirement IDs: JDB-01, JDB-02, JDB-07
- Status: Accepted (documents). Positions: see Q83 and docs/adr/0001-position-storage.md.

## Q75 — One persistence interface or two?
- Assumption: two. `IDocumentStore` for documents (read, replace whole, version) and, from SS-12, a separate interface for append-only records (positions, track points, events). One component still writes to disk.
- Why: each entity sees only the operations that make sense for it; an append-only record has no update or version, and a document has no append.
- Alternatives rejected:
  - One interface that also gets `AppendAsync`: half of its methods would be invalid for half of the entities.
- Requirement IDs: JDB-01, JDB-06
- Status: Accepted

## Q76 — Are ids case-sensitive in the store?
- Assumption: yes, as in JSON and HTTP: `dev-001` and `DEV-001` are two documents. In file and folder names each uppercase letter is written as `!` + lowercase (`DEV-001` → `!d!e!v-001.json`); `!` is not a valid id character, so two ids never map to one file, on any disk. The id inside the file is stored as is.
- Why: macOS and Windows disks ignore case, Linux does not. Without the encoding a second save of `DEV-001` silently overwrites `dev-001` on a Mac but not in the container.
- Alternatives rejected:
  - Case-insensitive ids (a case variant rejected as a duplicate): a rule the requirements do not give.
  - Lowercase-only ids: a `DEV-001` the requirements allow would get 400.
- Requirement IDs: JDB-01, JDB-02
- Status: Accepted

## Q45 — Which resources get ETag, and is If-Match required?
- Assumption: every document (tenant, site, map, device, alert). The version is the `ETag` (`"3"`). `If-Match` is required on every change to an existing document: the controller copies it into `doc.Version`, and a missing or unreadable `If-Match` counts as version 0, so the store rejects it like a stale one. A stale version returns 412 ProblemDetails. The version in the request body is never used.
- Why: the owner chose required over optional; reusing the store check keeps one rule and no separate 428 path.
- Alternatives rejected:
  - Optional `If-Match` (missing = last write wins): a client that forgets the header silently overwrites.
  - 428 for a missing header and 400 for a malformed one: more code paths for the same outcome (the write is refused).
- Requirement IDs: JDB-03, JDB-10
- Changed (SS-47): the map PUT is the exception: `If-Match` is optional there (Q91). Absent = replace the latest map; present = checked, and an unreadable value is `412`.
- Status: Changed

## Q14 — How is a tenant created, and where do its settings live?
- Assumption: tenants exist only in configuration, section `Tenants`, keyed by tenant id, each with `RetentionDays` (PRV-04) and `SilentAfterMinutes` (TRK-06). Defaults: `tenant-a` and `tenant-b`, 30 days and 10 minutes. Validated at startup in `SiteSenseContext.Init`; read through `SiteSenseContext.Tenants`. In Docker the values come from env vars in `docker-compose.yml`, because the delivery ZIP has no `appsettings.json`. The tenant's store folder is created on its first save (SS-9). Tenant ids are also in the Keycloak realm (`tenant_id` of users and gateway clients) and are kept in sync by hand; a tenant id is never renamed, since it is also the store folder name.
- Why: no requirement asks for a tenant endpoint or a tenant document; Keycloak only tags who belongs to which tenant, it does not hold the list of valid tenants or their settings.
- Alternatives rejected:
  - A tenant document in the store: versioned data that nothing edits.
  - Keycloak as the only source (Organizations + Admin API at startup): reworks SS-5 and SS-6 and makes startup depend on Keycloak.
  - One seed file that generates the realm: a generator and a drift test for two tenants that never change.
- Requirement IDs: NF-04, PRV-04, TRK-06
- Status: Accepted

## Q02 — How does a token taken on the host validate in the API container?
- Assumption: the API accepts exactly one issuer, `http://localhost:8081/realms/sitesense`, which Keycloak always writes (`KC_HOSTNAME`, Q74). In compose the API downloads the signing keys from the internal name (`Auth:MetadataAddress` = `http://keycloak:8081/realms/sitesense/.well-known/openid-configuration`); on the host it reads them from the issuer. HTTPS for the key download is required by default (`Auth:RequireHttpsMetadata = true`); the template and compose set it to `false` for the local plain-HTTP Keycloak (Q74). The keys are cached by the JWT bearer handler and every signature is checked locally.
- Why: inside the container `localhost:8081` is the container itself; a fixed issuer plus an internal key address keeps one valid issuer and no second code path.
- Alternatives rejected:
  - Accepting both `localhost:8081` and `keycloak:8081` as issuers: two valid issuers for one realm, and the token never carries the second one anyway.
- Requirement IDs: IDN-01
- Status: Accepted

## Q77 — How do tests get tokens?
- Assumption: tests sign real JWTs with an RSA key generated in the test run and send them through the real JWT bearer handler, which sees that key through a static OpenID configuration (no network). Keycloak runs only with docker compose.
- Why: the tests then prove the validation itself (signature, issuer, audience, lifetime, `tenant_id`), which a fake auth handler would skip.
- Alternatives rejected:
  - A test auth handler that injects claims: no validation tested.
- Requirement IDs: IDN-01, NF-02
- Status: Accepted

## Q78 — Is there an endpoint that shows the caller's identity?
- Assumption: yes, `GET /api/v1/me`, always on, for any valid token. It returns `{"tenantId": "...", "roles": [...]}`; `roles` is empty for gateways.
- Why: it proves live that a token from the host validates in the container and carries the right tenant and roles; the demo client can use it too. It returns only what the caller's own token already says.
- Alternatives rejected:
  - Only in test mode: the check would need test mode on.
  - No endpoint: the issuer trap could only be checked once a real endpoint exists.
- Requirement IDs: IDN-01, IDN-02
- Status: Accepted

## Q79 — What happens to a valid token without a usable `tenant_id`?
- Assumption: `401`. A token passes only with exactly one `tenant_id` that is a valid store id (`^[A-Za-z0-9_-]{1,128}$`). Token lifetime follows the real clock with 30 s skew, not `SiteSenseContext.Time`, so moving the test clock never expires a token.
- Why: no endpoint ever sees a caller without a tenant, and a hostile value (`../x`) never reaches the store.
- Alternatives rejected:
  - `403`: the caller is not a valid identity at all, so it is an authentication failure.
- Requirement IDs: IDN-02
- Status: Accepted

## Q80 — What protects an endpoint with no authorization attribute?
- Assumption: a fallback policy requiring an authenticated user. `/health`, `/openapi/v1.json` and `/scalar` are `AllowAnonymous` (Q69). A request without a token to an unknown route or with a wrong method gets `401`; with a token, `404` or `405`.
- Why: a new endpoint is never open by mistake; SS-7 adds the role policies on top.
- Alternatives rejected:
  - Endpoints open until SS-7: every endpoint added in between would be public.
- Requirement IDs: IDN-01, IDN-03

## Q50 — Which real site is the test map?
- Assumption: the Cittadella Universitaria in Catania (OSM way 231201634). `data/build_map.py` (Python 3, standard library only) converts a saved, gzipped OSM extract (`data/osm/cittadella.osm.gz`) into `data/map.geojson`; `--download` refreshes the extract. No CI check of the output.
- Why: a dense, real walk network (footways, 30 step runs, gates on the perimeter) and 5 real `emergency=assembly_point` nodes. The saved extract (about 250 KB) makes a re-run give the same map, since OSM changes daily.
- Alternatives rejected:
  - PoliMi Bovisa, CNR Bologna, Kilometro Rosso: smaller walk network or fewer steps.
  - Always downloading, no saved extract: the map would change whenever OSM is edited.
- Requirement IDs: D-DATA
- Status: Accepted

## Q43 — What happens when a site is posted again with the same `siteId`, and how is a site updated?
- Assumption: `POST /api/v1/sites` only creates; an existing `siteId` in the same tenant gives `409` and changes nothing. Updates go through `PUT /api/v1/sites/{siteId}` with the site JSON and `If-Match` (Q45): current version → `200 { "siteId" }` and the new `ETag`; missing or stale → `412`; a site not in the caller's tenant → `404`; a body `siteId` different from the URL → `400`. Both are Admin only. `siteId` in the body is optional (one DTO for both): on `POST`, absent or null → the server makes a new id (32 hex characters), given → used as is (the contract posts the site JSON with its `siteId`); on `PUT` the URL is the id, and a body `siteId` must be absent or equal to it. The same `siteId` in another tenant is a different site.
- Why: standard REST (POST creates, PUT replaces a known resource), chosen by the owner. The site JSON carries `siteId`, and the map and device URLs use it, so a given `siteId` must be kept; a UI that has no id leaves it out. `PUT` for a site is not in the API contract; API-01 ("management of sites") covers it.
- Alternatives rejected:
  - `POST` as create-or-update with `If-Match`: one verb with two meanings; 409 and 412 mixed on one endpoint.
  - `POST` with an empty id to create and a given id to update: the contract posts the site JSON with its `siteId` to create it.
  - `siteId` always required: a UI would have to invent a code before creating a site.
- Requirement IDs: API-01, C-7.3a, JDB-03
- Status: Accepted

## Q81 — Who may read a site (`GET /api/v1/sites/{siteId}`)?
- Assumption: every role (`CanSeeAggregates`: Admin, Safety, Viewer). Another tenant's site, a missing one or an invalid id gives `404`. The response is the site JSON with `ETag`.
- Why: the site holds only configuration, no personal data, and navigation and occupancy clients of every role need it.
- Alternatives rejected:
  - Admin only (`CanManage`): Viewer and Safety clients could not read the site they navigate.
  - No GET: the cross-tenant `404` and the ETag could not be tested.
- Requirement IDs: API-01, IDN-02
- Status: Accepted

## Q82 — How strictly is the site JSON checked on `POST /api/v1/sites`?
- Assumption: fully, before anything is stored; errors are `400` `ValidationProblemDetails`. All fields required; `siteId` a valid store name (`[A-Za-z0-9_-]`, 1–128); `name` not empty; `timeZone` a time zone id known to the runtime (IANA on Linux); `walkingSpeedMps`, `connectToleranceM`, `maxSnapDistanceM` > 0; `unpavedSpeedFactor`, `stepsSpeedFactor` in (0, 1].
- Why: the site settings drive routing and snapping later; a bad value would surface far from its cause.
- Alternatives rejected:
  - Required fields and `siteId` only: a zero speed or an unknown zone breaks routing later.
- Requirement IDs: F-6.1, API-05

## Q17 — What happens to positions and state when a new map version arrives?
- Assumption: forward only. New positions use the current map; nothing already processed is recomputed. Each track point keeps the `mapVersion` it was computed with.
- Why: reprocessing past tracks and alerts on every upload is costly and changes history the users already saw; the stored `mapVersion` keeps every point explainable.
- Alternatives rejected:
  - Recompute the current area of every device on upload: extra code for a case the requirements do not describe.
- Requirement IDs: MAP-01, API-01
- Changed (SS-47): `mapFile` is no longer a required field; it is accepted if sent and ignored (Q92).
- Status: Changed

## Q40 — Which `elementId` does a map error carry when there is no id?
- Assumption: a missing perimeter gives `elementId: null`; a feature without a readable `properties.id` gives `features[<index>]`, its position in the `features` array (0-based).
- Why: the contract always has `elementId`; `null` says "no element", and the index points the Admin to the exact feature.
- Alternatives rejected:
  - A fixed name such as `"perimeter"`: it looks like a real id the file does not contain.
- Requirement IDs: MAP-01
- Status: Accepted

## Q41 — Which map checks besides the three the requirements list?
- Assumption: also duplicate ids, unknown `kind`, wrong geometry type, unreadable coordinates, enum values not allowed, wrong property types, and entrances off a path. "On a path" means within the site's `connectToleranceM` of a path segment, checked at upload time. Only types are checked, not ranges, and geometry validity, overlap and containment are not checked.
- Why: each of these would otherwise crash or silently corrupt tracking and navigation later; all come back in the same 422 list.
- Alternatives rejected:
  - Only the three listed checks: a bad file would pass and fail later with a less clear error.
- Requirement IDs: MAP-01
- Status: Accepted

## Q20 — Which local metric projection does a map use?
- Assumption: a local equirectangular projection on the WGS84 ellipsoid, one per map, with its origin at the perimeter centroid. Meters per degree come from the prime-vertical and meridional radii at the origin; the inverse is exact.
- Why: the owner chose it. Sites span at most a few km, where the error is sub-centimeter, and it keeps Domain free of packages.
- Alternatives rejected:
  - ProjNet with UTM: precise over large areas, but adds a dependency to Domain and needs handling for sites that cross a UTM zone border.
- Requirement IDs: MAP-02
- Status: Accepted

## Q12 — A batch has positions for a device unknown in the gateway's tenant. What happens?
- Assumption: `202`. Those positions are dropped, the response returns how many were dropped, and the drop is logged without coordinates. Implemented with batch ingestion (SS-22).
- Why: the owner chose it. One unknown device must not block the rest of the batch, and a device of another tenant is treated as unknown, so nothing leaks across tenants.
- Alternatives rejected:
  - `422` for the whole batch: one bad device id would make the gateway resend thousands of valid positions.
- Requirement IDs: IDN-05, TRK-01
- Status: Changed — superseded by Q60 (a batch has one deviceId, so an unknown device means the whole batch: 404, nothing stored).

## Q38 — How does the simulated clock of test mode behave?
- Assumption: `PUT /api/v1/test/clock` `{ "nowMs" }` (Admin, test mode on) replaces `SiteSenseContext.Time` with one global clock for the whole process, all tenants included. It stays frozen at `nowMs` until the next `PUT` and answers `204`. Only the current time is frozen: timers, `GetTimestamp` and delays keep real time, and JWT lifetime checks keep real time so real tokens stay valid. With `SITESENSE_TEST_MODE` off (the default), every path under `/api/v1/test` answers `404` to every caller, with or without a token, before authentication runs. Code must read `SiteSenseContext.Time` on each call, never keep it in a field.
- Why: deterministic checks of time logic (silent devices, retention, timestamps): the same steps give the same result, whatever the speed of the machine. Chosen by the owner.
- Alternatives rejected:
  - A clock that keeps ticking from `nowMs`: results depend on how long the test takes.
  - One clock per tenant: every job and service would have to pick the tenant clock, for no evaluation gain.
  - `401` before `404` when test mode is off: shows that the endpoints exist.
- Requirement IDs: C-7.2a, C-7.2b

## Q13 — Is the `deviceId` unique per tenant, per site, or globally?
- Assumption: unique per tenant, across all its sites. Registering an id already used in the tenant, on the same or another site, answers `409`. Another tenant may use the same id.
- Why: devices do not send a `siteId`. The gateway batch format is fixed (`{ deviceId, positions }`) and the gateway token carries only the tenant (Q59), so the server finds the site only from `tenant + deviceId`, through the device registration.
- Alternatives rejected:
  - Unique per site: a batch for an id used on two sites would be ambiguous; the track endpoint has no `siteId` either.
  - Globally unique: a `409` would reveal that another tenant has that device, against the 404 rule for other tenants (IDN-02).
- Requirement IDs: API-01, IDN-02, IDN-05, TRK-01
- Status: Accepted

## Q85 — Who may read a device (`GET /api/v1/sites/{siteId}/devices/{deviceId}`)?
- Assumption: Admin only (`CanManage`), the role that registers devices. A device registered on another site of the tenant answers `404`.
- Why: the endpoint is not in the API contract; it lets the Admin read back what was registered. Viewer never sees device ids (PRV-03). Widening it to Safety later is a one-line policy change.
- Requirement IDs: API-01, PRV-03

## Q16 — Are raw positions and the filtered track stored apart?
- Assumption: yes. One append log per record type: raw positions in `Position/` now, the filtered track in `TrackPoint/` later. Both fall under the same retention rules.
- Why: raw positions are kept as received, for audit and replay; the filtered track is derived and can be recomputed. Mixing them would make one log hold two meanings.
- Alternatives rejected:
  - Store only the filtered track: a filter change would lose the raw input it needs.
- Requirement IDs: JDB-06, JDB-09
- Status: Accepted

## Q83 — How are positions stored?
- Assumption: in JSON lines, one file per tenant, device and UTC hour: `<root>/<tenantId>/Position/<deviceId>/<yyyy-MM-ddTHH>.jsonl`. A late position goes to the file of its own hour. Each append writes all its lines and fsyncs each touched file before it returns. A missing final newline is written first before an append, so a torn line is kept on its own line and never rewritten. On read, only newline-terminated lines count, and lines that do not parse are skipped. One lock per device log (tenant, type and key), held until restart. Records with equal timestamps keep append order.
- Why: a one-hour read touches at most two files and about 7,200 lines, which fits the 200 ms target. A kill can only tear the line being written, and the reader never returns a half record. One lock per device keeps the lock count equal to the number of devices.
- Alternatives rejected:
  - Day files per device: a one-hour read would parse up to 86,400 lines.
  - One file per position: 60,000 files in 10 minutes.
- Requirement IDs: JDB-06, JDB-02, JDB-07
- Status: Accepted

## Q05 — When does the API accept a position batch, and what does "accepted" mean?
- Assumption: `202` only after the new positions are appended to the raw position log and fsynced. Positions outside the site perimeter are dropped in memory before any write, never stored and never logged with coordinates; a point on the perimeter boundary is kept. A device whose site has no map or no perimeter gets `202` with every position dropped. The same device and timestamp is stored once: the first stored wins, later copies (in the same batch or a retry) count as duplicates. The check and the write run under the log's per-device lock, so concurrent retries cannot both write. The `202` body is `{ received, stored, duplicates, outsidePerimeter }`. A crash between two hour files of one batch leaves part of it on disk with no `202`; the gateway's retry completes it (at least once). The raw log is the hand-off to processing: ordered processing, drain and restart replay belong to SS-23.
- Why: a retry must have no side effect, and an accepted position must survive a restart.
- Requirement IDs: TRK-01, API-02, PRV-02, NF-05
- Status: Accepted

## Q44 — How is a position batch validated?
- Assumption: a structurally invalid batch is rejected with `400` and nothing is stored: missing field, empty `positions`, more than 5,000 positions, `lat` outside [-90, 90], `lon` outside [-180, 180], `accuracyM` below 0, `timestampMs` below 0 or after year 9999. One bad position rejects the whole batch. Error messages name the field, never the rejected value. A future timestamp is not rejected here: plausibility is the filters' job.
- Why: a structural error is a gateway bug to fix, and dropping silently would hide it; no coordinates in responses (Q62).
- Requirement IDs: API-02, API-05, TRK-01

## Q91 — Does the map PUT need If-Match?
- Assumption: no, it is optional. Absent = replace the latest map (last write wins; the server retries a lost race up to 3 attempts in total, then `409`). Present = checked: a stale version is `412`, and an unreadable value (`abc`, `*`) is `412` too, also on a first upload.
- Why: no endpoint returns the map version, so a client could not upload a map twice without `If-Match`; the contract says only `PUT` → `200 { mapVersion }`.
- Alternatives rejected:
  - `If-Match` required (Q45): a second upload would fail with `412`.
- Requirement IDs: F-6.1, JDB-03
- Status: Accepted

## Q92 — What happens to `mapFile` in the site JSON?
- Assumption: it is accepted and ignored: not stored, not returned. `data/site.json` keeps it as the file format, naming the map file next to it.
- Why: nothing reads it; the map comes from `PUT /map`. Ignoring it keeps old site files and old stored sites working.
- Alternatives rejected:
  - Keep it required: the client has to type a name that sends no file.
- Requirement IDs: F-6.1, API-05
- Status: Accepted

## Q52 — Is the Hangfire dashboard exposed?
- Assumption: no. The dashboard is not mapped in any environment.
- Why: not required; one less surface to secure. Job state is checked through the test endpoint and the stored results.
- Alternatives rejected:
  - Dashboard in test mode for Admin only: extra auth wiring for a page the evaluation does not use.
- Requirement IDs: JOB-01
- Status: Accepted

## Q86 — How do periodic jobs run per site, and how does a retry avoid duplicates?
- Assumption: one Hangfire recurring job per job name, every minute (UTC cron), enqueues one Hangfire job per site of every tenant, with the minute slot (from `SiteSenseContext.Time`, Q38) as an argument. A retry reuses the slot; the job creates its outputs with ids built only from its inputs and `Version = 0`, so a second create is rejected. No production job is registered in SS-31: a probe job in the test project proves the mechanism, and the test endpoint answers 404 for every name until SS-30, SS-32 and SS-33 add theirs.
- Test endpoint: `POST /api/v1/test/jobs/{jobName}/run` runs only the caller's tenant (tenant isolation) and answers `204` when every site is done, like `PUT /test/clock`.
- Storage per host: `Hangfire:Storage` is `Redis` (default, `ConnectionStrings:Redis` required) or `InMemory`, used only by the test host since CI has no Redis (Q72). Redis is published on `127.0.0.1:6379` for `dotnet run`.
- Alternatives rejected:
  - One recurring job per job and site: must be added and removed with every site.
  - A small production job such as `heartbeat`: a fake job name in the API and a document per site every minute.
  - Running every tenant from the test endpoint: an Admin of one tenant would touch another tenant.
- Requirement IDs: JOB-01, JOB-02, C-7.2c
- Status: Accepted

## Q18 — Within `connectToleranceM`: endpoint to endpoint only, or also endpoint to the middle of another path?
- Assumption: endpoint to endpoint only, between different paths, at a distance strictly less than `connectToleranceM`. A short connector edge joins them.
- Why: MAP-03 speaks of endpoints that are less than `connectToleranceM` apart; crossings and T junctions share a vertex by the map rules.
- Requirement IDs: MAP-03
- Status: Accepted

## Q19 — "Shared vertex": exact coordinates or with a tolerance?
- Assumption: two vertices are the same graph node when their local x/y, rounded to 1 cm, are equal.
- Why: identical GeoJSON coordinates always give the same node; the 1 cm grid also absorbs tiny rounding differences.
- Requirement IDs: MAP-03
- Status: Accepted

## Q21 — Do `slopePercent` and `widthM` enter the route cost?
- Assumption: no. Time = length / (`walkingSpeedMps` × `unpavedSpeedFactor` if unpaved × `stepsSpeedFactor` if steps); both factors multiply when both apply. Connectors use the plain walking speed.
- Why: NAV-03 names only speed, surface and steps; slope has no site parameter and no direction.
- Requirement IDs: NAV-03
- Status: Accepted

## Q25 — Is a stretch that only touches the boundary of a `restricted` or `hazard` area traversable?
- Assumption: yes. A stretch (path edge or connector) is not traversable only if it passes through the inside of the area; touching or running along the boundary is allowed.
- Why: a path along a fence is not inside the fenced area; blocking on contact would cut paths that only border it.
- Requirement IDs: NAV-02
- Status: Accepted

## Q86 — When is the route graph cached per map version?
- Assumption: SS-17 only builds the graph (`SiteMap.BuildRouteGraph`); SS-19, its first user, adds the in-memory cache keyed by tenant, site and map version.
- Why: a cache with no caller is untested code.
- Requirement IDs: MAP-03, NAV-01
- Status: Accepted

## Q87 — Are tolerance connectors checked against blocked areas?
- Assumption: yes, with the same test as path edges.
- Why: a connector is walked like any other stretch; skipping the check could route through a hazard edge.
- Requirement IDs: NAV-02
- Status: Accepted

## Q51 — How is the simulator run, and does it set up the site itself?
- Assumption: a console app run with `dotnet run`, plus an optional compose service `simulator` under the profile `sim`, so `docker compose up` still starts only the system. By default it creates the site, uploads the map and registers its devices with the test admin (409 = already there); `--no-setup` turns this off.
- Why: the owner wanted a container, but the system start must not depend on it; positions of unregistered devices are dropped (Q12), so a fresh stack needs the setup.
- Requirement IDs: SIM-01, NF-01
- Status: Accepted

## Q93 — Where on the map do simulated devices walk?
- Assumption: only on `kind=path` lines, using path vertices strictly inside the site perimeter (shared vertices join paths, rounded to 1e-7 degrees). Movement uses a local equirectangular scale in lon/lat; logs go to the console (no logging package), and time comes from a `TimeProvider`.
- Why: 11 path vertices of the real map lie outside the perimeter and ingest would drop them; the simulator references no project, so no shared projection, and the error is negligible at site size.
- Requirement IDs: SIM-01
- Status: Accepted
