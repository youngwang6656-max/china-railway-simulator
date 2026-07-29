using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public interface IStableIdSource
{
    StableId Next(string category);
}

public sealed class DeterministicStableIdSource : IStableIdSource
{
    private readonly string _scope;
    private long _sequence;

    public DeterministicStableIdSource(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _scope = scope;
    }

    public StableId Next(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return new StableId(
            $"{_scope}:{category}:{checked(++_sequence):D5}");
    }
}

public readonly record struct TrackPlacementOptions(
    int TrackSpacingMillimeters,
    int DesignSpeedKph)
{
    public static TrackPlacementOptions HighSpeedDoubleTrack { get; } =
        new(5_000, 350);
}

public interface ITrackPlacementStrategy
{
    RailGraphPatch CreatePatch(
        RailTopology topology,
        AlignmentDraft draft,
        TrackPlacementOptions options);
}

public sealed class DoubleTrackCorridorStrategy : ITrackPlacementStrategy
{
    private readonly IStableIdSource _ids;

    public DoubleTrackCorridorStrategy(IStableIdSource ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public RailGraphPatch CreatePatch(
        RailTopology topology,
        AlignmentDraft draft,
        TrackPlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(draft);
        if (options.TrackSpacingMillimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.DesignSpeedKph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var boundaryIndices = FindBoundaryIndices(draft.Waypoints);
        if (boundaryIndices.Length < 2)
        {
            throw new ArgumentException(
                "A railway draft needs at least two topology boundaries.",
                nameof(draft));
        }

        var addedNodes = ImmutableArray.CreateBuilder<RailNode>();
        var addedPorts = ImmutableArray.CreateBuilder<RailPort>();
        var addedAlignments = ImmutableArray.CreateBuilder<Alignment>();
        var addedTracks = ImmutableArray.CreateBuilder<TrackEdge>();
        var addedConnections =
            ImmutableArray.CreateBuilder<TrackConnection>();
        var boundaryNodes = new List<RailNode>(boundaryIndices.Length);
        foreach (var index in boundaryIndices)
        {
            var waypoint = draft.Waypoints[index];
            var existing = waypoint.StationId.HasValue
                ? topology.Nodes.Values.FirstOrDefault(
                    node => node.StationId == waypoint.StationId)
                : null;
            if (existing is not null)
            {
                boundaryNodes.Add(existing);
                continue;
            }

            var node = new RailNode(
                _ids.Next("node"),
                waypoint.StationId.HasValue
                    ? RailNodeKind.StationBoundary
                    : RailNodeKind.Endpoint,
                waypoint.Position,
                waypoint.StationId);
            addedNodes.Add(node);
            boundaryNodes.Add(node);
        }

        var upTracks = new List<TrackEdge>(boundaryIndices.Length - 1);
        var downTracks = new List<TrackEdge>(boundaryIndices.Length - 1);
        var halfSpacing = options.TrackSpacingMillimeters / 2;
        for (var segment = 0; segment < boundaryIndices.Length - 1; segment++)
        {
            var startIndex = boundaryIndices[segment];
            var endIndex = boundaryIndices[segment + 1];
            var controlPoints = draft.Waypoints[startIndex..(endIndex + 1)]
                .Select(waypoint => waypoint.Position)
                .ToImmutableArray();
            var alignment = new Alignment(
                _ids.Next("alignment"),
                controlPoints,
                CalculateLength(controlPoints));
            addedAlignments.Add(alignment);

            var leftNode = boundaryNodes[segment];
            var rightNode = boundaryNodes[segment + 1];
            var upFrom = NewPort(leftNode.Id, RailPortDirection.Outbound);
            var upTo = NewPort(rightNode.Id, RailPortDirection.Inbound);
            var downFrom = NewPort(rightNode.Id, RailPortDirection.Outbound);
            var downTo = NewPort(leftNode.Id, RailPortDirection.Inbound);
            addedPorts.Add(upFrom);
            addedPorts.Add(upTo);
            addedPorts.Add(downFrom);
            addedPorts.Add(downTo);

            var upTrack = new TrackEdge(
                _ids.Next("track-up"),
                upFrom.Id,
                upTo.Id,
                alignment.Id,
                -halfSpacing,
                options.DesignSpeedKph);
            var downTrack = new TrackEdge(
                _ids.Next("track-down"),
                downFrom.Id,
                downTo.Id,
                alignment.Id,
                halfSpacing,
                options.DesignSpeedKph);
            addedTracks.Add(upTrack);
            addedTracks.Add(downTrack);
            upTracks.Add(upTrack);
            downTracks.Add(downTrack);
        }

        for (var boundary = 1;
             boundary < boundaryNodes.Count - 1;
             boundary++)
        {
            var node = boundaryNodes[boundary];
            addedConnections.Add(new TrackConnection(
                _ids.Next("connection-up"),
                node.Id,
                upTracks[boundary - 1].Id,
                upTracks[boundary].Id));
            addedConnections.Add(new TrackConnection(
                _ids.Next("connection-down"),
                node.Id,
                downTracks[boundary].Id,
                downTracks[boundary - 1].Id));
        }

        var allTrackIds = addedTracks
            .Select(track => track.Id)
            .ToImmutableArray();
        var group = new TrackGroup(
            _ids.Next("track-group"),
            allTrackIds);
        return new RailGraphPatch
        {
            AddedNodes = addedNodes.ToImmutable(),
            AddedPorts = addedPorts.ToImmutable(),
            AddedAlignments = addedAlignments.ToImmutable(),
            AddedTracks = addedTracks.ToImmutable(),
            AddedTrackGroups = [group],
            AddedConnections = addedConnections.ToImmutable()
        };
    }

    private RailPort NewPort(
        StableId nodeId,
        RailPortDirection direction)
    {
        return new RailPort(_ids.Next("port"), nodeId, direction);
    }

    private static ImmutableArray<int> FindBoundaryIndices(
        ImmutableArray<DraftWaypoint> waypoints)
    {
        var values = ImmutableArray.CreateBuilder<int>();
        values.Add(0);
        for (var index = 1; index < waypoints.Length - 1; index++)
        {
            if (waypoints[index].StationId.HasValue)
            {
                values.Add(index);
            }
        }

        values.Add(waypoints.Length - 1);
        return values.ToImmutable();
    }

    private static long CalculateLength(
        ImmutableArray<WorldPoint> points)
    {
        var length = 0d;
        for (var index = 1; index < points.Length; index++)
        {
            length += points[index - 1].DistanceMetersTo(points[index]) * 1000d;
        }

        return checked((long)Math.Round(length));
    }
}

public readonly record struct ConnectorMovement(
    StableId NodeId,
    StableId FromTrackId,
    StableId ToTrackId);

public sealed class ExplicitConnectorStrategy
{
    private readonly IStableIdSource _ids;

    public ExplicitConnectorStrategy(IStableIdSource ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public RailGraphPatch CreateConnectionPatch(
        RailTopology topology,
        ImmutableArray<ConnectorMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (movements.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one connector movement is required.",
                nameof(movements));
        }

        var candidate = topology;
        var connections = ImmutableArray.CreateBuilder<TrackConnection>(
            movements.Length);
        foreach (var movement in movements)
        {
            var connection = new TrackConnection(
                _ids.Next("connector-movement"),
                movement.NodeId,
                movement.FromTrackId,
                movement.ToTrackId);
            candidate = candidate.AddConnection(connection);
            connections.Add(connection);
        }

        return RailGraphPatch.Empty with
        {
            AddedConnections = connections.MoveToImmutable()
        };
    }
}
