using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Projection;

public sealed record RailRegionSelection(
    ImmutableHashSet<StableId> NodeIds,
    ImmutableHashSet<StableId> PortIds,
    ImmutableHashSet<StableId> TrackIds,
    ImmutableHashSet<StableId> TrackGroupIds,
    ImmutableHashSet<StableId> ConnectionIds);

public sealed class RailSpatialIndex
{
    private readonly ImmutableDictionary<WorldChunkId, ChunkContents> _chunks;

    private RailSpatialIndex(
        int chunkSizeMeters,
        ImmutableDictionary<WorldChunkId, ChunkContents> chunks)
    {
        ChunkSizeMeters = chunkSizeMeters;
        _chunks = chunks;
    }

    public int ChunkSizeMeters { get; }

    public static RailSpatialIndex Build(
        RailTopology topology,
        int chunkSizeMeters)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        var chunks = new Dictionary<WorldChunkId, MutableChunkContents>();
        foreach (var node in topology.Nodes.Values)
        {
            var chunk = GetOrCreate(
                chunks,
                WorldChunkId.FromPoint(
                    node.Position,
                    chunkSizeMeters));
            chunk.NodeIds.Add(node.Id);
        }

        foreach (var port in topology.Ports.Values)
        {
            var node = topology.Nodes[port.NodeId];
            GetOrCreate(
                    chunks,
                    WorldChunkId.FromPoint(
                        node.Position,
                        chunkSizeMeters))
                .PortIds.Add(port.Id);
        }

        var groupsByTrack = topology.TrackGroups.Values
            .SelectMany(
                group => group.TrackIds.Select(
                    trackId => (TrackId: trackId, GroupId: group.Id)))
            .ToLookup(item => item.TrackId, item => item.GroupId);
        foreach (var track in topology.Tracks.Values)
        {
            var alignment = topology.Alignments[track.AlignmentId];
            foreach (var chunkId in WorldChunkCoverage.TracePolyline(
                         alignment.ControlPoints,
                         chunkSizeMeters,
                         Math.Abs((long)track.LateralOffsetMillimeters)))
            {
                var chunk = GetOrCreate(chunks, chunkId);
                chunk.TrackIds.Add(track.Id);
                chunk.TrackGroupIds.UnionWith(groupsByTrack[track.Id]);
            }
        }

        foreach (var connection in topology.Connections.Values)
        {
            var node = topology.Nodes[connection.NodeId];
            GetOrCreate(
                    chunks,
                    WorldChunkId.FromPoint(
                        node.Position,
                        chunkSizeMeters))
                .ConnectionIds.Add(connection.Id);
        }

        return new RailSpatialIndex(
            chunkSizeMeters,
            chunks.ToImmutableDictionary(
                item => item.Key,
                item => item.Value.Freeze()));
    }

    public RailRegionSelection Query(
        WorldBounds bounds,
        IWorldRegionProvider regionProvider)
    {
        ArgumentNullException.ThrowIfNull(regionProvider);
        var nodes = ImmutableHashSet.CreateBuilder<StableId>();
        var ports = ImmutableHashSet.CreateBuilder<StableId>();
        var tracks = ImmutableHashSet.CreateBuilder<StableId>();
        var groups = ImmutableHashSet.CreateBuilder<StableId>();
        var connections = ImmutableHashSet.CreateBuilder<StableId>();
        foreach (var chunkId in regionProvider.EnumerateChunks(
                     bounds,
                     ChunkSizeMeters))
        {
            if (!_chunks.TryGetValue(chunkId, out var chunk))
            {
                continue;
            }

            nodes.UnionWith(chunk.NodeIds);
            ports.UnionWith(chunk.PortIds);
            tracks.UnionWith(chunk.TrackIds);
            groups.UnionWith(chunk.TrackGroupIds);
            connections.UnionWith(chunk.ConnectionIds);
        }

        return new RailRegionSelection(
            nodes.ToImmutable(),
            ports.ToImmutable(),
            tracks.ToImmutable(),
            groups.ToImmutable(),
            connections.ToImmutable());
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

    private sealed record ChunkContents(
        ImmutableHashSet<StableId> NodeIds,
        ImmutableHashSet<StableId> PortIds,
        ImmutableHashSet<StableId> TrackIds,
        ImmutableHashSet<StableId> TrackGroupIds,
        ImmutableHashSet<StableId> ConnectionIds);

    private sealed class MutableChunkContents
    {
        public HashSet<StableId> NodeIds { get; } = [];

        public HashSet<StableId> PortIds { get; } = [];

        public HashSet<StableId> TrackIds { get; } = [];

        public HashSet<StableId> TrackGroupIds { get; } = [];

        public HashSet<StableId> ConnectionIds { get; } = [];

        public ChunkContents Freeze()
        {
            return new ChunkContents(
                NodeIds.ToImmutableHashSet(),
                PortIds.ToImmutableHashSet(),
                TrackIds.ToImmutableHashSet(),
                TrackGroupIds.ToImmutableHashSet(),
                ConnectionIds.ToImmutableHashSet());
        }
    }
}
