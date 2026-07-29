using System.Security.Cryptography;
using System.Text;
using RailSim.Core.Rail;

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

    public static string HashState(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.UTF8,
                   leaveOpen: true))
        {
            writer.Write(world.BaseWorld.ContentHash);
            WriteTopology(writer, world.RailTopology);
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

    private static void WriteTopology(
        BinaryWriter writer,
        RailTopology topology)
    {
        foreach (var node in topology.Nodes.Values.OrderBy(item => item.Id))
        {
            writer.Write(node.Id.Value);
            writer.Write((int)node.Kind);
            WritePoint(writer, node.Position);
            WriteOptionalId(writer, node.StationId);
        }

        foreach (var port in topology.Ports.Values.OrderBy(item => item.Id))
        {
            writer.Write(port.Id.Value);
            writer.Write(port.NodeId.Value);
            writer.Write((int)port.Direction);
        }

        foreach (var alignment in topology.Alignments.Values.OrderBy(item => item.Id))
        {
            writer.Write(alignment.Id.Value);
            writer.Write(alignment.LengthMillimeters);
            writer.Write(alignment.ControlPoints.Length);
            foreach (var point in alignment.ControlPoints)
            {
                WritePoint(writer, point);
            }
        }

        foreach (var track in topology.Tracks.Values.OrderBy(item => item.Id))
        {
            writer.Write(track.Id.Value);
            writer.Write(track.FromPortId.Value);
            writer.Write(track.ToPortId.Value);
            writer.Write(track.AlignmentId.Value);
            writer.Write(track.LateralOffsetMillimeters);
            writer.Write(track.DesignSpeedKph);
        }

        foreach (var group in topology.TrackGroups.Values.OrderBy(item => item.Id))
        {
            writer.Write(group.Id.Value);
            foreach (var trackId in group.TrackIds.OrderBy(item => item))
            {
                writer.Write(trackId.Value);
            }
        }

        foreach (var connection in topology.Connections.Values.OrderBy(item => item.Id))
        {
            writer.Write(connection.Id.Value);
            writer.Write(connection.NodeId.Value);
            writer.Write(connection.FromTrackId.Value);
            writer.Write(connection.ToTrackId.Value);
        }
    }
}
