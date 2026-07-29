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
            var maximumDelta = Math.Max(
                Math.Abs(end.XMillimeters - start.XMillimeters),
                Math.Abs(end.YMillimeters - start.YMillimeters));
            var sampleStride = Math.Max(1L, chunkSizeMillimeters / 4);
            var steps = Math.Max(
                1L,
                (maximumDelta + sampleStride - 1) / sampleStride);
            for (var step = 0L; step <= steps; step++)
            {
                var ratio = step / (double)steps;
                var point = new WorldPoint(
                    checked((long)Math.Round(
                        start.XMillimeters +
                        ((end.XMillimeters - start.XMillimeters) * ratio))),
                    checked((long)Math.Round(
                        start.YMillimeters +
                        ((end.YMillimeters - start.YMillimeters) * ratio))));
                AddPadded(
                    chunks,
                    point,
                    chunkSizeMeters,
                    paddingMillimeters);
            }
        }

        return chunks
            .OrderBy(chunk => chunk.X)
            .ThenBy(chunk => chunk.Y)
            .ToImmutableArray();
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
