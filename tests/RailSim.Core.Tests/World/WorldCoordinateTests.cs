using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.World;

public sealed class WorldCoordinateTests
{
    [Fact]
    public void ContinentalProfileSupportsDistantPreciseLocalCoordinates()
    {
        var parameters = WorldGenerationParameters.CreateContinental(42);
        var origin = new WorldPoint(5_000_000_000, 4_000_000_000);
        var nearby = origin.OffsetMillimeters(1_435, -620);

        Assert.True(parameters.Bounds.Contains(origin));
        Assert.Equal(1_435, nearby.XMillimeters - origin.XMillimeters);
        Assert.Equal(-620, nearby.YMillimeters - origin.YMillimeters);
    }

    [Fact]
    public void RegionalProfileProvidesExpectedDefaultPopulation()
    {
        var parameters = WorldGenerationParameters.CreateRegional(20260729);

        Assert.Equal(WorldScaleProfile.Regional, parameters.ScaleProfile);
        Assert.Equal(2, parameters.MegaCityCount);
        Assert.Equal(8, parameters.LargeCityCount);
        Assert.Equal(18, parameters.MediumCityCount);
        Assert.Equal(36, parameters.SmallCityCount);
        Assert.Equal(64, parameters.CityCount);
        Assert.Equal(64, parameters.StationAnchorCount);
    }

    [Fact]
    public void OneCityCanOwnMultipleMajorStations()
    {
        var cityId = new StableId("city:hub");
        var stations = new[]
        {
            new StationAnchor(
                new StableId("station:east"),
                "东站",
                StationTier.Mega,
                WorldPoint.Zero,
                cityId,
                null),
            new StationAnchor(
                new StableId("station:south"),
                "南站",
                StationTier.Mega,
                WorldPoint.Zero,
                cityId,
                null)
        };

        Assert.All(stations, station => Assert.Equal(cityId, station.CityId));
        Assert.Equal(2, stations.Select(station => station.Id).Distinct().Count());
    }

    [Fact]
    public void StableIdRejectsBlankValues()
    {
        Assert.Throws<ArgumentException>(() => new StableId("  "));
    }

    [Fact]
    public void GeneratedWorldRegionIndexReturnsOnlyIntersectingChunkContent()
    {
        var world = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260729));
        var city = world.Cities.Values.OrderBy(item => item.Id).First();
        var halfSize = 250_000L;
        var viewport = new WorldBounds(
            city.Position.OffsetMillimeters(-halfSize, -halfSize),
            city.Position.OffsetMillimeters(halfSize, halfSize));

        var selection = world.RegionIndex.Query(
            viewport,
            new GridWorldRegionProvider());

        Assert.Contains(city.Id, selection.CityIds);
        Assert.True(selection.CityIds.Count < world.Cities.Count);
        Assert.All(
            selection.ChunkIds,
            chunk => Assert.Contains(chunk, world.RegionIndex.Chunks.Keys));
    }

    [Fact]
    public void SparseDiagonalPolylineIndexesEveryCrossedChunk()
    {
        var chunks = WorldChunkCoverage.TracePolyline(
        [
            new WorldPoint(1_000, 1_000),
            new WorldPoint(255_999_000, 255_999_000)
        ],
        chunkSizeMeters: 64_000);

        Assert.Contains(new WorldChunkId(0, 0), chunks);
        Assert.Contains(new WorldChunkId(1, 1), chunks);
        Assert.Contains(new WorldChunkId(2, 2), chunks);
        Assert.Contains(new WorldChunkId(3, 3), chunks);
    }
}
