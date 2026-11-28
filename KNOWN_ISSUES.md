# Known issues

Known limits and what to do with more time.

## SS-31 — Hangfire and idempotent jobs
- The test endpoint answers 404 for `anomaly-detection` and `silent-devices` until SS-33 and SS-30 add them.
- The recurring trigger fires on real time, while the slot comes from the simulated clock. With a frozen clock, every real minute gives the same slot, so the jobs create nothing new until the clock moves. This is intended in test mode.
- An id built from long site and device ids can exceed the 128-character store limit, and ids that contain `_` can collide. With more time: hash the id parts.
- `/health` does not check Redis (Q71). With more time: add a Redis health check.

## SS-44 — Device registration
- A device cannot be moved in one call: delete it (`DELETE` with `If-Match`), then register it again on the new site.
- Listing a site's devices is a full scan of the tenant's devices: no index and no cursor paging (SS-11, #43, postponed).
- A `deviceId` is unique per tenant (Q13), so the simulator must give devices on different sites of one tenant different ids (e.g. `<siteId>-001`).

## SS-12 — Position log
- The directory is not fsynced after a new hour file is created. A power loss right after the first write to a new hour can lose that file. Documents (SS-9) have the same limit. With more time: fsync the directory after creating a file, in both stores.
- On macOS, .NET `fsync` is not `F_FULLFSYNC`, so data may still be in the disk cache after an fsync. A power loss can lose recent appends. With more time: use `F_FULLFSYNC` through a native call on macOS, and test it on real hardware.
- Locks are never evicted until restart. Documents keep one lock per document; logs keep one lock per device log. Memory grows with the number of devices and documents. With more time: remove idle locks with a reference count, or use a lock pool with a fixed size.
- The raw log keeps duplicates. A retried batch or a partial batch is stored twice. Reads return both. With more time: dedup by device and timestamp in SS-22 (TRK-01).
- No cursor paging (SS-11, issue #43, is postponed). A read returns all records in the time range, so a large range returns a large list. With more time: add a cursor to `ReadRangeAsync` when SS-11 (#43) is picked up.

## SS-32 — Position retention
- The raw `Position` log and the `TrackPoint` log are both purged with the same cutoff in `RetentionJob`.
- Empty device folders stay after their last hour file is deleted, and so does the device's lock. With more time: remove an empty folder and its lock under that lock.
- The hourly retention schedule is off on the in-memory test host (`Hangfire:Storage=InMemory`); tests run it through the test endpoint.

## SS-22 — Batch ingest
- Each batch re-reads the hour files it touches to find stored timestamps (up to ~3,600 lines per file). With more time, keep an in-memory set of timestamps per open hour file, if the JDB-09 benchmark shows the cost.
- Each batch reads and parses the whole site map to find the perimeter. With more time, cache the projected perimeter by map version.
- The per-device lock is held during reads and fsyncs: one slow batch delays the next batch of the same device (other devices are not blocked).
- A map change does not re-check positions already stored under the old perimeter.

## SS-17 — Route graph
- A path end that touches the middle of another path without a shared vertex is not joined (Q18): only endpoint pairs closer than `connectToleranceM` get a connector.
- Two vertices less than 1 cm apart can stay separate nodes if they fall on each side of a 1 cm grid line (Q19); identical coordinates always join.
- Connectors compare all endpoint pairs (O(n²)) and each edge is tested against every blocked polygon (with a bounding-box reject); fine for hundreds of paths, add a grid index for thousands.
- The graph is not cached yet: SS-19 adds the per-map-version cache (Q86).
## SS-23 — Ordered processing and drain
- Drain waits until the tenant has zero pending runs, so a constant load (a running simulator) can delay it. With more time: a per-tenant sequence number, wait only for runs queued before the drain.
- A late position replays every later point of that device; the track log grows by one revision per late batch. Retention removes old hours.
- A retried batch made only of duplicates still marks the device and causes a needless replay (same output, newer revision).
- A failed run leaves the device marked; it is retried only by its next batch or a restart, and drain answers `500` until then.
- Ingest writes the marker doc twice per batch (two extra fsyncs); not measured against the ingest target yet.

## SS-46 — Admin UI and browser login
- The Keycloak realm is imported only when the realm does not exist yet: after pulling this change, run `docker compose up -d --force-recreate keycloak` once, or the `sitesense-ui` client is missing and login fails.
- Data Protection keys are kept in memory: an API restart invalidates the login cookies, so users log in again.
- Logout is a `GET` (browser navigation): another page could log the user out. It cannot change data.
- The cookie keeps the roles of the login; a role change in Keycloak applies at the next login (cookie life: 1 hour, sliding).
- The API builds the OIDC redirect URI from the `Host` header that nginx forwards; a different UI port needs a matching redirect URI in the realm.
- No silent token renewal; the API does not call Keycloak with the user's tokens after login.
- The map view uses the public OpenStreetMap tile server: fine for development and light interactive use only. A production deployment needs its own tile server or a tile provider.

## SS-49 — Live map
- The login cookie lives 1 hour and an open socket does not slide it: when it expires the hub closes and the user logs in again.
- Nothing moves until the producers exist: positions (SS-50), then the occupancy and alert stories. Until then the map shows the structure and the status chip.
- The map still uses the public OpenStreetMap tile server (see SS-46).

## SS-50 — Position events
- A crash after the processing save and before the publish loses those `PositionUpdated` events (at most once, Q115). The track is correct; catch-up cannot return events never logged.
- A map change does not re-assign old track points: they keep the `areaId` of the old map until their range is replayed (SS-25).
- Overlapping areas are not validated by the map import: the first in map order wins (Q119).
- A late batch older than the retention cutoff finds no older track records and is published as new; retention deletes it at the next run.

## SS-28 — AreaIntrusion alarms
- The detector reads the filtered track (SS-24): dropped jumps never count, and each kept point is averaged with the previous one, so an exit or entry can show one sample later than the raw GPS.
- A late point that splits a stay inside an area can make a second alarm for the later entry (a new entry time is a new id).
- A replay under a changed map can add an alarm for an area that did not exist when the points were first processed.
- Alerts are read for the whole site and filtered in memory (`Alert.ListAsync`); slow with many alarms. With more time: one document per site and day.
- An alarm appears 30 s after the entry (dwell, Q123); `eventTimeMs` is still the entry instant.
- A stay already inside at the start of the 10 minute lookback is treated as starting at the first walked point; a duplicate alarm is possible only with positions sparser than the lookback.
- An alarm of a device that goes silent stays `Active` (no exit is seen).
- A replay can publish a duplicate `AlertUpdated` (the save is idempotent, the live event is at most once but not deduplicated).

## Delivery — not implemented
These requirements are not in the delivered version. The API answers `404` for their endpoints.
- Navigation: `POST /sites/{id}/routes` and `/evacuation-route` (NAV-01…04, API-03). The route graph exists (SS-17, ADR 0001); the router, the snapping and the per-map-version graph cache do not. With more time: A* on the existing graph, with time cost from the site parameters.
- Tracking filter and area hysteresis (TRK-03, TRK-04): stored track points are ordered and deduplicated, but not filtered, and `areaId` is `null`. With more time: a speed gate scaled by `accuracyM` and the real time gap, an accuracy-weighted smoother, and an enter/exit margin on area borders.
- `GET /devices/{id}/track`, `GET /sites/{id}/occupancy` (API-04, EVT-01): the stored data exists (track log, event log), but the query endpoints do not.
- AI: `ZoneAnomaly` (AI-01, AI-03) and `SuddenImmobility` (AI-02) are not implemented. ADR 0004 records the parameters we would use. The `anomaly-detection` and `silent-devices` jobs answer `404` on the test endpoint (TRK-06 not implemented).
- Secondary index and cursor paging (JDB-05, JDB-08; SS-11) and the JDB-09 benchmark (SS-13, no `docs/RESULTS.md`).
- NF-02 test gaps that follow from the above: there are no tests for navigation, the jump and border-noise filters, or AI detection.
