using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Rail;

public sealed record RailTopologyChangeSet(
    long BaseRevision,
    long ResultRevision,
    ImmutableArray<WorldChunkId> AffectedChunks,
    ImmutableHashSet<StableId> RemovedNodeIds,
    ImmutableHashSet<StableId> RemovedPortIds,
    ImmutableHashSet<StableId> RemovedTrackIds,
    ImmutableHashSet<StableId> RemovedTrackGroupIds,
    ImmutableHashSet<StableId> RemovedConnectionIds,
    ImmutableHashSet<StableId> AddedNodeIds,
    ImmutableHashSet<StableId> AddedPortIds,
    ImmutableHashSet<StableId> AddedTrackIds,
    ImmutableHashSet<StableId> AddedTrackGroupIds,
    ImmutableHashSet<StableId> AddedConnectionIds);
