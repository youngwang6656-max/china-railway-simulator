using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public readonly record struct DraftWaypoint(
    WorldPoint Position,
    StableId? StationId);

public sealed record AlignmentDraft
{
    public AlignmentDraft(
        ImmutableArray<DraftWaypoint> waypoints,
        ImmutableArray<WorldPoint> sampledPoints)
    {
        if (waypoints.Length < 2)
        {
            throw new ArgumentException(
                "An alignment draft requires at least two waypoints.",
                nameof(waypoints));
        }

        if (sampledPoints.Length < 2)
        {
            throw new ArgumentException(
                "An alignment draft requires at least two sampled points.",
                nameof(sampledPoints));
        }

        Waypoints = waypoints;
        SampledPoints = sampledPoints;
    }

    public ImmutableArray<DraftWaypoint> Waypoints { get; }

    public ImmutableArray<WorldPoint> SampledPoints { get; }
}

public interface IAlignmentSketchStrategy
{
    AlignmentDraft Build(ImmutableArray<DraftWaypoint> waypoints);
}

public sealed class ControlPointSplineStrategy : IAlignmentSketchStrategy
{
    private const int SamplesPerSegment = 8;

    public AlignmentDraft Build(ImmutableArray<DraftWaypoint> waypoints)
    {
        if (waypoints.Length < 2)
        {
            throw new ArgumentException(
                "At least two waypoints are required.",
                nameof(waypoints));
        }

        for (var index = 1; index < waypoints.Length; index++)
        {
            if (waypoints[index - 1].Position == waypoints[index].Position)
            {
                throw new ArgumentException(
                    "Consecutive waypoints must not share a position.",
                    nameof(waypoints));
            }
        }

        var samples = ImmutableArray.CreateBuilder<WorldPoint>(
            ((waypoints.Length - 1) * SamplesPerSegment) + 1);
        samples.Add(waypoints[0].Position);
        for (var segment = 0; segment < waypoints.Length - 1; segment++)
        {
            var p0 = waypoints[Math.Max(0, segment - 1)].Position;
            var p1 = waypoints[segment].Position;
            var p2 = waypoints[segment + 1].Position;
            var p3 = waypoints[Math.Min(waypoints.Length - 1, segment + 2)]
                .Position;
            for (var sample = 1; sample <= SamplesPerSegment; sample++)
            {
                var t = sample / (double)SamplesPerSegment;
                samples.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return new AlignmentDraft(waypoints, samples.MoveToImmutable());
    }

    private static WorldPoint CatmullRom(
        WorldPoint p0,
        WorldPoint p1,
        WorldPoint p2,
        WorldPoint p3,
        double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return new WorldPoint(
            Interpolate(
                p0.XMillimeters,
                p1.XMillimeters,
                p2.XMillimeters,
                p3.XMillimeters,
                t,
                t2,
                t3),
            Interpolate(
                p0.YMillimeters,
                p1.YMillimeters,
                p2.YMillimeters,
                p3.YMillimeters,
                t,
                t2,
                t3));
    }

    private static long Interpolate(
        long p0,
        long p1,
        long p2,
        long p3,
        double t,
        double t2,
        double t3)
    {
        var value = 0.5d *
            ((2d * p1) +
             ((-p0 + p2) * t) +
             (((2d * p0) - (5d * p1) + (4d * p2) - p3) * t2) +
             (((-p0 + (3d * p1) - (3d * p2) + p3)) * t3));
        return checked((long)Math.Round(value));
    }
}
