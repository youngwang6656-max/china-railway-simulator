using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.Core.Persistence;

public sealed class JsonWorldRepository : IWorldRepository
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public void Save(string path, WorldState world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(world);
        WorldStateValidator.Validate(world);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
            throw new ArgumentException(
                "A world path must include a directory.",
                nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var document = ToDocument(world);
            var json = JsonSerializer.Serialize(document, JsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(
                           stream,
                           new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                           bufferSize: 16 * 1024,
                           leaveOpen: true))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            var verified = Load(temporaryPath);
            if (!string.Equals(
                    WorldContentHasher.HashState(world),
                    WorldContentHasher.HashState(verified),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Temporary world verification produced a different content hash.");
            }

            if (File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, null);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public WorldState Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        WorldDocumentDto document;
        try
        {
            var json = File.ReadAllText(Path.GetFullPath(path));
            document = JsonSerializer.Deserialize<WorldDocumentDto>(
                    json,
                    JsonOptions) ??
                throw new InvalidDataException("World document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "World document contains invalid JSON.",
                exception);
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported world schema version {document.SchemaVersion}.");
        }

        try
        {
            return FromDocument(document);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidOperationException or
                  OverflowException or
                  NullReferenceException)
        {
            throw new InvalidDataException(
                "World document failed structural validation.",
                exception);
        }
    }

    private static WorldDocumentDto ToDocument(WorldState world)
    {
        var generated = world.BaseWorld;
        var topology = world.RailTopology;
        return new WorldDocumentDto(
            CurrentSchemaVersion,
            world.Revision,
            generated.ContentHash,
            WorldContentHasher.HashState(world),
            ToDto(generated.Parameters),
            new TerrainDto(
                generated.Terrain.Width,
                generated.Terrain.Height,
                generated.Terrain.ElevationMeters.ToArray()),
            generated.Rivers
                .OrderBy(river => river.Id)
                .Select(
                    river => new RiverDto(
                        river.Id.Value,
                        (int)river.WidthClass,
                        river.Points.Select(ToDto).ToArray()))
                .ToArray(),
            generated.Cities.Values
                .OrderBy(city => city.Id)
                .Select(
                    city => new CityDto(
                        city.Id.Value,
                        city.Name,
                        (int)city.Tier,
                        ToDto(city.Position),
                        city.PopulationWeight))
                .ToArray(),
            generated.Stations.Values
                .OrderBy(station => station.Id)
                .Select(
                    station => new StationDto(
                        station.Id.Value,
                        station.Name,
                        (int)station.Tier,
                        ToDto(station.Position),
                        station.CityId?.Value,
                        station.DetailZoneId?.Value))
                .ToArray(),
            new TopologyDto(
                topology.Nodes.Values
                    .OrderBy(node => node.Id)
                    .Select(
                        node => new NodeDto(
                            node.Id.Value,
                            (int)node.Kind,
                            ToDto(node.Position),
                            node.StationId?.Value))
                    .ToArray(),
                topology.Ports.Values
                    .OrderBy(port => port.Id)
                    .Select(
                        port => new PortDto(
                            port.Id.Value,
                            port.NodeId.Value,
                            (int)port.Direction))
                    .ToArray(),
                topology.Alignments.Values
                    .OrderBy(alignment => alignment.Id)
                    .Select(
                        alignment => new AlignmentDto(
                            alignment.Id.Value,
                            alignment.ControlPoints.Select(ToDto).ToArray(),
                            alignment.LengthMillimeters))
                    .ToArray(),
                topology.Tracks.Values
                    .OrderBy(track => track.Id)
                    .Select(
                        track => new TrackDto(
                            track.Id.Value,
                            track.FromPortId.Value,
                            track.ToPortId.Value,
                            track.AlignmentId.Value,
                            track.LateralOffsetMillimeters,
                            track.DesignSpeedKph))
                    .ToArray(),
                topology.TrackGroups.Values
                    .OrderBy(group => group.Id)
                    .Select(
                        group => new TrackGroupDto(
                            group.Id.Value,
                            group.TrackIds
                                .OrderBy(trackId => trackId)
                                .Select(trackId => trackId.Value)
                                .ToArray()))
                    .ToArray(),
                topology.Connections.Values
                    .OrderBy(connection => connection.Id)
                    .Select(
                        connection => new ConnectionDto(
                            connection.Id.Value,
                            connection.NodeId.Value,
                            connection.FromTrackId.Value,
                            connection.ToTrackId.Value))
                    .ToArray()));
    }

    private static WorldState FromDocument(WorldDocumentDto document)
    {
        var parameters = FromDto(document.Parameters);
        var terrain = new HeightField(
            document.Terrain.Width,
            document.Terrain.Height,
            document.Terrain.ElevationMeters);
        var rivers = document.Rivers
            .Select(
                river => new RiverPath(
                    Id(river.Id),
                    ParseEnum<RiverWidthClass>(river.WidthClass),
                    river.Points.Select(FromDto).ToImmutableArray()))
            .ToImmutableArray();
        var cities = document.Cities
            .Select(
                city => new CitySite(
                    Id(city.Id),
                    city.Name,
                    ParseEnum<CityTier>(city.Tier),
                    FromDto(city.Position),
                    city.PopulationWeight))
            .ToImmutableDictionary(city => city.Id);
        var stations = document.Stations
            .Select(
                station => new StationAnchor(
                    Id(station.Id),
                    station.Name,
                    ParseEnum<StationTier>(station.Tier),
                    FromDto(station.Position),
                    OptionalId(station.CityId),
                    OptionalId(station.DetailZoneId)))
            .ToImmutableDictionary(station => station.Id);
        var generated = new GeneratedWorld(
            parameters,
            terrain,
            rivers,
            cities,
            stations,
            document.GeneratedContentHash);
        var generatedHash = WorldContentHasher.HashGeneratedWorld(generated);
        if (!string.Equals(
                document.GeneratedContentHash,
                generatedHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Generated world content hash does not match the document.");
        }

        var topology = FromDto(document.Topology);
        var world = new WorldState(document.Revision, generated, topology);
        var stateHash = WorldContentHasher.HashState(world);
        if (!string.Equals(
                document.StateContentHash,
                stateHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "World state content hash does not match the document.");
        }

        WorldStateValidator.Validate(world);
        return world;
    }

    private static RailTopology FromDto(TopologyDto value)
    {
        var topology = RailTopology.Empty;
        foreach (var node in value.Nodes)
        {
            topology = topology.AddNode(new RailNode(
                Id(node.Id),
                ParseEnum<RailNodeKind>(node.Kind),
                FromDto(node.Position),
                OptionalId(node.StationId)));
        }

        foreach (var port in value.Ports)
        {
            topology = topology.AddPort(new RailPort(
                Id(port.Id),
                Id(port.NodeId),
                ParseEnum<RailPortDirection>(port.Direction)));
        }

        foreach (var alignment in value.Alignments)
        {
            topology = topology.AddAlignment(new Alignment(
                Id(alignment.Id),
                alignment.ControlPoints
                    .Select(FromDto)
                    .ToImmutableArray(),
                alignment.LengthMillimeters));
        }

        foreach (var track in value.Tracks)
        {
            topology = topology.AddTrack(new TrackEdge(
                Id(track.Id),
                Id(track.FromPortId),
                Id(track.ToPortId),
                Id(track.AlignmentId),
                track.LateralOffsetMillimeters,
                track.DesignSpeedKph));
        }

        foreach (var group in value.TrackGroups)
        {
            topology = topology.AddTrackGroup(new TrackGroup(
                Id(group.Id),
                group.TrackIds.Select(Id).ToImmutableArray()));
        }

        foreach (var connection in value.Connections)
        {
            topology = topology.AddConnection(new TrackConnection(
                Id(connection.Id),
                Id(connection.NodeId),
                Id(connection.FromTrackId),
                Id(connection.ToTrackId)));
        }

        return topology;
    }

    private static ParametersDto ToDto(
        WorldGenerationParameters value)
    {
        return new ParametersDto(
            value.Seed,
            value.GeneratorVersion,
            (int)value.ScaleProfile,
            ToDto(value.Bounds.Minimum),
            ToDto(value.Bounds.Maximum),
            value.ChunkSizeMeters,
            value.HeightSamplesX,
            value.HeightSamplesY,
            value.MegaCityCount,
            value.LargeCityCount,
            value.MediumCityCount,
            value.SmallCityCount,
            value.StationAnchorCount,
            value.MajorRiverCount);
    }

    private static WorldGenerationParameters FromDto(ParametersDto value)
    {
        return new WorldGenerationParameters(
            value.Seed,
            value.GeneratorVersion,
            ParseEnum<WorldScaleProfile>(value.ScaleProfile),
            new WorldBounds(
                FromDto(value.Minimum),
                FromDto(value.Maximum)),
            value.ChunkSizeMeters,
            value.HeightSamplesX,
            value.HeightSamplesY,
            value.MegaCityCount,
            value.LargeCityCount,
            value.MediumCityCount,
            value.SmallCityCount,
            value.StationAnchorCount,
            value.MajorRiverCount);
    }

    private static PointDto ToDto(WorldPoint value)
    {
        return new PointDto(value.XMillimeters, value.YMillimeters);
    }

    private static WorldPoint FromDto(PointDto value)
    {
        return new WorldPoint(value.XMillimeters, value.YMillimeters);
    }

    private static TEnum ParseEnum<TEnum>(int value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw new InvalidDataException(
                $"Value {value} is invalid for {typeof(TEnum).Name}.");
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static StableId Id(string value)
    {
        return new StableId(value);
    }

    private static StableId? OptionalId(string? value)
    {
        return value is null ? null : Id(value);
    }

    private sealed record WorldDocumentDto(
        int SchemaVersion,
        long Revision,
        string GeneratedContentHash,
        string StateContentHash,
        ParametersDto Parameters,
        TerrainDto Terrain,
        RiverDto[] Rivers,
        CityDto[] Cities,
        StationDto[] Stations,
        TopologyDto Topology);

    private sealed record ParametersDto(
        ulong Seed,
        int GeneratorVersion,
        int ScaleProfile,
        PointDto Minimum,
        PointDto Maximum,
        int ChunkSizeMeters,
        int HeightSamplesX,
        int HeightSamplesY,
        int MegaCityCount,
        int LargeCityCount,
        int MediumCityCount,
        int SmallCityCount,
        int StationAnchorCount,
        int MajorRiverCount);

    private sealed record TerrainDto(
        int Width,
        int Height,
        int[] ElevationMeters);

    private sealed record RiverDto(
        string Id,
        int WidthClass,
        PointDto[] Points);

    private sealed record CityDto(
        string Id,
        string Name,
        int Tier,
        PointDto Position,
        int PopulationWeight);

    private sealed record StationDto(
        string Id,
        string Name,
        int Tier,
        PointDto Position,
        string? CityId,
        string? DetailZoneId);

    private sealed record TopologyDto(
        NodeDto[] Nodes,
        PortDto[] Ports,
        AlignmentDto[] Alignments,
        TrackDto[] Tracks,
        TrackGroupDto[] TrackGroups,
        ConnectionDto[] Connections);

    private sealed record NodeDto(
        string Id,
        int Kind,
        PointDto Position,
        string? StationId);

    private sealed record PortDto(
        string Id,
        string NodeId,
        int Direction);

    private sealed record AlignmentDto(
        string Id,
        PointDto[] ControlPoints,
        long LengthMillimeters);

    private sealed record TrackDto(
        string Id,
        string FromPortId,
        string ToPortId,
        string AlignmentId,
        int LateralOffsetMillimeters,
        int DesignSpeedKph);

    private sealed record TrackGroupDto(
        string Id,
        string[] TrackIds);

    private sealed record ConnectionDto(
        string Id,
        string NodeId,
        string FromTrackId,
        string ToTrackId);

    private readonly record struct PointDto(
        long XMillimeters,
        long YMillimeters);
}
