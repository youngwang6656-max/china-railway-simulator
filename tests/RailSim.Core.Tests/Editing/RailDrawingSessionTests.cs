using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class RailDrawingSessionTests
{
    [Fact]
    public void CursorCandidateAppearsInPreviewButNotConfirmedPoints()
    {
        var session = NewSession();
        session.Begin(Waypoint(0, 0));

        session.UpdatePreview(FreeRequest(100, 20));

        Assert.Single(session.ConfirmedWaypoints);
        Assert.Equal(2, session.PreviewWaypoints.Length);
        Assert.Equal(Point(100, 20), session.PreviewWaypoints[^1].Position);
    }

    [Fact]
    public void ConfirmingPreviewPromotesCandidateExactlyOnce()
    {
        var session = NewSession();
        session.Begin(Waypoint(0, 0));
        session.UpdatePreview(FreeRequest(100, 20));

        var result = session.ConfirmPreviewPoint();

        Assert.True(result.Accepted);
        Assert.Equal(2, session.ConfirmedWaypoints.Length);
        Assert.Null(session.CurrentSnap);
        Assert.Equal(Point(100, 20), session.ConfirmedWaypoints[^1].Position);
    }

    [Fact]
    public void DuplicateCandidateIsRejectedWithoutMutatingDraft()
    {
        var session = NewSession();
        session.Begin(Waypoint(0, 0));
        session.UpdatePreview(FreeRequest(0, 0));

        var result = session.ConfirmPreviewPoint();

        Assert.False(result.Accepted);
        Assert.Equal("rail.drawing.duplicate_point", result.Code);
        Assert.Single(session.ConfirmedWaypoints);
    }

    [Fact]
    public void SamePositionCandidateCanRebindAStaleSnapTarget()
    {
        var session = NewSession();
        session.Load(
            ImmutableArray.Create(
                Waypoint(0, 0),
                new DraftWaypoint(
                    Point(100, 0),
                    null,
                    SplitTrackId: new StableId("removed-track"))));
        session.UpdatePreview(FreeRequest(100, 0));

        Assert.Equal(2, session.PreviewWaypoints.Length);
        Assert.Null(session.PreviewWaypoints[^1].SplitTrackId);
        var result = session.ConfirmPreviewPoint();

        Assert.True(result.Accepted);
        Assert.Null(session.ConfirmedWaypoints[^1].SplitTrackId);
        Assert.Equal(Point(100, 0), session.ConfirmedWaypoints[^1].Position);
    }

    [Fact]
    public void RightClickStepBackRemovesOnePointThenCancelsOrigin()
    {
        var session = SessionWithConfirmedPoints(
            Point(0, 0),
            Point(100, 0));

        Assert.Equal(
            RailDrawingUndoResult.PointRemoved,
            session.UndoStep());
        Assert.Single(session.ConfirmedWaypoints);
        Assert.Equal(
            RailDrawingUndoResult.DraftCancelled,
            session.UndoStep());
        Assert.Equal(RailDrawingState.Idle, session.State);
        Assert.Empty(session.ConfirmedWaypoints);
    }

    [Fact]
    public void CancellingWaypointDragRestoresPreDragPosition()
    {
        var session = SessionWithConfirmedPoints(
            Point(0, 0),
            Point(100, 0));
        session.BeginWaypointDrag(1);
        session.MoveWaypoint(1, Waypoint(140, 30));

        session.CancelWaypointDrag();

        Assert.Equal(Point(100, 0), session.ConfirmedWaypoints[1].Position);
        Assert.Equal(RailDrawingState.Previewing, session.State);
    }

    [Fact]
    public void DragCannotCollapseWaypointOntoItsNeighbor()
    {
        var session = SessionWithConfirmedPoints(
            Point(0, 0),
            Point(100, 0),
            Point(200, 0));
        session.BeginWaypointDrag(1);

        Assert.Throws<ArgumentException>(
            () => session.MoveWaypoint(1, Waypoint(0, 0)));
        Assert.Equal(
            Point(100, 0),
            session.ConfirmedWaypoints[1].Position);
    }

    [Fact]
    public void ResetAfterCommitKeepsHybridModeButClearsDraft()
    {
        var session = SessionWithConfirmedPoints(
            Point(0, 0),
            Point(100, 0));

        session.ResetAfterCommit();

        Assert.Equal(
            RailDrawingInputMode.HybridGuided,
            session.InputMode);
        Assert.Empty(session.ConfirmedWaypoints);
        Assert.Equal(RailDrawingState.Idle, session.State);
    }

    [Fact]
    public void WarningStateReturnsToPreviewWhenCursorChanges()
    {
        var session = SessionWithConfirmedPoints(
            Point(0, 0),
            Point(100, 0));
        session.MarkWarningPending();

        session.UpdatePreview(FreeRequest(150, 20));

        Assert.Equal(RailDrawingState.Previewing, session.State);
    }

    private static RailDrawingSession NewSession()
    {
        return new RailDrawingSession(new RailSnapResolver());
    }

    private static RailDrawingSession SessionWithConfirmedPoints(
        params WorldPoint[] points)
    {
        var session = NewSession();
        session.Begin(new DraftWaypoint(points[0], null));
        foreach (var point in points.Skip(1))
        {
            session.UpdatePreview(
                new RailSnapRequest(
                    point,
                    ImmutableArray<RailSnapCandidate>.Empty,
                    false,
                    16));
            Assert.True(session.ConfirmPreviewPoint().Accepted);
        }

        return session;
    }

    private static RailSnapRequest FreeRequest(
        long xMeters,
        long yMeters)
    {
        return new RailSnapRequest(
            Point(xMeters, yMeters),
            ImmutableArray<RailSnapCandidate>.Empty,
            false,
            16);
    }

    private static DraftWaypoint Waypoint(
        long xMeters,
        long yMeters)
    {
        return new DraftWaypoint(Point(xMeters, yMeters), null);
    }

    private static WorldPoint Point(
        long xMeters,
        long yMeters)
    {
        return new WorldPoint(xMeters * 1000, yMeters * 1000);
    }
}
