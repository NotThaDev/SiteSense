---
name: sitesense-security
description: >-
  SiteSense identity, roles, authorization policies, tenant isolation, privacy rules,
  test identities and SignalR audiences. Use when adding or changing an endpoint, a
  hub event, a log line or a stored personal field, or when asking who may see or do
  what.
---

# SiteSense security

## Identity basics

- OIDC/OAuth2. JWTs are validated (IDN-01).
- Tenant comes from the token claim `tenant_id`, read with `User.TenantId()` (`Api/Auth/SiteSenseClaims.cs`). Never take it from the URL or body (IDN-02).
- **Decided (Q79):** a token without exactly one valid `tenant_id` is rejected with `401`. Issuer, keys and config: Q02.
- **Decided (Q80):** every endpoint needs a valid token (fallback policy) unless it is `AllowAnonymous`: only `/health`, `/openapi/v1.json`, `/scalar`.
- **Decided:** the gateway token also carries `tenant_id`, added by a hardcoded-claim mapper on each gateway client in the realm. The API reads the tenant the same way for users and gateways, never from the request body.
- Gateway devices use client credentials with scope `positions:write`. One client = one tenant (IDN-05).
- IdP config (realm, clients, roles, test users) is versioned in the repo and loaded at startup (IDN-04).

## Roles

| Role | Can do | Cannot do |
|---|---|---|
| `Admin` | Manage sites, maps, devices. Use test endpoints (when test mode is on). | See individual positions, tracks or alerts (**Decided, Q09:** roles are separate, for privacy). |
| `Safety` | See individual positions, tracks, alerts. Routing and occupancy. | Manage sites and maps. |
| `Viewer` | See aggregates only: occupancy. Routing. | See positions, tracks or device IDs. |
| Gateway client | Post position batches (`positions:write`). | Anything else. |
| UI client `sitesense-ui` | Public Keycloak client used only by the API for the browser login (Auth Code + PKCE). Tokens stay in the API; the browser gets an `HttpOnly` cookie. | Be called by the browser directly. |

**Decided (Q61):** each user has exactly one role. No user combines roles and there is no super user. The policies do not depend on this, so allowing several roles later needs only a realm change and an `ASSUMPTIONS.md` update.

## Policies

Names, roles and the scope string are constants in `Api/Auth/SiteSensePolicies.cs`; policies are registered in `AuthSetup.AddSiteSenseAuth`.

| Policy | Granted to |
|---|---|
| `CanManage` | Admin |
| `CanSeeIndividuals` | Safety |
| `CanSeeAggregates` | Admin, Safety, Viewer |
| `CanWritePositions` | Gateway client (scope `positions:write`, exact match in the space-separated `scope` claim) |
| `TestMode` | Admin, only when test mode is on; with test mode off the whole `/api/v1/test` prefix is 404 for everyone (Q38) |

## Endpoint to policy

Request and response shapes: `../sitesense-context/SKILL.md`.

| Method + path | Policy |
|---|---|
| `POST /api/v1/sites` | `CanManage` |
| `GET /api/v1/sites` | `CanManage` (Q109) |
| `PUT /api/v1/sites/{siteId}/map` | `CanManage` |
| `POST /api/v1/sites/{siteId}/devices` | `CanManage` |
| `GET /api/v1/sites/{siteId}/devices` | `CanManage` (Q85) |
| `GET /api/v1/sites/{siteId}/devices/{deviceId}` | `CanManage` (Q85) |
| `DELETE /api/v1/sites/{siteId}/devices/{deviceId}` | `CanManage` |
| `POST /api/v1/positions` | `CanWritePositions` |
| `POST /api/v1/sites/{siteId}/routes` | `CanSeeAggregates` (covers all user roles) |
| `POST /api/v1/sites/{siteId}/evacuation-route` | `CanSeeAggregates` (same note as routes) |
| `GET /api/v1/sites/{siteId}/occupancy` | `CanSeeAggregates` |
| `GET /api/v1/devices/{deviceId}/track` | `CanSeeIndividuals` |
| `GET /api/v1/sites/{siteId}/alerts` | `CanSeeIndividuals` |
| `GET /api/v1/events?after={seq}` | `CanSeeAggregates`; items filtered by audience policy (Q35, Q108) |
| `PUT /api/v1/test/clock` | `TestMode` |
| `POST /api/v1/test/jobs/{jobName}/run` | `TestMode` |
| `POST /api/v1/test/drain` | `TestMode` |
| `GET /api/v1/me` | Authenticated (fallback policy, Q78) |
| `GET /api/v1/auth/login`, `GET /api/v1/auth/logout` | Anonymous (browser login, Q110) |
| SignalR hub | Authenticated; groups and audiences in the SignalR section |

## Fixed rules

| Case | Result | Source |
|---|---|---|
| Resource belongs to another tenant | `404` (same as not found) | IDN-02 |
| Position batch whose `deviceId` is not registered, or belongs to another tenant | Whole batch rejected with `404`; nothing stored | IDN-02, decided |
| Right tenant, role too low | `403` Forbidden | Q10, decided |
| Valid token whose `tenant_id` is not in the `Tenants` configuration | `403` ProblemDetails, on every endpoint | Q14, decided |
| Authorization in code | Named policies only (`RequireAuthorization("…")`). Never `IsInRole` or role strings in handlers. | IDN-03 |
| Tenant filter | In the store or query layer, applied to every read and write. Not ad hoc in handlers. | IDN-02 |
| Browser (admin UI) call | Cookie from the BFF login; a call without `Authorization: Bearer` must send `X-CSRF: 1`, else `403` ProblemDetails | Q111, decided |

Test-mode behaviour: see `../sitesense-context/SKILL.md`.

## Test identities

- Tenants: `tenant-a`, `tenant-b`.
- Users, password `Password1!` for all: `admin-a`, `safety-a`, `viewer-a`, `admin-b`, `safety-b`, `viewer-b`.
- Public client `sitesense-verify`: password flow, for verification only.
- `gateway-a` (secret `gateway-secret-a`) → tenant-a; `gateway-b` (secret `gateway-secret-b`) → tenant-b.
- Keycloak admin console: `admin` / `admin`, compose defaults, local only (Q70).

Rule on credentials in the repo: `../dotnet-coding/SKILL.md`.

## Privacy

| Rule | Meaning |
|---|---|
| PRV-01 | Devices are pseudonymous. No worker names or identifying data stored. |
| PRV-02 | Positions outside the site perimeter are dropped before storage. Never stored. Never logged with coordinates. |
| PRV-03 | `Viewer` sees aggregates only. Never positions, tracks or device IDs. |
| PRV-04 | Positions are deleted after a per-tenant number of days (retention job). |
| Logs | Individual coordinates only at `Debug` level (off by default), never at `Information` or above. Positions outside the perimeter: never, at any level (PRV-02). | Q62, decided |

## SignalR

**Decided (Q108, changes Q63):** one hub group per tenant, site and audience policy: `tenant:{tenantId}:site:{siteId}:audience:{policy}`.

- On connect the hub requires `CanSeeAggregates`; tenant and role come from the token only. Browsers send the token as `access_token` in the query string, read only for the hub path.
- The client asks to join a site; the server checks the site belongs to the caller's tenant (otherwise rejects), then adds the connection to its role group.
- Events are sent once per target group; SignalR delivers to each connection. A Viewer is never in a Safety group, so individual data cannot reach it.
- Connections close when the token expires (`CloseOnAuthenticationExpiration`); the client reconnects with a new token and joins again.

| Event | Audience | Policy |
|---|---|---|
| `PositionUpdated` | `Safety` only | `CanSeeIndividuals` |
| `OccupancyChanged` | All user roles | `CanSeeAggregates` |
| `AlertRaised` | `Safety` only, every alert type including `ZoneAnomaly` (**Decided, Q36**) | `CanSeeIndividuals` |

Rules:
- A user gets events of its own tenant only (RTM-01).
- Events are emitted only after the state is saved (RTM-03). Rule owner: `../sitesense-context/SKILL.md`.
- Catch-up endpoint policy: see the endpoint → policy table above.

## New endpoint checklist

- [ ] Named policy chosen from the policy table. Policy added to the table in the same PR.
- [ ] Tenant taken from `tenant_id` in the token, not from URL or body.
- [ ] Foreign-tenant resource returns `404`.
- [ ] `Viewer` gets no individual data (no positions, tracks, device IDs).
- [ ] No personal data in logs; coordinates only at `Debug`.
- [ ] Isolation test added: `../sitesense-testing/SKILL.md`.
- [ ] Endpoint shape matches the contract: `../sitesense-context/SKILL.md`.
