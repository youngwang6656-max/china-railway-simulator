using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class RailDrawingSnapCandidateProviderTests
{
    [Fact]
    public void NearbyTrackAndStationCandidatesComeFromRegionIndexes()
    {
        var world = WorldWithStraightTrack();
        var track = world.RailTopology.Tracks.Values.First();
        var alignment = world.RailTopology.Alignments[track.AlignmentId];
        var midpoint = alignment.ControlPoints[
            alignment.ControlPoints.Length / 2];
        var stationNode = world.RailTopology.Nodes.Values
            .First(node => node.StationId.HasValue);
        var provider = new RailDrawingSnapCandidateProvider();

        var trackCandidates = provider.BuildCandidates(
            world,
            midpoint.OffsetMillimeters(0, 2_000),
            ImmutableArray<DraftWaypoint>.Empty,
            metersPerPixel: 1);
        var stationCandidates = provider.BuildCandidates(
            world,
            stationNode.Position,
            ImmutableArray<DraftWaypoint>.Empty,
            metersPerPixel: 1);

        Assert.Contains(
            trackCandidates,
            candidate => candidate.Kind == RailSnapKind.TrackSplit);
        Assert.Contains(
            stationCandidates,
            candidate => candidate.Kind == RailSnapKind.RailNode);
        Assert.Contains(
            stationCandidates,
            candidate => candidate.Kind == RailSnapKind.Station);
    }

    [Fact]
    public void AngleGuideAppearsOnlyWithinEightPixels()
    {
        var world = EmptyWorld();
        var confirmed = ImmutableArray.Create(
            Waypoint(0, 0),
            Waypoint(1_000, 0));
        var provider = new RailDrawingSnapCandidateProvider();

        var near = provider.BuildCandidates(
            world,
            Point(2_000, 10),
            confirmed,
            metersPerPixel: 2);
        var far = provider.BuildCandidates(
            world,
            Point(2_000, 50),
            confirmed,
            metersPerPixel: 2);

        Assert.Contains(
            near,
            candidate =>
                candidate.Kind == RailSnapKind.AngleGuide &&
                candidate.GuideAngleDegrees == 0);
        Assert.DoesNotContain(
            far,
            candidate => candidate.Kind == RailSnapKind.AngleGuide);
    }

    [Fact]
    public void EndpointHeadingFollowsIncidentTrackTowardDraft()
    {
        var world = WorldWithStraightTrack();
        var node = world.RailTopology.Nodes.Values
            .OrderBy(item => item.Position.XMillimeters)
            .First();
        var provider = new RailDrawingSnapCandidateProvider();

        var heading = provider.FindEndpointHeading(
            world,
            new DraftWaypoint(
                node.Position,
                node.StationId,
                node.Id),
            node.Position.OffsetMillimeters(10_000_000, 0),
            isStart: true);

        Assert.NotNull(heading);
        Assert.InRange(Math.Abs(heading!.Value), 0, 0.001);
    }

    private static WorldState EmptyWorld()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260730));
        return new WorldState(0, generated, RailTopology.Empty);
    }

    private static WorldState WorldWithStraightTrack()
    {
        var world = EmptyWorld();
        var station = world.BaseWorld.Stations.Values
            .OrderBy(item => item.Id)
            .First();
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                new DraftWaypoint(
                    station.Position,
                    station.Id),
                new DraftWaypoint(
                    station.Position.OffsetMillimeters(
                        100_000_000,
                        0),
                    null)));
        var patch = new DoubleTrackCorridorStrategy(
                new DeterministicStableIdSource("candidate-provider"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        return new WorldState(
            1,
            world.BaseWorld,
            patch.ApplyTo(RailTopology.Empty));
    }

    private static DraftWaypoint Waypoint(
        long xMeters,
        long yMeters)
    {
        return new DraftWaypoint(Point(xMeters, yMeters), null);
    }

    private static WorldPoint Point(long xMeters, long yMeters)
    {
        return new WorldPoint(xMeters * 1000, yMeters * 1000);
    }
}
