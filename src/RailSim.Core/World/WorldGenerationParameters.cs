namespace RailSim.Core.World;

public enum WorldScaleProfile
{
    Regional,
    Continental,
    Custom
}

public sealed record WorldGenerationParameters
{
    public WorldGenerationParameters(
        ulong seed,
        int generatorVersion,
        WorldScaleProfile scaleProfile,
        WorldBounds bounds,
        int chunkSizeMeters,
        int heightSamplesX,
        int heightSamplesY,
        int megaCityCount,
        int largeCityCount,
        int mediumCityCount,
        int smallCityCount,
        int stationAnchorCount,
        int majorRiverCount)
    {
        if (generatorVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generatorVersion));
        }

        if (chunkSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        if (heightSamplesX < 2 || heightSamplesY < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heightSamplesX),
                "Height fields need at least two samples on each axis.");
        }

        if (megaCityCount < 0 ||
            largeCityCount < 0 ||
            mediumCityCount < 0 ||
            smallCityCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(megaCityCount),
                "City counts cannot be negative.");
        }

        var cityCount = checked(
            megaCityCount +
            largeCityCount +
            mediumCityCount +
            smallCityCount);
        if (cityCount == 0)
        {
            throw new ArgumentException("At least one city is required.");
        }

        if (stationAnchorCount <= 0 || stationAnchorCount > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(stationAnchorCount));
        }

        if (majorRiverCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(majorRiverCount));
        }

        Seed = seed;
        GeneratorVersion = generatorVersion;
        ScaleProfile = scaleProfile;
        Bounds = bounds;
        ChunkSizeMeters = chunkSizeMeters;
        HeightSamplesX = heightSamplesX;
        HeightSamplesY = heightSamplesY;
        MegaCityCount = megaCityCount;
        LargeCityCount = largeCityCount;
        MediumCityCount = mediumCityCount;
        SmallCityCount = smallCityCount;
        StationAnchorCount = stationAnchorCount;
        MajorRiverCount = majorRiverCount;
    }

    public ulong Seed { get; }

    public int GeneratorVersion { get; }

    public WorldScaleProfile ScaleProfile { get; }

    public WorldBounds Bounds { get; }

    public int ChunkSizeMeters { get; }

    public int HeightSamplesX { get; }

    public int HeightSamplesY { get; }

    public int MegaCityCount { get; }

    public int LargeCityCount { get; }

    public int MediumCityCount { get; }

    public int SmallCityCount { get; }

    public int StationAnchorCount { get; }

    public int MajorRiverCount { get; }

    public int CityCount => checked(
        MegaCityCount +
        LargeCityCount +
        MediumCityCount +
        SmallCityCount);

    public static WorldGenerationParameters CreateRegional(ulong seed)
    {
        return new(
            seed,
            generatorVersion: 1,
            WorldScaleProfile.Regional,
            new WorldBounds(
                WorldPoint.Zero,
                new WorldPoint(1_000_000_000, 800_000_000)),
            chunkSizeMeters: 64_000,
            heightSamplesX: 512,
            heightSamplesY: 384,
            megaCityCount: 2,
            largeCityCount: 8,
            mediumCityCount: 18,
            smallCityCount: 36,
            stationAnchorCount: 64,
            majorRiverCount: 5);
    }

    public static WorldGenerationParameters CreateContinental(ulong seed)
    {
        return new(
            seed,
            generatorVersion: 1,
            WorldScaleProfile.Continental,
            new WorldBounds(
                WorldPoint.Zero,
                new WorldPoint(6_000_000_000, 6_000_000_000)),
            chunkSizeMeters: 64_000,
            heightSamplesX: 2_048,
            heightSamplesY: 2_048,
            megaCityCount: 8,
            largeCityCount: 32,
            mediumCityCount: 96,
            smallCityCount: 256,
            stationAnchorCount: 512,
            majorRiverCount: 24);
    }
}
