# Device simulator

Console app `simulator/SiteSense.Simulator`. It moves N test devices along the paths of the site map and sends their positions to the API as the gateway. Use it to fill a stack with realistic GPS traffic.

It talks to the API over HTTP only. It is a separate app, not part of the backend (see [architecture](architecture.md)).

## How to run

Host, with the stack up (`docker compose up`):

```bash
dotnet run --project simulator/SiteSense.Simulator -- --devices 5 --duration-s 30
```

Docker. Plain `docker compose up` does not start it; the service sits under the profile `sim`:

```bash
docker compose --profile sim up simulator
```

The compose service sets:

| Env var | Value in compose |
|---|---|
| `SIM_API` | `http://api:8080` |
| `SIM_TOKEN_URL` | `http://keycloak:8081/realms/sitesense/protocol/openid-connect/token` |
| `SIM_SITE` | `/data/site.json` |
| `SIM_MAP` | `/data/map.geojson` |

It mounts `./data` read-only. The token issuer stays `http://localhost:8081` (`KC_HOSTNAME`), so the API accepts the token (Q02).

While it runs, every processed position is published as a `PositionUpdated` event: connect to the hub (`/api/v1/realtime`) as Safety, or poll `GET /api/v1/events?after=0`, to watch the devices move.

## Setup

Setup runs by default. Turn it off with `--no-setup`.

1. Log in as `admin-a` (password grant, public client `sitesense-verify`).
2. Create the site from `data/site.json`.
   - 201: upload the map from `data/map.geojson`.
   - 409: the site exists, the map upload is skipped.
3. Register each device. 409: already registered.

Setup is needed because positions of unregistered devices are dropped (Q12). A fresh stack has no devices until setup runs.

The credentials are the committed test credentials of the realm. They are never production secrets.

## Options

Each option takes an argument. An env var is read when the argument is missing; the argument wins.

| Option | Env var | Default | Meaning |
|---|---|---|---|
| `--api` | `SIM_API` | `http://localhost:8080` | API base URL |
| `--token-url` | `SIM_TOKEN_URL` | `http://localhost:8081/realms/sitesense/protocol/openid-connect/token` | Token endpoint |
| `--client` | none | `gateway-a` | Gateway client (client credentials, scope `positions:write`) |
| `--secret` | `SIM_SECRET` | `gateway-secret-a` | Gateway client secret |
| `--admin-user` | none | `admin-a` | Test admin for setup |
| `--admin-password` | `SIM_ADMIN_PASSWORD` | `Password1!` | Test admin password for setup |
| `--site` | `SIM_SITE` | `data/site.json` | Site file created in setup |
| `--map` | `SIM_MAP` | `data/map.geojson` | Map file uploaded in setup |
| `--devices` | none | `10` | Number of simulated devices |
| `--device-prefix` | none | `sim-` | Prefix of device IDs |
| `--interval-s` | none | `1` | Seconds per fix: one fix per device per interval |
| `--batch-s` | none | `5` | Seconds between batches of one device |
| `--duration-s` | none | `0` | Run length in seconds; `0` = until Ctrl+C |
| `--seed` | none | `42` | Random seed |
| `--start-ms` | none | now | Start time, Unix ms |
| `--no-setup` | none | off | Skip setup |

## Movement model

- Devices walk only on the `kind=path` lines of the map. Only path vertices inside the site perimeter are used (Q93).
- Each device has its own `Random(seed + index)`.
- Speed is fixed per device: 1.3 ± 0.3 m/s (0.8 to 1.8 m/s).
- A device walks edge to edge. At a node it picks a random neighbour, but not the one it came from, unless the node is a dead end.
- Each fix is the true point plus Gaussian noise with sigma = accuracy / 2.
- Accuracy is uniform between 3 and 8 m per fix.
- Timestamp: `start + tick × interval`.

## Sending and retries

- One batch per device every `--batch-s` seconds.
- Response handling:

| Response | Action |
|---|---|
| 202 | Clear the buffer |
| 401 | Refresh the token and retry once |
| 5xx or network error | Keep the buffer for the next round |
| Other 4xx | Log and drop |

- Ingest is idempotent, so resent positions are safe.
- One log line per round: devices, positions sent, failures.
- Ctrl+C sends what is left in the buffer, then exits.

## Reproducibility

With a fixed `--seed` and `--start-ms`, two runs produce identical batches. Without `--start-ms`, the start is the current time, so timestamps differ between runs.

```bash
dotnet run --project simulator/SiteSense.Simulator -- --devices 5 --duration-s 30 --seed 42 --start-ms 1767225600000
```
