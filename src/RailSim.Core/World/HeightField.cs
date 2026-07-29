using System.Collections.Immutable;

namespace RailSim.Core.World;

public sealed class HeightField
{
    public HeightField(
        int width,
        int height,
        IEnumerable<int> elevationMeters)
    {
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(elevationMeters);
        var values = elevationMeters.ToImmutableArray();
        if (values.Length != checked(width * height))
        {
            throw new ArgumentException(
                "Height sample count must match width times height.",
                nameof(elevationMeters));
        }

        Width = width;
        Height = height;
        ElevationMeters = values;
        ContentHash = WorldContentHasher.HashTerrain(this);
    }

    public int Width { get; }

    public int Height { get; }

    public ImmutableArray<int> ElevationMeters { get; }

    public string ContentHash { get; }

    public int this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            return ElevationMeters[(y * Width) + x];
        }
    }

    public int SampleAt(WorldPoint point, WorldBounds bounds)
    {
        if (!bounds.Contains(point))
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }

        var normalizedX =
            (point.XMillimeters - bounds.Minimum.XMillimeters) /
            (double)bounds.WidthMillimeters;
        var normalizedY =
            (point.YMillimeters - bounds.Minimum.YMillimeters) /
            (double)bounds.HeightMillimeters;
        var x = Math.Clamp(
            (int)Math.Round(normalizedX * (Width - 1)),
            0,
            Width - 1);
        var y = Math.Clamp(
            (int)Math.Round(normalizedY * (Height - 1)),
            0,
            Height - 1);
        return this[x, y];
    }
}
