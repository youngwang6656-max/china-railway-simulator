using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class RailDrawingPreviewTests
{
    [Fact]
    public void PreviewContainsOffsetTracksAndLengths()
    {
        var preview = BuildPreview(
            [
                Waypoint(0, 0),
                Waypoint(10_000, 0)
            ],
            metersPerPixel: 50);

        Assert.Equal(
            preview.Draft.SampledPoints.Length,
            preview.UpTrackPoints.Length);
        Assert.Equal(
            preview.Draft.SampledPoints.Length,
            preview.DownTrackPoints.Length);
        Assert.Equal(
            5_000d,
            preview.UpTrackPoints[4].DistanceMetersTo(
                preview.DownTrackPoints[4]) * 1000d,
            precision: 3);
        Assert.Equal(
            10_000_000,
            preview.Metrics.TotalLengthMillimeters);
        Assert.Equal(
            10_000_000,
            preview.Metrics.CurrentSegmentLengthMillimeters);
        Assert.True(preview.Metrics.VerticalProfilePending);
        Assert.Equal(350, preview.Metrics.EstimatedSpeedKph);
    }

    [Fact]
    public void CloserZoomUsesMoreSamplesForTheSameCurve()
    {
        var waypoints = ImmutableArray.Create(
            Waypoint(0, 0),
            Waypoint(5_000, 4_000),
            Waypoint(10_000, 0));

        var far = BuildPreview(waypoints, metersPerPixel: 1_000);
        var close = BuildPreview(waypoints, metersPerPixel: 5);

        Assert.True(
            close.Draft.SampledPoints.Length >
            far.Draft.SampledPoints.Length);
    }

    [Fact]
    public void SnappedEndpointHeadingControlsTheFinalCurveTangent()
    {
        var draft = new ControlPointSplineStrategy(
                samplesPerSegment: 64)
            .Build(
                ImmutableArray.Create(
                    Waypoint(0, 0),
                    Waypoint(10_000, 5_000)),
                new AlignmentEndpointHints(
                    null,
                    EndHeadingRadians: 0));
        var previous = draft.SampledPoints[^2];
        var final = draft.SampledPoints[^1];
        var angle = Math.Atan2(
            final.YMillimeters - previous.YMillimeters,
            final.XMillimeters - previous.XMillimeters);

        Assert.InRange(Math.Abs(angle), 0, 0.03);
        Assert.True(final.XMillimeters > previous.XMillimeters);
    }

    [Fact]
    public void PreviewDraftIsTheExactGeometryUsedForPlacement()
    {
        var preview = BuildPreview(
            [
                Waypoint(0, 0),
                Waypoint(5_000, 4_000),
                Waypoint(10_000, 0)
            ],
            metersPerPixel: 5);
        var patch = new DoubleTrackCorridorStrategy(
                new DeterministicStableIdSource("preview-match"))
            .CreatePatch(
                RailTopology.Empty,
                preview.Draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);

        Assert.Equal(
            preview.Draft.SampledPoints,
            Assert.Single(patch.AddedAlignments).ControlPoints);
    }

    [Theory]
    [InlineData(7_000, 344)]
    [InlineData(20_000, 350)]
    public void CurveSpeedIsConservativeAndDesignSpeedCapped(
        double radiusMeters,
        int expectedKph)
    {
        Assert.Equal(
            expectedKph,
            new ConservativeCurveSpeedEstimator().EstimateKph(
                radiusMeters,
                350));
    }

    private static RailDrawingPreview BuildPreview(
        ImmutableArray<DraftWaypoint> waypoints,
        double metersPerPixel)
    {
        return new RailDrawingPreviewBuilder(
                new ConservativeCurveSpeedEstimator())
            .Build(
                waypoints,
                new RailSnapResult(
                    waypoints[^1],
                    RailSnapKind.Free,
                    null,
                    null),
                TrackPlacementOptions.HighSpeedDoubleTrack,
                AlignmentEndpointHints.None,
                metersPerPixel);
    }

    private static DraftWaypoint Waypoint(
        long xMeters,
        long yMeters)
    {
        return new DraftWaypoint(
            new WorldPoint(xMeters * 1000, yMeters * 1000),
            null);
    }
}
