using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Projection;

public sealed class WorldMapProjection : IWorldProjection
{
    private readonly IMapLodPolicy _lodPolicy;

    public WorldMapProjection(IMapLodPolicy lodPolicy)
    {
        _lodPolicy = lodPolicy ??
            throw new ArgumentNullException(nameof(lodPolicy));
    }

    public WorldMapFrame Project(
        WorldState world,
        MapViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(viewport);
        var lod = _lodPolicy.Select(viewport.MetersPerPixel);
        var rivers = world.BaseWorld.Rivers
            .Where(river => Intersects(viewport.Bounds, river.Points))
            .Select(
                river => new ProjectedRiver(
                    river.Id,
                    river.WidthClass,
                    river.Points))
            .ToImmutableArray();
        var cities = world.BaseWorld.Cities.Values
            .Where(city => viewport.Bounds.Contains(city.Position))
            .OrderBy(city => city.Id)
            .Select(
                city => new ProjectedCity(
                    city.Id,
                    city.Name,
                    city.Tier,
                    city.Position))
            .ToImmutableArray();
        var stations = world.BaseWorld.Stations.Values
            .Where(station => viewport.Bounds.Contains(station.Position))
            .OrderBy(station => station.Id)
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
            tracks,
            connections);
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

            values.Add(ToProjectedTrack(track, alignment));
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
        TrackEdge track,
        Alignment alignment)
    {
        return new ProjectedTrack(
            track.Id,
            alignment.ControlPoints,
            track.LateralOffsetMillimeters,
            track.DesignSpeedKph,
            false);
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
