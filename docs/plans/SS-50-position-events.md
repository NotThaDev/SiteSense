# SS-50 — Publish `PositionUpdated` from position processing

- Status: Approved
- Issue: #139
- Plan PR: #140   Implementation PR: #<n>

## Problem

The real-time hub exists (SS-35: `/api/v1/realtime`, groups per tenant, site and audience; event log with
`GET /api/v1/events?after=`), but nothing publishes events. Position processing (SS-23) writes a pass-through
`TrackPoint` per accepted position and stops there. A Safety user connected to the hub never receives
`PositionUpdated`, so no client (the demo client, the map in SS-49) can show devices moving.

```mermaid
flowchart LR
  G["Gateway"] -->|"POST /positions"| I["Ingest"] --> P["PositionProcessor"] --> T["TrackPoint log"]
  H["SiteHub (SS-35)"] -.->|"nothing to send"| C["Safety client"]
```

## Goal

After a track point is persisted, the processor publishes one `PositionUpdated` event for it, to the
device's site, Safety audience only (EventAudience, Q36). Each position is published **once**, also when a
late batch makes the processor replay. Occupancy and alerts are out of scope: they need the area of each
point (SS-25) and the alert rules; those stories publish `OccupancyChanged` and `AlertRaised`.

## Solution

### Payload (fixed here, Q115)

The same fields as a track point in the contract (`GET /devices/{id}/track` → `points[]`), plus the device:

```json
{ "seq": 42, "type": "PositionUpdated", "siteId": "site-01", "atMs": 1793268000500,
  "data": { "deviceId": "dev-001", "timestampMs": 1793268000000, "lat": 45.5001, "lon": 9.2004, "areaId": null } }
```

`areaId` is `null` until SS-25 assigns areas; the filter (SS-24) changes the coordinates, not the shape.

### When and how often

```mermaid
sequenceDiagram
  participant P as PositionProcessor.RunAsync
  participant T as TrackPoint log
  participant D as DeviceProcessing doc
  participant E as EventPublisher
  P->>D: load doc: saved CursorMs and Revision
  P->>T: read track records in [from, cursor + 1) with Revision <= saved Revision
  P->>T: append the run's points (new revision on replay)
  P->>D: SaveAsync (version check)
  alt conflict
    P->>P: reload and run again (nothing published yet)
  else saved
    P->>E: PublishAsync PositionUpdated, one per point whose timestamp was not processed before, in timestamp order
  end
```

- **New vs replayed, from the saved state only:** a raw position is new when `timestamp > saved CursorMs`, or
  (late run) when no track record with that timestamp and `Revision <= saved Revision` exists. The read uses the
  raw log records (`[from, cursor + 1)`, the log range is end-exclusive), not `TrackPoint.ReadAsync`, which
  folds to the latest revision and would hide that a point came from a failed attempt.
- **Why the saved state:** points appended by an attempt whose save failed (conflict, crash, error) carry
  `saved Revision + 1` on a late run, or lie after the saved cursor on a normal run, so they never count as
  published. A retry or a restart therefore computes the same set and publishes it once.
- **After persistence (RTM-03):** events go out only after `DeviceProcessing.SaveAsync` succeeds.
- **Publish errors:** `EventPublisher.PublishAsync` throws when the event-log append fails (only the SignalR
  send is swallowed). The processor wraps each publish in `try/catch`, logs and goes on, so the device is not
  counted as failed and drain does not return 500; `OperationCanceledException` from shutdown is rethrown.
- **Site:** `Device.GetAsync(tenantId, deviceId)` once per run; `SiteId` is `init`-only, so a device never changes
  site; a device deleted meanwhile publishes nothing.
- **At most once:** a crash after the save and before the publish loses those events. The track is still correct;
  catch-up cannot return what was never logged. Recorded in KNOWN_ISSUES.
- **Retention edge:** a late batch older than the retention cutoff finds no older track records and would be
  published as new; retention deletes it at the next run anyway. Recorded in KNOWN_ISSUES.
- `RunAsync` becomes an instance method; `PositionProcessor` gets `IRealtimePublisher` through its constructor
  (both are singletons, registered in `Program.cs`).
- `POST /api/v1/test/drain` waits for processing, which now includes the publish, so tests see the events.

## Steps

| # | Step | Files | Done when | PR |
|---|---|---|---|---|
| 1 | `PositionUpdatedData(DeviceId, TimestampMs, Lat, Lon, AreaId)` record next to `EventTypes` | `Realtime/TenantEvent.cs` | build passes | impl |
| 2 | `PositionProcessor`: inject `IRealtimePublisher`, `RunAsync` instance method; compute the new set from the saved state as above; after a successful save, publish it in timestamp order, each publish in `try/catch` (rethrow cancellation) | `Services/PositionProcessor.cs` | build passes | impl |
| 3 | Tests (see Tests) | `tests/SiteSense.Tests/...` processing / realtime tests | all pass | impl |
| 4 | Docs: ASSUMPTIONS Q115 (payload, once per position, after persistence, at most once on crash); KNOWN_ISSUES (crash window; `areaId` null until SS-25); `docs/simulator.md` or README one line: run the simulator and watch `PositionUpdated` | docs | docs match the code | impl |

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Replay publishes old positions again | duplicate dots / events | known-timestamp check; test with a late batch |
| 100 positions/s × events | event log and SignalR load | one small JSON per position (Q34, no throttle); the event log is append-only |
| Publish failure (log write) | the run would throw after the save, drain 500 | the processor catches per event and logs; the track is already saved |
| Slow publish holds the device run | backlog | publish is an append plus a fire-and-forget send (SS-35) |

## Tests

- `PositionEventsTests`: `Batch_PublishesOnePositionUpdatedPerPosition` (via the test publisher or `GET /events` after drain, data fields as in Q115);
  `DuplicateBatch_PublishesNothingNew`; `LateBatch_PublishesOnlyTheLatePositions`;
  `Events_GoToTheDeviceSite`; `Viewer_DoesNotSeePositionUpdatedInEvents` (audience filter, Q36);
  `ConcurrentMark_DoesNotPublishTwice` (a mark during the run forces the retry path);
  `FailedSave_RetryPublishesTheLatePositionsOnce` (save fails once after the append on a late run: the late positions are published exactly once);
  `PublishFailure_DoesNotFailTheRun` (a publisher that throws: drain still 200, track saved).

## Open questions

| # | Question | Options (recommended first) | Decision |
|---|---|---|---|
| 1 | Which events now | `PositionUpdated` only / also a simple occupancy | `PositionUpdated` only (user, 2026-11-27) |
| 2 | Branch | Standalone from `main` / on top of the UI stack | From `main` (user, 2026-11-27) |
| 3 | Crash between save and publish | At most once, documented (a failed attempt before the save is covered by the saved-state rule) / publish before save (at least once, clients drop repeated `deviceId + timestampMs`) | At most once (user, 2026-11-27) |

## Changelog

| Date | Change | Why (review comment or finding) |
|---|---|---|
| 2026-11-27 | First version | User: publish the real-time signal so the map UI can work |
| 2026-11-27 | New set from the saved state (cursor, revision), raw log read with end-exclusive range, per-event try/catch, instance method, retention edge, more tests | Sonnet plan validation |
| 2026-11-27 | Open question 3 decided; Approved | User review |
