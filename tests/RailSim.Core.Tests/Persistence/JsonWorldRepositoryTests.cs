using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Persistence;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Persistence;

public sealed class JsonWorldRepositoryTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "railsim-tests",
            Guid.NewGuid().ToString("N"));

    public JsonWorldRepositoryTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void SaveLoadPreservesWorldAndTopologyHash()
    {
        var path = Path.Combine(
            _temporaryDirectory,
            "world.crsim-world.json");
        var expected = CreateWorldWithMainLine();
        var repository = new JsonWorldRepository();

        repository.Save(path, expected);
        var actual = repository.Load(path);

        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(
            expected.BaseWorld.ContentHash,
            actual.BaseWorld.ContentHash);
        Assert.Equal(
            WorldContentHasher.HashState(expected),
            WorldContentHasher.HashState(actual));
        Assert.Equal(6, actual.RailTopology.Tracks.Count);
        Assert.Equal(4, actual.RailTopology.Connections.Count);
    }

    [Fact]
    public void UnknownSchemaVersionIsRejected()
    {
        var path = Path.Combine(_temporaryDirectory, "future.json");
        File.WriteAllText(path, """{"schemaVersion":999}""");

        var exception = Assert.Throws<InvalidDataException>(
            () => new JsonWorldRepository().Load(path));

        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TruncatedDocumentIsRejected()
    {
        var path = Path.Combine(_temporaryDirectory, "truncated.json");
        File.WriteAllText(path, """{"schemaVersion":1,"world":""");

        Assert.Throws<InvalidDataException>(
            () => new JsonWorldRepository().Load(path));
    }

    [Fact]
    public void SaveReplacesExistingDocumentWithoutLeavingTemporaryFiles()
    {
        var path = Path.Combine(_temporaryDirectory, "replace.json");
        var repository = new JsonWorldRepository();
        repository.Save(path, CreateWorldWithMainLine(seed: 1));

        repository.Save(path, CreateWorldWithMainLine(seed: 2));
        var loaded = repository.Load(path);

        Assert.Equal((ulong)2, loaded.BaseWorld.Parameters.Seed);
        Assert.Empty(
            Directory.GetFiles(
                _temporaryDirectory,
                "*.tmp",
                SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void SaveRejectsMissingCityReferenceWithoutCreatingAFile()
    {
        var path = Path.Combine(_temporaryDirectory, "invalid-world.json");
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(7));
        var station = generated.Stations.Values.OrderBy(item => item.Id).First();
        var invalidStation = new StationAnchor(
            station.Id,
            station.Name,
            station.Tier,
            station.Position,
            new StableId("city:missing"),
            station.DetailZoneId);
        var stations = generated.Stations.SetItem(
            invalidStation.Id,
            invalidStation);
        var withoutHash = new GeneratedWorld(
            generated.Parameters,
            generated.Terrain,
            generated.Rivers,
            generated.Cities,
            stations,
            string.Empty);
        var invalidGenerated = new GeneratedWorld(
            generated.Parameters,
            generated.Terrain,
            generated.Rivers,
            generated.Cities,
            stations,
            WorldContentHasher.HashGeneratedWorld(withoutHash));
        var world = new WorldState(
            0,
            invalidGenerated,
            RailTopology.Empty);

        Assert.Throws<InvalidDataException>(
            () => new JsonWorldRepository().Save(path, world));
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static WorldState CreateWorldWithMainLine(ulong seed = 20260729)
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(seed));
        var stations = generated.Stations.Values
            .OrderBy(station => station.Position.XMillimeters)
            .ThenBy(station => station.Id)
            .ToArray();
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                Waypoint(stations[4]),
                Waypoint(stations[20]),
                Waypoint(stations[38]),
                Waypoint(stations[58])));
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource($"save-{seed}"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        return new WorldState(1, generated, patch.ApplyTo(RailTopology.Empty));
    }

    private static DraftWaypoint Waypoint(StationAnchor station)
    {
        return new DraftWaypoint(station.Position, station.Id);
    }
}
