using System.Text.Json;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Helpers;

public static class MapValidator
{
    private static readonly string[] Kinds = ["area", "path", "assemblyPoint", "entrance"];
    private static readonly string[] AreaTypes = ["perimeter", "work", "rest", "parking", "restricted", "hazard"];
    private static readonly string[] Surfaces = ["paved", "unpaved"];

    public static (SiteMap? Map, IReadOnlyList<MapError> Errors) Validate(string geoJson, double connectToleranceM)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(geoJson);
        }
        catch (JsonException)
        {
            return NotCollection();
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "type") != "FeatureCollection"
                || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                return NotCollection();

            var s = new State();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < features.GetArrayLength(); i++)
                ReadFeature(features[i], $"features[{i}]", seenIds, s);

            CheckPerimeters(s);
            CheckOnPath(s, connectToleranceM);

            if (s.Errors.Count > 0) return (null, s.Errors);
            return (new SiteMap
            {
                Areas = s.Areas,
                Paths = s.Paths,
                AssemblyPoints = s.AssemblyPoints,
                Entrances = s.Entrances,
            }, s.Errors);
        }
    }

    private static (SiteMap?, IReadOnlyList<MapError>) NotCollection() =>
        (null, new List<MapError> { new(null, "not a GeoJSON FeatureCollection") });

    private static void ReadFeature(JsonElement feature, string label, HashSet<string> seenIds, State s)
    {
        if (feature.ValueKind != JsonValueKind.Object || Str(feature, "type") != "Feature"
            || !feature.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
        {
            s.Errors.Add(new MapError(label, "not a GeoJSON Feature"));
            return;
        }

        var rawId = Str(props, "id");
        var idKnown = rawId is not null && rawId.Trim().Length > 0;
        var elementId = idKnown ? rawId! : label;
        if (!idKnown) s.Errors.Add(new MapError(label, "missing property 'id'"));

        var kind = Str(props, "kind");
        if (kind is null)
        {
            s.Errors.Add(new MapError(elementId, "missing property 'kind'"));
            return;
        }
        if (Array.IndexOf(Kinds, kind) < 0)
        {
            s.Errors.Add(new MapError(elementId, $"unknown kind '{kind}'"));
            return;
        }

        if (idKnown && !seenIds.Add(rawId!)) s.Errors.Add(new MapError(rawId, "duplicate id"));

        switch (kind)
        {
            case "area": ReadArea(feature, props, elementId, idKnown, s); break;
            case "path": ReadPath(feature, props, elementId, s); break;
            case "assemblyPoint": ReadAssemblyPoint(feature, props, elementId, s); break;
            case "entrance": ReadEntrance(feature, props, elementId, s); break;
        }
    }

    private static void ReadArea(JsonElement feature, JsonElement props, string id, bool idKnown, State s)
    {
        var before = s.Errors.Count;
        var name = RequireString(props, "name", id, s);
        var areaType = RequireString(props, "areaType", id, s);

        int? capacity = null;
        if (props.TryGetProperty("capacity", out var cap))
        {
            if (cap.ValueKind == JsonValueKind.Number && cap.TryGetInt32(out var n)) capacity = n;
            else s.Errors.Add(new MapError(id, "invalid property 'capacity'"));
        }

        if (areaType is not null && Array.IndexOf(AreaTypes, areaType) < 0)
            s.Errors.Add(new MapError(id, $"invalid areaType '{areaType}'"));

        var rings = ReadGeometry<double[][][]>(feature, "Polygon", id, s, ReadRings);
        if (rings is not null)
        {
            AddPoints(s, rings.SelectMany(r => r));
            if (areaType == "perimeter") s.PerimeterRing ??= rings[0];
        }
        // Counted even with bad coordinates, so a broken perimeter is not also reported as missing.
        if (areaType == "perimeter" && idKnown) s.PerimeterIds.Add(id);

        if (s.Errors.Count == before && name is not null && areaType is not null && rings is not null)
            s.Areas.Add(new MapArea(id, name, areaType, capacity, rings));
    }

    private static void ReadPath(JsonElement feature, JsonElement props, string id, State s)
    {
        var before = s.Errors.Count;
        var surface = RequireString(props, "surface", id, s);
        var steps = RequireBool(props, "steps", id, s);
        var slope = RequireNumber(props, "slopePercent", id, s);
        var width = RequireNumber(props, "widthM", id, s);

        if (surface is not null && Array.IndexOf(Surfaces, surface) < 0)
            s.Errors.Add(new MapError(id, $"invalid surface '{surface}'"));

        var line = ReadGeometry<double[][]>(feature, "LineString", id, s, e => ReadPositions(e, 2));
        if (line is not null)
        {
            s.ReadablePaths.Add(line);
            AddPoints(s, line);
        }

        if (s.Errors.Count == before && surface is not null && steps is not null && slope is not null && width is not null && line is not null)
            s.Paths.Add(new MapPath(id, surface, steps.Value, slope.Value, width.Value, line));
    }

    private static void ReadAssemblyPoint(JsonElement feature, JsonElement props, string id, State s)
    {
        var before = s.Errors.Count;
        var name = RequireString(props, "name", id, s);
        var capacity = RequireInt(props, "capacity", id, s);
        var pos = ReadGeometry<double[]>(feature, "Point", id, s, ReadPosition);
        if (pos is not null) AddReadablePoint(s, id, pos);

        if (s.Errors.Count == before && name is not null && capacity is not null && pos is not null)
            s.AssemblyPoints.Add(new MapAssemblyPoint(id, name, capacity.Value, pos));
    }

    private static void ReadEntrance(JsonElement feature, JsonElement props, string id, State s)
    {
        var before = s.Errors.Count;
        var name = RequireString(props, "name", id, s);
        var pos = ReadGeometry<double[]>(feature, "Point", id, s, ReadPosition);
        if (pos is not null) AddReadablePoint(s, id, pos);

        if (s.Errors.Count == before && name is not null && pos is not null)
            s.Entrances.Add(new MapEntrance(id, name, pos));
    }

    private static void CheckPerimeters(State s)
    {
        if (s.PerimeterIds.Count == 0)
            s.Errors.Add(new MapError(null, "perimeter missing"));
        else if (s.PerimeterIds.Count > 1)
            foreach (var id in s.PerimeterIds)
                s.Errors.Add(new MapError(id, "multiple perimeters"));
    }

    private static void CheckOnPath(State s, double connectToleranceM)
    {
        if (s.ReadablePoints.Count == 0) return;

        var reference = s.PerimeterRing is not null
            ? s.PerimeterRing.Select(p => (Lon: p[0], Lat: p[1])).ToList()
            : s.AllPoints;
        var centroidLat = reference.Average(p => p.Lat);
        // shortcut: LocalProjection rejects |lat| > 89; the on-path check is skipped there instead of throwing, upgrade if polar sites appear.
        if (Math.Abs(centroidLat) > 89) return;

        var projection = LocalProjection.FromCentroid(reference);
        var lines = s.ReadablePaths
            .Select(line => (IReadOnlyList<(double X, double Y)>)line.Select(p => projection.ToLocal(p[0], p[1])).ToList())
            .ToList();

        foreach (var (id, pos) in s.ReadablePoints)
        {
            var local = projection.ToLocal(pos[0], pos[1]);
            var nearest = lines.Count == 0
                ? double.PositiveInfinity
                : lines.Min(line => PlanarGeometry.DistanceToPolyline(local, line));
            if (nearest > connectToleranceM) s.Errors.Add(new MapError(id, "not on a path"));
        }
    }

    private static void AddReadablePoint(State s, string id, double[] pos)
    {
        s.ReadablePoints.Add((id, pos));
        AddPoints(s, [pos]);
    }

    private static void AddPoints(State s, IEnumerable<double[]> positions)
    {
        foreach (var p in positions) s.AllPoints.Add((p[0], p[1]));
    }

    // Returns null and records the error when the geometry type is wrong or the coordinates cannot be read.
    private static T? ReadGeometry<T>(JsonElement feature, string expected, string id, State s, Func<JsonElement, T?> read) where T : class
    {
        var geometry = feature.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object ? g : (JsonElement?)null;
        if (geometry is null || Str(geometry.Value, "type") != expected)
        {
            s.Errors.Add(new MapError(id, $"geometry must be {expected}"));
            return null;
        }

        var value = geometry.Value.TryGetProperty("coordinates", out var c) ? read(c) : null;
        if (value is null) s.Errors.Add(new MapError(id, "invalid coordinates"));
        return value;
    }

    // A position is [lon, lat]; extra numbers after the first two are read but dropped.
    private static double[]? ReadPosition(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array) return null;
        var values = new List<double>();
        foreach (var v in e.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d)) return null;
            values.Add(d);
        }
        if (values.Count < 2) return null;
        if (values[0] is < -180 or > 180 || values[1] is < -90 or > 90) return null;
        return [values[0], values[1]];
    }

    private static double[][]? ReadPositions(JsonElement e, int min)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < min) return null;
        var result = new double[e.GetArrayLength()][];
        var i = 0;
        foreach (var p in e.EnumerateArray())
        {
            var pos = ReadPosition(p);
            if (pos is null) return null;
            result[i++] = pos;
        }
        return result;
    }

    private static double[][][]? ReadRings(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 1) return null;
        var rings = new double[e.GetArrayLength()][][];
        var i = 0;
        foreach (var ring in e.EnumerateArray())
        {
            var r = ReadPositions(ring, 4);
            if (r is null) return null;
            rings[i++] = r;
        }
        return rings;
    }

    private static string? RequireString(JsonElement props, string name, string id, State s)
    {
        var v = Str(props, name);
        if (v is null) s.Errors.Add(new MapError(id, $"missing property '{name}'"));
        return v;
    }

    private static bool? RequireBool(JsonElement props, string name, string id, State s)
    {
        if (props.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        s.Errors.Add(new MapError(id, $"missing property '{name}'"));
        return null;
    }

    private static double? RequireNumber(JsonElement props, string name, string id, State s)
    {
        if (props.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        s.Errors.Add(new MapError(id, $"missing property '{name}'"));
        return null;
    }

    private static int? RequireInt(JsonElement props, string name, string id, State s)
    {
        if (props.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        s.Errors.Add(new MapError(id, $"missing property '{name}'"));
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private sealed class State
    {
        public readonly List<MapError> Errors = [];
        public readonly List<MapArea> Areas = [];
        public readonly List<MapPath> Paths = [];
        public readonly List<MapAssemblyPoint> AssemblyPoints = [];
        public readonly List<MapEntrance> Entrances = [];
        public readonly List<string> PerimeterIds = [];
        public double[][]? PerimeterRing;
        public readonly List<double[][]> ReadablePaths = [];
        public readonly List<(string Id, double[] Pos)> ReadablePoints = [];
        public readonly List<(double Lon, double Lat)> AllPoints = [];
    }
}
