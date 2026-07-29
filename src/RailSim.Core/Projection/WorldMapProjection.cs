using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Projection;

public sealed class WorldMapProjection : IWorldProjection
{
    private readonly IMapLodPolicy _lodPolicy;
    private readonly IWorldRegionProvider _regionProvider;

    public WorldMapProjection(IMapLodPolicy lodPolicy)
        : this(lodPolicy, new GridWorldRegionProvider())
    {
    }

    public WorldMapProjection(
        IMapLodPolicy lodPolicy,
        IWorldRegionProvider regionProvider)
    {
        _lodPolicy = lodPolicy ??
            throw new ArgumentNullException(nameof(lodPolicy));
        _regionProvider = regionProvider ??
            throw new ArgumentNullException(nameof(regionProvider));
    }

    public WorldMapFrame Project(
        WorldState world,
        MapViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(viewport);
        var lod = _lodPolicy.Select(viewport.MetersPerPixel);
        var region = world.BaseWorld.RegionIndex.Query(
            viewport.Bounds,
            _regionProvider);
        var rivers = region.RiverIds
            .OrderBy(id => id)
            .Select(id => world.BaseWorld.RiversById[id])
            .Where(river => Intersects(viewport.Bounds, river.Points))
            .Select(
                river => new ProjectedRiver(
                    river.Id,
                    river.WidthClass,
                    river.Points))
            .ToImmutableArray();
        var cities = region.CityIds
            .OrderBy(id => id)
            .Select(id => world.BaseWorld.Cities[id])
            .Where(city => viewport.Bounds.Contains(city.Position))
            .Select(
                city => new ProjectedCity(
                    city.Id,
                    city.Name,
                    city.Tier,
                    city.Position))
            .ToImmutableArray();
        var stations = region.StationIds
            .OrderBy(id => id)
            .Select(id => world.BaseWorld.Stations[id])
            .Where(station => viewport.Bounds.Contains(station.Position))
            .Select(
                station => new ProjectedStation(
                    station.Id,
                    station.Name,
                    station.Tier,
                    station.Position))
            .ToImmutableArray();
        var tracks = lod == MapLod.Regional
            ? ProjectGroups(world.RailTopology, viewport.Bounds)
            : ProjectTracks(world.RailTopology, viewport.Bounds);
        var nodes = lod == MapLod.Edit
            ? ProjectNodes(world.RailTopology, viewport.Bounds)
            : [];
        var ports = lod == MapLod.Edit
            ? ProjectPorts(world.RailTopology, viewport.Bounds)
            : [];
        var connections = lod == MapLod.Edit
            ? ProjectConnections(world.RailTopology, viewport.Bounds)
            : [];
        return new WorldMapFrame(
            world.Revision,
            world.BaseWorld.Terrain.ContentHash,
            lod,
            rivers,
            cities,
            stations,
            nodes,
            ports,
            tracks,
            connections);
    }

    private static ImmutableArray<ProjectedNode> ProjectNodes(
        RailTopology topology,
        WorldBounds bounds)
    {
        return topology.Nodes.Values
            .Where(node => bounds.Contains(node.Position))
            .OrderBy(node => node.Id)
            .Select(
                node => new ProjectedNode(
                    node.Id,
                    node.Kind,
                    node.Position))
            .ToImmutableArray();
    }

    private static ImmutableArray<ProjectedPort> ProjectPorts(
        RailTopology topology,
        WorldBounds bounds)
    {
        return topology.Ports.Values
            .Where(
                port =>
                    topology.Nodes.TryGetValue(port.NodeId, out var node) &&
                    bounds.Contains(node.Position))
            .OrderBy(port => port.Id)
            .Select(
                port => new ProjectedPort(
                    port.Id,
                    port.NodeId,
                    port.Direction,
                    topology.Nodes[port.NodeId].Position))
            .ToImmutableArray();
    }

    private static ImmutableArray<ProjectedTrack> ProjectGroups(
        RailTopology topology,
        WorldBounds bounds)
    {
        var values = ImmutableArray.CreateBuilder<ProjectedTrack>();
        var groupedTracks = new HashSet<StableId>();
        foreach (var group in topology.TrackGroups.Values.OrderBy(item => item.Id))
        {
            foreach (var trackId in group.TrackIds)
            {
                groupedTracks.Add(trackId);
            }

            var tracks = group.TrackIds
                .Where(topology.Tracks.ContainsKey)
                .Select(trackId => topology.Tracks[trackId])
                .ToArray();
            var alignments = tracks
                .Select(track => topology.Alignments[track.AlignmentId])
                .DistinctBy(alignment => alignment.Id)
                .OrderBy(alignment => alignment.Id)
                .ToArray();
            var points = ConcatenateAlignments(alignments);
            if (points.Length == 0 || !Intersects(bounds, points))
            {
                continue;
            }

            values.Add(new ProjectedTrack(
                group.Id,
                points,
                null,
                null,
                0,
                tracks.Max(track => track.DesignSpeedKph),
                true));
        }

        foreach (var track in topology.Tracks.Values
                     .Where(track => !groupedTracks.Contains(track.Id))
                     .OrderBy(track => track.Id))
        {
            var alignment = topology.Alignments[track.AlignmentId];
            if (!Intersects(bounds, alignment.ControlPoints))
            {
                continue;
            }

            values.Add(ToProjectedTrack(topology, track, alignment));
        }

        return values.ToImmutable();
    }

    private static ImmutableArray<ProjectedTrack> ProjectTracks(
        RailTopology topology,
        WorldBounds bounds)
    {
        return topology.Tracks.Values
            .OrderBy(track => track.Id)
            .Where(
                track => Intersects(
                    bounds,
                    topology.Alignments[track.AlignmentId].ControlPoints))
            .Select(
                track => ToProjectedTrack(
                    topology,
                    track,
                    topology.Alignments[track.AlignmentId]))
            .ToImmutableArray();
    }

    private static ImmutableArray<ProjectedConnection> ProjectConnections(
        RailTopology topology,
        WorldBounds bounds)
    {
        return topology.Connections.Values
            .OrderBy(connection => connection.Id)
            .Where(
                connection =>
                    topology.Nodes.TryGetValue(
                        connection.NodeId,
                        out var node) &&
                    bounds.Contains(node.Position))
            .Select(
                connection => new ProjectedConnection(
                    connection.Id,
                    connection.NodeId,
                    connection.FromTrackId,
                    connection.ToTrackId,
                    topology.Nodes[connection.NodeId].Position))
            .ToImmutableArray();
    }

    private static ProjectedTrack ToProjectedTrack(
        RailTopology topology,
        TrackEdge track,
        Alignment alignment)
    {
        var fromNode = topology.Nodes[
            topology.Ports[track.FromPortId].NodeId];
        var toNode = topology.Nodes[
            topology.Ports[track.ToPortId].NodeId];
        var points = OffsetPoints(
            alignment.ControlPoints,
            track.LateralOffsetMillimeters);
        var forward =
            points[0].DistanceMetersTo(fromNode.Position) <=
            points[^1].DistanceMetersTo(fromNode.Position);
        if (!forward)
        {
            points = points.Reverse().ToImmutableArray();
        }

        return new ProjectedTrack(
            track.Id,
            points,
            fromNode.Id,
            toNode.Id,
            track.LateralOffsetMillimeters,
            track.DesignSpeedKph,
            false);
    }

    private static ImmutableArray<WorldPoint> OffsetPoints(
        ImmutableArray<WorldPoint> points,
        int offsetMillimeters)
    {
        if (offsetMillimeters == 0)
        {
            return points;
        }

        var result = ImmutableArray.CreateBuilder<WorldPoint>(points.Length);
        for (var index = 0; index < points.Length; index++)
        {
            var previous = points[Math.Max(0, index - 1)];
            var next = points[Math.Min(points.Length - 1, index + 1)];
            var deltaX =
                (double)next.XMillimeters - previous.XMillimeters;
            var deltaY =
                (double)next.YMillimeters - previous.YMillimeters;
            var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (length <= 0)
            {
                result.Add(points[index]);
                continue;
            }

            var offsetX = (-deltaY / length) * offsetMillimeters;
            var offsetY = (deltaX / length) * offsetMillimeters;
            result.Add(new WorldPoint(
                checked((long)Math.Round(
                    points[index].XMillimeters + offsetX)),
                checked((long)Math.Round(
                    points[index].YMillimeters + offsetY))));
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<WorldPoint> ConcatenateAlignments(
        IReadOnlyList<Alignment> alignments)
    {
        var points = ImmutableArray.CreateBuilder<WorldPoint>();
        foreach (var alignment in alignments)
        {
            foreach (var point in alignment.ControlPoints)
            {
                if (points.Count == 0 || points[^1] != point)
                {
                    points.Add(point);
                }
            }
        }

        return points.ToImmutable();
    }

    private static bool Intersects(
        WorldBounds bounds,
        ImmutableArray<WorldPoint> points)
    {
        if (points.IsDefaultOrEmpty)
        {
            return false;
        }

        var minimumX = points.Min(point => point.XMillimeters);
        var maximumX = points.Max(point => point.XMillimeters);
        var minimumY = points.Min(point => point.YMillimeters);
        var maximumY = points.Max(point => point.YMillimeters);
        return maximumX >= bounds.Minimum.XMillimeters &&
            minimumX <= bounds.Maximum.XMillimeters &&
            maximumY >= bounds.Minimum.YMillimeters &&
            minimumY <= bounds.Maximum.YMillimeters;
    }
}
