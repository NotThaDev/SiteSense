# Known issues

Known limits and what to do with more time.

## SS-44 — Device registration
- A device cannot be moved in one call: delete it (`DELETE` with `If-Match`), then register it again on the new site.
- No list of a site's devices yet: a follow-up adds a store `ListAsync` and `Device.GetAllAsync(tenantId, siteId = null)`, without paging (SS-11, postponed).
- A `deviceId` is unique per tenant (Q13), so the simulator must give devices on different sites of one tenant different ids (e.g. `<siteId>-001`).
