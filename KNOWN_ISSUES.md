# Known issues

Known limits and what to do with more time.

## SS-44 — Device registration
- A device cannot be deleted or moved to another site. With more time: `DELETE /api/v1/sites/{siteId}/devices/{deviceId}` with `If-Match`, then register it again on the new site.
- No list of a site's devices. With more time: `GET /api/v1/sites/{siteId}/devices`, which needs a secondary index by site (SS-11, postponed).
- A `deviceId` is unique per tenant (Q13), so the simulator must give devices on different sites of one tenant different ids (e.g. `<siteId>-001`).
