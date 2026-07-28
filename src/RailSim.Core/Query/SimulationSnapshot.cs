using System.Collections.ObjectModel;
using RailSim.Core.Time;

namespace RailSim.Core.Query;

public interface ISimulationSnapshotSection
{
    string Key { get; }
}

public interface ISnapshotContributor
{
    string Key { get; }

    ISimulationSnapshotSection Capture();
}

public sealed record SimulationNotice(string Code, string Message);

public sealed class SimulationNoticeSink
{
    private readonly List<SimulationNotice> _notices = new();

    public void Add(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _notices.Add(new SimulationNotice(code, message));
    }

    public IReadOnlyList<SimulationNotice> Snapshot()
    {
        return _notices.ToArray();
    }
}

public sealed record SimulationSnapshot
{
    public SimulationSnapshot(
        SimulationTick tick,
        long lastCommandSequence,
        IEnumerable<SimulationNotice> notices,
        IReadOnlyDictionary<string, ISimulationSnapshotSection> sections)
    {
        Tick = tick;
        LastCommandSequence = lastCommandSequence;
        Notices = new ReadOnlyCollection<SimulationNotice>(notices.ToArray());
        Sections = new ReadOnlyDictionary<string, ISimulationSnapshotSection>(
            sections.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    public SimulationTick Tick { get; }

    public long LastCommandSequence { get; }

    public IReadOnlyList<SimulationNotice> Notices { get; }

    public IReadOnlyDictionary<string, ISimulationSnapshotSection> Sections { get; }

    public static SimulationSnapshot Empty { get; } = new(
        SimulationTick.Zero,
        0,
        Array.Empty<SimulationNotice>(),
        new Dictionary<string, ISimulationSnapshotSection>());
}

public interface ISimulationQuery
{
    SimulationSnapshot GetLatest();
}
