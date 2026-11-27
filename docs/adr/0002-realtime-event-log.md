# 0002 — Realtime event log and hub

- Status: Accepted
- Requirement IDs: RTM-01, RTM-03, RTM-05

## Context
Dashboards must see position, alert and occupancy changes as they happen. A client that reconnects
must catch up on what it missed, and it must see only what its role allows. The raw position log
(ADR 0001) is per device and has no order across devices, so it cannot serve as a feed for a tenant.

## Decision
Events go to a per-tenant event log on `IAppendLog` (`events` key). Each event gets a seq, one per
tenant, counted from 1. The log record's `TimestampMs` is `seq × 100` (Q105), so the existing hour files
hold 36,000 seqs each; the wall-clock time is kept in `AtMs` inside the envelope. The last seq per
tenant is kept in memory and loaded once with `ReadLastAsync`. The publisher appends the event, then
sends it inside the tenant lock, so clients get events in seq order. The send has a 5 s cap and its
errors are logged and swallowed; catch-up via `GET /api/v1/events` covers a missed send.
Audience is a named policy per event type, never a role string (IDN-03). Hub groups are one per
site and audience policy: `tenant:{tenant}:site:{site}:audience:{policy}` (Q108). A send goes to the
one group of the type's policy, so a connection in both policies gets each event once.

## Alternatives considered
| Option | Pro | Con | Why not |
|---|---|---|---|
| Publish-time key with a full scan of the log | No counter to keep | Reads every record on each catch-up | Cost grows with the whole history |
| A new seq-indexed log file type | Direct seq lookup | A second storage format to test and to keep crash-safe | The seq is the record key already; `ReadRangeAsync` finds it |
| Role groups with role strings | Simple group names | Roles in code; a new role means new groups | Breaks IDN-03; named policies are the rule |
| Send outside the tenant lock | Publishing never waits on SignalR | Clients can see seq 7 before seq 6 | Order matters more than publish latency; benchmark first (plan Risks) |

## Consequences
- Easier: one catch-up endpoint and one hub send path share the same envelope and audience map.
- Easier: a new event type needs one line in `EventAudience`.
- Harder: a slow client group send delays the next publish of the same tenant (5 s cap).
- Harder: the event log grows without end (`KNOWN_ISSUES.md`; SS-32 can add a sweep).
- Harder: the first publish per tenant per process reads the newest hour file once.
- Revisit: when the benchmark (JDB-09) runs with SS-23 positions, or when the log needs a sweep.
