#!/usr/bin/env python3
"""Build data/map.geojson (SiteSense map format) from a saved OpenStreetMap extract.

    python3 -I data/build_map.py              convert data/osm/cittadella.osm.gz
    python3 -I data/build_map.py --download   fetch a fresh extract first, then convert

Standard library only, Python 3.9+. Rules and mapping: docs/plans/SS-18-osm-map.md.
The same input always gives the same map.geojson, byte for byte.
"""
import argparse
import collections
import datetime
import gzip
import json
import math
import os
import re
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
OSM_FILE = os.path.join(HERE, "osm", "cittadella.osm.gz")
OUT_FILE = os.path.join(HERE, "map.geojson")

BBOX = "15.0670,37.5230,15.0790,37.5332"  # west,south,east,north
API_URL = "https://api.openstreetmap.org/api/0.6/map?bbox=" + BBOX
USER_AGENT = "sitesense-map-builder/1.0"
PERIMETER_WAY = 231201634
PERIMETER_NAME = "Cittadella Universitaria"

# Local equirectangular projection around the bbox center (fine for a 1 km site).
_w, _s, _e, _n = [float(v) for v in BBOX.split(",")]
LON0, LAT0 = (_w + _e) / 2, (_s + _n) / 2
M_PER_DEG_LON = math.cos(math.radians(LAT0)) * 111320.0
M_PER_DEG_LAT = 110540.0

TOL = 0.01                # m: points closer than this to a segment touch it
CONNECT_TOL_M = 0.5       # path ends closer than this are connected (site.json connectToleranceM)
MAX_SNAP_M = 30.0         # site.json maxSnapDistanceM
DEFAULT_AP_CAPACITY = 200

AREA_PRIORITY = ["hazard", "restricted", "work", "parking", "rest"]
PATH_HIGHWAYS = {"footway", "path", "pedestrian", "steps", "service", "track", "living_street"}
DEFAULT_WIDTH = {"footway": 2, "path": 1.5, "steps": 2, "pedestrian": 4, "track": 3,
                 "service": 4, "living_street": 5}
PAVED = {"asphalt", "paving_stones", "concrete", "sett", "paved", "concrete:plates",
         "concrete:lanes", "cobblestone", "bricks", "metal", "wood", "paving_stones:lanes"}
UNPAVED = {"gravel", "dirt", "grass", "ground", "compacted", "unpaved", "sand", "earth",
           "fine_gravel", "mud", "pebblestone", "grass_paver"}

RE_WIDTH = re.compile(r"^(\d+(?:\.\d+)?)\s*(?:m)?$")
RE_INCLINE = re.compile(r"^(-?\d+(?:\.\d+)?)\s*%$")
RE_INT = re.compile(r"^\d+$")


# ---------------------------------------------------------------- geometry

def xy(p):
    return ((p[0] - LON0) * M_PER_DEG_LON, (p[1] - LAT0) * M_PER_DEG_LAT)


def lonlat(x, y):
    return (round(x / M_PER_DEG_LON + LON0, 7), round(y / M_PER_DEG_LAT + LAT0, 7))


def dist(p, q):
    a, b = xy(p), xy(q)
    return math.hypot(a[0] - b[0], a[1] - b[1])


def closest_on_segment(p, a, b):
    """(distance m, closest point in xy) from p to segment ab."""
    px, py = xy(p)
    ax, ay = xy(a)
    bx, by = xy(b)
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L2))
    cx, cy = ax + t * dx, ay + t * dy
    return math.hypot(px - cx, py - cy), (cx, cy)


def _side(p, a, b):
    """Signed distance in m of p from the line ab."""
    px, py = xy(p)
    ax, ay = xy(a)
    bx, by = xy(b)
    L = math.hypot(bx - ax, by - ay)
    if L == 0:
        return 0.0
    return ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) / L


def proper_cross(a, b, c, d):
    """True when ab and cd cross at one interior point (not a touch)."""
    s1, s2 = _side(a, c, d), _side(b, c, d)
    s3, s4 = _side(c, a, b), _side(d, a, b)
    return ((s1 > TOL and s2 < -TOL) or (s1 < -TOL and s2 > TOL)) and \
           ((s3 > TOL and s4 < -TOL) or (s3 < -TOL and s4 > TOL))


def cross_point(a, b, c, d):
    ax, ay = xy(a)
    bx, by = xy(b)
    cx, cy = xy(c)
    dx, dy = xy(d)
    r = (bx - ax, by - ay)
    s = (dx - cx, dy - cy)
    den = r[0] * s[1] - r[1] * s[0]
    t = ((cx - ax) * s[1] - (cy - ay) * s[0]) / den
    return lonlat(ax + t * r[0], ay + t * r[1])


def segs_intersect(a, b, c, d):
    """Proper crossing, or any endpoint within TOL of the other segment."""
    if proper_cross(a, b, c, d):
        return True
    return (closest_on_segment(a, c, d)[0] < TOL or closest_on_segment(b, c, d)[0] < TOL or
            closest_on_segment(c, a, b)[0] < TOL or closest_on_segment(d, a, b)[0] < TOL)


def ring_edges(ring):
    n = len(ring)
    return [(ring[i], ring[(i + 1) % n]) for i in range(n)]


def locate(p, ring):
    """'on' (on the boundary), 'in' or 'out' for point p and a ring (no repeated last point)."""
    for a, b in ring_edges(ring):
        if closest_on_segment(p, a, b)[0] < TOL:
            return "on"
    px, py = xy(p)
    inside = False
    for a, b in ring_edges(ring):
        ax, ay = xy(a)
        bx, by = xy(b)
        if (ay > py) != (by > py) and px < (bx - ax) * (py - ay) / (by - ay) + ax:
            inside = not inside
    return "in" if inside else "out"


def midpoint(a, b):
    return ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)


def signed_area(ring):
    pts = [xy(p) for p in ring]
    return sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
               for i in range(len(pts))) / 2


def ring_is_simple(ring):
    edges = ring_edges(ring)
    n = len(edges)
    for i in range(n):
        for j in range(i + 1, n):
            if j == i + 1 or (i == 0 and j == n - 1):
                continue
            if segs_intersect(edges[i][0], edges[i][1], edges[j][0], edges[j][1]):
                return False
    return True


def inside_ring(points, ring):
    """Points, segments between them and their midpoints stay inside or on the ring."""
    if any(locate(p, ring) == "out" for p in points):
        return False
    for i in range(len(points) - 1):
        a, b = points[i], points[i + 1]
        if locate(midpoint(a, b), ring) == "out":
            return False
        if any(proper_cross(a, b, c, d) for c, d in ring_edges(ring)):
            return False
    return True


def rings_overlap(r1, r2):
    e1, e2 = ring_edges(r1), ring_edges(r2)
    if any(proper_cross(a, b, c, d) for a, b in e1 for c, d in e2):
        return True
    if any(locate(p, r2) == "in" for p in r1) or any(locate(p, r1) == "in" for p in r2):
        return True
    if any(locate(midpoint(a, b), r2) == "in" for a, b in e1) or \
            any(locate(midpoint(a, b), r1) == "in" for a, b in e2):
        return True
    return set(r1) == set(r2)


def seg_hits_ring(a, b, ring):
    if locate(a, ring) != "out" or locate(b, ring) != "out":
        return True
    return any(segs_intersect(a, b, c, d) for c, d in ring_edges(ring))


# ---------------------------------------------------------------- path junctions

def join_pass(paths):
    """Find the vertices crossings, T-touches and collinear overlaps need.

    paths: list of dicts with "pts" (list of lon/lat tuples) and "level".
    Returns (inserts, cross_level): inserts maps (path index, segment index) to points
    to add; cross_level counts crossings between different levels (not joined).
    """
    segs = []
    for pi, p in enumerate(paths):
        for si in range(len(p["pts"]) - 1):
            a, b = p["pts"][si], p["pts"][si + 1]
            (ax, ay), (bx, by) = xy(a), xy(b)
            box = (min(ax, bx) - TOL, min(ay, by) - TOL, max(ax, bx) + TOL, max(ay, by) + TOL)
            segs.append((pi, si, a, b, box, p["level"]))
    inserts = collections.defaultdict(set)
    cross_level = 0
    for i in range(len(segs)):
        pi, si, a, b, bi, li = segs[i]
        for j in range(i + 1, len(segs)):
            pj, sj, c, d, bj, lj = segs[j]
            if bi[2] < bj[0] or bj[2] < bi[0] or bi[3] < bj[1] or bj[3] < bi[1]:
                continue
            if li != lj:
                if proper_cross(a, b, c, d):
                    cross_level += 1
                continue
            if proper_cross(a, b, c, d):
                x = cross_point(a, b, c, d)
                inserts[(pi, si)].add(x)
                inserts[(pj, sj)].add(x)
            for p in (c, d):
                if p != a and p != b and closest_on_segment(p, a, b)[0] < TOL:
                    inserts[(pi, si)].add(p)
            for p in (a, b):
                if p != c and p != d and closest_on_segment(p, c, d)[0] < TOL:
                    inserts[(pj, sj)].add(p)
    for key in inserts:
        pi, si = key
        a, b = paths[pi]["pts"][si], paths[pi]["pts"][si + 1]
        inserts[key] = {p for p in inserts[key] if p != a and p != b}
    return {k: v for k, v in inserts.items() if v}, cross_level


def apply_inserts(paths, inserts):
    for pi, p in enumerate(paths):
        new = [p["pts"][0]]
        for si in range(len(p["pts"]) - 1):
            a, b = p["pts"][si], p["pts"][si + 1]
            new.extend(sorted(inserts.get((pi, si), ()), key=lambda q: (dist(a, q), q)))
            new.append(b)
        p["pts"] = new


def insert_vertex(path, si, point):
    path["pts"].insert(si + 1, point)


# ---------------------------------------------------------------- connectivity

def build_graph(paths, blocked_rings):
    """Walkable graph as routing builds it.

    paths: list of (id, pts). Returns (component of each vertex, total length per component,
    walkable flag per (path index, segment index)).
    """
    parent = {}

    def find(v):
        parent.setdefault(v, v)
        while parent[v] != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v

    walkable = {}
    for pi, (_, pts) in enumerate(paths):
        for si in range(len(pts) - 1):
            a, b = pts[si], pts[si + 1]
            ok = not any(seg_hits_ring(a, b, r) for r in blocked_rings)
            walkable[(pi, si)] = ok
            if ok:
                parent[find(a)] = find(b)
    ends = sorted({p for pi, (_, pts) in enumerate(paths) for p in (pts[0], pts[-1]) if p in parent})
    for i in range(len(ends)):
        for j in range(i + 1, len(ends)):
            if dist(ends[i], ends[j]) < CONNECT_TOL_M:
                parent[find(ends[i])] = find(ends[j])
    length = collections.defaultdict(float)
    for (pi, si), ok in walkable.items():
        if ok:
            pts = paths[pi][1]
            length[find(pts[si])] += dist(pts[si], pts[si + 1])
    comp = {v: find(v) for v in parent}
    return comp, dict(length), walkable


def largest_component(length):
    return max(sorted(length), key=lambda c: length[c])


# ---------------------------------------------------------------- OSM reading

def download():
    req = urllib.request.Request(API_URL, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
    except (urllib.error.URLError, OSError) as e:
        sys.exit("download failed (%s): %s" % (API_URL, e))
    os.makedirs(os.path.dirname(OSM_FILE), exist_ok=True)
    with open(OSM_FILE, "wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as z:
            z.write(data)
    print("downloaded %d bytes at %s UTC" % (
        len(data), datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")))


def read_osm():
    with gzip.open(OSM_FILE, "rb") as f:
        root = ET.parse(f).getroot()
    nodes, ntags, ways = {}, {}, {}
    for el in root.iter("node"):
        i = int(el.get("id"))
        nodes[i] = (round(float(el.get("lon")), 7), round(float(el.get("lat")), 7))
        tags = {t.get("k"): t.get("v") for t in el.findall("tag")}
        if tags:
            ntags[i] = tags
    for el in root.iter("way"):
        ways[int(el.get("id"))] = ([int(n.get("ref")) for n in el.findall("nd")],
                                   {t.get("k"): t.get("v") for t in el.findall("tag")})
    return nodes, ntags, dict(sorted(ways.items()))


def dedupe(pts):
    out = []
    for p in pts:
        if not out or out[-1] != p:
            out.append(p)
    return out


# ---------------------------------------------------------------- classification

def area_type(tags):
    if ("construction" in tags or tags.get("landuse") == "construction" or
            tags.get("building") == "construction" or tags.get("natural") == "water"):
        return "hazard"
    if "power" in tags:
        return "restricted"
    if "building" in tags:
        return "work"
    if tags.get("amenity") == "parking":
        return "parking"
    if tags.get("leisure") in ("garden", "park", "pitch") or tags.get("landuse") in ("grass", "meadow"):
        return "rest"
    return None


def parse_capacity(tags, stats):
    v = tags.get("capacity")
    if v is None:
        return None
    if RE_INT.match(v):
        return int(v)
    stats["capacity tag not a number"] += 1
    return None


def path_props(way_id, tags, stats):
    hw = tags["highway"]
    surf = tags.get("surface")
    if surf in PAVED:
        surface = "paved"
    elif surf in UNPAVED:
        surface = "unpaved"
    else:
        if surf is not None:
            stats["unknown surface value"] += 1
        surface = "unpaved" if hw in ("track", "path") else "paved"
    slope = 0
    inc = tags.get("incline")
    if inc is not None:
        m = RE_INCLINE.match(inc.strip())
        if m:
            slope = abs(float(m.group(1)))
        else:
            stats["incline not a number in %"] += 1
    width = DEFAULT_WIDTH[hw]
    w = tags.get("width")
    if w is not None:
        m = RE_WIDTH.match(w.strip())
        if m:
            width = float(m.group(1))
        else:
            stats["width not a number in m"] += 1
    return {"id": "p-%d" % way_id, "surface": surface, "steps": hw == "steps",
            "slopePercent": slope, "widthM": width}


def level_of(tags):
    try:
        layer = int(tags.get("layer", "0"))
    except ValueError:
        layer = 0
    return (layer, tags.get("bridge", "no") != "no", tags.get("tunnel", "no") != "no")


# ---------------------------------------------------------------- build

def build():
    nodes, ntags, ways = read_osm()
    dropped = collections.defaultdict(list)   # reason -> ids
    stats = collections.Counter()             # fallbacks used

    # 1 perimeter
    if PERIMETER_WAY not in ways:
        sys.exit("perimeter way %d not in the extract" % PERIMETER_WAY)
    pnodes = ways[PERIMETER_WAY][0]
    if pnodes[0] != pnodes[-1]:
        sys.exit("perimeter way is not closed")
    perimeter = dedupe([nodes[n] for n in pnodes])[:-1]
    if not ring_is_simple(perimeter):
        sys.exit("perimeter ring is not simple")
    perim_node_id = {nodes[n]: n for n in pnodes}
    if signed_area(perimeter) < 0:
        perimeter.reverse()

    # 2 areas, in priority order
    cands = []
    for wid, (nds, tags) in ways.items():
        if wid == PERIMETER_WAY or len(nds) < 4 or nds[0] != nds[-1]:
            continue
        t = area_type(tags)
        if t is None:
            continue
        ring = dedupe([nodes[n] for n in nds])[:-1]
        cands.append((AREA_PRIORITY.index(t), wid, t, ring, tags))
    cands.sort(key=lambda c: (c[0], c[1]))
    areas = []
    for _, wid, t, ring, tags in cands:
        aid = "a-%d" % wid
        if len(ring) < 3 or abs(signed_area(ring)) < 0.01:
            dropped["area degenerate"].append(aid)
        elif not ring_is_simple(ring):
            dropped["area self-intersecting"].append(aid)
        elif not inside_ring(ring + [ring[0]], perimeter):
            dropped["area not inside the perimeter"].append(aid)
        elif any(rings_overlap(ring, k["ring"]) for k in areas):
            dropped["area overlaps a higher-priority area"].append(aid)
        else:
            if signed_area(ring) < 0:
                ring = ring[::-1]
            props = {"kind": "area", "id": aid, "areaType": t,
                     "name": tags.get("name") or {"work": "Building", "parking": "Parking",
                                                  "rest": "Rest area", "hazard": "Hazard zone",
                                                  "restricted": "Restricted area"}[t] + " %d" % wid}
            cap = parse_capacity(tags, stats)
            if cap is not None:
                props["capacity"] = cap
            areas.append({"ring": ring, "props": props})
    blocked = [a["ring"] for a in areas if a["props"]["areaType"] in ("hazard", "restricted")]

    # 3 paths: cut at nodes into runs inside the perimeter
    paths = []
    for wid, (nds, tags) in ways.items():
        if tags.get("highway") not in PATH_HIGHWAYS:
            continue
        pts = []
        for n in nds:
            if not pts or pts[-1] != nodes[n]:
                pts.append(nodes[n])
        last = len(pts) - 1
        ok = [locate(p, perimeter) != "out" for p in pts]
        runs, start = [], None
        for i in range(len(pts)):
            if not ok[i]:
                if start is not None:
                    runs.append((start, i - 1))
                    start = None
                continue
            if start is None:
                start = i
            elif not inside_ring([pts[i - 1], pts[i]], perimeter):
                runs.append((start, i - 1))
                start = i
        if start is not None:
            runs.append((start, last))
        props = path_props(wid, tags, stats)
        for r, (s, e) in enumerate(runs):
            pid = "p-%d-%d" % (wid, r)
            if e - s < 1:
                continue
            ends_ok = all(i == end or locate(pts[i], perimeter) == "on"
                          for i, end in ((s, 0), (e, last)))
            if not ends_ok:
                dropped["path run ends inside the site, not at a perimeter node or way end"].append(pid)
                continue
            p = dict(props)
            p["id"] = pid
            paths.append({"id": pid, "pts": pts[s:e + 1], "level": level_of(tags), "props": p})

    # junctions: shared vertices, repeat until stable
    for _ in range(50):
        inserts, cross_level = join_pass(paths)
        if not inserts:
            break
        apply_inserts(paths, inserts)
    else:
        sys.exit("path junction pass did not converge")
    stats["crossings between different levels, not joined"] = cross_level

    # 5 assembly points: snap onto the nearest walkable segment
    aps = []
    for nid in sorted(ntags):
        if ntags[nid].get("emergency") != "assembly_point":
            continue
        p = nodes[nid]
        if locate(p, perimeter) == "out":
            sys.exit("assembly point ap-%d is outside the perimeter" % nid)
        best = None
        for pi, path in enumerate(paths):
            for si in range(len(path["pts"]) - 1):
                a, b = path["pts"][si], path["pts"][si + 1]
                if any(seg_hits_ring(a, b, r) for r in blocked):
                    continue
                d, c = closest_on_segment(p, a, b)
                if best is None or d < best[0]:
                    best = (d, pi, si, lonlat(*c))
        if best is None or best[0] > MAX_SNAP_M:
            sys.exit("assembly point ap-%d is %s m from a walkable path (max %d)" % (
                nid, "no" if best is None else "%.1f" % best[0], MAX_SNAP_M))
        _, pi, si, q = best
        if q not in paths[pi]["pts"]:
            insert_vertex(paths[pi], si, q)
        tags = ntags[nid]
        cap = parse_capacity(tags, stats)
        aps.append({"point": q, "props": {"kind": "assemblyPoint", "id": "ap-%d" % nid,
                                          "name": tags.get("name") or "Assembly point %d" % nid,
                                          "capacity": cap if cap is not None else DEFAULT_AP_CAPACITY}})

    # 6 connectivity: keep the largest walkable component
    comp, length, walkable = build_graph([(p["id"], p["pts"]) for p in paths], blocked)
    main = largest_component(length)
    keep = []
    for pi, p in enumerate(paths):
        if any(ok and comp[p["pts"][si]] == main
               for (qi, si), ok in walkable.items() if qi == pi):
            keep.append(p)
        else:
            dropped["path outside the largest walkable component"].append(p["id"])
    paths = keep
    for a in aps:
        if comp.get(a["point"]) != main:
            sys.exit("assembly point %s is outside the walkable component" % a["props"]["id"])

    # 4 entrances: kept path vertices that are perimeter nodes
    path_vertices = {v for p in paths for v in p["pts"] if comp.get(v) == main}
    entrances = []
    seen = set()
    for p in sorted(perim_node_id):
        nid = perim_node_id[p]
        if nid in seen:
            continue
        seen.add(nid)
        if p in path_vertices:
            entrances.append({"point": p, "props": {"kind": "entrance", "id": "e-%d" % nid,
                                                    "name": ntags.get(nid, {}).get("name") or "Gate %d" % nid}})
        elif any(p in q["pts"] for q in paths):
            dropped["entrance outside the walkable component"].append("e-%d" % nid)
    if not entrances:
        sys.exit("no entrance left in the walkable component")

    # features
    feats = [{"type": "Feature",
              "geometry": {"type": "Polygon", "coordinates": [[list(p) for p in perimeter + [perimeter[0]]]]},
              "properties": {"kind": "area", "id": "perimeter", "name": PERIMETER_NAME,
                             "areaType": "perimeter"}}]
    for a in areas:
        feats.append({"type": "Feature",
                      "geometry": {"type": "Polygon", "coordinates": [[list(p) for p in a["ring"] + [a["ring"][0]]]]},
                      "properties": a["props"]})
    for p in paths:
        pr = {k: v for k, v in p["props"].items()}
        pr["kind"] = "path"
        feats.append({"type": "Feature",
                      "geometry": {"type": "LineString", "coordinates": [list(q) for q in p["pts"]]},
                      "properties": pr})
    for e in entrances + aps:
        feats.append({"type": "Feature", "geometry": {"type": "Point", "coordinates": list(e["point"])},
                      "properties": e["props"]})
    feats.sort(key=lambda f: (f["properties"]["kind"], f["properties"]["id"]))
    levels = {p["id"]: p["level"] for p in paths}
    return feats, levels, dropped, stats


# ---------------------------------------------------------------- rule check

def check_rules(feats, levels):
    errors = []

    def fail(fid, reason):
        errors.append("%s: %s" % (fid, reason))

    def is_num(v):
        return isinstance(v, (int, float)) and not isinstance(v, bool)

    required = {
        "area": ({"kind", "id", "name", "areaType"}, {"capacity"}),
        "path": ({"kind", "id", "surface", "steps", "slopePercent", "widthM"}, set()),
        "assemblyPoint": ({"kind", "id", "name", "capacity"}, set()),
        "entrance": ({"kind", "id", "name"}, set()),
    }
    id_re = {"area": r"^(perimeter|a-\d+)$", "path": r"^p-\d+-\d+$",
             "assemblyPoint": r"^ap-\d+$", "entrance": r"^e-\d+$"}
    seen_ids = set()
    perimeter = None
    areas, paths, points = [], [], []
    for f in feats:
        pr = f["properties"]
        fid = pr.get("id", "?")
        kind = pr.get("kind")
        if kind not in required:
            fail(fid, "unknown kind %r" % kind)
            continue
        req, opt = required[kind]
        keys = set(pr)
        if not req <= keys or keys - req - opt:
            fail(fid, "properties %s, expected %s (+ optional %s)" % (sorted(keys), sorted(req), sorted(opt)))
            continue
        if fid in seen_ids:
            fail(fid, "duplicate id")
        seen_ids.add(fid)
        if not re.match(id_re[kind], fid):
            fail(fid, "id does not match the id format")
        g = f["geometry"]
        if kind == "area":
            if pr["areaType"] not in ("perimeter", "hazard", "restricted", "work", "parking", "rest"):
                fail(fid, "bad areaType %r" % pr["areaType"])
            if not pr["name"]:
                fail(fid, "empty name")
            if "capacity" in pr and (not isinstance(pr["capacity"], int) or isinstance(pr["capacity"], bool)
                                     or pr["capacity"] < 0):
                fail(fid, "bad capacity")
            if g["type"] != "Polygon" or len(g["coordinates"]) != 1:
                fail(fid, "area must be a Polygon with one ring")
                continue
            c = [tuple(p) for p in g["coordinates"][0]]
            if len(c) < 4 or c[0] != c[-1]:
                fail(fid, "ring not closed or too short")
                continue
            ring = c[:-1]
            if len(set(ring)) != len(ring):
                fail(fid, "repeated vertex in ring")
            if signed_area(ring) <= 0:
                fail(fid, "ring is not counter-clockwise")
            if not ring_is_simple(ring):
                fail(fid, "ring self-intersects")
            if pr["areaType"] == "perimeter":
                if perimeter is not None:
                    fail(fid, "more than one perimeter")
                perimeter = ring
            else:
                areas.append((fid, pr["areaType"], ring))
        elif kind == "path":
            if pr["surface"] not in ("paved", "unpaved"):
                fail(fid, "bad surface")
            if not isinstance(pr["steps"], bool):
                fail(fid, "steps must be a bool")
            if not is_num(pr["slopePercent"]) or pr["slopePercent"] < 0:
                fail(fid, "bad slopePercent")
            if not is_num(pr["widthM"]) or pr["widthM"] <= 0:
                fail(fid, "bad widthM")
            if g["type"] != "LineString":
                fail(fid, "path must be a LineString")
                continue
            c = [tuple(p) for p in g["coordinates"]]
            if len(c) < 2 or any(c[i] == c[i + 1] for i in range(len(c) - 1)):
                fail(fid, "path has fewer than 2 vertices or a repeated vertex")
            paths.append((fid, c))
        else:
            if not pr["name"]:
                fail(fid, "empty name")
            if kind == "assemblyPoint" and (not isinstance(pr["capacity"], int) or pr["capacity"] <= 0):
                fail(fid, "bad capacity")
            if g["type"] != "Point":
                fail(fid, "%s must be a Point" % kind)
                continue
            points.append((fid, kind, tuple(g["coordinates"])))
        coords = g["coordinates"]
        flat = [p for r in coords for p in r] if g["type"] == "Polygon" else (
            coords if g["type"] == "LineString" else [coords])
        if any(len(p) != 2 or not -180 <= p[0] <= 180 or not -90 <= p[1] <= 90 for p in flat):
            fail(fid, "coordinate out of range or not [lon, lat]")
    if perimeter is None:
        fail("perimeter", "no perimeter")
    if errors:
        return errors

    for fid, t, ring in areas:
        if not inside_ring(ring + [ring[0]], perimeter):
            fail(fid, "not inside the perimeter")
    for i in range(len(areas)):
        for j in range(i + 1, len(areas)):
            if rings_overlap(areas[i][2], areas[j][2]):
                fail(areas[i][0], "overlaps " + areas[j][0])
    for fid, c in paths:
        if not inside_ring(c, perimeter):
            fail(fid, "not inside the perimeter")
    pl = [{"pts": c, "level": levels.get(fid)} for fid, c in paths]
    inserts, _ = join_pass(pl)
    for (pi, si), pts in sorted(inserts.items()):
        fail(paths[pi][0], "segment %d crosses or touches another path without a shared vertex at %s"
             % (si, sorted(pts)[0]))
    vertices = {p for _, c in paths for p in c}
    for fid, kind, p in points:
        if locate(p, perimeter) == "out":
            fail(fid, "not inside the perimeter")
        if p not in vertices:
            fail(fid, "not a vertex of any path")
    if not any(k == "entrance" for _, k, _ in points):
        fail("entrance", "no entrance")
    if not any(k == "assemblyPoint" for _, k, _ in points):
        fail("assemblyPoint", "no assembly point")
    if errors:
        return errors
    blocked = [r for _, t, r in areas if t in ("hazard", "restricted")]
    comp, length, _ = build_graph(paths, blocked)
    main = largest_component(length)
    for fid, kind, p in points:
        if comp.get(p) != main:
            fail(fid, "not in the largest walkable component")
    return errors


# ---------------------------------------------------------------- output

def write_map(feats):
    lines = [json.dumps(f, sort_keys=True, separators=(",", ":")) for f in feats]
    text = '{"type":"FeatureCollection","features":[\n' + ",\n".join(lines) + "\n]}\n"
    with open(OUT_FILE, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def summary(feats, dropped, stats):
    kinds = collections.Counter(f["properties"]["kind"] for f in feats)
    print("features: " + ", ".join("%s=%d" % kv for kv in sorted(kinds.items())))
    types = collections.Counter(f["properties"]["areaType"] for f in feats if f["properties"]["kind"] == "area")
    print("areas by type: " + ", ".join("%s=%d" % kv for kv in sorted(types.items())))
    steps = sum(1 for f in feats if f["properties"].get("steps") is True)
    print("paths with steps=true: %d" % steps)
    for reason, ids in sorted(dropped.items()):
        print("dropped (%s): %d%s" % (reason, len(ids), " e.g. " + ", ".join(ids[:5]) if ids else ""))
    for reason, n in sorted(stats.items()):
        print("fallback/info (%s): %d" % (reason, n))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--download", action="store_true", help="fetch a fresh OSM extract first")
    args = ap.parse_args()
    if args.download:
        download()
    if not os.path.exists(OSM_FILE):
        sys.exit("missing %s; run with --download" % OSM_FILE)
    feats, levels, dropped, stats = build()
    errors = check_rules(feats, levels)
    if errors:
        for e in errors[:50]:
            print("RULE CHECK FAILED  " + e, file=sys.stderr)
        sys.exit(1)
    write_map(feats)
    print("rule check passed")
    summary(feats, dropped, stats)


if __name__ == "__main__":
    main()
