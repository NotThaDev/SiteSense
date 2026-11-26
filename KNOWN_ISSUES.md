# Known issues

Known limits and what to do with more time.

## SS-44 — Device registration
- A device cannot be moved in one call: delete it (`DELETE` with `If-Match`), then register it again on the new site.
- Listing a site's devices is a full scan of the tenant's devices: no index and no cursor paging (SS-11, postponed).
- A `deviceId` is unique per tenant (Q13), so the simulator must give devices on different sites of one tenant different ids (e.g. `<siteId>-001`).
