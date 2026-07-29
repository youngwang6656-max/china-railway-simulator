using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public enum RailSnapKind
{
    Free,
    AngleGuide,
    TrackSplit,
    Station,
    RailNode
}

public sealed record RailSnapCandidate(
    StableId? Id,
    RailSnapKind Kind,
    DraftWaypoint Waypoint,
    double ScreenDistancePixels,
    double? GuideAngleDegrees = null);

public sealed record RailSnapRequest(
    WorldPoint FreePosition,
    ImmutableArray<RailSnapCandidate> Candidates,
    bool SnappingDisabled,
    double MaximumDistancePixels);

public sealed record RailSnapResult(
    DraftWaypoint Waypoint,
    RailSnapKind Kind,
    StableId? TargetId,
    double? GuideAngleDegrees);

public interface IRailSnapResolver
{
    RailSnapResult Resolve(RailSnapRequest request);
}

public sealed class RailSnapResolver : IRailSnapResolver
{
    public RailSnapResult Resolve(RailSnapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.MaximumDistancePixels) ||
            request.MaximumDistancePixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.SnappingDisabled)
        {
            return Free(request.FreePosition);
        }

        var candidate = request.Candidates
            .Where(
                item =>
                    double.IsFinite(item.ScreenDistancePixels) &&
                    item.ScreenDistancePixels >= 0 &&
                    item.ScreenDistancePixels <=
                    request.MaximumDistancePixels)
            .OrderByDescending(item => item.Kind)
            .ThenBy(item => item.ScreenDistancePixels)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        if (candidate is null)
        {
            return Free(request.FreePosition);
        }

        var waypoint = candidate.Kind switch
        {
            RailSnapKind.RailNode => candidate.Waypoint with
            {
                NodeId = candidate.Id,
                SplitTrackId = null
            },
            RailSnapKind.Station => candidate.Waypoint with
            {
                StationId = candidate.Id,
                NodeId = null,
                SplitTrackId = null
            },
            RailSnapKind.TrackSplit => candidate.Waypoint with
            {
                StationId = null,
                NodeId = null,
                SplitTrackId = candidate.Id
            },
            _ => candidate.Waypoint with
            {
                StationId = null,
                NodeId = null,
                SplitTrackId = null
            }
        };
        return new RailSnapResult(
            waypoint,
            candidate.Kind,
            candidate.Id,
            candidate.GuideAngleDegrees);
    }

    private static RailSnapResult Free(WorldPoint position)
    {
        return new RailSnapResult(
            new DraftWaypoint(position, null),
            RailSnapKind.Free,
            null,
            null);
    }
}
