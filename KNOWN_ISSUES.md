# Known issues

Known limits and what to do with more time.

## SS-31 — Hangfire and idempotent jobs
- No periodic job is registered yet: the test endpoint answers 404 for `anomaly-detection`, `silent-devices` and `retention` until SS-33, SS-30 and SS-32 add them.
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
- No retention yet. Position files grow without limit. With more time: delete whole hour files older than the retention period in SS-32.

## SS-22 — Batch ingest
- Each batch re-reads the hour files it touches to find stored timestamps (up to ~3,600 lines per file). With more time, keep an in-memory set of timestamps per open hour file, if the JDB-09 benchmark shows the cost.
- Each batch reads and parses the whole site map to find the perimeter. With more time, cache the projected perimeter by map version.
- The per-device lock is held during reads and fsyncs: one slow batch delays the next batch of the same device (other devices are not blocked).
- A map change does not re-check positions already stored under the old perimeter.
