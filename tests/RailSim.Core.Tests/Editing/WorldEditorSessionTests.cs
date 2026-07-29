using System.Collections.Immutable;
using RailSim.Core.Commands;
using RailSim.Core.Editing;
using RailSim.Core.Query;
using RailSim.Core.Rail;
using RailSim.Core.Time;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class WorldEditorSessionTests
{
    [Fact]
    public void ApplyUndoRedoRoundTripsWorldContentHash()
    {
        var session = CreateSession();
        var before = session.CurrentHash;
        var accepted = session.Apply(MainLinePatch(), session.Revision);
        var after = session.CurrentHash;

        Assert.Equal(CommandStatus.Accepted, accepted.Status);
        Assert.NotEqual(before, after);
        Assert.Equal(
            CommandStatus.Accepted,
            session.Undo(session.Revision).Status);
        Assert.Equal(before, session.CurrentHash);
        Assert.Equal(
            CommandStatus.Accepted,
            session.Redo(session.Revision).Status);
        Assert.Equal(after, session.CurrentHash);
    }

    [Fact]
    public void RejectedPatchChangesNeitherWorldNorHistory()
    {
        var session = CreateSession();
        var before = session.CurrentHash;
        var invalid = RailGraphPatch.Empty with
        {
            AddedTracks =
            [
                new TrackEdge(
                    new StableId("track:invalid"),
                    new StableId("port:missing-a"),
                    new StableId("port:missing-b"),
                    new StableId("alignment:missing"),
                    0,
                    160)
            ]
        };

        var result = session.Apply(invalid, session.Revision);

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal(before, session.CurrentHash);
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void StaleRevisionIsRejected()
    {
        var session = CreateSession();
        Assert.Equal(
            CommandStatus.Accepted,
            session.Apply(MainLinePatch(), session.Revision).Status);

        var result = session.Apply(MainLinePatch("second"), baseRevision: 0);

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal("world.edit.stale_revision", result.Code);
    }

    [Fact]
    public void NewEditAfterUndoClearsRedo()
    {
        var session = CreateSession();
        session.Apply(MainLinePatch(), session.Revision);
        session.Undo(session.Revision);

        session.Apply(MainLinePatch("replacement"), session.Revision);

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void CommandBusAcceptsImmutablePatchAndPublishesWorldSection()
    {
        var session = CreateSession();
        var bus = new SimulationCommandBus();
        WorldEditCommandRegistration.Register(bus, session);
        bus.Enqueue(
            new ApplyRailEditCommand(MainLinePatch(), session.Revision),
            new SimulationTick(1));

        var results = bus.DispatchDue(new SimulationTick(1));
        var section = Assert.IsType<WorldSnapshotSection>(session.Capture());

        Assert.Single(results);
        Assert.Equal(CommandStatus.Accepted, results[0].Status);
        Assert.Equal("world", section.Key);
        Assert.Equal(6, section.World.RailTopology.Tracks.Count);
    }

    [Fact]
    public void CommandHandlersCanFollowAReplacedEditorSession()
    {
        var first = CreateSession();
        var active = first;
        var bus = new SimulationCommandBus();
        WorldEditCommandRegistration.Register(bus, () => active);
        active = CreateSession();
        bus.Enqueue(
            new ApplyRailEditCommand(
                MainLinePatch("replacement-session"),
                active.Revision),
            new SimulationTick(1));

        var result = Assert.Single(bus.DispatchDue(new SimulationTick(1)));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Empty(first.World.RailTopology.Tracks);
        Assert.Equal(6, active.World.RailTopology.Tracks.Count);
    }

    [Fact]
    public void EngineeringWarningsRequireExplicitAcknowledgement()
    {
        var session = CreateSession();
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                Waypoint(0, 0, "station:a"),
                Waypoint(100, 0, "station:b")));
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("warning"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                new TrackPlacementOptions(3_000, 160));

        var rejected = session.Apply(patch, session.Revision);
        var acknowledged = patch with
        {
            AcknowledgedWarningCodes =
                ImmutableHashSet.Create("rail.engineering.track_spacing")
        };
        var accepted = session.Apply(acknowledged, session.Revision);

        Assert.Equal(CommandStatus.Rejected, rejected.Status);
        Assert.Equal("rail.warning.unacknowledged", rejected.Code);
        Assert.Equal(CommandStatus.Accepted, accepted.Status);
    }

    private static WorldEditorSession CreateSession()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260729));
        return new WorldEditorSession(
            new WorldState(0, generated, RailTopology.Empty),
            new RailEditValidator());
    }

    private static RailGraphPatch MainLinePatch(string scope = "main")
    {
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                Waypoint(0, 0, "station:a"),
                Waypoint(120, 70, "station:c"),
                Waypoint(250, 40, "station:d"),
                Waypoint(400, 120, "station:b")));
        return new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource(scope))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
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
