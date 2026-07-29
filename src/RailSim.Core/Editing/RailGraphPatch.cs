using System.Collections.Immutable;
using RailSim.Core.Rail;

namespace RailSim.Core.Editing;

public sealed record RailGraphPatch
{
    public ImmutableArray<RailNode> AddedNodes { get; init; } = [];

    public ImmutableArray<RailPort> AddedPorts { get; init; } = [];

    public ImmutableArray<Alignment> AddedAlignments { get; init; } = [];

    public ImmutableArray<TrackEdge> AddedTracks { get; init; } = [];

    public ImmutableArray<TrackGroup> AddedTrackGroups { get; init; } = [];

    public ImmutableArray<TrackConnection> AddedConnections { get; init; } = [];

    public static RailGraphPatch Empty { get; } = new();

    public RailTopology ApplyTo(RailTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        var candidate = topology;
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

    public RailGraphPatch Combine(RailGraphPatch other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new RailGraphPatch
        {
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
}
