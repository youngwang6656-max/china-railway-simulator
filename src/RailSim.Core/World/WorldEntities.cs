namespace RailSim.Core.World;

using System.Collections.Immutable;

public enum CityTier
{
    Mega,
    Large,
    Medium,
    Small
}

public enum StationTier
{
    Mega,
    Large,
    Medium,
    Small
}

public sealed record CitySite
{
    public CitySite(
        StableId id,
        string name,
        CityTier tier,
        WorldPoint position,
        int populationWeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (populationWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(populationWeight));
        }

        Id = id;
        Name = name;
        Tier = tier;
        Position = position;
        PopulationWeight = populationWeight;
    }

    public StableId Id { get; }

    public string Name { get; }

    public CityTier Tier { get; }

    public WorldPoint Position { get; }

    public int PopulationWeight { get; }
}

public sealed record StationAnchor
{
    public StationAnchor(
        StableId id,
        string name,
        StationTier tier,
        WorldPoint position,
        StableId? cityId,
        StableId? detailZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Tier = tier;
        Position = position;
        CityId = cityId;
        DetailZoneId = detailZoneId;
    }

    public StableId Id { get; }

    public string Name { get; }

    public StationTier Tier { get; }

    public WorldPoint Position { get; }

    public StableId? CityId { get; }

    public StableId? DetailZoneId { get; }
}

public enum RiverWidthClass
{
    Regional,
    Major
}

public sealed record RiverPath
{
    public RiverPath(
        StableId id,
        RiverWidthClass widthClass,
        ImmutableArray<WorldPoint> points)
    {
        if (points.Length < 2)
        {
            throw new ArgumentException(
                "A river path requires at least two points.",
                nameof(points));
        }

        Id = id;
        WidthClass = widthClass;
        Points = points;
    }

    public StableId Id { get; }

    public RiverWidthClass WidthClass { get; }

    public ImmutableArray<WorldPoint> Points { get; }
}

public sealed class GeneratedWorld
{
    public GeneratedWorld(
        WorldGenerationParameters parameters,
        HeightField terrain,
        ImmutableArray<RiverPath> rivers,
        ImmutableDictionary<StableId, CitySite> cities,
        ImmutableDictionary<StableId, StationAnchor> stations,
        string contentHash)
    {
        Parameters = parameters ??
            throw new ArgumentNullException(nameof(parameters));
        Terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        if (rivers.IsDefault)
        {
            throw new ArgumentException(
                "River collection must be initialized.",
                nameof(rivers));
        }

        Rivers = rivers;
        Cities = cities ??
            throw new ArgumentNullException(nameof(cities));
        Stations = stations ??
            throw new ArgumentNullException(nameof(stations));
        ContentHash = contentHash ??
            throw new ArgumentNullException(nameof(contentHash));
        RiversById = rivers.ToImmutableDictionary(river => river.Id);
        RegionIndex = WorldRegionIndex.Build(
            parameters,
            rivers,
            cities.Values,
            stations.Values);
    }

    public WorldGenerationParameters Parameters { get; }

    public HeightField Terrain { get; }

    public ImmutableArray<RiverPath> Rivers { get; }

    public ImmutableDictionary<StableId, RiverPath> RiversById { get; }

    public ImmutableDictionary<StableId, CitySite> Cities { get; }

    public ImmutableDictionary<StableId, StationAnchor> Stations { get; }

    public string ContentHash { get; }

    public WorldRegionIndex RegionIndex { get; }
}
