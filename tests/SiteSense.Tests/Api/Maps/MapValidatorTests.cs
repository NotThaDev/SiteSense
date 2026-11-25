using System.Text.Json.Nodes;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models.Map;

namespace SiteSense.Tests.Api.Maps;

public sealed class MapValidatorTests
{
    private const double Tolerance = 0.5;

    [Fact]
    public void RealSiteMap_HasNoErrors()
    {
        var (map, errors) = MapValidator.Validate(MapFixtures.RealMap, MapFixtures.RealMapToleranceM);

        Assert.Empty(errors);
        Assert.NotNull(map);
        Assert.Single(map.Areas, a => a.AreaType == "perimeter");
        Assert.NotEmpty(map.Paths);
        Assert.NotEmpty(map.AssemblyPoints);
        Assert.NotEmpty(map.Entrances);
    }

    [Fact]
    public void ValidBaseMap_HasNoErrorsAndReadsEveryKind()
    {
        var (map, errors) = MapValidator.Validate(MapFixtures.BaseMap, Tolerance);

        Assert.Empty(errors);
        Assert.NotNull(map);
        Assert.Equal(2, map.Areas.Count);
        Assert.Single(map.Paths);
        Assert.Single(map.AssemblyPoints);
        Assert.Single(map.Entrances);
        Assert.Equal(50, map.AssemblyPoints[0].Capacity);
        Assert.Equal("paved", map.Paths[0].Surface);
        Assert.Equal(15.0705, map.Entrances[0].Coordinates[0]);
    }

    [Fact]
    public void PerimeterMissing_ReportsNullElement()
    {
        var json = Edit(f => Props(f, "perimeter")["areaType"] = "work");

        Assert.Equal(new[] { Err(null, "perimeter missing") }, Errors(json));
    }

    [Fact]
    public void TwoPerimeters_ReportsEachOne()
    {
        var json = Edit(f =>
        {
            var copy = (JsonObject)Feature(f, "perimeter").DeepClone();
            copy["properties"]!["id"] = "perimeter-2";
            f.Add(copy);
        });

        Assert.Equal(
            new[] { Err("perimeter", "multiple perimeters"), Err("perimeter-2", "multiple perimeters") },
            Errors(json));
    }

    [Fact]
    public void MissingName_ReportsProperty()
    {
        var json = Edit(f => Props(f, "ap-1").Remove("name"));

        Assert.Equal(new[] { Err("ap-1", "missing property 'name'") }, Errors(json));
    }

    [Fact]
    public void FeatureWithoutId_ReportsIndex()
    {
        var json = Edit(f => f[1]!["properties"]!.AsObject().Remove("id"));

        Assert.Equal(new[] { Err("features[1]", "missing property 'id'") }, Errors(json));
    }

    [Fact]
    public void UnknownKind_IsReported()
    {
        var json = Edit(f => Props(f, "work-1")["kind"] = "tree");

        Assert.Equal(new[] { Err("work-1", "unknown kind 'tree'") }, Errors(json));
    }

    [Fact]
    public void InvalidAreaType_IsReported()
    {
        var json = Edit(f => Props(f, "work-1")["areaType"] = "office");

        Assert.Equal(new[] { Err("work-1", "invalid areaType 'office'") }, Errors(json));
    }

    [Fact]
    public void InvalidSurface_IsReported()
    {
        var json = Edit(f => Props(f, "p1")["surface"] = "gravel");

        Assert.Equal(new[] { Err("p1", "invalid surface 'gravel'") }, Errors(json));
    }

    [Fact]
    public void WrongGeometryType_IsReported()
    {
        var json = Edit(f => Feature(f, "p1")["geometry"]!["type"] = "Point");

        // The unreadable path leaves no readable path, so the points also report "not on a path" (by rule).
        Assert.Contains(Err("p1", "geometry must be LineString"), Errors(json));
    }

    [Fact]
    public void DuplicateId_IsReportedOnLaterOccurrence()
    {
        var json = Edit(f => f.Add(Feature(f, "e-1").DeepClone()));

        Assert.Equal(new[] { Err("e-1", "duplicate id") }, Errors(json));
    }

    [Fact]
    public void AssemblyPointTenMetersOffPath_IsNotOnPath()
    {
        var json = Edit(f => Feature(f, "ap-1")["geometry"]!["coordinates"] = JsonNode.Parse("[15.0710, 37.5251]"));

        Assert.Equal(new[] { Err("ap-1", "not on a path") }, Errors(json));
    }

    [Fact]
    public void EntranceTenMetersOffPath_IsNotOnPath()
    {
        var json = Edit(f => Feature(f, "e-1")["geometry"]!["coordinates"] = JsonNode.Parse("[15.0705, 37.5251]"));

        Assert.Equal(new[] { Err("e-1", "not on a path") }, Errors(json));
    }

    [Fact]
    public void AssemblyPointThirtyCentimetersOffPath_IsAccepted()
    {
        var json = Edit(f => Feature(f, "ap-1")["geometry"]!["coordinates"] = JsonNode.Parse("[15.0710, 37.5250027]"));

        Assert.Empty(Errors(json));
    }

    [Fact]
    public void NoPaths_ReportsPointsNotOnPath()
    {
        var json = Edit(f => f.Remove(Feature(f, "p1")));

        Assert.Equal(
            new[] { Err("ap-1", "not on a path"), Err("e-1", "not on a path") },
            Errors(json));
    }

    [Fact]
    public void RootNotFeatureCollection_ReturnsSingleError()
    {
        var json = """{ "type": "Feature", "features": [] }""";

        Assert.Equal(new[] { Err(null, "not a GeoJSON FeatureCollection") }, Errors(json));
    }

    [Fact]
    public void InvalidJson_ReturnsSingleError()
    {
        Assert.Equal(new[] { Err(null, "not a GeoJSON FeatureCollection") }, Errors("{ not json"));
    }

    [Fact]
    public void FeatureThatIsAString_IsNotAFeature()
    {
        var json = Edit(f => f[1] = "oops");

        Assert.Contains(Err("features[1]", "not a GeoJSON Feature"), Errors(json));
    }

    [Fact]
    public void BadCoordinates_AreInvalid()
    {
        var json = Edit(f =>
        {
            Feature(f, "e-1")["geometry"]!["coordinates"] = JsonNode.Parse("[\"15.0705\", 37.5250]");
            Feature(f, "ap-1")["geometry"]!["coordinates"] = JsonNode.Parse("[15.0710, 200]");
        });

        Assert.Equal(
            new[] { Err("ap-1", "invalid coordinates"), Err("e-1", "invalid coordinates") },
            Errors(json));
    }

    [Fact]
    public void MissingPerimeterAndPointOffPath_ReportedInOrder()
    {
        var json = Edit(f =>
        {
            Props(f, "perimeter")["areaType"] = "work";
            Feature(f, "ap-1")["geometry"]!["coordinates"] = JsonNode.Parse("[15.0710, 37.5251]");
        });

        Assert.Equal(
            new[] { Err(null, "perimeter missing"), Err("ap-1", "not on a path") },
            Errors(json));
    }

    private static (string? Id, string Reason) Err(string? id, string reason) => (id, reason);

    private static List<(string? Id, string Reason)> Errors(string json) =>
        MapValidator.Validate(json, Tolerance).Errors.Select(e => (e.ElementId, e.Reason)).ToList();

    // Applies an edit to a fresh copy of the base map and returns the JSON text.
    private static string Edit(Action<JsonArray> edit)
    {
        var root = JsonNode.Parse(MapFixtures.BaseMap)!.AsObject();
        edit(root["features"]!.AsArray());
        return root.ToJsonString();
    }

    private static JsonObject Feature(JsonArray features, string id) =>
        features.Select(f => f!.AsObject()).First(f => (string?)f["properties"]!["id"] == id);

    private static JsonObject Props(JsonArray features, string id) =>
        Feature(features, id)["properties"]!.AsObject();
}
