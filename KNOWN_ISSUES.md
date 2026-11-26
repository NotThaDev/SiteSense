# Known issues

Entries are added at the end of each story, and when a limit is found.

## SS-12 — Position log

- The directory is not fsynced after a new hour file is created. A power loss right after the first write to a new hour can lose that file. Documents (SS-9) have the same limit. With more time: fsync the directory after creating a file, in both stores.
- On macOS, .NET `fsync` is not `F_FULLFSYNC`, so data may still be in the disk cache after an fsync. A power loss can lose recent appends. With more time: use `F_FULLFSYNC` through a native call on macOS, and test it on real hardware.
- Locks are never evicted until restart. Documents keep one lock per document; logs keep one lock per device log. Memory grows with the number of devices and documents. With more time: remove idle locks with a reference count, or use a lock pool with a fixed size.
- The raw log keeps duplicates. A retried batch or a partial batch is stored twice. Reads return both. With more time: dedup by device and timestamp in SS-22 (TRK-01).
- No cursor paging (SS-11, issue #43, is postponed). A read returns all records in the time range, so a large range returns a large list. With more time: add a cursor to `ReadRangeAsync` when SS-11 (#43) is picked up.
- No retention yet. Position files grow without limit. With more time: delete whole hour files older than the retention period in SS-32.
