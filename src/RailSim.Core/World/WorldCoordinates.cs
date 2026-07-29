namespace RailSim.Core.World;

public readonly record struct WorldPoint(
    long XMillimeters,
    long YMillimeters)
{
    public static WorldPoint Zero { get; } = new(0, 0);

    public WorldPoint OffsetMillimeters(long x, long y)
    {
        return new(
            checked(XMillimeters + x),
            checked(YMillimeters + y));
    }

    public double DistanceMetersTo(WorldPoint other)
    {
        var deltaX = (other.XMillimeters - XMillimeters) / 1000d;
        var deltaY = (other.YMillimeters - YMillimeters) / 1000d;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}

public readonly record struct WorldBounds
{
    public WorldBounds(WorldPoint minimum, WorldPoint maximum)
    {
        if (maximum.XMillimeters <= minimum.XMillimeters ||
            maximum.YMillimeters <= minimum.YMillimeters)
        {
            throw new ArgumentException(
                "World bounds maximum must be greater than minimum.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public WorldPoint Minimum { get; }

    public WorldPoint Maximum { get; }

    public long WidthMillimeters =>
        checked(Maximum.XMillimeters - Minimum.XMillimeters);

    public long HeightMillimeters =>
        checked(Maximum.YMillimeters - Minimum.YMillimeters);

    public bool Contains(WorldPoint point)
    {
        return point.XMillimeters >= Minimum.XMillimeters &&
            point.XMillimeters <= Maximum.XMillimeters &&
            point.YMillimeters >= Minimum.YMillimeters &&
            point.YMillimeters <= Maximum.YMillimeters;
    }

    public WorldBounds ExpandMillimeters(long margin)
    {
        if (margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

        return new WorldBounds(
            Minimum.OffsetMillimeters(-margin, -margin),
            Maximum.OffsetMillimeters(margin, margin));
    }
}

public readonly record struct WorldChunkId(long X, long Y)
{
    public static WorldChunkId FromPoint(
        WorldPoint point,
        int chunkSizeMeters)
    {
        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        var size = checked(chunkSizeMeters * 1000L);
        return new(
            FloorDivide(point.XMillimeters, size),
            FloorDivide(point.YMillimeters, size));
    }

    private static long FloorDivide(long value, long divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }
}
