# Known issues

Known limits and what to do with more time.

## SS-44 — Device registration
- A device cannot be moved in one call: delete it (`DELETE` with `If-Match`), then register it again on the new site.
- Listing a site's devices is a full scan of the tenant's devices: no index and no cursor paging (SS-11, postponed).
- A `deviceId` is unique per tenant (Q13), so the simulator must give devices on different sites of one tenant different ids (e.g. `<siteId>-001`).

## SS-17 — Route graph
- A path end that touches the middle of another path without a shared vertex is not joined (Q18): only endpoint pairs closer than `connectToleranceM` get a connector.
- Two vertices less than 1 cm apart can stay separate nodes if they fall on each side of a 1 cm grid line (Q19); identical coordinates always join.
- Connectors compare all endpoint pairs (O(n²)) and each edge is tested against every blocked polygon (with a bounding-box reject); fine for hundreds of paths, add a grid index for thousands.
- The graph is not cached yet: SS-19 adds the per-map-version cache (Q86).
