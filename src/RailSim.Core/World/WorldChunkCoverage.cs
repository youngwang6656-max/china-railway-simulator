using System.Collections.Immutable;

namespace RailSim.Core.World;

public static class WorldChunkCoverage
{
    public static ImmutableArray<WorldChunkId> TracePolyline(
        ImmutableArray<WorldPoint> points,
        int chunkSizeMeters,
        long paddingMillimeters = 0)
    {
        if (points.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Polyline points must be initialized and non-empty.",
                nameof(points));
        }

        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        if (paddingMillimeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingMillimeters));
        }

        var chunks = new HashSet<WorldChunkId>();
        if (points.Length == 1)
        {
            AddPadded(chunks, points[0], chunkSizeMeters, paddingMillimeters);
        }

        var chunkSizeMillimeters = checked(chunkSizeMeters * 1000L);
        for (var index = 1; index < points.Length; index++)
        {
            var start = points[index - 1];
            var end = points[index];
            var minimum = new WorldPoint(
                checked(
                    Math.Min(start.XMillimeters, end.XMillimeters) -
                    paddingMillimeters),
                checked(
                    Math.Min(start.YMillimeters, end.YMillimeters) -
                    paddingMillimeters));
            var maximum = new WorldPoint(
                checked(
                    Math.Max(start.XMillimeters, end.XMillimeters) +
                    paddingMillimeters),
                checked(
                    Math.Max(start.YMillimeters, end.YMillimeters) +
                    paddingMillimeters));
            var minimumChunk = WorldChunkId.FromPoint(
                minimum,
                chunkSizeMeters);
            var maximumChunk = WorldChunkId.FromPoint(
                maximum,
                chunkSizeMeters);
            for (var y = minimumChunk.Y; y <= maximumChunk.Y; y++)
            {
                for (var x = minimumChunk.X; x <= maximumChunk.X; x++)
                {
                    if (IntersectsExpandedChunk(
                            start,
                            end,
                            x,
                            y,
                            chunkSizeMillimeters,
                            paddingMillimeters))
                    {
                        chunks.Add(new WorldChunkId(x, y));
                    }
                }
            }
        }

        return chunks
            .OrderBy(chunk => chunk.X)
            .ThenBy(chunk => chunk.Y)
            .ToImmutableArray();
    }

    private static bool IntersectsExpandedChunk(
        WorldPoint start,
        WorldPoint end,
        long chunkX,
        long chunkY,
        long chunkSizeMillimeters,
        long paddingMillimeters)
    {
        var minimumX = checked(
            (chunkX * chunkSizeMillimeters) - paddingMillimeters);
        var maximumX = checked(
            ((chunkX + 1) * chunkSizeMillimeters) +
            paddingMillimeters);
        var minimumY = checked(
            (chunkY * chunkSizeMillimeters) - paddingMillimeters);
        var maximumY = checked(
            ((chunkY + 1) * chunkSizeMillimeters) +
            paddingMillimeters);
        var deltaX =
            (double)end.XMillimeters - start.XMillimeters;
        var deltaY =
            (double)end.YMillimeters - start.YMillimeters;
        var minimumT = 0d;
        var maximumT = 1d;
        return ClipAxis(
                start.XMillimeters,
                deltaX,
                minimumX,
                maximumX,
                ref minimumT,
                ref maximumT) &&
            ClipAxis(
                start.YMillimeters,
                deltaY,
                minimumY,
                maximumY,
                ref minimumT,
                ref maximumT);
    }

    private static bool ClipAxis(
        long start,
        double delta,
        long minimum,
        long maximum,
        ref double minimumT,
        ref double maximumT)
    {
        if (delta == 0)
        {
            return start >= minimum && start <= maximum;
        }

        var first = (minimum - start) / delta;
        var second = (maximum - start) / delta;
        if (first > second)
        {
            (first, second) = (second, first);
        }

        minimumT = Math.Max(minimumT, first);
        maximumT = Math.Min(maximumT, second);
        return minimumT <= maximumT;
    }

    private static void AddPadded(
        ISet<WorldChunkId> chunks,
        WorldPoint point,
        int chunkSizeMeters,
        long paddingMillimeters)
    {
        var minimum = point.OffsetMillimeters(
            -paddingMillimeters,
            -paddingMillimeters);
        var maximum = point.OffsetMillimeters(
            paddingMillimeters,
            paddingMillimeters);
        var minimumChunk = WorldChunkId.FromPoint(
            minimum,
            chunkSizeMeters);
        var maximumChunk = WorldChunkId.FromPoint(
            maximum,
            chunkSizeMeters);
        for (var y = minimumChunk.Y; y <= maximumChunk.Y; y++)
        {
            for (var x = minimumChunk.X; x <= maximumChunk.X; x++)
            {
                chunks.Add(new WorldChunkId(x, y));
            }
        }
    }
}
