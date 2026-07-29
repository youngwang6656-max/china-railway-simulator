using System.Collections.Immutable;
using RailSim.Core.Projection;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public sealed class RailDrawingSnapCandidateProvider
{
    private const double SnapRadiusPixels = 16;
    private const double AngleGuideRadiusPixels = 8;
    private RailSpatialIndex? _railIndex;
    private GeneratedWorld? _indexedBaseWorld;
    private long _indexedRevision = -1;
    private ImmutableDictionary<StableId, StableId> _nodeIdByStationId =
        ImmutableDictionary<StableId, StableId>.Empty;
    private ImmutableDictionary<StableId, ImmutableArray<StableId>>
        _trackIdsByNodeId =
            ImmutableDictionary<StableId, ImmutableArray<StableId>>.Empty;

    public ImmutableArray<RailSnapCandidate> BuildCandidates(
        WorldState world,
        WorldPoint point,
        ImmutableArray<DraftWaypoint> confirmedWaypoints,
        double metersPerPixel)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!double.IsFinite(metersPerPixel) ||
            metersPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metersPerPixel));
        }

        var radiusMeters =
            metersPerPixel * SnapRadiusPixels;
        var radiusMillimeters = checked((long)Math.Ceiling(
            radiusMeters * 1000d));
        var bounds = new WorldBounds(
            point.OffsetMillimeters(
                -radiusMillimeters,
                -radiusMillimeters),
            point.OffsetMillimeters(
                radiusMillimeters,
                radiusMillimeters));
        var regionProvider = new GridWorldRegionProvider();
        var railSelection = GetRailIndex(world).Query(
            bounds,
            regionProvider);
        var candidates =
            ImmutableArray.CreateBuilder<RailSnapCandidate>();
        foreach (var id in railSelection.NodeIds)
        {
            if (!world.RailTopology.Nodes.TryGetValue(id, out var node))
            {
                continue;
            }

            AddWithinSnapRadius(
                candidates,
                new RailSnapCandidate(
                    node.Id,
                    RailSnapKind.RailNode,
                    new DraftWaypoint(
                        node.Position,
                        node.StationId,
                        node.Id),
                    node.Position.DistanceMetersTo(point) /
                    metersPerPixel));
        }

        var geographic = world.BaseWorld.RegionIndex.Query(
            bounds,
            regionProvider);
        foreach (var id in geographic.StationIds)
        {
            var station = world.BaseWorld.Stations[id];
            AddWithinSnapRadius(
                candidates,
                new RailSnapCandidate(
                    station.Id,
                    RailSnapKind.Station,
                    new DraftWaypoint(
                        station.Position,
                        station.Id),
                    station.Position.DistanceMetersTo(point) /
                    metersPerPixel));
        }

        foreach (var id in railSelection.TrackIds)
        {
            if (!world.RailTopology.Tracks.TryGetValue(id, out var track))
            {
                continue;
            }

            var alignment = world.RailTopology.Alignments[
                track.AlignmentId];
            var nearest = NearestPointOnPolyline(
                point,
                alignment.ControlPoints);
            AddWithinSnapRadius(
                candidates,
                new RailSnapCandidate(
                    track.Id,
                    RailSnapKind.TrackSplit,
                    new DraftWaypoint(
                        nearest.Point,
                        null,
                        SplitTrackId: track.Id),
                    nearest.DistanceMeters / metersPerPixel));
        }

        AddAngleGuideCandidates(
            candidates,
            point,
            confirmedWaypoints,
            metersPerPixel);
        return candidates
            .OrderByDescending(candidate => candidate.Kind)
            .ThenBy(candidate => candidate.ScreenDistancePixels)
            .ThenBy(candidate => candidate.Id)
            .ToImmutableArray();
    }

    public double? FindEndpointHeading(
        WorldState world,
        DraftWaypoint endpoint,
        WorldPoint adjacent,
        bool isStart)
    {
        ArgumentNullException.ThrowIfNull(world);
        var trackIds = new HashSet<StableId>();
        if (endpoint.SplitTrackId.HasValue)
        {
            trackIds.Add(endpoint.SplitTrackId.Value);
        }

        var nodeId = endpoint.NodeId;
        if (!nodeId.HasValue && endpoint.StationId.HasValue)
        {
            GetRailIndex(world);
            nodeId = _nodeIdByStationId.TryGetValue(
                endpoint.StationId.Value,
                out var stationNodeId)
                ? stationNodeId
                : null;
        }

        if (nodeId.HasValue)
        {
            GetRailIndex(world);
            if (_trackIdsByNodeId.TryGetValue(
                    nodeId.Value,
                    out var incidentTrackIds))
            {
                trackIds.UnionWith(incidentTrackIds);
            }
        }

        if (trackIds.Count == 0)
        {
            return null;
        }

        var desiredX = isStart
            ? adjacent.XMillimeters - endpoint.Position.XMillimeters
            : endpoint.Position.XMillimeters - adjacent.XMillimeters;
        var desiredY = isStart
            ? adjacent.YMillimeters - endpoint.Position.YMillimeters
            : endpoint.Position.YMillimeters - adjacent.YMillimeters;
        var desiredLength = Math.Sqrt(
            ((double)desiredX * desiredX) +
            ((double)desiredY * desiredY));
        if (desiredLength <= 0)
        {
            return null;
        }

        var best = trackIds
            .Where(world.RailTopology.Tracks.ContainsKey)
            .Select(
                id =>
                {
                    var track = world.RailTopology.Tracks[id];
                    var alignment = world.RailTopology.Alignments[
                        track.AlignmentId];
                    var nearest = NearestPointOnPolyline(
                        endpoint.Position,
                        alignment.ControlPoints);
                    var x = nearest.TangentX;
                    var y = nearest.TangentY;
                    var tangentLength = Math.Sqrt((x * x) + (y * y));
                    var dot =
                        ((x * desiredX) + (y * desiredY)) /
                        (tangentLength * desiredLength);
                    if (dot < 0)
                    {
                        x = -x;
                        y = -y;
                        dot = -dot;
                    }

                    return new
                    {
                        Heading = Math.Atan2(y, x),
                        Score = dot,
                        Id = id
                    };
                })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        return best?.Heading;
    }

    private RailSpatialIndex GetRailIndex(WorldState world)
    {
        if (_railIndex is null ||
            !ReferenceEquals(_indexedBaseWorld, world.BaseWorld) ||
            _indexedRevision != world.Revision)
        {
            _railIndex = RailSpatialIndex.Build(
                world.RailTopology,
                world.BaseWorld.Parameters.ChunkSizeMeters);
            _nodeIdByStationId = world.RailTopology.Nodes.Values
                .Where(node => node.StationId.HasValue)
                .GroupBy(node => node.StationId!.Value)
                .ToImmutableDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(node => node.Id)
                        .First()
                        .Id);
            _trackIdsByNodeId = world.RailTopology.Tracks.Values
                .SelectMany(
                    track =>
                        new[]
                        {
                            (
                                NodeId: world.RailTopology.Ports[
                                    track.FromPortId].NodeId,
                                TrackId: track.Id),
                            (
                                NodeId: world.RailTopology.Ports[
                                    track.ToPortId].NodeId,
                                TrackId: track.Id)
                        })
                .GroupBy(item => item.NodeId)
                .ToImmutableDictionary(
                    group => group.Key,
                    group => group
                        .Select(item => item.TrackId)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToImmutableArray());
            _indexedBaseWorld = world.BaseWorld;
            _indexedRevision = world.Revision;
        }

        return _railIndex;
    }

    private static void AddWithinSnapRadius(
        ImmutableArray<RailSnapCandidate>.Builder candidates,
        RailSnapCandidate candidate)
    {
        if (candidate.ScreenDistancePixels <= SnapRadiusPixels)
        {
            candidates.Add(candidate);
        }
    }

    private static void AddAngleGuideCandidates(
        ImmutableArray<RailSnapCandidate>.Builder candidates,
        WorldPoint cursor,
        ImmutableArray<DraftWaypoint> confirmedWaypoints,
        double metersPerPixel)
    {
        if (confirmedWaypoints.Length < 2)
        {
            return;
        }

        var previous = confirmedWaypoints[^2].Position;
        var origin = confirmedWaypoints[^1].Position;
        var baseHeading = Math.Atan2(
            origin.YMillimeters - previous.YMillimeters,
            origin.XMillimeters - previous.XMillimeters);
        var distanceMillimeters =
            origin.DistanceMetersTo(cursor) * 1000d;
        foreach (var offsetDegrees in new[]
                 {
                     0d, -15d, 15d, -30d, 30d,
                     -45d, 45d, -90d, 90d
                 })
        {
            var heading =
                baseHeading + (offsetDegrees * Math.PI / 180d);
            var guided = new WorldPoint(
                checked((long)Math.Round(
                    origin.XMillimeters +
                    (Math.Cos(heading) * distanceMillimeters))),
                checked((long)Math.Round(
                    origin.YMillimeters +
                    (Math.Sin(heading) * distanceMillimeters))));
            var screenDistance =
                guided.DistanceMetersTo(cursor) /
                metersPerPixel;
            if (screenDistance <= AngleGuideRadiusPixels)
            {
                candidates.Add(new RailSnapCandidate(
                    null,
                    RailSnapKind.AngleGuide,
                    new DraftWaypoint(guided, null),
                    screenDistance,
                    offsetDegrees));
            }
        }
    }

    private static NearestPolylinePoint NearestPointOnPolyline(
        WorldPoint point,
        ImmutableArray<WorldPoint> points)
    {
        if (points.Length < 2)
        {
            throw new ArgumentException(
                "A polyline needs at least two points.",
                nameof(points));
        }

        var bestDistanceSquared = double.PositiveInfinity;
        var bestPoint = points[0];
        var bestTangentX = 0d;
        var bestTangentY = 0d;
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
            if (lengthSquared <= 0)
            {
                continue;
            }

            var t = Math.Clamp(
                (((point.XMillimeters - start.XMillimeters) * deltaX) +
                 ((point.YMillimeters - start.YMillimeters) * deltaY)) /
                lengthSquared,
                0d,
                1d);
            var x = start.XMillimeters + (deltaX * t);
            var y = start.YMillimeters + (deltaY * t);
            var distanceX = point.XMillimeters - x;
            var distanceY = point.YMillimeters - y;
            var distanceSquared =
                (distanceX * distanceX) +
                (distanceY * distanceY);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestPoint = new WorldPoint(
                    checked((long)Math.Round(x)),
                    checked((long)Math.Round(y)));
                bestTangentX = deltaX;
                bestTangentY = deltaY;
            }
        }

        return new NearestPolylinePoint(
            bestPoint,
            Math.Sqrt(bestDistanceSquared) / 1000d,
            bestTangentX,
            bestTangentY);
    }

    private sealed record NearestPolylinePoint(
        WorldPoint Point,
        double DistanceMeters,
        double TangentX,
        double TangentY);
}
