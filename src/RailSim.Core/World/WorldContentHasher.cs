using System.Security.Cryptography;
using System.Text;

namespace RailSim.Core.World;

public static class WorldContentHasher
{
    public static string HashTerrain(HeightField terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.UTF8,
                   leaveOpen: true))
        {
            writer.Write(terrain.Width);
            writer.Write(terrain.Height);
            foreach (var elevation in terrain.ElevationMeters)
            {
                writer.Write(elevation);
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public static string HashGeneratedWorld(GeneratedWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.UTF8,
                   leaveOpen: true))
        {
            WriteParameters(writer, world.Parameters);
            writer.Write(world.Terrain.ContentHash);
            foreach (var river in world.Rivers.OrderBy(item => item.Id))
            {
                writer.Write(river.Id.Value);
                writer.Write((int)river.WidthClass);
                writer.Write(river.Points.Length);
                foreach (var point in river.Points)
                {
                    WritePoint(writer, point);
                }
            }

            foreach (var city in world.Cities.Values.OrderBy(item => item.Id))
            {
                writer.Write(city.Id.Value);
                writer.Write(city.Name);
                writer.Write((int)city.Tier);
                WritePoint(writer, city.Position);
                writer.Write(city.PopulationWeight);
            }

            foreach (var station in world.Stations.Values.OrderBy(item => item.Id))
            {
                writer.Write(station.Id.Value);
                writer.Write(station.Name);
                writer.Write((int)station.Tier);
                WritePoint(writer, station.Position);
                WriteOptionalId(writer, station.CityId);
                WriteOptionalId(writer, station.DetailZoneId);
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteParameters(
        BinaryWriter writer,
        WorldGenerationParameters parameters)
    {
        writer.Write(parameters.Seed);
        writer.Write(parameters.GeneratorVersion);
        writer.Write((int)parameters.ScaleProfile);
        WritePoint(writer, parameters.Bounds.Minimum);
        WritePoint(writer, parameters.Bounds.Maximum);
        writer.Write(parameters.ChunkSizeMeters);
        writer.Write(parameters.HeightSamplesX);
        writer.Write(parameters.HeightSamplesY);
        writer.Write(parameters.MegaCityCount);
        writer.Write(parameters.LargeCityCount);
        writer.Write(parameters.MediumCityCount);
        writer.Write(parameters.SmallCityCount);
        writer.Write(parameters.StationAnchorCount);
        writer.Write(parameters.MajorRiverCount);
    }

    private static void WritePoint(BinaryWriter writer, WorldPoint point)
    {
        writer.Write(point.XMillimeters);
        writer.Write(point.YMillimeters);
    }

    private static void WriteOptionalId(
        BinaryWriter writer,
        StableId? id)
    {
        writer.Write(id.HasValue);
        if (id.HasValue)
        {
            writer.Write(id.Value.Value);
        }
    }
}
