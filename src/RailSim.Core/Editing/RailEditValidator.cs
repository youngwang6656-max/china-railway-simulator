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
        RailTopology topology,
        RailGraphPatch patch);
}

public sealed class RailEditValidator : IRailEditValidator
{
    public RailValidationResult Validate(
        RailTopology topology,
        RailGraphPatch patch)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(patch);
        var issues = ImmutableArray.CreateBuilder<RailValidationIssue>();
        try
        {
            _ = patch.ApplyTo(topology);
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
}
