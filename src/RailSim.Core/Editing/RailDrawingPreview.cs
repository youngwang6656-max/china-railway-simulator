using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public sealed record RailDrawingMetrics(
    long CurrentSegmentLengthMillimeters,
    long TotalLengthMillimeters,
    double? MinimumRadiusMeters,
    int EstimatedSpeedKph,
    bool VerticalProfilePending);

public sealed record RailDrawingPreview(
    AlignmentDraft Draft,
    ImmutableArray<WorldPoint> UpTrackPoints,
    ImmutableArray<WorldPoint> DownTrackPoints,
    RailDrawingMetrics Metrics,
    RailSnapResult Snap);

public interface ICurveSpeedEstimator
{
    int EstimateKph(double radiusMeters, int designSpeedKph);
}

public sealed class ConservativeCurveSpeedEstimator :
    ICurveSpeedEstimator
{
    private const double EffectiveCantMillimeters = 200;
    private const double CantCoefficient = 11.8;

    public int EstimateKph(
        double radiusMeters,
        int designSpeedKph)
    {
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        }

        if (designSpeedKph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(designSpeedKph));
        }

        var curveSpeed = checked((int)Math.Floor(
            Math.Sqrt(
                radiusMeters *
                EffectiveCantMillimeters /
                CantCoefficient)));
        return Math.Min(curveSpeed, designSpeedKph);
    }
}

public sealed class RailDrawingPreviewBuilder
{
    private const double MaximumChordErrorPixels = 0.75;
    private static readonly int[] SampleCounts = [8, 16, 32, 64];
    private readonly ICurveSpeedEstimator _speedEstimator;

    public RailDrawingPreviewBuilder(
        ICurveSpeedEstimator speedEstimator)
    {
        _speedEstimator = speedEstimator ??
            throw new ArgumentNullException(nameof(speedEstimator));
    }

    public RailDrawingPreview Build(
        ImmutableArray<DraftWaypoint> waypoints,
        RailSnapResult snap,
        TrackPlacementOptions options,
        AlignmentEndpointHints endpointHints,
        double metersPerPixel)
    {
        if (waypoints.Length < 2)
        {
            throw new ArgumentException(
                "At least two waypoints are required for a preview.",
                nameof(waypoints));
        }

        if (!double.IsFinite(metersPerPixel) ||
            metersPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metersPerPixel));
        }

        if (options.TrackSpacingMillimeters <= 0 ||
            options.DesignSpeedKph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var samplesPerSegment = ChooseSampleCount(
            waypoints,
            endpointHints,
            metersPerPixel);
        var draft = new ControlPointSplineStrategy(
                samplesPerSegment)
            .Build(waypoints, endpointHints);
        var (up, down) = OffsetTracks(
            draft.SampledPoints,
            options.TrackSpacingMillimeters / 2d);
        var totalLength = CalculateLength(
            draft.SampledPoints,
            0);
        var currentStart = Math.Max(
            0,
            draft.SampledPoints.Length - samplesPerSegment - 1);
        var currentLength = CalculateLength(
            draft.SampledPoints,
            currentStart);
        var minimumRadius = CalculateMinimumRadius(
            draft.SampledPoints);
        var speed = minimumRadius.HasValue
            ? _speedEstimator.EstimateKph(
                minimumRadius.Value,
                options.DesignSpeedKph)
            : options.DesignSpeedKph;
        return new RailDrawingPreview(
            draft,
            up,
            down,
            new RailDrawingMetrics(
                currentLength,
                totalLength,
                minimumRadius,
                speed,
                true),
            snap);
    }

    private static int ChooseSampleCount(
        ImmutableArray<DraftWaypoint> waypoints,
        AlignmentEndpointHints endpointHints,
        double metersPerPixel)
    {
        var reference = new ControlPointSplineStrategy(
                SampleCounts[^1])
            .Build(waypoints, endpointHints)
            .SampledPoints;
        foreach (var count in SampleCounts)
        {
            if (count == SampleCounts[^1])
            {
                return count;
            }

            var candidate = new ControlPointSplineStrategy(count)
                .Build(waypoints, endpointHints)
                .SampledPoints;
            var maximumErrorMeters = reference.Max(
                point => DistanceToPolylineMeters(
                    point,
                    candidate));
            if (maximumErrorMeters / metersPerPixel <=
                MaximumChordErrorPixels)
            {
                return count;
            }
        }

        return SampleCounts[^1];
    }

    private static (
        ImmutableArray<WorldPoint> Up,
        ImmutableArray<WorldPoint> Down)
        OffsetTracks(
            ImmutableArray<WorldPoint> points,
            double halfSpacingMillimeters)
    {
        var up = ImmutableArray.CreateBuilder<WorldPoint>(
            points.Length);
        var down = ImmutableArray.CreateBuilder<WorldPoint>(
            points.Length);
        for (var index = 0; index < points.Length; index++)
        {
            var before = points[Math.Max(0, index - 1)];
            var after = points[Math.Min(points.Length - 1, index + 1)];
            var tangentX =
                (double)after.XMillimeters - before.XMillimeters;
            var tangentY =
                (double)after.YMillimeters - before.YMillimeters;
            var magnitude = Math.Sqrt(
                (tangentX * tangentX) +
                (tangentY * tangentY));
            if (magnitude <= 0)
            {
                throw new InvalidDataException(
                    "Preview geometry contains a zero-length tangent.");
            }

            var offsetX =
                -tangentY / magnitude * halfSpacingMillimeters;
            var offsetY =
                tangentX / magnitude * halfSpacingMillimeters;
            up.Add(new WorldPoint(
                checked((long)Math.Round(
                    points[index].XMillimeters + offsetX)),
                checked((long)Math.Round(
                    points[index].YMillimeters + offsetY))));
            down.Add(new WorldPoint(
                checked((long)Math.Round(
                    points[index].XMillimeters - offsetX)),
                checked((long)Math.Round(
                    points[index].YMillimeters - offsetY))));
        }

        return (up.MoveToImmutable(), down.MoveToImmutable());
    }

    private static long CalculateLength(
        ImmutableArray<WorldPoint> points,
        int startIndex)
    {
        var lengthMillimeters = 0d;
        for (var index = Math.Max(1, startIndex + 1);
             index < points.Length;
             index++)
        {
            lengthMillimeters +=
                points[index - 1].DistanceMetersTo(points[index]) *
                1000d;
        }

        return checked((long)Math.Round(lengthMillimeters));
    }

    private static double? CalculateMinimumRadius(
        ImmutableArray<WorldPoint> points)
    {
        double? minimum = null;
        for (var index = 1; index < points.Length - 1; index++)
        {
            var first = points[index - 1];
            var middle = points[index];
            var last = points[index + 1];
            var firstX = first.XMillimeters / 1000d;
            var firstY = first.YMillimeters / 1000d;
            var middleX = middle.XMillimeters / 1000d;
            var middleY = middle.YMillimeters / 1000d;
            var lastX = last.XMillimeters / 1000d;
            var lastY = last.YMillimeters / 1000d;
            var cross = Math.Abs(
                ((middleX - firstX) * (lastY - firstY)) -
                ((middleY - firstY) * (lastX - firstX)));
            if (cross <= 1e-6)
            {
                continue;
            }

            var a = first.DistanceMetersTo(middle);
            var b = middle.DistanceMetersTo(last);
            var c = last.DistanceMetersTo(first);
            var radius = a * b * c / (2d * cross);
            if (double.IsFinite(radius) &&
                radius > 0 &&
                (!minimum.HasValue || radius < minimum.Value))
            {
                minimum = radius;
            }
        }

        return minimum;
    }

    private static double DistanceToPolylineMeters(
        WorldPoint point,
        ImmutableArray<WorldPoint> points)
    {
        var minimumSquared = double.PositiveInfinity;
        for (var index = 0; index < points.Length - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            var deltaX =
                (end.XMillimeters - start.XMillimeters) / 1000d;
            var deltaY =
                (end.YMillimeters - start.YMillimeters) / 1000d;
            var pointX =
                (point.XMillimeters - start.XMillimeters) / 1000d;
            var pointY =
                (point.YMillimeters - start.YMillimeters) / 1000d;
            var lengthSquared =
                (deltaX * deltaX) + (deltaY * deltaY);
            if (lengthSquared <= 0)
            {
                continue;
            }

            var t = Math.Clamp(
                ((pointX * deltaX) + (pointY * deltaY)) /
                lengthSquared,
                0d,
                1d);
            var distanceX = pointX - (deltaX * t);
            var distanceY = pointY - (deltaY * t);
            minimumSquared = Math.Min(
                minimumSquared,
                (distanceX * distanceX) +
                (distanceY * distanceY));
        }

        return Math.Sqrt(minimumSquared);
    }
}
