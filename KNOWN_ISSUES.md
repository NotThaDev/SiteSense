# Known issues

Known limits and what to do with more time.

## SS-44 — Device registration
- A device cannot be moved in one call: delete it (`DELETE` with `If-Match`), then register it again on the new site.
- Listing a site's devices is a full scan of the tenant's devices: no index and no cursor paging (SS-11, postponed).
- A `deviceId` is unique per tenant (Q13), so the simulator must give devices on different sites of one tenant different ids (e.g. `<siteId>-001`).

## SS-46 — Admin UI and browser login
- The Keycloak realm is imported only when the realm does not exist yet: after pulling this change, run `docker compose up -d --force-recreate keycloak` once, or the `sitesense-ui` client is missing and login fails.
- Data Protection keys are kept in memory: an API restart invalidates the login cookies, so users log in again.
- Logout is a `GET` (browser navigation): another page could log the user out. It cannot change data.
- The cookie keeps the roles of the login; a role change in Keycloak applies at the next login (cookie life: 1 hour, sliding).
- No silent token renewal; the API does not call Keycloak with the user's tokens after login.
