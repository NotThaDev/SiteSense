# Data sources

## OpenStreetMap tiles (map view)

- Source: OpenStreetMap standard tile layer, `https://tile.openstreetmap.org/{z}/{x}/{y}.png`.
- Used at runtime: the browser loads the tiles for the site page map (SS-48). Nothing is stored.
- License: Open Data Commons Open Database License (ODbL) 1.0 — https://opendatacommons.org/licenses/odbl/1-0/
- Attribution: © OpenStreetMap contributors, shown in the map corner and linked to https://www.openstreetmap.org/copyright
- Tile usage policy (https://operations.osmfoundation.org/policies/tiles/): light, interactive use only. The browser sends a `Referer`, so the app must not set `Referrer-Policy: no-referrer`.
- Library: Leaflet (BSD-2-Clause) draws the map; `react-leaflet` is the React binding.

## OpenStreetMap: Cittadella Universitaria, Catania

- Source: OpenStreetMap, site perimeter = way 231201634.
- API call: `https://api.openstreetmap.org/api/0.6/map?bbox=15.0670,37.5230,15.0790,37.5332`
- Downloaded: 2026-11-25T08:21:09Z (UTC)
- License: Open Data Commons Open Database License (ODbL) 1.0 — https://opendatacommons.org/licenses/odbl/1-0/ ; see also https://www.openstreetmap.org/copyright
- Attribution: © OpenStreetMap contributors
- Files with this data (both are ODbL, same attribution):
  - `data/osm/cittadella.osm.gz` — the saved extract
  - `data/map.geojson` — derived map
- Transformation: `data/build_map.py` converts the extract to the SiteSense map format; see [data/README.md](data/README.md).
