using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public sealed class RailTopologyEditStrategy
{
    private readonly IStableIdSource _ids;

    public RailTopologyEditStrategy(IStableIdSource ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public RailGraphPatch CreateDeletePatch(
        RailTopology topology,
        ImmutableHashSet<StableId> selectedIds,
        int chunkSizeMeters)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(selectedIds);
        EnsureChunkSize(chunkSizeMeters);

        var trackIds = selectedIds
            .Where(topology.Tracks.ContainsKey)
            .ToHashSet();
        foreach (var alignmentId in selectedIds.Where(
                     topology.Alignments.ContainsKey))
        {
            trackIds.UnionWith(
                topology.Tracks.Values
                    .Where(track => track.AlignmentId == alignmentId)
                    .Select(track => track.Id));
        }

        var selectedNodeIds = selectedIds
            .Where(topology.Nodes.ContainsKey)
            .ToHashSet();
        trackIds.UnionWith(
            topology.Tracks.Values
                .Where(
                    track =>
                        selectedNodeIds.Contains(
                            topology.Ports[track.FromPortId].NodeId) ||
                        selectedNodeIds.Contains(
                            topology.Ports[track.ToPortId].NodeId))
                .Select(track => track.Id));

        var groupIds = selectedIds
            .Where(topology.TrackGroups.ContainsKey)
            .ToHashSet();
        var expanded = true;
        while (expanded)
        {
            expanded = false;
            foreach (var group in topology.TrackGroups.Values)
            {
                if (!groupIds.Contains(group.Id) &&
                    !group.TrackIds.Any(trackIds.Contains))
                {
                    continue;
                }

                if (groupIds.Add(group.Id))
                {
                    expanded = true;
                }

                var before = trackIds.Count;
                trackIds.UnionWith(group.TrackIds);
                expanded |= before != trackIds.Count;
            }
        }

        var removedTracks = trackIds
            .Select(id => topology.Tracks[id])
            .OrderBy(track => track.Id)
            .ToImmutableArray();
        var removedConnections = topology.Connections.Values
            .Where(
                connection =>
                    selectedIds.Contains(connection.Id) ||
                    trackIds.Contains(connection.FromTrackId) ||
                    trackIds.Contains(connection.ToTrackId))
            .OrderBy(connection => connection.Id)
            .ToImmutableArray();
        var removedGroups = groupIds
            .Select(id => topology.TrackGroups[id])
            .OrderBy(group => group.Id)
            .ToImmutableArray();
        var removedAlignments = topology.Alignments.Values
            .Where(
                alignment =>
                {
                    var users = topology.Tracks.Values
                        .Where(
                            track =>
                                track.AlignmentId == alignment.Id)
                        .Select(track => track.Id)
                        .ToArray();
                    return users.Length > 0 &&
                        users.All(trackIds.Contains);
                })
            .OrderBy(alignment => alignment.Id)
            .ToImmutableArray();
        var removedPortIds = removedTracks
            .SelectMany(track => new[] { track.FromPortId, track.ToPortId })
            .Where(
                portId =>
                    topology.Tracks.Values
                        .Where(track => !trackIds.Contains(track.Id))
                        .All(
                            track =>
                                track.FromPortId != portId &&
                                track.ToPortId != portId))
            .ToHashSet();
        var removedPorts = removedPortIds
            .Select(id => topology.Ports[id])
            .OrderBy(port => port.Id)
            .ToImmutableArray();
        var candidateNodeIds = removedPorts
            .Select(port => port.NodeId)
            .Concat(selectedNodeIds)
            .ToHashSet();
        var removedNodes = topology.Nodes.Values
            .Where(
                node =>
                    candidateNodeIds.Contains(node.Id) &&
                    node.StationId is null &&
                    topology.Ports.Values
                        .Where(port => port.NodeId == node.Id)
                        .All(port => removedPortIds.Contains(port.Id)) &&
                    topology.Connections.Values
                        .Where(
                            connection =>
                                connection.NodeId == node.Id)
                        .All(removedConnections.Contains))
            .OrderBy(node => node.Id)
            .ToImmutableArray();

        var affectedChunks = Cover(
                removedAlignments.Select(
                    alignment => alignment.ControlPoints),
                chunkSizeMeters,
                removedTracks)
            .AddRange(
                removedConnections.Select(
                    connection => WorldChunkId.FromPoint(
                        topology.Nodes[connection.NodeId].Position,
                        chunkSizeMeters)))
            .AddRange(
                removedNodes.Select(
                    node => WorldChunkId.FromPoint(
                        node.Position,
                        chunkSizeMeters)))
            .Distinct()
            .OrderBy(chunk => chunk.X)
            .ThenBy(chunk => chunk.Y)
            .ToImmutableArray();
        return new RailGraphPatch
        {
            Intent = RailEditIntent.Custom,
            AffectedChunks = affectedChunks,
            RemovedNodes = removedNodes,
            RemovedPorts = removedPorts,
            RemovedAlignments = removedAlignments,
            RemovedTracks = removedTracks,
            RemovedTrackGroups = removedGroups,
            RemovedConnections = removedConnections
        };
    }

    public RailGraphPatch CreateDisconnectPatch(
        RailTopology topology,
        ImmutableHashSet<StableId> selectedIds,
        int chunkSizeMeters)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(selectedIds);
        EnsureChunkSize(chunkSizeMeters);
        var nodeIds = selectedIds
            .Where(topology.Nodes.ContainsKey)
            .ToHashSet();
        var trackIds = selectedIds
            .Where(topology.Tracks.ContainsKey)
            .ToHashSet();
        foreach (var groupId in selectedIds.Where(
                     topology.TrackGroups.ContainsKey))
        {
            trackIds.UnionWith(topology.TrackGroups[groupId].TrackIds);
        }

        var connections = topology.Connections.Values
            .Where(
                connection =>
                    selectedIds.Contains(connection.Id) ||
                    nodeIds.Contains(connection.NodeId) ||
                    trackIds.Contains(connection.FromTrackId) ||
                    trackIds.Contains(connection.ToTrackId))
            .OrderBy(connection => connection.Id)
            .ToImmutableArray();
        var chunks = connections
            .Select(
                connection => WorldChunkId.FromPoint(
                    topology.Nodes[connection.NodeId].Position,
                    chunkSizeMeters))
            .Distinct()
            .OrderBy(chunk => chunk.X)
            .ThenBy(chunk => chunk.Y)
            .ToImmutableArray();
        return RailGraphPatch.Empty with
        {
            Intent = RailEditIntent.Custom,
            AffectedChunks = chunks,
            RemovedConnections = connections
        };
    }

    public RailGraphPatch CreateRealignPatch(
        RailTopology topology,
        StableId targetTrackId,
        AlignmentDraft draft,
        int chunkSizeMeters)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(draft);
        EnsureChunkSize(chunkSizeMeters);
        if (!topology.Tracks.TryGetValue(targetTrackId, out var target))
        {
            throw new ArgumentException(
                $"Track {targetTrackId} does not exist.",
                nameof(targetTrackId));
        }

        var originalAlignment = topology.Alignments[target.AlignmentId];
        if (draft.SampledPoints[0] !=
                originalAlignment.ControlPoints[0] ||
            draft.SampledPoints[^1] !=
                originalAlignment.ControlPoints[^1])
        {
            throw new ArgumentException(
                "Realignment must preserve both topology boundary positions.",
                nameof(draft));
        }

        var tracks = topology.Tracks.Values
            .Where(
                track =>
                    track.AlignmentId == originalAlignment.Id)
            .OrderBy(track => track.Id)
            .ToImmutableArray();
        if (tracks.Length != 2)
        {
            throw new InvalidOperationException(
                "Realignment requires one opposing physical track pair.");
        }

        var trackIds = tracks
            .Select(track => track.Id)
            .ToImmutableHashSet();
        var groups = topology.TrackGroups.Values
            .Where(group => group.TrackIds.Any(trackIds.Contains))
            .OrderBy(group => group.Id)
            .ToImmutableArray();
        var connections = topology.Connections.Values
            .Where(
                connection =>
                    trackIds.Contains(connection.FromTrackId) ||
                    trackIds.Contains(connection.ToTrackId))
            .OrderBy(connection => connection.Id)
            .ToImmutableArray();
        var replacementAlignment = new Alignment(
            originalAlignment.Id,
            draft.SampledPoints,
            CalculateLength(draft.SampledPoints));
        var padding = tracks.Max(
            track =>
                Math.Abs((long)track.LateralOffsetMillimeters));
        var affectedChunks = WorldChunkCoverage.TracePolyline(
                originalAlignment.ControlPoints,
                chunkSizeMeters,
                padding)
            .AddRange(
                WorldChunkCoverage.TracePolyline(
                    replacementAlignment.ControlPoints,
                    chunkSizeMeters,
                    padding))
            .Distinct()
            .OrderBy(chunk => chunk.X)
            .ThenBy(chunk => chunk.Y)
            .ToImmutableArray();
        return new RailGraphPatch
        {
            Intent = RailEditIntent.Realign,
            AffectedChunks = affectedChunks,
            RemovedAlignments = [originalAlignment],
            RemovedTracks = tracks,
            RemovedTrackGroups = groups,
            RemovedConnections = connections,
            AddedAlignments = [replacementAlignment],
            AddedTracks = tracks,
            AddedTrackGroups = groups,
            AddedConnections = connections
        };
    }

    public RailGraphPatch CreateSplitPatch(
        RailTopology topology,
        StableId targetTrackId,
        WorldPoint requestedPoint,
        int chunkSizeMeters)
    {
        ArgumentNullException.ThrowIfNull(topology);
        EnsureChunkSize(chunkSizeMeters);
        if (!topology.Tracks.TryGetValue(targetTrackId, out var target))
        {
            throw new ArgumentException(
                $"Track {targetTrackId} does not exist.",
                nameof(targetTrackId));
        }

        var alignment = topology.Alignments[target.AlignmentId];
        var pair = topology.Tracks.Values
            .Where(track => track.AlignmentId == alignment.Id)
            .OrderBy(track => track.Id)
            .ToImmutableArray();
        if (pair.Length != 2)
        {
            throw new InvalidOperationException(
                "Track splitting requires one opposing physical pair.");
        }

        var group = topology.TrackGroups.Values.Single(
            candidate => pair.All(track => candidate.TrackIds.Contains(track.Id)));
        var split = SplitGeometry(
            alignment.ControlPoints,
            requestedPoint);
        var splitNode = new RailNode(
            _ids.Next("split-node"),
            RailNodeKind.Junction,
            split.Point,
            null);
        var firstAlignment = new Alignment(
            _ids.Next("split-alignment"),
            split.First,
            CalculateLength(split.First));
        var secondAlignment = new Alignment(
            _ids.Next("split-alignment"),
            split.Second,
            CalculateLength(split.Second));
        var ports = ImmutableArray.CreateBuilder<RailPort>();
        var tracks = ImmutableArray.CreateBuilder<TrackEdge>();
        var replacements =
            new Dictionary<StableId, ImmutableArray<TrackEdge>>();
        foreach (var original in pair)
        {
            var fromNode =
                topology.Nodes[topology.Ports[original.FromPortId].NodeId];
            var toNode =
                topology.Nodes[topology.Ports[original.ToPortId].NodeId];
            var travelsForward =
                fromNode.Position == alignment.ControlPoints[0];
            var first = NewTrack(
                travelsForward ? fromNode.Id : splitNode.Id,
                travelsForward ? splitNode.Id : toNode.Id,
                firstAlignment.Id,
                original,
                ports);
            var second = NewTrack(
                travelsForward ? splitNode.Id : fromNode.Id,
                travelsForward ? toNode.Id : splitNode.Id,
                secondAlignment.Id,
                original,
                ports);
            var ordered = travelsForward
                ? ImmutableArray.Create(first, second)
                : ImmutableArray.Create(second, first);
            tracks.AddRange(ordered);
            replacements.Add(original.Id, ordered);
        }

        var removedConnections = topology.Connections.Values
            .Where(
                connection =>
                    replacements.ContainsKey(connection.FromTrackId) ||
                    replacements.ContainsKey(connection.ToTrackId))
            .OrderBy(connection => connection.Id)
            .ToImmutableArray();
        var addedConnections = ImmutableArray.CreateBuilder<TrackConnection>();
        foreach (var connection in removedConnections)
        {
            var fromTrackId = replacements.TryGetValue(
                connection.FromTrackId,
                out var fromReplacements)
                ? ArrivingAt(
                    ports,
                    fromReplacements,
                    connection.NodeId).Id
                : connection.FromTrackId;
            var toTrackId = replacements.TryGetValue(
                connection.ToTrackId,
                out var toReplacements)
                ? DepartingFrom(
                    ports,
                    toReplacements,
                    connection.NodeId).Id
                : connection.ToTrackId;
            addedConnections.Add(
                connection with
                {
                    FromTrackId = fromTrackId,
                    ToTrackId = toTrackId
                });
        }

        foreach (var original in pair)
        {
            var replacement = replacements[original.Id];
            addedConnections.Add(new TrackConnection(
                _ids.Next("split-connection"),
                splitNode.Id,
                replacement[0].Id,
                replacement[1].Id));
        }

        var pairIds = pair.Select(track => track.Id).ToHashSet();
        var replacementTrackIds = group.TrackIds
            .SelectMany(
                id => pairIds.Contains(id)
                    ? replacements[id].Select(track => track.Id)
                    : [id])
            .ToImmutableArray();
        return new RailGraphPatch
        {
            Intent = RailEditIntent.Custom,
            AffectedChunks = WorldChunkCoverage.TracePolyline(
                alignment.ControlPoints,
                chunkSizeMeters,
                pair.Max(
                    track =>
                        Math.Abs(
                            (long)track.LateralOffsetMillimeters))),
            RemovedPorts = pair
                .SelectMany(
                    track => new[]
                    {
                        topology.Ports[track.FromPortId],
                        topology.Ports[track.ToPortId]
                    })
                .DistinctBy(port => port.Id)
                .OrderBy(port => port.Id)
                .ToImmutableArray(),
            RemovedAlignments = [alignment],
            RemovedTracks = pair,
            RemovedTrackGroups = [group],
            RemovedConnections = removedConnections,
            AddedNodes = [splitNode],
            AddedPorts = ports.ToImmutable(),
            AddedAlignments = [firstAlignment, secondAlignment],
            AddedTracks = tracks.ToImmutable(),
            AddedTrackGroups =
            [
                new TrackGroup(group.Id, replacementTrackIds)
            ],
            AddedConnections = addedConnections.ToImmutable()
        };
    }

    private TrackEdge NewTrack(
        StableId fromNodeId,
        StableId toNodeId,
        StableId alignmentId,
        TrackEdge template,
        ImmutableArray<RailPort>.Builder ports)
    {
        var from = new RailPort(
            _ids.Next("split-port"),
            fromNodeId,
            RailPortDirection.Outbound);
        var to = new RailPort(
            _ids.Next("split-port"),
            toNodeId,
            RailPortDirection.Inbound);
        ports.Add(from);
        ports.Add(to);
        return new TrackEdge(
            _ids.Next("split-track"),
            from.Id,
            to.Id,
            alignmentId,
            template.LateralOffsetMillimeters,
            template.DesignSpeedKph);
    }

    private static TrackEdge ArrivingAt(
        ImmutableArray<RailPort>.Builder ports,
        ImmutableArray<TrackEdge> candidates,
        StableId nodeId)
    {
        return candidates.Single(
            track =>
                NodeIdForPort(ports, track.ToPortId) ==
                nodeId);
    }

    private static TrackEdge DepartingFrom(
        ImmutableArray<RailPort>.Builder ports,
        ImmutableArray<TrackEdge> candidates,
        StableId nodeId)
    {
        return candidates.Single(
            track =>
                NodeIdForPort(ports, track.FromPortId) ==
                nodeId);
    }

    private static StableId NodeIdForPort(
        ImmutableArray<RailPort>.Builder ports,
        StableId portId)
    {
        return ports.Single(port => port.Id == portId).NodeId;
    }

    private static SplitAlignment SplitGeometry(
        ImmutableArray<WorldPoint> points,
        WorldPoint requested)
    {
        var bestSegment = -1;
        var bestDistanceSquared = double.PositiveInfinity;
        var bestT = 0d;
        for (var index = 0; index < points.Length - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            var deltaX =
                (double)end.XMillimeters - start.XMillimeters;
            var deltaY =
                (double)end.YMillimeters - start.YMillimeters;
            var lengthSquared =
                (deltaX * deltaX) + (deltaY * deltaY);
            var t = Math.Clamp(
                (((requested.XMillimeters - start.XMillimeters) * deltaX) +
                 ((requested.YMillimeters - start.YMillimeters) * deltaY)) /
                lengthSquared,
                0d,
                1d);
            var x = start.XMillimeters + (deltaX * t);
            var y = start.YMillimeters + (deltaY * t);
            var distanceX = requested.XMillimeters - x;
            var distanceY = requested.YMillimeters - y;
            var distanceSquared =
                (distanceX * distanceX) + (distanceY * distanceY);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestSegment = index;
                bestT = t;
            }
        }

        var segmentStart = points[bestSegment];
        var segmentEnd = points[bestSegment + 1];
        var point = new WorldPoint(
            checked((long)Math.Round(
                segmentStart.XMillimeters +
                ((segmentEnd.XMillimeters -
                  segmentStart.XMillimeters) * bestT))),
            checked((long)Math.Round(
                segmentStart.YMillimeters +
                ((segmentEnd.YMillimeters -
                  segmentStart.YMillimeters) * bestT))));
        if (point.DistanceMetersTo(points[0]) < 1 ||
            point.DistanceMetersTo(points[^1]) < 1)
        {
            throw new ArgumentException(
                "Split point must be at least one metre from both endpoints.",
                nameof(requested));
        }

        var first = points[..(bestSegment + 1)].ToBuilder();
        if (first[^1] != point)
        {
            first.Add(point);
        }

        var second = ImmutableArray.CreateBuilder<WorldPoint>();
        second.Add(point);
        for (var index = bestSegment + 1; index < points.Length; index++)
        {
            if (second[^1] != points[index])
            {
                second.Add(points[index]);
            }
        }

        return new SplitAlignment(
            point,
            first.ToImmutable(),
            second.ToImmutable());
    }

    private static ImmutableArray<WorldChunkId> Cover(
        IEnumerable<ImmutableArray<WorldPoint>> geometries,
        int chunkSizeMeters,
        ImmutableArray<TrackEdge> tracks)
    {
        var padding = tracks.IsEmpty
            ? 0
            : tracks.Max(
                track =>
                    Math.Abs((long)track.LateralOffsetMillimeters));
        return geometries
            .SelectMany(
                points => WorldChunkCoverage.TracePolyline(
                    points,
                    chunkSizeMeters,
                    padding))
            .Distinct()
            .OrderBy(chunk => chunk.X)
            .ThenBy(chunk => chunk.Y)
            .ToImmutableArray();
    }

    private static long CalculateLength(
        ImmutableArray<WorldPoint> points)
    {
        var length = 0d;
        for (var index = 1; index < points.Length; index++)
        {
            length += points[index - 1].DistanceMetersTo(points[index]) *
                1000d;
        }

        return checked((long)Math.Round(length));
    }

    private static void EnsureChunkSize(int chunkSizeMeters)
    {
        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }
    }

    private sealed record SplitAlignment(
        WorldPoint Point,
        ImmutableArray<WorldPoint> First,
        ImmutableArray<WorldPoint> Second);
}
