using RailSim.Core.World;

namespace RailSim.Core.Query;

public sealed record WorldSnapshotSection(
    WorldState World) : ISimulationSnapshotSection
{
    public string Key => "world";
}
