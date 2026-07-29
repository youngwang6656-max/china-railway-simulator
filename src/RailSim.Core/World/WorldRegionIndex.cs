using System.Collections.Immutable;

namespace RailSim.Core.World;

public sealed record WorldChunkContents(
    WorldChunkId Id,
    ImmutableHashSet<StableId> RiverIds,
    ImmutableHashSet<StableId> CityIds,
    ImmutableHashSet<StableId> StationIds);

public sealed record WorldRegionSelection(
    ImmutableArray<WorldChunkId> ChunkIds,
    ImmutableHashSet<StableId> RiverIds,
    ImmutableHashSet<StableId> CityIds,
    ImmutableHashSet<StableId> StationIds);

public sealed class WorldRegionIndex
{
    private WorldRegionIndex(
        int chunkSizeMeters,
        ImmutableDictionary<WorldChunkId, WorldChunkContents> chunks)
    {
        ChunkSizeMeters = chunkSizeMeters;
        Chunks = chunks;
    }

    public int ChunkSizeMeters { get; }

    public ImmutableDictionary<WorldChunkId, WorldChunkContents> Chunks
    {
        get;
    }

    public WorldRegionSelection Query(
        WorldBounds bounds,
        IWorldRegionProvider regionProvider)
    {
        ArgumentNullException.ThrowIfNull(regionProvider);
        var chunkIds = regionProvider.EnumerateChunks(
            bounds,
            ChunkSizeMeters);
        var rivers = ImmutableHashSet.CreateBuilder<StableId>();
        var cities = ImmutableHashSet.CreateBuilder<StableId>();
        var stations = ImmutableHashSet.CreateBuilder<StableId>();
        var populatedChunks = ImmutableArray.CreateBuilder<WorldChunkId>();
        foreach (var chunkId in chunkIds)
        {
            if (!Chunks.TryGetValue(chunkId, out var chunk))
            {
                continue;
            }

            populatedChunks.Add(chunkId);
            rivers.UnionWith(chunk.RiverIds);
            cities.UnionWith(chunk.CityIds);
            stations.UnionWith(chunk.StationIds);
        }

        return new WorldRegionSelection(
            populatedChunks.ToImmutable(),
            rivers.ToImmutable(),
            cities.ToImmutable(),
            stations.ToImmutable());
    }

    internal static WorldRegionIndex Build(
        WorldGenerationParameters parameters,
        IEnumerable<RiverPath> rivers,
        IEnumerable<CitySite> cities,
        IEnumerable<StationAnchor> stations)
    {
        var builders =
            new Dictionary<WorldChunkId, MutableChunkContents>();
        foreach (var river in rivers)
        {
            for (var index = 1; index < river.Points.Length; index++)
            {
                var start = river.Points[index - 1];
                var end = river.Points[index];
                var chunkSizeMillimeters = checked(
                    parameters.ChunkSizeMeters * 1000L);
                var maximumDelta = Math.Max(
                    Math.Abs(end.XMillimeters - start.XMillimeters),
                    Math.Abs(end.YMillimeters - start.YMillimeters));
                var steps = Math.Max(
                    1L,
                    (maximumDelta + (chunkSizeMillimeters / 2) - 1) /
                    (chunkSizeMillimeters / 2));
                for (var step = 0L; step <= steps; step++)
                {
                    var ratio = step / (double)steps;
                    var point = new WorldPoint(
                        checked((long)Math.Round(
                            start.XMillimeters +
                            ((end.XMillimeters - start.XMillimeters) *
                             ratio))),
                        checked((long)Math.Round(
                            start.YMillimeters +
                            ((end.YMillimeters - start.YMillimeters) *
                             ratio))));
                    GetOrCreate(
                        builders,
                        WorldChunkId.FromPoint(
                            point,
                            parameters.ChunkSizeMeters))
                        .RiverIds.Add(river.Id);
                }
            }
        }

        foreach (var city in cities)
        {
            GetOrCreate(
                builders,
                WorldChunkId.FromPoint(
                    city.Position,
                    parameters.ChunkSizeMeters))
                .CityIds.Add(city.Id);
        }

        foreach (var station in stations)
        {
            GetOrCreate(
                builders,
                WorldChunkId.FromPoint(
                    station.Position,
                    parameters.ChunkSizeMeters))
                .StationIds.Add(station.Id);
        }

        var chunks = builders.ToImmutableDictionary(
            item => item.Key,
            item => new WorldChunkContents(
                item.Key,
                item.Value.RiverIds.ToImmutableHashSet(),
                item.Value.CityIds.ToImmutableHashSet(),
                item.Value.StationIds.ToImmutableHashSet()));
        return new WorldRegionIndex(parameters.ChunkSizeMeters, chunks);
    }

    private static MutableChunkContents GetOrCreate(
        IDictionary<WorldChunkId, MutableChunkContents> chunks,
        WorldChunkId id)
    {
        if (!chunks.TryGetValue(id, out var chunk))
        {
            chunk = new MutableChunkContents();
            chunks.Add(id, chunk);
        }

        return chunk;
    }

    private sealed class MutableChunkContents
    {
        public HashSet<StableId> RiverIds { get; } = [];

        public HashSet<StableId> CityIds { get; } = [];

        public HashSet<StableId> StationIds { get; } = [];
    }
}

public sealed class GridWorldRegionProvider : IWorldRegionProvider
{
    public ImmutableArray<WorldChunkId> EnumerateChunks(
        WorldBounds bounds,
        int chunkSizeMeters)
    {
        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        var minimum = WorldChunkId.FromPoint(
            bounds.Minimum,
            chunkSizeMeters);
        var maximum = WorldChunkId.FromPoint(
            bounds.Maximum,
            chunkSizeMeters);
        var chunks = ImmutableArray.CreateBuilder<WorldChunkId>();
        for (var y = minimum.Y; y <= maximum.Y; y++)
        {
            for (var x = minimum.X; x <= maximum.X; x++)
            {
                chunks.Add(new WorldChunkId(x, y));
            }
        }

        return chunks.ToImmutable();
    }
}
