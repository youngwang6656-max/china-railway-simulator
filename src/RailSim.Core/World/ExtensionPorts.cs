using System.Collections.Immutable;

namespace RailSim.Core.World;

public readonly record struct TerrainSampleEdit(
    WorldPoint Position,
    int ElevationMeters);

public interface ITerrainSource
{
    HeightField ReadTerrain(WorldGenerationParameters parameters);
}

public interface ITerrainGenerator
{
    HeightField GenerateTerrain(
        WorldGenerationParameters parameters,
        ulong terrainSeed);
}

public sealed class TerrainSourceGeneratorAdapter : ITerrainGenerator
{
    private readonly ITerrainSource _source;

    public TerrainSourceGeneratorAdapter(ITerrainSource source)
    {
        _source = source ??
            throw new ArgumentNullException(nameof(source));
    }

    public HeightField GenerateTerrain(
        WorldGenerationParameters parameters,
        ulong terrainSeed)
    {
        _ = terrainSeed;
        return _source.ReadTerrain(parameters);
    }
}

public interface ITerrainEditService
{
    HeightField Apply(
        HeightField source,
        WorldBounds bounds,
        ImmutableArray<TerrainSampleEdit> edits);
}

public interface ICityPlacementStrategy
{
    ImmutableArray<CitySite> PlaceCities(
        WorldGenerationParameters parameters,
        HeightField terrain,
        ImmutableArray<RiverPath> rivers,
        ulong placementSeed,
        INameGenerator nameGenerator);
}

public interface INameGenerator
{
    ImmutableArray<string> GenerateCityNames(
        int count,
        ulong nameSeed);
}

public sealed record WorldChunkSnapshot
{
    public WorldChunkSnapshot(
        WorldChunkId id,
        int schemaVersion,
        string contentHash,
        ImmutableArray<byte> payload)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (payload.IsDefault)
        {
            throw new ArgumentException(
                "Chunk payload must be initialized.",
                nameof(payload));
        }

        Id = id;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash;
        Payload = payload;
    }

    public WorldChunkId Id { get; }

    public int SchemaVersion { get; }

    public string ContentHash { get; }

    public ImmutableArray<byte> Payload { get; }
}

public interface IWorldChunkStore
{
    ValueTask<WorldChunkSnapshot?> ReadAsync(
        WorldChunkId id,
        CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        WorldChunkSnapshot chunk,
        CancellationToken cancellationToken = default);
}

public interface IWorldRegionProvider
{
    ImmutableArray<WorldChunkId> EnumerateChunks(
        WorldBounds bounds,
        int chunkSizeMeters);
}

public sealed record WorldStreamingPlan
{
    public WorldStreamingPlan(
        ImmutableArray<WorldChunkId> load,
        ImmutableArray<WorldChunkId> retain,
        ImmutableArray<WorldChunkId> unload)
    {
        if (load.IsDefault || retain.IsDefault || unload.IsDefault)
        {
            throw new ArgumentException(
                "Streaming plan collections must be initialized.");
        }

        Load = load;
        Retain = retain;
        Unload = unload;
    }

    public ImmutableArray<WorldChunkId> Load { get; }

    public ImmutableArray<WorldChunkId> Retain { get; }

    public ImmutableArray<WorldChunkId> Unload { get; }
}

public interface IWorldStreamingPolicy
{
    WorldStreamingPlan Plan(
        WorldBounds viewport,
        int chunkSizeMeters,
        ImmutableHashSet<WorldChunkId> loadedChunks);
}
