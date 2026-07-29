using System.Collections.Immutable;

namespace RailSim.Core.World;

public interface IWorldGenerator
{
    GeneratedWorld Generate(WorldGenerationParameters parameters);
}

public sealed class ProceduralWorldGenerator :
    IWorldGenerator,
    ITerrainGenerator,
    ICityPlacementStrategy,
    INameGenerator
{
    private static readonly string[] NamePrefixes =
    [
        "安", "北", "滨", "昌", "川", "德", "东", "丰", "高", "广",
        "海", "河", "湖", "华", "江", "金", "景", "临", "龙", "南",
        "宁", "平", "青", "清", "瑞", "山", "上", "太", "新", "兴",
        "阳", "永", "云", "镇"
    ];

    private static readonly string[] NameSuffixes =
    [
        "城", "川", "港", "关", "海", "河", "湖", "江", "岭", "陵",
        "宁", "平", "山", "湾", "溪", "县", "阳", "原", "州", "庄"
    ];

    private readonly ITerrainGenerator _terrainGenerator;
    private readonly ICityPlacementStrategy _cityPlacement;
    private readonly INameGenerator _nameGenerator;

    public ProceduralWorldGenerator()
    {
        _terrainGenerator = this;
        _cityPlacement = this;
        _nameGenerator = this;
    }

    public ProceduralWorldGenerator(
        ITerrainGenerator terrainGenerator,
        ICityPlacementStrategy cityPlacement,
        INameGenerator nameGenerator)
    {
        _terrainGenerator = terrainGenerator ??
            throw new ArgumentNullException(nameof(terrainGenerator));
        _cityPlacement = cityPlacement ??
            throw new ArgumentNullException(nameof(cityPlacement));
        _nameGenerator = nameGenerator ??
            throw new ArgumentNullException(nameof(nameGenerator));
    }

    public GeneratedWorld Generate(WorldGenerationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var terrain = _terrainGenerator.GenerateTerrain(
            parameters,
            SeedDeriver.Derive(parameters.Seed, "terrain"));
        var rivers = GenerateRivers(parameters, terrain);
        var cities = _cityPlacement
            .PlaceCities(
                parameters,
                terrain,
                rivers,
                SeedDeriver.Derive(parameters.Seed, "cities"),
                _nameGenerator)
            .ToImmutableDictionary(city => city.Id);
        var stations = GenerateStations(parameters, cities);
        var withoutHash = new GeneratedWorld(
            parameters,
            terrain,
            rivers,
            cities,
            stations,
            string.Empty);
        var generated = new GeneratedWorld(
            parameters,
            terrain,
            rivers,
            cities,
            stations,
            WorldContentHasher.HashGeneratedWorld(withoutHash));
        WorldStateValidator.Validate(
            new WorldState(0, generated, Rail.RailTopology.Empty));
        return generated;
    }

    public HeightField GenerateTerrain(
        WorldGenerationParameters parameters,
        ulong terrainSeed)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var elevations = new int[
            checked(parameters.HeightSamplesX * parameters.HeightSamplesY)];
        for (var y = 0; y < parameters.HeightSamplesY; y++)
        {
            for (var x = 0; x < parameters.HeightSamplesX; x++)
            {
                var broad = ValueNoise(x, y, 96, terrainSeed);
                var regional = ValueNoise(
                    x,
                    y,
                    48,
                    terrainSeed ^ 0xA0761D6478BD642FUL);
                var local = ValueNoise(
                    x,
                    y,
                    20,
                    terrainSeed ^ 0xE7037ED1A0B428DBUL);
                var detail = ValueNoise(
                    x,
                    y,
                    8,
                    terrainSeed ^ 0x8EBC6AF09C88C6E3UL);
                var westernRise =
                    ((parameters.HeightSamplesX - 1 - x) * 520) /
                    (parameters.HeightSamplesX - 1);
                var elevation =
                    160 +
                    westernRise +
                    ((broad * 8) / 20) +
                    ((regional * 5) / 20) +
                    ((local * 3) / 20) +
                    ((detail * 2) / 20);
                elevations[(y * parameters.HeightSamplesX) + x] =
                    Math.Clamp(elevation, 0, 2_800);
            }
        }

        return new HeightField(
            parameters.HeightSamplesX,
            parameters.HeightSamplesY,
            elevations);
    }

    private static int ValueNoise(
        int x,
        int y,
        int cellSize,
        ulong seed)
    {
        var left = x / cellSize;
        var top = y / cellSize;
        var offsetX = x % cellSize;
        var offsetY = y % cellSize;
        var topLeft = LatticeNoise(left, top, seed);
        var topRight = LatticeNoise(left + 1, top, seed);
        var bottomLeft = LatticeNoise(left, top + 1, seed);
        var bottomRight = LatticeNoise(left + 1, top + 1, seed);
        var topValue =
            ((topLeft * (cellSize - offsetX)) + (topRight * offsetX)) /
            cellSize;
        var bottomValue =
            ((bottomLeft * (cellSize - offsetX)) +
             (bottomRight * offsetX)) /
            cellSize;
        return
            ((topValue * (cellSize - offsetY)) + (bottomValue * offsetY)) /
            cellSize;
    }

    private static int LatticeNoise(int x, int y, ulong seed)
    {
        var value =
            seed ^
            (unchecked((ulong)x) * 0x9E3779B97F4A7C15UL) ^
            (unchecked((ulong)y) * 0xBF58476D1CE4E5B9UL);
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (int)(value % 2_049UL) - 1_024;
    }

    private static ImmutableArray<RiverPath> GenerateRivers(
        WorldGenerationParameters parameters,
        HeightField terrain)
    {
        var random = new DeterministicRandom(
            SeedDeriver.Derive(parameters.Seed, "rivers"));
        var rivers = ImmutableArray.CreateBuilder<RiverPath>(
            parameters.MajorRiverCount);
        var bounds = parameters.Bounds;
        for (var riverIndex = 0;
             riverIndex < parameters.MajorRiverCount;
             riverIndex++)
        {
            const int pointCount = 32;
            var points = ImmutableArray.CreateBuilder<WorldPoint>(pointCount);
            var x = bounds.Minimum.XMillimeters;
            var y = SelectHighSourceY(
                random,
                terrain,
                bounds,
                x);
            var searchStep = Math.Max(
                1L,
                bounds.HeightMillimeters / 36);
            for (var pointIndex = 0;
                 pointIndex < pointCount;
                 pointIndex++)
            {
                x = bounds.Minimum.XMillimeters +
                    ((bounds.WidthMillimeters * pointIndex) /
                     (pointCount - 1));
                if (pointIndex > 0)
                {
                    y = SelectLowestCandidateY(
                        random,
                        terrain,
                        bounds,
                        x,
                        y,
                        searchStep);
                }

                points.Add(new WorldPoint(x, y));
            }

            rivers.Add(new RiverPath(
                new StableId($"river:{riverIndex:D3}"),
                riverIndex == 0
                    ? RiverWidthClass.Major
                    : RiverWidthClass.Regional,
                points.MoveToImmutable()));
        }

        return rivers.MoveToImmutable();
    }

    private static long SelectHighSourceY(
        DeterministicRandom random,
        HeightField terrain,
        WorldBounds bounds,
        long x)
    {
        var bestY = bounds.Minimum.YMillimeters;
        var bestElevation = int.MinValue;
        for (var candidateIndex = 0; candidateIndex < 16; candidateIndex++)
        {
            var candidateY = random.NextInt64(
                bounds.Minimum.YMillimeters,
                bounds.Maximum.YMillimeters + 1);
            var elevation = terrain.SampleAt(
                new WorldPoint(x, candidateY),
                bounds);
            if (elevation > bestElevation)
            {
                bestElevation = elevation;
                bestY = candidateY;
            }
        }

        return bestY;
    }

    private static long SelectLowestCandidateY(
        DeterministicRandom random,
        HeightField terrain,
        WorldBounds bounds,
        long x,
        long currentY,
        long searchStep)
    {
        var bestY = currentY;
        var bestScore = long.MaxValue;
        for (var offset = -3; offset <= 3; offset++)
        {
            var jitter = random.NextInt64(
                -(searchStep / 4),
                (searchStep / 4) + 1);
            var candidateY = Math.Clamp(
                currentY + (offset * searchStep) + jitter,
                bounds.Minimum.YMillimeters,
                bounds.Maximum.YMillimeters);
            var elevation = terrain.SampleAt(
                new WorldPoint(x, candidateY),
                bounds);
            var score = (elevation * 1_000L) +
                random.NextInt32(1_000);
            if (score < bestScore)
            {
                bestScore = score;
                bestY = candidateY;
            }
        }

        return bestY;
    }

    public ImmutableArray<CitySite> PlaceCities(
        WorldGenerationParameters parameters,
        HeightField terrain,
        ImmutableArray<RiverPath> rivers,
        ulong placementSeed,
        INameGenerator nameGenerator)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(nameGenerator);
        if (rivers.IsDefault)
        {
            throw new ArgumentException(
                "River collection must be initialized.",
                nameof(rivers));
        }

        var random = new DeterministicRandom(placementSeed);
        var names = nameGenerator.GenerateCityNames(
            parameters.CityCount,
            SeedDeriver.Derive(parameters.Seed, "names"));
        var tiers = CreateTierSequence(parameters);
        var cities = ImmutableArray.CreateBuilder<CitySite>(
            parameters.CityCount);
        var positions = new List<WorldPoint>(parameters.CityCount);

        for (var index = 0; index < tiers.Length; index++)
        {
            var tier = tiers[index];
            var minimumSpacing = MinimumSpacingMeters(tier);
            var candidate = WorldPoint.Zero;
            var accepted = false;
            for (var attempt = 0; attempt < 2_000; attempt++)
            {
                candidate = NextCityCandidate(
                    random,
                    parameters.Bounds,
                    rivers,
                    tier);
                var spacingScale = Math.Max(0.25, 1d - (attempt / 2_000d));
                var requiredSpacing = minimumSpacing * spacingScale;
                var suitableTerrain =
                    terrain.SampleAt(candidate, parameters.Bounds) <= 1_250;
                var spaced = positions.All(
                    position =>
                        position.DistanceMetersTo(candidate) >= requiredSpacing);
                if (suitableTerrain && spaced)
                {
                    accepted = true;
                    break;
                }
            }

            if (!accepted)
            {
                candidate = NextCityCandidate(
                    random,
                    parameters.Bounds,
                    rivers,
                    tier);
            }

            var id = new StableId($"city:{index:D3}");
            var city = new CitySite(
                id,
                names[index],
                tier,
                candidate,
                PopulationWeight(tier));
            cities.Add(city);
            positions.Add(candidate);
        }

        return cities.MoveToImmutable();
    }

    private static WorldPoint NextCityCandidate(
        DeterministicRandom random,
        WorldBounds bounds,
        ImmutableArray<RiverPath> rivers,
        CityTier tier)
    {
        if (tier is not (CityTier.Mega or CityTier.Large) ||
            rivers.IsEmpty)
        {
            return NextInteriorPoint(random, bounds);
        }

        var river = rivers[random.NextInt32(rivers.Length)];
        var anchor = river.Points[random.NextInt32(river.Points.Length)];
        const long maximumOffsetMillimeters = 140_000_000;
        var marginX = Math.Max(1L, bounds.WidthMillimeters / 20);
        var marginY = Math.Max(1L, bounds.HeightMillimeters / 20);
        return new WorldPoint(
            Math.Clamp(
                anchor.XMillimeters +
                random.NextInt64(
                    -maximumOffsetMillimeters,
                    maximumOffsetMillimeters + 1),
                bounds.Minimum.XMillimeters + marginX,
                bounds.Maximum.XMillimeters - marginX),
            Math.Clamp(
                anchor.YMillimeters +
                random.NextInt64(
                    -maximumOffsetMillimeters,
                    maximumOffsetMillimeters + 1),
                bounds.Minimum.YMillimeters + marginY,
                bounds.Maximum.YMillimeters - marginY));
    }

    private static ImmutableDictionary<StableId, StationAnchor> GenerateStations(
        WorldGenerationParameters parameters,
        ImmutableDictionary<StableId, CitySite> cities)
    {
        var stations =
            ImmutableDictionary.CreateBuilder<StableId, StationAnchor>();
        var orderedCities = cities.Values.OrderBy(city => city.Id).ToArray();
        for (var index = 0;
             index < parameters.StationAnchorCount;
             index++)
        {
            var city = orderedCities[index % orderedCities.Length];
            var suffix = index < orderedCities.Length
                ? "站"
                : $"{(index / orderedCities.Length) + 1}站";
            var station = new StationAnchor(
                new StableId($"station:{index:D3}"),
                city.Name + suffix,
                ToStationTier(city.Tier),
                city.Position,
                city.Id,
                detailZoneId: null);
            stations.Add(station.Id, station);
        }

        return stations.ToImmutable();
    }

    private static ImmutableArray<CityTier> CreateTierSequence(
        WorldGenerationParameters parameters)
    {
        var tiers = ImmutableArray.CreateBuilder<CityTier>(
            parameters.CityCount);
        AddTier(tiers, CityTier.Mega, parameters.MegaCityCount);
        AddTier(tiers, CityTier.Large, parameters.LargeCityCount);
        AddTier(tiers, CityTier.Medium, parameters.MediumCityCount);
        AddTier(tiers, CityTier.Small, parameters.SmallCityCount);
        return tiers.MoveToImmutable();
    }

    private static void AddTier(
        ImmutableArray<CityTier>.Builder tiers,
        CityTier tier,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            tiers.Add(tier);
        }
    }

    private static WorldPoint NextInteriorPoint(
        DeterministicRandom random,
        WorldBounds bounds)
    {
        var marginX = Math.Max(1L, bounds.WidthMillimeters / 20);
        var marginY = Math.Max(1L, bounds.HeightMillimeters / 20);
        return new WorldPoint(
            random.NextInt64(
                bounds.Minimum.XMillimeters + marginX,
                bounds.Maximum.XMillimeters - marginX),
            random.NextInt64(
                bounds.Minimum.YMillimeters + marginY,
                bounds.Maximum.YMillimeters - marginY));
    }

    private static string NextUniqueName(
        DeterministicRandom random,
        HashSet<string> usedNames)
    {
        var combinationCount = NamePrefixes.Length * NameSuffixes.Length;
        var initial = random.NextInt32(combinationCount);
        for (var offset = 0; offset < combinationCount; offset++)
        {
            var value = (initial + offset) % combinationCount;
            var name =
                NamePrefixes[value / NameSuffixes.Length] +
                NameSuffixes[value % NameSuffixes.Length];
            if (usedNames.Add(name))
            {
                return name;
            }
        }

        throw new InvalidOperationException("The configured name set is exhausted.");
    }

    public ImmutableArray<string> GenerateCityNames(
        int count,
        ulong nameSeed)
    {
        var combinationCount = checked(
            NamePrefixes.Length * NameSuffixes.Length);
        if (count <= 0 || count > combinationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var random = new DeterministicRandom(nameSeed);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var names = ImmutableArray.CreateBuilder<string>(count);
        for (var index = 0; index < count; index++)
        {
            names.Add(NextUniqueName(random, usedNames));
        }

        return names.MoveToImmutable();
    }

    private static double MinimumSpacingMeters(CityTier tier)
    {
        return tier switch
        {
            CityTier.Mega => 180_000,
            CityTier.Large => 95_000,
            CityTier.Medium => 48_000,
            CityTier.Small => 22_000,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }

    private static int PopulationWeight(CityTier tier)
    {
        return tier switch
        {
            CityTier.Mega => 100,
            CityTier.Large => 55,
            CityTier.Medium => 24,
            CityTier.Small => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }

    private static StationTier ToStationTier(CityTier tier)
    {
        return tier switch
        {
            CityTier.Mega => StationTier.Mega,
            CityTier.Large => StationTier.Large,
            CityTier.Medium => StationTier.Medium,
            CityTier.Small => StationTier.Small,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }
}
