using System.Collections.Immutable;
using System.Text;
using RailSim.Core.Editing;
using RailSim.Core.Persistence;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Persistence;

public sealed class FileWorldChunkStoreTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "railsim-chunk-tests",
            Guid.NewGuid().ToString("N"));

    public FileWorldChunkStoreTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task FarApartChunksRoundTripIndependently()
    {
        var store = new FileWorldChunkStore(_temporaryDirectory);
        var first = Snapshot(
            new WorldChunkId(0, 0),
            "river:regional-0001");
        var second = Snapshot(
            new WorldChunkId(93, 93),
            "track:continental-9001");

        await store.WriteAsync(first);
        await store.WriteAsync(second);
        var reloadedFirst = await store.ReadAsync(first.Id);
        var reloadedSecond = await store.ReadAsync(second.Id);
        var missing = await store.ReadAsync(new WorldChunkId(47, 12));

        Assert.Equal(first.Id, reloadedFirst!.Id);
        Assert.Equal(first.SchemaVersion, reloadedFirst.SchemaVersion);
        Assert.Equal(first.ContentHash, reloadedFirst.ContentHash);
        Assert.Equal(first.Payload.ToArray(), reloadedFirst.Payload.ToArray());
        Assert.Equal(second.Id, reloadedSecond!.Id);
        Assert.Equal(second.SchemaVersion, reloadedSecond.SchemaVersion);
        Assert.Equal(second.ContentHash, reloadedSecond.ContentHash);
        Assert.Equal(second.Payload.ToArray(), reloadedSecond.Payload.ToArray());
        Assert.Null(missing);
        Assert.Equal(
            "river:regional-0001",
            Encoding.UTF8.GetString(reloadedFirst.Payload.AsSpan()));
        Assert.Equal(
            "track:continental-9001",
            Encoding.UTF8.GetString(reloadedSecond.Payload.AsSpan()));
    }

    [Fact]
    public async Task ReplacingChunkLeavesNoTemporaryFile()
    {
        var store = new FileWorldChunkStore(_temporaryDirectory);
        var id = new WorldChunkId(-4, 7);

        await store.WriteAsync(Snapshot(id, "first"));
        await store.WriteAsync(Snapshot(id, "second"));

        var actual = await store.ReadAsync(id);
        Assert.Equal(
            "second",
            Encoding.UTF8.GetString(actual!.Payload.AsSpan()));
        Assert.Empty(
            Directory.GetFiles(
                _temporaryDirectory,
                "*.tmp",
                SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StreamingPolicyUnloadReloadPreservesStablePayload()
    {
        var store = new FileWorldChunkStore(_temporaryDirectory);
        var origin = Snapshot(
            new WorldChunkId(0, 0),
            "track:stable-across-unload");
        var distant = Snapshot(
            new WorldChunkId(93, 93),
            "station:continental-mega");
        await store.WriteAsync(origin);
        await store.WriteAsync(distant);
        var coordinator = new WorldStreamingCoordinator(
            store,
            new AdjacentChunkStreamingPolicy(prefetchRadius: 0),
            chunkSizeMeters: 64_000);

        await coordinator.UpdateAsync(BoundsInside(origin.Id));
        Assert.Contains(origin.Id, coordinator.Loaded.Keys);
        await coordinator.UpdateAsync(BoundsInside(distant.Id));
        Assert.DoesNotContain(origin.Id, coordinator.Loaded.Keys);
        Assert.Contains(distant.Id, coordinator.Loaded.Keys);
        await coordinator.UpdateAsync(BoundsInside(origin.Id));

        var reloaded = coordinator.Loaded[origin.Id];
        Assert.Equal(origin.ContentHash, reloaded.ContentHash);
        Assert.Equal(
            "track:stable-across-unload",
            Encoding.UTF8.GetString(reloaded.Payload.AsSpan()));
    }

    [Fact]
    public async Task GeneratedWorldAndRailManifestRetainsStableIds()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260729));
        var stations = generated.Stations.Values
            .OrderBy(station => station.Position.XMillimeters)
            .ThenBy(station => station.Id)
            .Take(2)
            .ToArray();
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("chunk-manifest"))
            .CreatePatch(
                RailTopology.Empty,
                new ControlPointSplineStrategy().Build(
                [
                    new DraftWaypoint(
                        stations[0].Position,
                        stations[0].Id),
                    new DraftWaypoint(
                        stations[1].Position,
                        stations[1].Id)
                ]),
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var world = new WorldState(
            1,
            generated,
            patch.ApplyTo(RailTopology.Empty));
        var chunkId = patch.AffectedChunks[
            patch.AffectedChunks.Length / 2];
        var codec = new WorldChunkSnapshotCodec();
        var snapshot = codec.Encode(world, chunkId);
        var store = new FileWorldChunkStore(_temporaryDirectory);
        await store.WriteAsync(snapshot);
        var coordinator = new WorldStreamingCoordinator(
            store,
            new AdjacentChunkStreamingPolicy(prefetchRadius: 0),
            generated.Parameters.ChunkSizeMeters);

        await coordinator.UpdateAsync(BoundsInside(chunkId));
        var manifest = codec.Decode(coordinator.Loaded[chunkId]);

        Assert.Equal(chunkId, manifest.Id);
        Assert.NotEmpty(manifest.TrackIds);
        Assert.All(
            manifest.TrackIds,
            id => Assert.Contains(id, world.RailTopology.Tracks.Keys));
        Assert.All(
            manifest.RailNodeIds,
            id => Assert.Contains(id, world.RailTopology.Nodes.Keys));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static WorldChunkSnapshot Snapshot(
        WorldChunkId id,
        string payload)
    {
        return new WorldChunkSnapshot(
            id,
            schemaVersion: 1,
            $"hash:{payload}",
            Encoding.UTF8.GetBytes(payload).ToImmutableArray());
    }

    private static WorldBounds BoundsInside(WorldChunkId id)
    {
        const long chunkSizeMillimeters = 64_000_000;
        var minimum = new WorldPoint(
            checked((id.X * chunkSizeMillimeters) + 1),
            checked((id.Y * chunkSizeMillimeters) + 1));
        return new WorldBounds(
            minimum,
            minimum.OffsetMillimeters(10_000, 10_000));
    }
}
