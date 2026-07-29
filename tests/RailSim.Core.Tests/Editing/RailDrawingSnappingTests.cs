using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class RailDrawingSnappingTests
{
    [Fact]
    public void RailNodeWinsOverCloserLowerPriorityCandidates()
    {
        var request = Request(
            Candidate("station", RailSnapKind.Station, 2),
            Candidate("node", RailSnapKind.RailNode, 7));

        var result = new RailSnapResolver().Resolve(request);

        Assert.Equal(RailSnapKind.RailNode, result.Kind);
        Assert.Equal(new StableId("node"), result.TargetId);
        Assert.Equal(new StableId("node"), result.Waypoint.NodeId);
    }

    [Fact]
    public void ShiftReturnsTheFreeCursorPosition()
    {
        var request = Request(
            true,
            16,
            Candidate("node", RailSnapKind.RailNode, 1));

        var result = new RailSnapResolver().Resolve(request);

        Assert.Equal(RailSnapKind.Free, result.Kind);
        Assert.Equal(request.FreePosition, result.Waypoint.Position);
        Assert.Null(result.TargetId);
    }

    [Fact]
    public void CandidateOutsidePixelThresholdDoesNotSnap()
    {
        var result = new RailSnapResolver().Resolve(
            Request(
                false,
                16,
                Candidate("node", RailSnapKind.RailNode, 16.01)));

        Assert.Equal(RailSnapKind.Free, result.Kind);
    }

    [Fact]
    public void StableIdBreaksEqualKindAndDistanceTies()
    {
        var result = new RailSnapResolver().Resolve(
            Request(
                Candidate("z", RailSnapKind.Station, 4),
                Candidate("a", RailSnapKind.Station, 4)));

        Assert.Equal(new StableId("a"), result.TargetId);
        Assert.Equal(new StableId("a"), result.Waypoint.StationId);
    }

    [Fact]
    public void TrackSplitCandidateCarriesTargetIntoDraftWaypoint()
    {
        var result = new RailSnapResolver().Resolve(
            Request(Candidate("track", RailSnapKind.TrackSplit, 3)));

        Assert.Equal(RailSnapKind.TrackSplit, result.Kind);
        Assert.Equal(new StableId("track"), result.Waypoint.SplitTrackId);
        Assert.Null(result.Waypoint.NodeId);
    }

    private static RailSnapRequest Request(
        params RailSnapCandidate[] candidates)
    {
        return Request(false, 16, candidates);
    }

    private static RailSnapRequest Request(
        bool snappingDisabled,
        double maximumDistancePixels,
        params RailSnapCandidate[] candidates)
    {
        return new RailSnapRequest(
            new WorldPoint(50_000, 75_000),
            candidates.ToImmutableArray(),
            snappingDisabled,
            maximumDistancePixels);
    }

    private static RailSnapCandidate Candidate(
        string id,
        RailSnapKind kind,
        double distance)
    {
        return new RailSnapCandidate(
            new StableId(id),
            kind,
            new DraftWaypoint(
                new WorldPoint(
                    id[0] * 10_000L,
                    id[^1] * 10_000L),
                null),
            distance);
    }
}
