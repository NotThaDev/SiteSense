# 0001 — Position storage

- Status: Accepted
- Requirement IDs: JDB-06, JDB-02, JDB-04, JDB-07, JDB-09

## Context
GPS devices send positions all the time. The target is 100 positions per second for 10 minutes
(60,000 records) with none lost, and a one-hour track of one device must load in under 200 ms.
The document store (SS-9) saves one whole JSON file per document. A position never changes and
has no version, so it does not fit that model. Stored as documents, each position is one file, or
one track file is rewritten on every batch. Nothing can append yet.

## Decision
Add a second persistence interface, `IAppendLog`, next to `IDocumentStore` (Q75). `FileAppendLog`
implements it. Records are JSON lines in one file per tenant, device and UTC hour:
`<root>/<tenantId>/<TypeName>/<key>/<yyyy-MM-ddTHH>.jsonl`. An append writes all lines of one call
and fsyncs each touched file before it returns. Only newline-terminated lines count on read, and
lines that do not parse are skipped. Before an append, a missing final newline is written first, so
a torn line stays on its own line and is never rewritten. Each device log has one lock. The first
record type is `Position`. Raw positions and the filtered track use separate logs (Q16). Records
with equal timestamps keep append order.

## Alternatives considered
| Option | Pro | Con | Why not |
|---|---|---|---|
| One file per position | Simple write, no shared file | 60,000 files in 10 minutes; 3,600 files to open for one hour of track | File count and open cost grow with the load |
| One track document rewritten per batch | One file per device, easy to read | The file grows, and every batch rewrites all of it | Write cost grows with the history |
| One file per tenant and day (all devices) | Few files | A one-hour read of one device parses every device's lines of that day; one lock per file for all devices | Reads and locks would mix devices |
| Day files per device | Few files per device | A one-hour read parses up to 86,400 lines | Fails the 200 ms target for one hour of track; changed to hour files on plan review |
| A single `FileStore` refactor shared with documents | One storage class | Mixes whole-file replace with append; a larger change to SS-9 code | Two small classes that share helpers are simpler to test |
| Group commit fsync (periodic flush) | Fewer fsyncs under load | Records acknowledged before they are on disk; more code | JDB-02 needs every returned append to survive a kill; one fsync per call is enough at this load |

## Consequences
- Easier: late positions land in their own hour file, with no special case.
- Easier: a one-hour read opens at most two files and parses about 7,200 lines.
- Easier: retention (SS-32) deletes whole hour files.
- Harder: no index for queries across devices; each query is one device log.
- Harder: the raw log keeps duplicates from retries and partial batches until SS-22 dedups them.
- Harder: after a new hour file is created, the directory is not fsynced. On macOS, .NET `fsync`
  is not `F_FULLFSYNC`. The guarantee covers a process kill (`kill -9`), not a power loss.
- Revisit: when SS-22 lands (dedup rules), when SS-11 (#43) needs cursor paging, and when SS-13 measures
  the load targets.
- Write-once ingest (SS-22): AppendNewAsync reads the stored timestamps of each touched hour file under the per-device lock and appends only new ones, so a retried batch has no side effect.
