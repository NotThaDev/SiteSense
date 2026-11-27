# 0001 — Path graph structure

- Status: Accepted
- Requirement IDs: MAP-03, NAV-01, NAV-02, NAV-03

## Context
Pedestrian routing (fastest route, evacuation) needs a graph built from the map's walk paths. MAP-03 says two paths
connect at their shared vertices and at endpoints closer than `connectToleranceM`. NAV-02 says a stretch between two
consecutive vertices that intersects a `restricted` or `hazard` area is not traversable. NAV-03 asks for a time cost
from the site's walking speed, surface and steps factors. Distances must be in metres (MAP-02). Maps are small
(the sample map has 293 paths) and change rarely, so the graph is built once per map version and kept in memory.

## Decision
`RouteGraphBuilder` (pure Domain code, no packages) builds an undirected `RouteGraph` in local metres from
`LocalProjection.FromCentroid(perimeter)`, the same origin the map validator uses. Every path vertex is a node;
vertices with the same position rounded to 1 cm are one node, so paths that share a vertex join (Q19). Every pair of
consecutive vertices is one edge carrying `LengthM`, `TimeS = LengthM / (walkingSpeedMps × unpaved factor × steps
factor)` (Q21) and its `PathId`. Path endpoints of different paths closer than `connectToleranceM` get a short
connector edge (`PathId = null`, plain walking speed), endpoint to endpoint only (Q18). Each edge, connectors
included, that passes through the inside of a `restricted` or `hazard` polygon stays in the graph with
`Traversable = false`; touching or running along the boundary does not block (Q25). Nodes, an edge list and a
per-node list of edge indices (adjacency list) are the whole structure.

## Alternatives considered
| Option | Pro | Con | Why not |
|---|---|---|---|
| Node per path junction only, edges = whole path pieces between junctions | Fewer nodes and edges | NAV-02 is per stretch between two vertices: a piece would need to be split again at blocked stretches; snapping (NAV-01) needs every vertex | Node per vertex matches the rule unit directly |
| Remove blocked edges instead of marking them | Router needs no check | The rule is invisible and cannot be tested on the graph; evacuation may later want to leave a blocked area (Q25) | Marking keeps the rule visible and testable |
| Merge endpoints within tolerance into one node | No extra edge | Moves geometry by up to `connectToleranceM`; the merge is order-dependent for chains of close endpoints | A connector keeps exact geometry and its real length |
| Directed edges (one per direction) | Ready for slope-dependent cost | Twice the edges; slope is not in the cost (Q21) | Pedestrians walk both ways at the same cost |
| NetTopologySuite noding and `Intersects` | Robust library geometry | Domain has no package by rule; noding would add vertices the map rules already guarantee | Three small planar tests are enough |
| Adjacency matrix | O(1) edge lookup | O(n²) memory on thousands of nodes; parallel edges not representable | Adjacency list fits sparse path graphs |

## Consequences
- SS-19, SS-20 and SS-21 run A*/Dijkstra on `Edges` and `EdgesOf`, skipping `Traversable = false`; snapping uses the vertex nodes.
- Parallel edges (two paths on the same stretch) are kept; the router takes the cheaper one.
- A path end that touches the middle of another path without a shared vertex is not joined (Q18); the map rules say such paths share a vertex.
- The connector step compares all endpoint pairs (O(n²)); fine for hundreds of paths, revisit with a grid index if maps reach thousands.
- The in-memory cache per map version is added by SS-19, the first user (Q86).
