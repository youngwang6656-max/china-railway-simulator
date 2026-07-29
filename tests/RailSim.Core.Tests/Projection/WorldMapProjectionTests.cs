using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Projection;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Projection;

public sealed class WorldMapProjectionTests
{
    [Fact]
    public void RegionalLodMergesTrackGroupWithoutChangingWorld()
    {
        var world = CreateWorldWithMainLine();
        var before = WorldContentHasher.HashState(world);
        var frame = new WorldMapProjection(new DefaultMapLodPolicy())
            .Project(
                world,
                MapViewport.Regional(world.BaseWorld.Parameters.Bounds));

        Assert.Equal(MapLod.Regional, frame.Lod);
        Assert.Single(frame.Tracks);
        Assert.True(frame.Tracks[0].IsAggregate);
        Assert.True(
            frame.Tracks.Length < world.RailTopology.Tracks.Count);
        Assert.Equal(before, WorldContentHasher.HashState(world));
    }

    [Fact]
    public void LineLodReturnsIndependentPhysicalTracks()
    {
        var world = CreateWorldWithMainLine();
        var viewport = new MapViewport(
            world.BaseWorld.Parameters.Bounds,
            metersPerPixel: 100);

        var frame = new WorldMapProjection(new DefaultMapLodPolicy())
            .Project(world, viewport);

        Assert.Equal(MapLod.Line, frame.Lod);
        Assert.Equal(6, frame.Tracks.Length);
        Assert.All(frame.Tracks, track => Assert.False(track.IsAggregate));
        Assert.Equal(
            6,
            frame.Tracks.Select(track => track.SourceId).Distinct().Count());
    }

    [Fact]
    public void PhysicalTrackGeometryIsOffsetAndOrderedInTravelDirection()
    {
        var world = CreateWorldWithMainLine();
        var frame = new WorldMapProjection(new DefaultMapLodPolicy())
            .Project(
                world,
                new MapViewport(
                    world.BaseWorld.Parameters.Bounds,
                    metersPerPixel: 100));

        foreach (var projected in frame.Tracks)
        {
            var track = world.RailTopology.Tracks[projected.SourceId];
            var fromNode = world.RailTopology.Nodes[
                world.RailTopology.Ports[track.FromPortId].NodeId];
            var toNode = world.RailTopology.Nodes[
                world.RailTopology.Ports[track.ToPortId].NodeId];

            Assert.Equal(fromNode.Id, projected.FromNodeId);
            Assert.Equal(toNode.Id, projected.ToNodeId);
            Assert.InRange(
                projected.Points[0].DistanceMetersTo(fromNode.Position),
                2.4,
                2.6);
            Assert.InRange(
                projected.Points[^1].DistanceMetersTo(toNode.Position),
                2.4,
                2.6);
        }

        var firstAlignment = world.RailTopology.Alignments.Values
            .OrderBy(alignment => alignment.Id)
            .First();
        var pair = frame.Tracks
            .Where(
                projected =>
                    world.RailTopology.Tracks[projected.SourceId].AlignmentId ==
                    firstAlignment.Id)
            .ToArray();
        Assert.Equal(2, pair.Length);
        Assert.InRange(
            pair[0].Points[0].DistanceMetersTo(pair[1].Points[^1]),
            4.9,
            5.1);
    }

    [Fact]
    public void EditLodIncludesOnlyExplicitConnections()
    {
        var world = CreateWorldWithMainLine();
        var viewport = new MapViewport(
            world.BaseWorld.Parameters.Bounds,
            metersPerPixel: 10);

        var frame = new WorldMapProjection(new DefaultMapLodPolicy())
            .Project(world, viewport);

        Assert.Equal(MapLod.Edit, frame.Lod);
        Assert.Equal(4, frame.Connections.Length);
        Assert.Equal(
            world.RailTopology.Nodes.Count,
            frame.Nodes.Length);
        Assert.Equal(
            world.RailTopology.Ports.Count,
            frame.Ports.Length);
        Assert.Equal(
            world.RailTopology.Connections.Keys.OrderBy(id => id),
            frame.Connections.Select(item => item.SourceId).OrderBy(id => id));
    }

    [Fact]
    public void ViewportCullsCitiesOutsideItsBounds()
    {
        var world = CreateWorldWithMainLine();
        var city = world.BaseWorld.Cities.Values.OrderBy(item => item.Id).First();
        var halfSize = 500_000L;
        var viewportBounds = new WorldBounds(
            city.Position.OffsetMillimeters(-halfSize, -halfSize),
            city.Position.OffsetMillimeters(halfSize, halfSize));

        var frame = new WorldMapProjection(new DefaultMapLodPolicy())
            .Project(
                world,
                new MapViewport(viewportBounds, metersPerPixel: 10));

        Assert.Contains(frame.Cities, item => item.SourceId == city.Id);
        Assert.All(
            frame.Cities,
            item => Assert.True(viewportBounds.Contains(item.Position)));
    }

    [Fact]
    public void ProjectionQueriesOneChunkBeyondViewport()
    {
        var world = CreateWorldWithMainLine();
        var city = world.BaseWorld.Cities.Values.OrderBy(item => item.Id).First();
        var viewportBounds = new WorldBounds(
            city.Position.OffsetMillimeters(-500_000, -500_000),
            city.Position.OffsetMillimeters(500_000, 500_000));
        var provider = new CapturingRegionProvider();

        _ = new WorldMapProjection(
                new DefaultMapLodPolicy(),
                provider)
            .Project(
                world,
                new MapViewport(viewportBounds, metersPerPixel: 10));

        var margin =
            world.BaseWorld.Parameters.ChunkSizeMeters * 1000L;
        Assert.Equal(
            viewportBounds.Minimum.OffsetMillimeters(-margin, -margin),
            provider.LastBounds.Minimum);
        Assert.Equal(
            viewportBounds.Maximum.OffsetMillimeters(margin, margin),
            provider.LastBounds.Maximum);
    }

    [Fact]
    public void RailSpatialIndexReturnsOnlyNearbyTopologyCandidates()
    {
        var near = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("near"))
            .CreatePatch(
                RailTopology.Empty,
                new ControlPointSplineStrategy().Build(
                [
                    new DraftWaypoint(
                        new WorldPoint(10_000_000, 10_000_000),
                        null),
                    new DraftWaypoint(
                        new WorldPoint(100_000_000, 10_000_000),
                        null)
                ]),
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var topology = near.ApplyTo(RailTopology.Empty);
        var far = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("far"))
            .CreatePatch(
                topology,
                new ControlPointSplineStrategy().Build(
                [
                    new DraftWaypoint(
                        new WorldPoint(800_000_000, 700_000_000),
                        null),
                    new DraftWaypoint(
                        new WorldPoint(900_000_000, 700_000_000),
                        null)
                ]),
                TrackPlacementOptions.HighSpeedDoubleTrack);
        topology = far.ApplyTo(topology);
        var index = RailSpatialIndex.Build(topology, 64_000);

        var region = index.Query(
            new WorldBounds(
                WorldPoint.Zero,
                new WorldPoint(150_000_000, 100_000_000)),
            new GridWorldRegionProvider());

        Assert.Equal(2, region.TrackIds.Count);
        Assert.Single(region.TrackGroupIds);
        Assert.All(
            region.TrackIds,
            id => Assert.StartsWith("near:", id.Value));
    }

    private static WorldState CreateWorldWithMainLine()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260729));
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                Waypoint(0, 0, "station:a"),
                Waypoint(120, 70, "station:c"),
                Waypoint(250, 40, "station:d"),
                Waypoint(400, 120, "station:b")));
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("projection"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        return new WorldState(1, generated, patch.ApplyTo(RailTopology.Empty));
    }

    private static DraftWaypoint Waypoint(
        long xKilometers,
        long yKilometers,
        string stationId)
    {
        return new DraftWaypoint(
            new WorldPoint(
                xKilometers * 1_000_000,
                yKilometers * 1_000_000),
            new StableId(stationId));
    }

    private sealed class CapturingRegionProvider : IWorldRegionProvider
    {
        public WorldBounds LastBounds { get; private set; }

        public ImmutableArray<WorldChunkId> EnumerateChunks(
            WorldBounds bounds,
            int chunkSizeMeters)
        {
            LastBounds = bounds;
            return new GridWorldRegionProvider().EnumerateChunks(
                bounds,
                chunkSizeMeters);
        }
    }
}
