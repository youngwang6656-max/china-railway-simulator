namespace RailSim.Core.Query;

public sealed class SnapshotStore : ISimulationQuery
{
    private SimulationSnapshot _latest = SimulationSnapshot.Empty;

    public SimulationSnapshot GetLatest()
    {
        return Volatile.Read(ref _latest);
    }

    public void Publish(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var independentCopy = new SimulationSnapshot(
            snapshot.Tick,
            snapshot.LastCommandSequence,
            snapshot.Notices,
            snapshot.Sections);
        Volatile.Write(ref _latest, independentCopy);
    }
}
