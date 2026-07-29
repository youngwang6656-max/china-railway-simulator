using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class RailDrawingCommitComposerTests
{
    [Fact]
    public void TrackSplitSnapAndNewCorridorAreOneReversiblePatch()
    {
        var world = WorldWithExistingDoubleTrack();
        var target = world.RailTopology.Tracks.Values
            .OrderBy(track => track.Id)
            .First();
        var targetAlignment =
            world.RailTopology.Alignments[target.AlignmentId];
        var splitPoint = targetAlignment.ControlPoints[
            targetAlignment.ControlPoints.Length / 2];
        var origin = splitPoint.OffsetMillimeters(
            -20_000_000,
            15_000_000);
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                new DraftWaypoint(origin, null),
                new DraftWaypoint(
                    splitPoint,
                    null,
                    SplitTrackId: target.Id)));

        var patch = new RailDrawingCommitComposer()
            .CreateDoubleTrackPatch(
                world,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack,
                new DeterministicStableIdSource("attached"));
        var changed = patch.ApplyTo(world.RailTopology);
        var restored = patch.Invert().ApplyTo(changed);
        var splitNode = patch.AddedNodes.Single(
            node => node.Position == splitPoint);

        Assert.DoesNotContain(target.Id, changed.Tracks.Keys);
        Assert.True(
            changed.Ports.Values.Count(
                port => port.NodeId == splitNode.Id) >= 6);
        Assert.Contains(
            changed.Connections.Values,
            connection => connection.NodeId == splitNode.Id);
        Assert.Equal(
            Hash(world, world.RailTopology),
            Hash(world, restored));
    }

    [Fact]
    public void RepeatedSplitTargetIsRejectedBeforeCreatingIds()
    {
        var world = WorldWithExistingDoubleTrack();
        var target = world.RailTopology.Tracks.Values
            .OrderBy(track => track.Id)
            .First();
        var alignment =
            world.RailTopology.Alignments[target.AlignmentId];
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                new DraftWaypoint(
                    alignment.ControlPoints[2],
                    null,
                    SplitTrackId: target.Id),
                new DraftWaypoint(
                    alignment.ControlPoints[^3],
                    null,
                    SplitTrackId: target.Id)));

        var exception = Assert.Throws<RailTopologyException>(
            () => new RailDrawingCommitComposer()
                .CreateDoubleTrackPatch(
                    world,
                    draft,
                    TrackPlacementOptions.HighSpeedDoubleTrack,
                    new DeterministicStableIdSource("repeated")));

        Assert.Equal(
            "rail.drawing.repeated_split_target",
            exception.Code);
    }

    [Fact]
    public void MissingPreviewSnapTargetIsRejected()
    {
        var world = WorldWithExistingDoubleTrack();
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                new DraftWaypoint(new WorldPoint(0, 0), null),
                new DraftWaypoint(
                    new WorldPoint(10_000_000, 0),
                    null,
                    SplitTrackId: new StableId("missing-track"))));

        var exception = Assert.Throws<RailTopologyException>(
            () => new RailDrawingCommitComposer()
                .CreateDoubleTrackPatch(
                    world,
                    draft,
                    TrackPlacementOptions.HighSpeedDoubleTrack,
                    new DeterministicStableIdSource("missing")));

        Assert.Equal(
            "rail.drawing.snap_target_missing",
            exception.Code);
    }

    private static WorldState WorldWithExistingDoubleTrack()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260730));
        var stations = generated.Stations.Values
            .OrderBy(station => station.Position.XMillimeters)
            .ThenBy(station => station.Id)
            .Take(2)
            .ToArray();
        var draft = new ControlPointSplineStrategy().Build(
            stations
                .Select(
                    station => new DraftWaypoint(
                        station.Position,
                        station.Id))
                .ToImmutableArray());
        var patch = new DoubleTrackCorridorStrategy(
                new DeterministicStableIdSource("existing"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        return new WorldState(
            0,
            generated,
            patch.ApplyTo(RailTopology.Empty));
    }

    private static string Hash(
        WorldState world,
        RailTopology topology)
    {
        return WorldContentHasher.HashState(
            new WorldState(0, world.BaseWorld, topology));
    }
}
