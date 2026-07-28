using System.Collections.ObjectModel;
using RailSim.Core.Time;

namespace RailSim.Core.Query;

public interface ISimulationSnapshotSection
{
    /// <summary>Gets the stable, non-empty key for this snapshot section.</summary>
    /// <remarks>
    /// Implementations must be immutable values. A published snapshot retains section values
    /// by reference and does not deep-copy their internal state.
    /// </remarks>
    string Key { get; }
}

/// <summary>Captures an immutable section of simulation state for publication.</summary>
public interface ISnapshotContributor
{
    /// <summary>Gets the non-empty key of the section captured by this contributor.</summary>
    string Key { get; }

    /// <summary>Captures the current immutable section value.</summary>
    /// <remarks>
    /// Each call must return a non-null immutable section whose <see cref="ISimulationSnapshotSection.Key" />
    /// matches <see cref="Key" />. Captured sections are retained by reference in published snapshots.
    /// </remarks>
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

/// <summary>Represents the immutable latest-state query payload for one simulation tick.</summary>
public sealed record SimulationSnapshot
{
    /// <summary>Initializes a snapshot from independently owned outer collection copies.</summary>
    /// <param name="tick">The simulation tick represented by the snapshot.</param>
    /// <param name="lastCommandSequence">The last command sequence dispatched at or before this tick.</param>
    /// <param name="notices">The non-null notices to copy.</param>
    /// <param name="sections">The non-null section mapping to copy.</param>
    /// <remarks>
    /// The snapshot owns copies of the outer notices and sections collections. Section values are not
    /// deep-copied, so callers and contributors must supply immutable
    /// <see cref="ISimulationSnapshotSection" /> values.
    /// </remarks>
    public SimulationSnapshot(
        SimulationTick tick,
        long lastCommandSequence,
        IEnumerable<SimulationNotice> notices,
        IReadOnlyDictionary<string, ISimulationSnapshotSection> sections)
    {
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(sections);

        var noticeCopy = notices.ToArray();
        if (noticeCopy.Any(notice => notice is null))
        {
            throw new ArgumentException(
                "Snapshot notices cannot contain null elements.",
                nameof(notices));
        }

        var sectionCopy = new Dictionary<string, ISimulationSnapshotSection>();
        foreach (var (key, section) in sections)
        {
            if (section is null)
            {
                throw new ArgumentException(
                    "Snapshot sections cannot contain null values.",
                    nameof(sections));
            }

            sectionCopy.Add(key, section);
        }

        Tick = tick;
        LastCommandSequence = lastCommandSequence;
        Notices = new ReadOnlyCollection<SimulationNotice>(noticeCopy);
        Sections = new ReadOnlyDictionary<string, ISimulationSnapshotSection>(
            sectionCopy);
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
