using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public enum RailValidationSeverity
{
    Warning,
    Error
}

public sealed record RailValidationIssue(
    string Code,
    RailValidationSeverity Severity,
    StableId? EntityId,
    WorldPoint? Position);

public sealed record RailValidationResult(
    ImmutableArray<RailValidationIssue> Issues)
{
    public bool HasErrors =>
        Issues.Any(issue => issue.Severity == RailValidationSeverity.Error);
}

public interface IRailEditValidator
{
    RailValidationResult Validate(
        WorldState world,
        RailGraphPatch patch);
}

public sealed class RailEditValidator : IRailEditValidator
{
    public RailValidationResult Validate(
        WorldState world,
        RailGraphPatch patch)
    {
        ArgumentNullException.ThrowIfNull(world);
        var structural = Validate(world.RailTopology, patch);
        if (structural.HasErrors)
        {
            return structural;
        }

        var issues = structural.Issues.ToBuilder();
        try
        {
            var candidate = patch.ApplyTo(world.RailTopology);
            WorldStateValidator.Validate(
                new WorldState(
                    world.Revision,
                    world.BaseWorld,
                    candidate));
        }
        catch (Exception exception)
            when (exception is InvalidDataException or
                  ArgumentException or
                  OverflowException)
        {
            issues.Add(new RailValidationIssue(
                "world.state.invalid",
                RailValidationSeverity.Error,
                null,
                null));
            return new RailValidationResult(issues.ToImmutable());
        }

        AddGeometryWarnings(world, patch, issues);
        return new RailValidationResult(issues.ToImmutable());
    }

    public RailValidationResult Validate(
        RailTopology topology,
        RailGraphPatch patch)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(patch);
        var issues = ImmutableArray.CreateBuilder<RailValidationIssue>();
        if (patch.IsEmpty)
        {
            issues.Add(new RailValidationIssue(
                "rail.patch.empty",
                RailValidationSeverity.Error,
                null,
                null));
            return new RailValidationResult(issues.ToImmutable());
        }

        RailTopology candidate;
        try
        {
            candidate = patch.ApplyTo(topology);
        }
        catch (RailTopologyException exception)
        {
            issues.Add(new RailValidationIssue(
                exception.Code,
                RailValidationSeverity.Error,
                null,
                null));
            return new RailValidationResult(issues.ToImmutable());
        }

        if (patch.Intent == RailEditIntent.DoubleTrackCorridor &&
            patch.AddedTracks.IsEmpty)
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.incomplete",
                RailValidationSeverity.Error,
                null,
                null));
        }
        else if (
            (patch.Intent is RailEditIntent.DoubleTrackCorridor or
                RailEditIntent.Connector) &&
            patch.AddedTracks.Length > 0)
        {
            ValidateDoubleTrackCompleteness(
                patch,
                candidate,
                issues);
        }

        foreach (var track in patch.AddedTracks)
        {
            if (Math.Abs(track.LateralOffsetMillimeters) < 2_000)
            {
                issues.Add(new RailValidationIssue(
                    "rail.engineering.track_spacing",
                    RailValidationSeverity.Warning,
                    track.Id,
                    null));
            }
        }

        return new RailValidationResult(issues.ToImmutable());
    }

    private static void ValidateDoubleTrackCompleteness(
        RailGraphPatch patch,
        RailTopology candidate,
        ImmutableArray<RailValidationIssue>.Builder issues)
    {
        var addedAlignmentIds = patch.AddedAlignments
            .Select(alignment => alignment.Id)
            .ToHashSet();
        var alignmentCoverageComplete =
            patch.AddedTracks.All(
                track => addedAlignmentIds.Contains(track.AlignmentId)) &&
            patch.AddedAlignments.All(
                alignment =>
                    patch.AddedTracks.Count(
                        track => track.AlignmentId == alignment.Id) == 2);
        if (!alignmentCoverageComplete)
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.alignment_incomplete",
                RailValidationSeverity.Error,
                null,
                null));
        }

        foreach (var alignment in patch.AddedAlignments)
        {
            var tracks = patch.AddedTracks
                .Where(track => track.AlignmentId == alignment.Id)
                .ToArray();
            var validPair =
                tracks.Length == 2 &&
                tracks[0].LateralOffsetMillimeters ==
                -tracks[1].LateralOffsetMillimeters &&
                tracks[0].LateralOffsetMillimeters != 0;
            if (validPair)
            {
                var firstFrom = candidate.Ports[tracks[0].FromPortId].NodeId;
                var firstTo = candidate.Ports[tracks[0].ToPortId].NodeId;
                var secondFrom = candidate.Ports[tracks[1].FromPortId].NodeId;
                var secondTo = candidate.Ports[tracks[1].ToPortId].NodeId;
                validPair =
                    firstFrom == secondTo &&
                    firstTo == secondFrom;
            }

            if (!validPair)
            {
                issues.Add(new RailValidationIssue(
                    "rail.double_track.incomplete",
                    RailValidationSeverity.Error,
                    alignment.Id,
                    null));
            }
        }

        var addedTrackIds = patch.AddedTracks
            .Select(track => track.Id)
            .ToHashSet();
        if (patch.AddedTrackGroups.Length != 1 ||
            !patch.AddedTrackGroups[0].TrackIds.ToHashSet()
                .SetEquals(addedTrackIds))
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.group_incomplete",
                RailValidationSeverity.Error,
                null,
                null));
        }

        var referencedPortIds = patch.AddedTracks
            .SelectMany(track => new[] { track.FromPortId, track.ToPortId })
            .ToHashSet();
        if (!patch.AddedPorts
                .Select(port => port.Id)
                .ToHashSet()
                .SetEquals(referencedPortIds))
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.port_incomplete",
                RailValidationSeverity.Error,
                null,
                null));
        }

        var nodesWithAddedPorts = patch.AddedPorts
            .Select(port => port.NodeId)
            .ToHashSet();
        if (patch.AddedNodes.Any(
                node => !nodesWithAddedPorts.Contains(node.Id)))
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.node_incomplete",
                RailValidationSeverity.Error,
                null,
                null));
        }

        if (patch.AddedConnections.Any(
                connection =>
                    !addedTrackIds.Contains(connection.FromTrackId) &&
                    !addedTrackIds.Contains(connection.ToTrackId)))
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.connection_orphan",
                RailValidationSeverity.Error,
                null,
                null));
        }

        ValidateStraightConnections(
            patch,
            candidate,
            issues);
    }

    private static void ValidateStraightConnections(
        RailGraphPatch patch,
        RailTopology candidate,
        ImmutableArray<RailValidationIssue>.Builder issues)
    {
        var missing = false;
        foreach (var node in candidate.Nodes.Values)
        {
            var incoming = patch.AddedTracks.Where(
                track =>
                    candidate.Ports[track.ToPortId].NodeId == node.Id);
            var outgoing = patch.AddedTracks.Where(
                track =>
                    candidate.Ports[track.FromPortId].NodeId == node.Id);
            foreach (var movement in incoming.Join(
                         outgoing,
                         track => track.LateralOffsetMillimeters,
                         track => track.LateralOffsetMillimeters,
                         (from, to) => (From: from.Id, To: to.Id)))
            {
                if (!candidate.Connections.Values.Any(
                        connection =>
                            connection.NodeId == node.Id &&
                            connection.FromTrackId == movement.From &&
                            connection.ToTrackId == movement.To))
                {
                    missing = true;
                    break;
                }
            }

            if (missing)
            {
                break;
            }
        }

        if (missing)
        {
            issues.Add(new RailValidationIssue(
                "rail.double_track.connection_incomplete",
                RailValidationSeverity.Error,
                null,
                null));
        }
    }

    private static void AddGeometryWarnings(
        WorldState world,
        RailGraphPatch patch,
        ImmutableArray<RailValidationIssue>.Builder issues)
    {
        foreach (var alignment in patch.AddedAlignments)
        {
            var tracks = patch.AddedTracks
                .Where(track => track.AlignmentId == alignment.Id)
                .ToArray();
            var designSpeed = tracks.Length == 0
                ? 160
                : tracks.Max(track => track.DesignSpeedKph);
            if (HasLowRadius(alignment.ControlPoints, designSpeed))
            {
                issues.Add(new RailValidationIssue(
                    "rail.engineering.low_radius",
                    RailValidationSeverity.Warning,
                    alignment.Id,
                    alignment.ControlPoints[alignment.ControlPoints.Length / 2]));
            }

            if (HasSteepTerrain(
                    alignment.ControlPoints,
                    world.BaseWorld.Terrain,
                    world.BaseWorld.Parameters.Bounds))
            {
                issues.Add(new RailValidationIssue(
                    "rail.engineering.steep_terrain",
                    RailValidationSeverity.Warning,
                    alignment.Id,
                    alignment.ControlPoints[alignment.ControlPoints.Length / 2]));
            }

            if (CrossesRiver(
                    alignment.ControlPoints,
                    world.BaseWorld.Rivers))
            {
                issues.Add(new RailValidationIssue(
                    "rail.engineering.river_crossing",
                    RailValidationSeverity.Warning,
                    alignment.Id,
                    alignment.ControlPoints[alignment.ControlPoints.Length / 2]));
            }
        }
    }

    private static bool HasLowRadius(
        ImmutableArray<WorldPoint> points,
        int designSpeedKph)
    {
        var minimumRadiusMeters = Math.Max(
            300d,
            designSpeedKph * designSpeedKph / 12d);
        for (var index = 1; index < points.Length - 1; index++)
        {
            var a = points[index - 1];
            var b = points[index];
            var c = points[index + 1];
            var sideA = a.DistanceMetersTo(b);
            var sideB = b.DistanceMetersTo(c);
            var sideC = c.DistanceMetersTo(a);
            var twiceArea = Math.Abs(
                ((b.XMillimeters - a.XMillimeters) *
                 (double)(c.YMillimeters - a.YMillimeters)) -
                ((b.YMillimeters - a.YMillimeters) *
                 (double)(c.XMillimeters - a.XMillimeters))) /
                1_000_000d;
            if (twiceArea <= 0)
            {
                continue;
            }

            var radius =
                (sideA * sideB * sideC) /
                (2d * twiceArea);
            if (radius < minimumRadiusMeters)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSteepTerrain(
        ImmutableArray<WorldPoint> points,
        HeightField terrain,
        WorldBounds bounds)
    {
        for (var index = 1; index < points.Length; index++)
        {
            var horizontal =
                points[index - 1].DistanceMetersTo(points[index]);
            if (horizontal <= 0)
            {
                continue;
            }

            var elevationDifference = Math.Abs(
                terrain.SampleAt(points[index], bounds) -
                terrain.SampleAt(points[index - 1], bounds));
            if (elevationDifference / horizontal > 0.035)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CrossesRiver(
        ImmutableArray<WorldPoint> alignment,
        ImmutableArray<RiverPath> rivers)
    {
        for (var alignmentIndex = 1;
             alignmentIndex < alignment.Length;
             alignmentIndex++)
        {
            foreach (var river in rivers)
            {
                for (var riverIndex = 1;
                     riverIndex < river.Points.Length;
                     riverIndex++)
                {
                    if (SegmentsIntersect(
                            alignment[alignmentIndex - 1],
                            alignment[alignmentIndex],
                            river.Points[riverIndex - 1],
                            river.Points[riverIndex]))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        WorldPoint a,
        WorldPoint b,
        WorldPoint c,
        WorldPoint d)
    {
        static double Cross(
            WorldPoint origin,
            WorldPoint first,
            WorldPoint second)
        {
            return
                ((first.XMillimeters - origin.XMillimeters) *
                 (double)(second.YMillimeters - origin.YMillimeters)) -
                ((first.YMillimeters - origin.YMillimeters) *
                 (double)(second.XMillimeters - origin.XMillimeters));
        }

        static bool OnSegment(
            WorldPoint first,
            WorldPoint second,
            WorldPoint point)
        {
            return
                point.XMillimeters >= Math.Min(
                    first.XMillimeters,
                    second.XMillimeters) &&
                point.XMillimeters <= Math.Max(
                    first.XMillimeters,
                    second.XMillimeters) &&
                point.YMillimeters >= Math.Min(
                    first.YMillimeters,
                    second.YMillimeters) &&
                point.YMillimeters <= Math.Max(
                    first.YMillimeters,
                    second.YMillimeters);
        }

        var first = Cross(a, b, c);
        var second = Cross(a, b, d);
        var third = Cross(c, d, a);
        var fourth = Cross(c, d, b);
        if (((first < 0 && second > 0) ||
             (first > 0 && second < 0)) &&
            ((third < 0 && fourth > 0) ||
             (third > 0 && fourth < 0)))
        {
            return true;
        }

        return
            (first == 0 && OnSegment(a, b, c)) ||
            (second == 0 && OnSegment(a, b, d)) ||
            (third == 0 && OnSegment(c, d, a)) ||
            (fourth == 0 && OnSegment(c, d, b));
    }
}
