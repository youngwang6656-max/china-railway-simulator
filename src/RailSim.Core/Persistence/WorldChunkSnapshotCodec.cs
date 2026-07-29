using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using RailSim.Core.Projection;
using RailSim.Core.World;

namespace RailSim.Core.Persistence;

public sealed record WorldChunkManifest(
    WorldChunkId Id,
    ImmutableArray<StableId> RiverIds,
    ImmutableArray<StableId> CityIds,
    ImmutableArray<StableId> StationIds,
    ImmutableArray<StableId> RailNodeIds,
    ImmutableArray<StableId> RailPortIds,
    ImmutableArray<StableId> TrackIds,
    ImmutableArray<StableId> TrackGroupIds,
    ImmutableArray<StableId> ConnectionIds);

public sealed class WorldChunkSnapshotCodec
{
    private const int SchemaVersion = 1;

    public WorldChunkSnapshot Encode(
        WorldState world,
        WorldChunkId id)
    {
        ArgumentNullException.ThrowIfNull(world);
        var geographic = world.BaseWorld.RegionIndex.Chunks.TryGetValue(
            id,
            out var contents)
            ? contents
            : new WorldChunkContents(
                id,
                ImmutableHashSet<StableId>.Empty,
                ImmutableHashSet<StableId>.Empty,
                ImmutableHashSet<StableId>.Empty);
        var bounds = ChunkBounds(
            id,
            world.BaseWorld.Parameters.ChunkSizeMeters);
        var rail = RailSpatialIndex
            .Build(
                world.RailTopology,
                world.BaseWorld.Parameters.ChunkSizeMeters)
            .Query(bounds, new GridWorldRegionProvider());
        var manifest = new ManifestDto
        {
            X = id.X,
            Y = id.Y,
            RiverIds = Values(geographic.RiverIds),
            CityIds = Values(geographic.CityIds),
            StationIds = Values(geographic.StationIds),
            RailNodeIds = Values(rail.NodeIds),
            RailPortIds = Values(rail.PortIds),
            TrackIds = Values(rail.TrackIds),
            TrackGroupIds = Values(rail.TrackGroupIds),
            ConnectionIds = Values(rail.ConnectionIds)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest);
        return new WorldChunkSnapshot(
            id,
            SchemaVersion,
            Convert.ToHexString(SHA256.HashData(payload)),
            payload.ToImmutableArray());
    }

    public WorldChunkManifest Decode(WorldChunkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported chunk schema {snapshot.SchemaVersion}.");
        }

        var payload = snapshot.Payload.AsSpan();
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        if (!string.Equals(
                hash,
                snapshot.ContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "World chunk content hash does not match its payload.");
        }

        ManifestDto document;
        try
        {
            document = JsonSerializer.Deserialize<ManifestDto>(payload) ??
                throw new InvalidDataException(
                    "World chunk manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "World chunk manifest contains invalid JSON.",
                exception);
        }

        var id = new WorldChunkId(document.X, document.Y);
        if (id != snapshot.Id)
        {
            throw new InvalidDataException(
                "World chunk manifest ID does not match its envelope.");
        }

        return new WorldChunkManifest(
            id,
            Ids(document.RiverIds),
            Ids(document.CityIds),
            Ids(document.StationIds),
            Ids(document.RailNodeIds),
            Ids(document.RailPortIds),
            Ids(document.TrackIds),
            Ids(document.TrackGroupIds),
            Ids(document.ConnectionIds));
    }

    private static WorldBounds ChunkBounds(
        WorldChunkId id,
        int chunkSizeMeters)
    {
        var size = checked(chunkSizeMeters * 1000L);
        var minimum = new WorldPoint(
            checked(id.X * size),
            checked(id.Y * size));
        return new WorldBounds(
            minimum,
            minimum.OffsetMillimeters(size, size));
    }

    private static string[] Values(
        IEnumerable<StableId> ids)
    {
        return ids
            .OrderBy(id => id)
            .Select(id => id.Value)
            .ToArray();
    }

    private static ImmutableArray<StableId> Ids(string[]? values)
    {
        if (values is null)
        {
            throw new InvalidDataException(
                "World chunk ID collection is missing.");
        }

        return values
            .Select(value => new StableId(value))
            .ToImmutableArray();
    }

    private sealed class ManifestDto
    {
        public long X { get; set; }

        public long Y { get; set; }

        public string[]? RiverIds { get; set; }

        public string[]? CityIds { get; set; }

        public string[]? StationIds { get; set; }

        public string[]? RailNodeIds { get; set; }

        public string[]? RailPortIds { get; set; }

        public string[]? TrackIds { get; set; }

        public string[]? TrackGroupIds { get; set; }

        public string[]? ConnectionIds { get; set; }
    }
}
