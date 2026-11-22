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
- Assumption: two. `SiteSense.Domain` holds pure domain code with no package or project references. `SiteSense.Api` references Domain and holds everything else in feature folders (store, auth, sites, tracking, navigation, anomalies, realtime, jobs). One test project, `tests/SiteSense.Tests`. The simulator stays a separate console app.
- Folders: `backend/` holds the backend projects (`SiteSense.Domain`, `SiteSense.Api`), `simulator/` holds the simulator, `tests/` holds the test project. The simulator is a separate app, not part of the backend, so it does not sit next to the backend projects.
- Why: a separate Domain project keeps the domain pure, enforced by the compiler. Separate Application and Infrastructure projects add ceremony and no value in a single-process app (Q01).
- Alternatives rejected:
  - Five projects (Domain, Application, Infrastructure, Api, Simulator) with one test project each: more projects and references to maintain, for boundaries one process does not need.
  - A single server project: nothing stops domain code from using ASP.NET, IO or the clock.
- Requirement IDs: NF-01, NF-02
- Status: Accepted
