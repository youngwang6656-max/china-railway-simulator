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
}
