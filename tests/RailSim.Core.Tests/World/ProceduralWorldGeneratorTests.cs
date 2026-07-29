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
    public void TierCountsMatchRegionalProfile()
    {
        var world = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(321));

        Assert.Equal(2, world.Cities.Values.Count(city => city.Tier == CityTier.Mega));
        Assert.Equal(8, world.Cities.Values.Count(city => city.Tier == CityTier.Large));
        Assert.Equal(18, world.Cities.Values.Count(city => city.Tier == CityTier.Medium));
        Assert.Equal(36, world.Cities.Values.Count(city => city.Tier == CityTier.Small));
    }
}
