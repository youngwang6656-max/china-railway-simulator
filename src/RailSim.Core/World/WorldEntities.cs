namespace RailSim.Core.World;

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
