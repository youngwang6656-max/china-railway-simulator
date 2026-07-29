using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public sealed class RailDrawingCommitComposer
{
    public RailGraphPatch CreateDoubleTrackPatch(
        WorldState world,
        AlignmentDraft draft,
        TrackPlacementOptions options,
        IStableIdSource ids)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(ids);

        var splitTargets = draft.Waypoints
            .Where(waypoint => waypoint.SplitTrackId.HasValue)
            .Select(waypoint => waypoint.SplitTrackId!.Value)
            .ToArray();
        if (splitTargets.Distinct().Count() != splitTargets.Length)
        {
            throw new RailTopologyException(
                "rail.drawing.repeated_split_target",
                "A draft cannot split the same stable track twice.");
        }

        var candidate = world.RailTopology;
        var combined = RailGraphPatch.Empty;
        var resolvedWaypoints = draft.Waypoints.ToBuilder();
        for (var index = 0; index < resolvedWaypoints.Count; index++)
        {
            var waypoint = resolvedWaypoints[index];
            if (!waypoint.SplitTrackId.HasValue)
            {
                continue;
            }

            var targetTrackId = waypoint.SplitTrackId.Value;
            if (!candidate.Tracks.ContainsKey(targetTrackId))
            {
                throw new RailTopologyException(
                    "rail.drawing.snap_target_missing",
                    $"Preview snap target {targetTrackId} no longer exists.");
            }

            var split = new RailTopologyEditStrategy(ids)
                .CreateSplitPatch(
                    candidate,
                    targetTrackId,
                    waypoint.Position,
                    world.BaseWorld.Parameters.ChunkSizeMeters);
            candidate = split.ApplyTo(candidate);
            combined = combined.Combine(split);
            var splitNode = split.AddedNodes.Single(
                node => node.Position == waypoint.Position);
            resolvedWaypoints[index] = waypoint with
            {
                StationId = null,
                NodeId = splitNode.Id,
                SplitTrackId = null
            };
        }

        var resolvedDraft = new AlignmentDraft(
            resolvedWaypoints.ToImmutable(),
            draft.SampledPoints);
        var placementOptions = options with
        {
            ChunkSizeMeters =
                world.BaseWorld.Parameters.ChunkSizeMeters
        };
        var corridor = new DoubleTrackCorridorStrategy(ids)
            .CreatePatch(
                candidate,
                resolvedDraft,
                placementOptions);
        return combined.Combine(corridor);
    }
}
