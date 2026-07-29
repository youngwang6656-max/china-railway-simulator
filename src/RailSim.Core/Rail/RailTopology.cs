using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Rail;

public sealed record RailTopology
{
    private RailTopology(
        ImmutableDictionary<StableId, RailNode> nodes,
        ImmutableDictionary<StableId, RailPort> ports,
        ImmutableDictionary<StableId, Alignment> alignments,
        ImmutableDictionary<StableId, TrackEdge> tracks,
        ImmutableDictionary<StableId, TrackGroup> trackGroups,
        ImmutableDictionary<StableId, TrackConnection> connections)
    {
        Nodes = nodes;
        Ports = ports;
        Alignments = alignments;
        Tracks = tracks;
        TrackGroups = trackGroups;
        Connections = connections;
    }

    public ImmutableDictionary<StableId, RailNode> Nodes { get; }

    public ImmutableDictionary<StableId, RailPort> Ports { get; }

    public ImmutableDictionary<StableId, Alignment> Alignments { get; }

    public ImmutableDictionary<StableId, TrackEdge> Tracks { get; }

    public ImmutableDictionary<StableId, TrackGroup> TrackGroups { get; }

    public ImmutableDictionary<StableId, TrackConnection> Connections { get; }

    public static RailTopology Empty { get; } = new(
        ImmutableDictionary<StableId, RailNode>.Empty,
        ImmutableDictionary<StableId, RailPort>.Empty,
        ImmutableDictionary<StableId, Alignment>.Empty,
        ImmutableDictionary<StableId, TrackEdge>.Empty,
        ImmutableDictionary<StableId, TrackGroup>.Empty,
        ImmutableDictionary<StableId, TrackConnection>.Empty);

    public RailTopology AddNode(RailNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new RailTopology(
            AddUnique(Nodes, node.Id, node, "rail.node.duplicate"),
            Ports,
            Alignments,
            Tracks,
            TrackGroups,
            Connections);
    }

    public RailTopology AddPort(RailPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!Nodes.ContainsKey(port.NodeId))
        {
            throw Error(
                "rail.port.node_missing",
                $"Port {port.Id} references missing node {port.NodeId}.");
        }

        return new RailTopology(
            Nodes,
            AddUnique(Ports, port.Id, port, "rail.port.duplicate"),
            Alignments,
            Tracks,
            TrackGroups,
            Connections);
    }

    public RailTopology AddAlignment(Alignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        return new RailTopology(
            Nodes,
            Ports,
            AddUnique(
                Alignments,
                alignment.Id,
                alignment,
                "rail.alignment.duplicate"),
            Tracks,
            TrackGroups,
            Connections);
    }

    public RailTopology AddTrack(TrackEdge track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!Ports.TryGetValue(track.FromPortId, out var fromPort) ||
            !Ports.TryGetValue(track.ToPortId, out var toPort))
        {
            throw Error(
                "rail.track.port_missing",
                $"Track {track.Id} references a missing port.");
        }

        if (!Alignments.ContainsKey(track.AlignmentId))
        {
            throw Error(
                "rail.track.alignment_missing",
                $"Track {track.Id} references missing alignment {track.AlignmentId}.");
        }

        if (fromPort.Direction == RailPortDirection.Inbound ||
            toPort.Direction == RailPortDirection.Outbound)
        {
            throw Error(
                "rail.track.port_direction",
                $"Track {track.Id} uses incompatible port directions.");
        }

        return new RailTopology(
            Nodes,
            Ports,
            Alignments,
            AddUnique(Tracks, track.Id, track, "rail.track.duplicate"),
            TrackGroups,
            Connections);
    }

    public RailTopology AddTrackGroup(TrackGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var missingTrack = group.TrackIds.FirstOrDefault(
            trackId => !Tracks.ContainsKey(trackId));
        if (missingTrack != default)
        {
            throw Error(
                "rail.group.track_missing",
                $"Track group {group.Id} references missing track {missingTrack}.");
        }

        return new RailTopology(
            Nodes,
            Ports,
            Alignments,
            Tracks,
            AddUnique(
                TrackGroups,
                group.Id,
                group,
                "rail.group.duplicate"),
            Connections);
    }

    public RailTopology AddConnection(TrackConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Nodes.ContainsKey(connection.NodeId))
        {
            throw Error(
                "rail.connection.node_missing",
                $"Connection {connection.Id} references missing node {connection.NodeId}.");
        }

        if (!Tracks.TryGetValue(connection.FromTrackId, out var fromTrack) ||
            !Tracks.TryGetValue(connection.ToTrackId, out var toTrack))
        {
            throw Error(
                "rail.connection.track_missing",
                $"Connection {connection.Id} references a missing track.");
        }

        var fromNode = Ports[fromTrack.ToPortId].NodeId;
        var toNode = Ports[toTrack.FromPortId].NodeId;
        if (fromNode != connection.NodeId || toNode != connection.NodeId)
        {
            throw Error(
                "rail.connection.node_mismatch",
                $"Connection {connection.Id} tracks do not meet at node {connection.NodeId}.");
        }

        return new RailTopology(
            Nodes,
            Ports,
            Alignments,
            Tracks,
            TrackGroups,
            AddUnique(
                Connections,
                connection.Id,
                connection,
                "rail.connection.duplicate"));
    }

    public RailTopology RemoveConnection(StableId id)
    {
        EnsureExists(Connections, id, "rail.connection.missing");
        return new RailTopology(
            Nodes,
            Ports,
            Alignments,
            Tracks,
            TrackGroups,
            Connections.Remove(id));
    }

    public RailTopology RemoveTrackGroup(StableId id)
    {
        EnsureExists(TrackGroups, id, "rail.group.missing");
        return new RailTopology(
            Nodes,
            Ports,
            Alignments,
            Tracks,
            TrackGroups.Remove(id),
            Connections);
    }

    public RailTopology RemoveTrack(StableId id)
    {
        EnsureExists(Tracks, id, "rail.track.missing");
        if (Connections.Values.Any(
                connection =>
                    connection.FromTrackId == id ||
                    connection.ToTrackId == id))
        {
            throw Error(
                "rail.track.connection_exists",
                $"Track {id} is still referenced by a connection.");
        }

        if (TrackGroups.Values.Any(group => group.TrackIds.Contains(id)))
        {
            throw Error(
                "rail.track.group_exists",
                $"Track {id} is still referenced by a track group.");
        }

        return new RailTopology(
            Nodes,
            Ports,
            Alignments,
            Tracks.Remove(id),
            TrackGroups,
            Connections);
    }

    public RailTopology RemoveAlignment(StableId id)
    {
        EnsureExists(Alignments, id, "rail.alignment.missing");
        if (Tracks.Values.Any(track => track.AlignmentId == id))
        {
            throw Error(
                "rail.alignment.track_exists",
                $"Alignment {id} is still referenced by a track.");
        }

        return new RailTopology(
            Nodes,
            Ports,
            Alignments.Remove(id),
            Tracks,
            TrackGroups,
            Connections);
    }

    public RailTopology RemovePort(StableId id)
    {
        EnsureExists(Ports, id, "rail.port.missing");
        if (Tracks.Values.Any(
                track => track.FromPortId == id || track.ToPortId == id))
        {
            throw Error(
                "rail.port.track_exists",
                $"Port {id} is still referenced by a track.");
        }

        return new RailTopology(
            Nodes,
            Ports.Remove(id),
            Alignments,
            Tracks,
            TrackGroups,
            Connections);
    }

    public RailTopology RemoveNode(StableId id)
    {
        EnsureExists(Nodes, id, "rail.node.missing");
        if (Ports.Values.Any(port => port.NodeId == id))
        {
            throw Error(
                "rail.node.port_exists",
                $"Node {id} is still referenced by a port.");
        }

        if (Connections.Values.Any(connection => connection.NodeId == id))
        {
            throw Error(
                "rail.node.connection_exists",
                $"Node {id} is still referenced by a connection.");
        }

        return new RailTopology(
            Nodes.Remove(id),
            Ports,
            Alignments,
            Tracks,
            TrackGroups,
            Connections);
    }

    private static ImmutableDictionary<StableId, TValue> AddUnique<TValue>(
        ImmutableDictionary<StableId, TValue> values,
        StableId id,
        TValue value,
        string duplicateCode)
    {
        if (values.ContainsKey(id))
        {
            throw Error(
                duplicateCode,
                $"Rail entity ID {id} already exists.");
        }

        return values.Add(id, value);
    }

    private static void EnsureExists<TValue>(
        ImmutableDictionary<StableId, TValue> values,
        StableId id,
        string missingCode)
    {
        if (!values.ContainsKey(id))
        {
            throw Error(
                missingCode,
                $"Rail entity ID {id} does not exist.");
        }
    }

    private static RailTopologyException Error(
        string code,
        string message)
    {
        return new RailTopologyException(code, message);
    }
}
