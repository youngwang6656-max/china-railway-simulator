using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.World;

public sealed class ProceduralWorldGeneratorTests
{
    [Fact]
    public void SameSeedAndParametersProduceSameWorldHash()
    {
        var parameters = WorldGenerationParameters.CreateRegional(20260729);
        var generator = new ProceduralWorldGenerator();

        var first = generator.Generate(parameters);
        var second = generator.Generate(parameters);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(64, first.Cities.Count);
        Assert.Equal(64, first.Stations.Count);
        Assert.Equal(parameters.MajorRiverCount, first.Rivers.Length);
    }

    [Fact]
    public void DifferentSeedChangesGeneratedContent()
    {
        var generator = new ProceduralWorldGenerator();

        var first = generator.Generate(
            WorldGenerationParameters.CreateRegional(10));
        var second = generator.Generate(
            WorldGenerationParameters.CreateRegional(11));

        Assert.NotEqual(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void GeneratedNamesAreUniqueAndPositionsStayInsideWorld()
    {
        var world = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(9876));

        Assert.Equal(
            world.Cities.Count,
            world.Cities.Values.Select(city => city.Name).Distinct().Count());
        Assert.All(
            world.Cities.Values,
            city => Assert.True(world.Parameters.Bounds.Contains(city.Position)));
        Assert.All(
            world.Stations.Values,
            station =>
            {
                Assert.True(world.Parameters.Bounds.Contains(station.Position));
                Assert.NotNull(station.CityId);
                Assert.True(world.Cities.ContainsKey(station.CityId!.Value));
            });
    }

    [Fact]
    public void TerrainAndRiversHaveQuantizedDeterministicData()
    {
        var parameters = WorldGenerationParameters.CreateRegional(55);
        var world = new ProceduralWorldGenerator().Generate(parameters);

        Assert.Equal(parameters.HeightSamplesX, world.Terrain.Width);
        Assert.Equal(parameters.HeightSamplesY, world.Terrain.Height);
        Assert.All(
            world.Rivers,
            river =>
            {
                Assert.True(river.Points.Length >= 2);
                Assert.All(
                    river.Points,
                    point => Assert.True(parameters.Bounds.Contains(point)));
            });
        Assert.Equal(
            world.Terrain.ContentHash,
            new ProceduralWorldGenerator()
                .Generate(parameters)
                .Terrain
                .ContentHash);
    }

    [Fact]
    public void RiversAreDownhillBiasedAndMajorCitiesStayNearWater()
    {
        var world = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(55));

        foreach (var river in world.Rivers)
        {
            var elevations = river.Points
                .Select(
                    point => world.Terrain.SampleAt(
                        point,
                        world.Parameters.Bounds))
                .ToArray();
            var downhillSteps = elevations
                .Zip(elevations.Skip(1))
                .Count(pair => pair.First >= pair.Second);

            Assert.True(elevations[0] > elevations[^1]);
            Assert.True(downhillSteps >= (elevations.Length - 1) / 2);
        }

        var majorCities = world.Cities.Values.Where(
            city => city.Tier is CityTier.Mega or CityTier.Large);
        Assert.All(
            majorCities,
            city => Assert.InRange(
                world.Rivers
                    .SelectMany(river => river.Points)
                    .Min(point => point.DistanceMetersTo(city.Position)),
                0,
                250_000));
    }

    [Fact]
    public void TierCountsMatchRegionalProfile()
    {
        var world = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(321));

        Assert.Equal(2, world.Cities.Values.Count(city => city.Tier == CityTier.Mega));
        Assert.Equal(8, world.Cities.Values.Count(city => city.Tier == CityTier.Large));
        Assert.Equal(18, world.Cities.Values.Count(city => city.Tier == CityTier.Medium));
        Assert.Equal(36, world.Cities.Values.Count(city => city.Tier == CityTier.Small));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void RegionalProfilesSupportRequiredStationCounts(int stationCount)
    {
        var baseline = WorldGenerationParameters.CreateRegional(123);
        var parameters = new WorldGenerationParameters(
            baseline.Seed,
            baseline.GeneratorVersion,
            baseline.ScaleProfile,
            baseline.Bounds,
            baseline.ChunkSizeMeters,
            baseline.HeightSamplesX,
            baseline.HeightSamplesY,
            baseline.MegaCityCount,
            baseline.LargeCityCount,
            baseline.MediumCityCount,
            baseline.SmallCityCount,
            stationCount,
            baseline.MajorRiverCount);

        var world = new ProceduralWorldGenerator().Generate(parameters);

        Assert.Equal(stationCount, world.Stations.Count);
        if (stationCount > world.Cities.Count)
        {
            Assert.Contains(
                world.Stations.Values.GroupBy(station => station.CityId),
                group => group.Count() > 1);
        }
    }

    [Fact]
    public void GeneratorConsumesReplacementTerrainCityAndNamePorts()
    {
        var defaults = new ProceduralWorldGenerator();
        var terrain = new CountingTerrainGenerator(defaults);
        var cities = new CountingCityPlacementStrategy(defaults);
        var names = new CountingNameGenerator(defaults);
        var generator = new ProceduralWorldGenerator(
            terrain,
            cities,
            names);

        var world = generator.Generate(
            WorldGenerationParameters.CreateRegional(77));

        Assert.Equal(1, terrain.CallCount);
        Assert.Equal(1, cities.CallCount);
        Assert.Equal(1, names.CallCount);
        Assert.Equal(64, world.Cities.Count);
    }

    private sealed class CountingTerrainGenerator(
        ITerrainGenerator inner) : ITerrainGenerator
    {
        public int CallCount { get; private set; }

        public HeightField GenerateTerrain(
            WorldGenerationParameters parameters,
            ulong terrainSeed)
        {
            CallCount++;
            return inner.GenerateTerrain(parameters, terrainSeed);
        }
    }

    private sealed class CountingCityPlacementStrategy(
        ICityPlacementStrategy inner) : ICityPlacementStrategy
    {
        public int CallCount { get; private set; }

        public System.Collections.Immutable.ImmutableArray<CitySite> PlaceCities(
            WorldGenerationParameters parameters,
            HeightField terrain,
            System.Collections.Immutable.ImmutableArray<RiverPath> rivers,
            ulong placementSeed,
            INameGenerator nameGenerator)
        {
            CallCount++;
            return inner.PlaceCities(
                parameters,
                terrain,
                rivers,
                placementSeed,
                nameGenerator);
        }
    }

    private sealed class CountingNameGenerator(
        INameGenerator inner) : INameGenerator
    {
        public int CallCount { get; private set; }

        public System.Collections.Immutable.ImmutableArray<string>
            GenerateCityNames(int count, ulong nameSeed)
        {
            CallCount++;
            return inner.GenerateCityNames(count, nameSeed);
        }
    }
}
