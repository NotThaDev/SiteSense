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
- Tenant comes from the token claim `tenant_id`. Never take it from the URL or body (IDN-02).
- **Decided:** the gateway token also carries `tenant_id`, added by a hardcoded-claim mapper on each gateway client in the realm. The API reads the tenant the same way for users and gateways, never from the request body.
- Gateway devices use client credentials with scope `positions:write`. One client = one tenant (IDN-05).
- IdP config (realm, clients, roles, test users) is versioned in the repo and loaded at startup (IDN-04).

## Roles

| Role | Can do | Cannot do |
|---|---|---|
| `Admin` | Manage sites, maps, devices. Use test endpoints (when test mode is on). | **Proposed (Q09, open):** see individual positions, tracks or alerts by default (privacy). |
| `Safety` | See individual positions, tracks, alerts. Routing and occupancy. | Manage sites and maps. |
| `Viewer` | See aggregates only: occupancy. Routing. | See positions, tracks or device IDs. |
| Gateway client | Post position batches (`positions:write`). | Anything else. |

## Policies

| Policy | Granted to |
|---|---|
| `CanManage` | Admin |
| `CanSeeIndividuals` | Safety |
| `CanSeeAggregates` | Admin, Safety, Viewer |
| `CanWritePositions` | Gateway client (scope `positions:write`) |
| `TestMode` | Admin, only when test mode is on |

## Endpoint to policy

Request and response shapes: `../sitesense-context/SKILL.md`.

| Method + path | Policy |
|---|---|
| `POST /api/v1/sites` | `CanManage` |
| `PUT /api/v1/sites/{siteId}/map` | `CanManage` |
| `POST /api/v1/sites/{siteId}/devices` | `CanManage` |
| `POST /api/v1/positions` | `CanWritePositions` |
| `POST /api/v1/sites/{siteId}/routes` | `CanSeeAggregates` (covers all user roles; **Proposed (Q09, open):** a separate navigation policy later) |
| `POST /api/v1/sites/{siteId}/evacuation-route` | `CanSeeAggregates` (same note as routes) |
| `GET /api/v1/sites/{siteId}/occupancy` | `CanSeeAggregates` |
| `GET /api/v1/devices/{deviceId}/track` | `CanSeeIndividuals` |
| `GET /api/v1/sites/{siteId}/alerts` | `CanSeeIndividuals` |
| `GET /api/v1/events?after={seq}` | **Proposed:** `CanSeeAggregates`, payload filtered by role |
| `PUT /api/v1/test/clock` | `TestMode` |
| `POST /api/v1/test/jobs/{jobName}/run` | `TestMode` |
| `POST /api/v1/test/drain` | `TestMode` |
| SignalR hub | Authenticated; groups and audiences in the SignalR section |

## Fixed rules

| Case | Result | Source |
|---|---|---|
| Resource belongs to another tenant | `404` (same as not found) | IDN-02 |
| Position batch whose `deviceId` is not registered, or belongs to another tenant | Whole batch rejected with `404`; nothing stored | IDN-02, decided |
| Right tenant, role too low | `403` **Proposed (Q10, open)** | — |
| Authorization in code | Named policies only (`RequireAuthorization("…")`). Never `IsInRole` or role strings in handlers. | IDN-03 |
| Tenant filter | In the store or query layer, applied to every read and write. Not ad hoc in handlers. | IDN-02 |

Test-mode behaviour: see `../sitesense-context/SKILL.md`.

## Test identities

- Tenants: `tenant-a`, `tenant-b`.
- Users, password `Password1!` for all: `admin-a`, `safety-a`, `viewer-a`, `admin-b`, `safety-b`, `viewer-b`.
- Public client `sitesense-verify`: password flow, for verification only.
- `gateway-a` (secret `gateway-secret-a`) → tenant-a; `gateway-b` (secret `gateway-secret-b`) → tenant-b.

Rule on credentials in the repo: `../dotnet-coding/SKILL.md`.

## Privacy

| Rule | Meaning |
|---|---|
| PRV-01 | Devices are pseudonymous. No worker names or identifying data stored. |
| PRV-02 | Positions outside the site perimeter are dropped before storage. Never stored. Never logged with coordinates. |
| PRV-03 | `Viewer` sees aggregates only. Never positions, tracks or device IDs. |
| PRV-04 | Positions are deleted after a per-tenant number of days (retention job). |
| Logs | **Proposed:** no individual coordinates in logs at Information level. | — |

## SignalR

**Proposed:** hub groups: one per tenant and site (`tenant:{tenantId}:site:{siteId}`), plus a role sub-group.

| Event | Audience |
|---|---|
| `PositionUpdated` | `Safety` only |
| `OccupancyChanged` | All user roles |
| `AlertRaised` | **Proposed (inferred from PRV-03):** `Safety` only |
| `ZoneAnomaly` for `Viewer` | **Proposed (Q36, open):** `Viewer` gets only `ZoneAnomaly`, with `deviceId` removed |

Rules:
- A user gets events of its own tenant only (RTM-01).
- Events are emitted only after the state is saved (RTM-03). Rule owner: `../sitesense-context/SKILL.md`.
- Catch-up endpoint policy: see the endpoint → policy table above.

## New endpoint checklist

- [ ] Named policy chosen from the policy table. Policy added to the table in the same PR.
- [ ] Tenant taken from `tenant_id` in the token, not from URL or body.
- [ ] Foreign-tenant resource returns `404`.
- [ ] `Viewer` gets no individual data (no positions, tracks, device IDs).
- [ ] No coordinates or personal data in logs.
- [ ] Isolation test added: `../sitesense-testing/SKILL.md`.
- [ ] Endpoint shape matches the contract: `../sitesense-context/SKILL.md`.
