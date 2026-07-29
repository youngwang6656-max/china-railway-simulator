using System.Collections.Immutable;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public sealed record EditHistorySnapshot
{
    public EditHistorySnapshot(
        int schemaVersion,
        long worldRevision,
        ImmutableArray<RailGraphPatch> undo,
        ImmutableArray<RailGraphPatch> redo)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        if (worldRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldRevision));
        }

        if (undo.IsDefault || redo.IsDefault)
        {
            throw new ArgumentException(
                "History collections must be initialized.");
        }

        SchemaVersion = schemaVersion;
        WorldRevision = worldRevision;
        Undo = undo;
        Redo = redo;
    }

    public int SchemaVersion { get; }

    public long WorldRevision { get; }

    public ImmutableArray<RailGraphPatch> Undo { get; }

    public ImmutableArray<RailGraphPatch> Redo { get; }
}

public interface IEditHistoryStore
{
    EditHistorySnapshot? Load(StableId worldId);

    void Save(StableId worldId, EditHistorySnapshot history);
}
