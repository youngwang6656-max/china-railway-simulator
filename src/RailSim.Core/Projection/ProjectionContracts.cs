using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Projection;

public enum MapLod
{
    Regional,
    Line,
    Edit
}

public sealed record MapViewport
{
    public MapViewport(
        WorldBounds bounds,
        double metersPerPixel)
    {
        if (!double.IsFinite(metersPerPixel) || metersPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metersPerPixel));
        }

        Bounds = bounds;
        MetersPerPixel = metersPerPixel;
    }

    public WorldBounds Bounds { get; }

    public double MetersPerPixel { get; }

    public static MapViewport Regional(WorldBounds bounds)
    {
        var largerAxisMeters = Math.Max(
            bounds.WidthMillimeters,
            bounds.HeightMillimeters) / 1000d;
        return new MapViewport(bounds, largerAxisMeters / 1280d);
    }
}

public interface IMapLodPolicy
{
    MapLod Select(double metersPerPixel);
}

public sealed class DefaultMapLodPolicy : IMapLodPolicy
{
    public MapLod Select(double metersPerPixel)
    {
        if (!double.IsFinite(metersPerPixel) || metersPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metersPerPixel));
        }

        if (metersPerPixel >= 250)
        {
            return MapLod.Regional;
        }

        return metersPerPixel >= 25
            ? MapLod.Line
            : MapLod.Edit;
    }
}

public interface IWorldProjection
{
    WorldMapFrame Project(
        WorldState world,
        MapViewport viewport);
}

public sealed record ProjectedCity(
    StableId SourceId,
    string Name,
    CityTier Tier,
    WorldPoint Position);

public sealed record ProjectedStation(
    StableId SourceId,
    string Name,
    StationTier Tier,
    WorldPoint Position);

public sealed record ProjectedRiver(
    StableId SourceId,
    RiverWidthClass WidthClass,
    ImmutableArray<WorldPoint> Points);

public sealed record ProjectedTrack(
    StableId SourceId,
    ImmutableArray<WorldPoint> Points,
    int LateralOffsetMillimeters,
    int DesignSpeedKph,
    bool IsAggregate);

public sealed record ProjectedConnection(
    StableId SourceId,
    StableId NodeId,
    StableId FromTrackId,
    StableId ToTrackId,
    WorldPoint Position);

public sealed record WorldMapFrame(
    long WorldRevision,
    string TerrainContentHash,
    MapLod Lod,
    ImmutableArray<ProjectedRiver> Rivers,
    ImmutableArray<ProjectedCity> Cities,
    ImmutableArray<ProjectedStation> Stations,
    ImmutableArray<ProjectedTrack> Tracks,
    ImmutableArray<ProjectedConnection> Connections);
