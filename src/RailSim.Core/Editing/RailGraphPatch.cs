using System.Collections.Immutable;
using RailSim.Core.Rail;

namespace RailSim.Core.Editing;

public sealed record RailGraphPatch
{
    public ImmutableArray<RailNode> RemovedNodes { get; init; } = [];

    public ImmutableArray<RailPort> RemovedPorts { get; init; } = [];

    public ImmutableArray<Alignment> RemovedAlignments { get; init; } = [];

    public ImmutableArray<TrackEdge> RemovedTracks { get; init; } = [];

    public ImmutableArray<TrackGroup> RemovedTrackGroups { get; init; } = [];

    public ImmutableArray<TrackConnection> RemovedConnections { get; init; } = [];

    public ImmutableArray<RailNode> AddedNodes { get; init; } = [];

    public ImmutableArray<RailPort> AddedPorts { get; init; } = [];

    public ImmutableArray<Alignment> AddedAlignments { get; init; } = [];

    public ImmutableArray<TrackEdge> AddedTracks { get; init; } = [];

    public ImmutableArray<TrackGroup> AddedTrackGroups { get; init; } = [];

    public ImmutableArray<TrackConnection> AddedConnections { get; init; } = [];

    public static RailGraphPatch Empty { get; } = new();

    public bool IsEmpty =>
        RemovedNodes.IsEmpty &&
        RemovedPorts.IsEmpty &&
        RemovedAlignments.IsEmpty &&
        RemovedTracks.IsEmpty &&
        RemovedTrackGroups.IsEmpty &&
        RemovedConnections.IsEmpty &&
        AddedNodes.IsEmpty &&
        AddedPorts.IsEmpty &&
        AddedAlignments.IsEmpty &&
        AddedTracks.IsEmpty &&
        AddedTrackGroups.IsEmpty &&
        AddedConnections.IsEmpty;

    public RailTopology ApplyTo(RailTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        var candidate = topology;
        foreach (var connection in RemovedConnections)
        {
            EnsureMatches(candidate.Connections, connection.Id, connection);
            candidate = candidate.RemoveConnection(connection.Id);
        }

        foreach (var group in RemovedTrackGroups)
        {
            EnsureMatches(candidate.TrackGroups, group.Id, group);
            candidate = candidate.RemoveTrackGroup(group.Id);
        }

        foreach (var track in RemovedTracks)
        {
            EnsureMatches(candidate.Tracks, track.Id, track);
            candidate = candidate.RemoveTrack(track.Id);
        }

        foreach (var alignment in RemovedAlignments)
        {
            EnsureMatches(candidate.Alignments, alignment.Id, alignment);
            candidate = candidate.RemoveAlignment(alignment.Id);
        }

        foreach (var port in RemovedPorts)
        {
            EnsureMatches(candidate.Ports, port.Id, port);
            candidate = candidate.RemovePort(port.Id);
        }

        foreach (var node in RemovedNodes)
        {
            EnsureMatches(candidate.Nodes, node.Id, node);
            candidate = candidate.RemoveNode(node.Id);
        }

        foreach (var node in AddedNodes)
        {
            candidate = candidate.AddNode(node);
        }

        foreach (var port in AddedPorts)
        {
            candidate = candidate.AddPort(port);
        }

        foreach (var alignment in AddedAlignments)
        {
            candidate = candidate.AddAlignment(alignment);
        }

        foreach (var track in AddedTracks)
        {
            candidate = candidate.AddTrack(track);
        }

        foreach (var group in AddedTrackGroups)
        {
            candidate = candidate.AddTrackGroup(group);
        }

        foreach (var connection in AddedConnections)
        {
            candidate = candidate.AddConnection(connection);
        }

        return candidate;
    }

    public RailGraphPatch Invert()
    {
        return new RailGraphPatch
        {
            RemovedNodes = AddedNodes,
            RemovedPorts = AddedPorts,
            RemovedAlignments = AddedAlignments,
            RemovedTracks = AddedTracks,
            RemovedTrackGroups = AddedTrackGroups,
            RemovedConnections = AddedConnections,
            AddedNodes = RemovedNodes,
            AddedPorts = RemovedPorts,
            AddedAlignments = RemovedAlignments,
            AddedTracks = RemovedTracks,
            AddedTrackGroups = RemovedTrackGroups,
            AddedConnections = RemovedConnections
        };
    }

    public RailGraphPatch Combine(RailGraphPatch other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new RailGraphPatch
        {
            RemovedNodes = RemovedNodes.AddRange(other.RemovedNodes),
            RemovedPorts = RemovedPorts.AddRange(other.RemovedPorts),
            RemovedAlignments =
                RemovedAlignments.AddRange(other.RemovedAlignments),
            RemovedTracks = RemovedTracks.AddRange(other.RemovedTracks),
            RemovedTrackGroups =
                RemovedTrackGroups.AddRange(other.RemovedTrackGroups),
            RemovedConnections =
                RemovedConnections.AddRange(other.RemovedConnections),
            AddedNodes = AddedNodes.AddRange(other.AddedNodes),
            AddedPorts = AddedPorts.AddRange(other.AddedPorts),
            AddedAlignments = AddedAlignments.AddRange(other.AddedAlignments),
            AddedTracks = AddedTracks.AddRange(other.AddedTracks),
            AddedTrackGroups =
                AddedTrackGroups.AddRange(other.AddedTrackGroups),
            AddedConnections =
                AddedConnections.AddRange(other.AddedConnections)
        };
    }

    private static void EnsureMatches<TValue>(
        ImmutableDictionary<World.StableId, TValue> values,
        World.StableId id,
        TValue expected)
        where TValue : notnull
    {
        if (!values.TryGetValue(id, out var actual) ||
            !EqualityComparer<TValue>.Default.Equals(actual, expected))
        {
            throw new RailTopologyException(
                "rail.patch.remove_mismatch",
                $"Rail entity {id} no longer matches the patch base value.");
        }
    }
}
