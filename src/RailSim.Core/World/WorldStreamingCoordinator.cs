using System.Collections.Immutable;

namespace RailSim.Core.World;

public sealed class AdjacentChunkStreamingPolicy : IWorldStreamingPolicy
{
    private readonly int _prefetchRadius;

    public AdjacentChunkStreamingPolicy(int prefetchRadius = 1)
    {
        if (prefetchRadius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prefetchRadius));
        }

        _prefetchRadius = prefetchRadius;
    }

    public WorldStreamingPlan Plan(
        WorldBounds viewport,
        int chunkSizeMeters,
        ImmutableHashSet<WorldChunkId> loadedChunks)
    {
        ArgumentNullException.ThrowIfNull(loadedChunks);
        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        var margin = checked(
            (long)_prefetchRadius * chunkSizeMeters * 1000L);
        var target = new GridWorldRegionProvider()
            .EnumerateChunks(
                viewport.ExpandMillimeters(margin),
                chunkSizeMeters)
            .ToImmutableHashSet();
        return new WorldStreamingPlan(
            target
                .Except(loadedChunks)
                .OrderBy(chunk => chunk.X)
                .ThenBy(chunk => chunk.Y)
                .ToImmutableArray(),
            target
                .Intersect(loadedChunks)
                .OrderBy(chunk => chunk.X)
                .ThenBy(chunk => chunk.Y)
                .ToImmutableArray(),
            loadedChunks
                .Except(target)
                .OrderBy(chunk => chunk.X)
                .ThenBy(chunk => chunk.Y)
                .ToImmutableArray());
    }
}

public sealed class WorldStreamingCoordinator
{
    private readonly IWorldChunkStore _store;
    private readonly IWorldStreamingPolicy _policy;
    private readonly int _chunkSizeMeters;
    private ImmutableDictionary<WorldChunkId, WorldChunkSnapshot> _loaded =
        ImmutableDictionary<WorldChunkId, WorldChunkSnapshot>.Empty;

    public WorldStreamingCoordinator(
        IWorldChunkStore store,
        IWorldStreamingPolicy policy,
        int chunkSizeMeters)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ??
            throw new ArgumentNullException(nameof(policy));
        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        _chunkSizeMeters = chunkSizeMeters;
    }

    public ImmutableDictionary<WorldChunkId, WorldChunkSnapshot> Loaded =>
        _loaded;

    public async ValueTask<WorldStreamingPlan> UpdateAsync(
        WorldBounds viewport,
        CancellationToken cancellationToken = default)
    {
        var plan = _policy.Plan(
            viewport,
            _chunkSizeMeters,
            _loaded.Keys.ToImmutableHashSet());
        var candidate = _loaded.ToBuilder();
        foreach (var id in plan.Unload)
        {
            candidate.Remove(id);
        }

        foreach (var id in plan.Load)
        {
            var chunk = await _store.ReadAsync(id, cancellationToken);
            if (chunk is not null)
            {
                candidate[id] = chunk;
            }
        }

        _loaded = candidate.ToImmutable();
        return plan;
    }
}
