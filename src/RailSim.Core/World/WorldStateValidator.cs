namespace RailSim.Core.World;

public static class WorldStateValidator
{
    public static void Validate(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var generated = world.BaseWorld;
        var parameters = generated.Parameters;
        if (generated.Terrain.Width != parameters.HeightSamplesX ||
            generated.Terrain.Height != parameters.HeightSamplesY)
        {
            throw Invalid(
                "Terrain dimensions do not match generation parameters.");
        }

        if (generated.Cities.Count != parameters.CityCount)
        {
            throw Invalid(
                "City count does not match generation parameters.");
        }

        if (generated.Stations.Count != parameters.StationAnchorCount)
        {
            throw Invalid(
                "Station count does not match generation parameters.");
        }

        foreach (var city in generated.Cities.Values)
        {
            EnsureInside(parameters.Bounds, city.Position, $"City {city.Id}");
        }

        foreach (var station in generated.Stations.Values)
        {
            EnsureInside(
                parameters.Bounds,
                station.Position,
                $"Station {station.Id}");
            if (station.CityId.HasValue &&
                !generated.Cities.ContainsKey(station.CityId.Value))
            {
                throw Invalid(
                    $"Station {station.Id} references missing city " +
                    $"{station.CityId.Value}.");
            }
        }

        foreach (var river in generated.Rivers)
        {
            foreach (var point in river.Points)
            {
                EnsureInside(parameters.Bounds, point, $"River {river.Id}");
            }
        }

        foreach (var node in world.RailTopology.Nodes.Values)
        {
            EnsureInside(parameters.Bounds, node.Position, $"Node {node.Id}");
            if (node.StationId.HasValue &&
                !generated.Stations.ContainsKey(node.StationId.Value))
            {
                throw Invalid(
                    $"Node {node.Id} references missing station " +
                    $"{node.StationId.Value}.");
            }
        }

        foreach (var alignment in world.RailTopology.Alignments.Values)
        {
            foreach (var point in alignment.ControlPoints)
            {
                EnsureInside(
                    parameters.Bounds,
                    point,
                    $"Alignment {alignment.Id}");
            }
        }
    }

    private static void EnsureInside(
        WorldBounds bounds,
        WorldPoint point,
        string entity)
    {
        if (!bounds.Contains(point))
        {
            throw Invalid($"{entity} lies outside world bounds.");
        }
    }

    private static InvalidDataException Invalid(string message)
    {
        return new InvalidDataException(message);
    }
}
