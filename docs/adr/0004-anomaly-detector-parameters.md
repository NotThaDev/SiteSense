# 0004 — Anomaly detector parameters

- Status: Proposed (not implemented: see `KNOWN_ISSUES.md`, Delivery)
- Requirement IDs: AI-01, AI-03, JOB-01, JOB-02

## Context
AI-01 asks for a `ZoneAnomaly` alert when the occupancy of an area changes in an unusual way. It
must use the ML.NET time-series detectors, and normal small changes must not raise an alert. The
input is one series per area: the device count per minute, built from the same rule as
`GET /occupancy` (last valid position in the area, less than 5 minutes old). The job runs every
minute per site (JOB-01). A retry must not create a second alert (JOB-02). Counts are small
integers, so a change of one person is a large relative change.

## Decision
We use SR-CNN in batch mode (`DetectEntireAnomalyBySrCnn`) on the last 60 minutes of each area's
series. One `MLContext` is created at startup and shared. The call keeps no state between runs,
so jobs for different sites can run at the same time with no prediction engine to lock (AI-03).
An alert is raised only when all of these hold for the newest minute:

| Parameter | Default | Why |
|---|---|---|
| `WindowMinutes` | 60 | Enough history for a stable baseline. Short enough to follow a shift change. |
| `MinHistoryMinutes` | 30 | Below this, the baseline is noise: no alert. |
| `Threshold` | 0.35 | SR-CNN score cut-off, starting value from the ML.NET samples. Not tuned yet. |
| `Sensitivity` | 90 | Width of the expected band in `AnomalyAndMargin` mode. |
| `Period` | 0 | No daily cycle inside a 60-minute window. |
| `MinDelta` | 3 people | Absolute guard: a change of 1–2 people is never an anomaly. |
| `MinRelativeDelta` | 0.5 | Count ≥ expected × 1.5. Also needed for large areas. |
| Direction | up only | A crowd is the risk. A sudden drop is left to evacuation monitoring. |

All values sit under `Anomaly:ZoneAnomaly` in configuration and are checked at startup. The alert
id is `ZoneAnomaly:{siteId}:{areaId}:{minuteMs}`. The job writes it with insert-if-absent, so a
retry or a second run for the same minute finds the alert already there (JOB-02).

## Alternatives considered
| Option | Pro | Con | Why not |
|---|---|---|---|
| SSA spike (`DetectSpikeBySsa`) | Follows trends, streaming | Needs training per area and a stateful `TimeSeriesPredictionEngine` per area, one writer at a time | More state to keep, lock and rebuild after a restart |
| IID spike | Simplest | Expects no trend: a shift change looks like a spike | Too many false alerts |
| Fixed threshold (count > capacity) | Easy to explain | Not ML, ignores what is normal for the area | AI-01 asks for the ML.NET detectors. Kept as a possible second rule. |

## Consequences
- Easier: no model state to persist or rebuild after a restart, and the job is idempotent by
  minute.
- Harder: each run re-scores 60 points per area. That is cheap for tens of areas, to be measured for
  hundreds.
- Revisit: tune `Threshold` and `MinDelta` on simulator scenarios (SIM-02 crowding). Add a weekly
  `Period` once there are days of data.
