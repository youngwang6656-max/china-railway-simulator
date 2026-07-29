using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Rail;

public enum RailNodeKind
{
    Endpoint,
    StationBoundary,
    Junction
}

public enum RailPortDirection
{
    Inbound,
    Outbound,
    Bidirectional
}

public sealed record RailNode(
    StableId Id,
    RailNodeKind Kind,
    WorldPoint Position,
    StableId? StationId);

public sealed record RailPort(
    StableId Id,
    StableId NodeId,
    RailPortDirection Direction);

public sealed record Alignment
{
    public Alignment(
        StableId id,
        ImmutableArray<WorldPoint> controlPoints,
        long lengthMillimeters)
    {
        if (controlPoints.Length < 2)
        {
            throw new ArgumentException(
                "An alignment requires at least two control points.",
                nameof(controlPoints));
        }

        if (lengthMillimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMillimeters));
        }

        var calculatedLength = 0d;
        for (var index = 1; index < controlPoints.Length; index++)
        {
            if (controlPoints[index - 1] == controlPoints[index])
            {
                throw new ArgumentException(
                    "Alignment control points cannot contain a zero-length segment.",
                    nameof(controlPoints));
            }

            calculatedLength +=
                controlPoints[index - 1].DistanceMetersTo(
                    controlPoints[index]) *
                1000d;
        }

        if (checked((long)Math.Round(calculatedLength)) != lengthMillimeters)
        {
            throw new ArgumentException(
                "Alignment length must match its canonical control-point geometry.",
                nameof(lengthMillimeters));
        }

        Id = id;
        ControlPoints = controlPoints;
        LengthMillimeters = lengthMillimeters;
    }

    public StableId Id { get; }

    public ImmutableArray<WorldPoint> ControlPoints { get; }

    public long LengthMillimeters { get; }
}

public sealed record TrackEdge
{
    public TrackEdge(
        StableId id,
        StableId fromPortId,
        StableId toPortId,
        StableId alignmentId,
        int lateralOffsetMillimeters,
        int designSpeedKph)
    {
        if (fromPortId == toPortId)
        {
            throw new ArgumentException(
                "A track must connect two different ports.");
        }

        if (designSpeedKph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(designSpeedKph));
        }

        Id = id;
        FromPortId = fromPortId;
        ToPortId = toPortId;
        AlignmentId = alignmentId;
        LateralOffsetMillimeters = lateralOffsetMillimeters;
        DesignSpeedKph = designSpeedKph;
    }

    public StableId Id { get; }

    public StableId FromPortId { get; }

    public StableId ToPortId { get; }

    public StableId AlignmentId { get; }

    public int LateralOffsetMillimeters { get; }

    public int DesignSpeedKph { get; }
}

public sealed record TrackGroup
{
    public TrackGroup(
        StableId id,
        ImmutableArray<StableId> trackIds)
    {
        if (trackIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A track group must contain at least one track.",
                nameof(trackIds));
        }

        if (trackIds.Distinct().Count() != trackIds.Length)
        {
            throw new ArgumentException(
                "A track group cannot contain duplicate track IDs.",
                nameof(trackIds));
        }

        Id = id;
        TrackIds = trackIds;
    }

    public StableId Id { get; }

    public ImmutableArray<StableId> TrackIds { get; }
}

public sealed record TrackConnection(
    StableId Id,
    StableId NodeId,
    StableId FromTrackId,
    StableId ToTrackId);

public sealed class RailTopologyException : InvalidOperationException
{
    public RailTopologyException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
