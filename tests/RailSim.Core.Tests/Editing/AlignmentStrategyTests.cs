using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class AlignmentStrategyTests
{
    [Fact]
    public void ControlPointStrategyPreservesEndpointsAndAddsSmoothSamples()
    {
        var waypoints = ImmutableArray.Create(
            Waypoint(0, 0, "station:a"),
            Waypoint(100, 80, null),
            Waypoint(220, 20, "station:b"));

        var draft = new ControlPointSplineStrategy().Build(waypoints);

        Assert.Equal(waypoints, draft.Waypoints);
        Assert.True(draft.SampledPoints.Length > waypoints.Length);
        Assert.Equal(waypoints[0].Position, draft.SampledPoints[0]);
        Assert.Equal(waypoints[^1].Position, draft.SampledPoints[^1]);
    }

    [Fact]
    public void DoubleTrackStrokeCreatesTwoIndependentOpposingTracksPerSegment()
    {
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("test"))
            .CreatePatch(
                RailTopology.Empty,
                MainLineDraft(),
                TrackPlacementOptions.HighSpeedDoubleTrack);

        Assert.Equal(6, patch.AddedTracks.Length);
        Assert.Equal(
            3,
            patch.AddedTracks.Count(
                track => track.LateralOffsetMillimeters < 0));
        Assert.Equal(
            3,
            patch.AddedTracks.Count(
                track => track.LateralOffsetMillimeters > 0));
        Assert.Equal(
            6,
            patch.AddedTracks.Select(track => track.Id).Distinct().Count());
        Assert.Equal(4, patch.AddedNodes.Length);
        Assert.Equal(4, patch.AddedConnections.Length);
        Assert.Equal(RailEditIntent.DoubleTrackCorridor, patch.Intent);
        Assert.NotEmpty(patch.AffectedChunks);
    }

    [Fact]
    public void ExplicitConnectorAddsOnlyRequestedMovements()
    {
        var mainPatch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("main"))
            .CreatePatch(
                RailTopology.Empty,
                MainLineDraft(),
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var topology = mainPatch.ApplyTo(RailTopology.Empty);
        var requested = topology.Connections.Values
            .OrderBy(connection => connection.Id)
            .Take(2)
            .Select(
                connection => new ConnectorMovement(
                    connection.NodeId,
                    connection.FromTrackId,
                    connection.ToTrackId))
            .ToImmutableArray();

        var patch = new ExplicitConnectorStrategy(
            new DeterministicStableIdSource("connector"))
            .CreateConnectionPatch(topology, requested);

        Assert.Equal(2, patch.AddedConnections.Length);
        Assert.Equal(
            requested.Select(
                movement => (
                    movement.NodeId,
                    movement.FromTrackId,
                    movement.ToTrackId)),
            patch.AddedConnections.Select(
                connection => (
                    connection.NodeId,
                    connection.FromTrackId,
                connection.ToTrackId)));
    }

    [Fact]
    public void PlacementCanReuseExplicitExistingNodeEndpoints()
    {
        var existing = new RailNode(
            new StableId("node:existing"),
            RailNodeKind.Junction,
            new WorldPoint(10_000_000, 20_000_000),
            null);
        var topology = RailTopology.Empty.AddNode(existing);
        var draft = new ControlPointSplineStrategy().Build(
        [
            new DraftWaypoint(
                existing.Position,
                null,
                existing.Id),
            new DraftWaypoint(
                new WorldPoint(110_000_000, 20_000_000),
                null,
                null)
        ]);

        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("reuse-node"))
            .CreatePatch(
                topology,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);

        Assert.Single(patch.AddedNodes);
        Assert.DoesNotContain(
            patch.AddedNodes,
            node => node.Id == existing.Id);
        Assert.Contains(
            patch.AddedPorts,
            port => port.NodeId == existing.Id);
    }

    [Fact]
    public void ValidatorRejectsConnectionWhoseTracksDoNotMeet()
    {
        var mainPatch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("validation"))
            .CreatePatch(
                RailTopology.Empty,
                MainLineDraft(),
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var topology = mainPatch.ApplyTo(RailTopology.Empty);
        var tracks = topology.Tracks.Values
            .OrderBy(track => track.Id)
            .ToArray();
        var invalid = RailGraphPatch.Empty with
        {
            AddedConnections =
            [
                new TrackConnection(
                    new StableId("connection:invalid"),
                    topology.Nodes.Keys.OrderBy(id => id).First(),
                    tracks[0].Id,
                    tracks[^1].Id)
            ]
        };

        var result = new RailEditValidator().Validate(topology, invalid);

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "rail.connection.node_mismatch");
    }

    [Fact]
    public void ValidatorRejectsStructurallyValidPartialDoubleTrack()
    {
        var draft = new ControlPointSplineStrategy().Build(
        [
            Waypoint(0, 0, "station:a"),
            Waypoint(100, 0, "station:b")
        ]);
        var complete = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("partial"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var remainingTrack = complete.AddedTracks[0];
        var partial = complete with
        {
            AddedTracks = [remainingTrack],
            AddedTrackGroups =
            [
                new TrackGroup(
                    complete.AddedTrackGroups[0].Id,
                    [remainingTrack.Id])
            ]
        };

        var result = new RailEditValidator().Validate(
            RailTopology.Empty,
            partial);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "rail.double_track.incomplete" &&
                issue.Severity == RailValidationSeverity.Error);
    }

    [Fact]
    public void WorldAwareValidatorReportsRiverCrossingWarning()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(55));
        var river = generated.Rivers[0];
        var draft = new ControlPointSplineStrategy().Build(
        [
            new DraftWaypoint(
                river.Points[0],
                new StableId("station:source")),
            new DraftWaypoint(
                river.Points[^1],
                new StableId("station:mouth"))
        ]);
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("river-warning"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var world = new WorldState(0, generated, RailTopology.Empty);

        var result = new RailEditValidator().Validate(world, patch);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "rail.engineering.river_crossing" &&
                issue.Severity == RailValidationSeverity.Warning);
    }

    private static AlignmentDraft MainLineDraft()
    {
        return new ControlPointSplineStrategy().Build(
        [
            Waypoint(0, 0, "station:a"),
            Waypoint(120, 70, "station:c"),
            Waypoint(250, 40, "station:d"),
            Waypoint(400, 120, "station:b")
        ]);
    }

    private static DraftWaypoint Waypoint(
        long xKilometers,
        long yKilometers,
        string? stationId)
    {
        return new DraftWaypoint(
            new WorldPoint(
                xKilometers * 1_000_000,
                yKilometers * 1_000_000),
            stationId is null ? null : new StableId(stationId));
    }
}
