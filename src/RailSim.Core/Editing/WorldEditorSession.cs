using System.Collections.Immutable;
using RailSim.Core.Commands;
using RailSim.Core.Query;
using RailSim.Core.World;

namespace RailSim.Core.Editing;

public sealed class WorldEditorSession : ISnapshotContributor
{
    private readonly object _gate = new();
    private readonly IRailEditValidator _validator;
    private readonly Stack<RailGraphPatch> _undo = new();
    private readonly Stack<RailGraphPatch> _redo = new();
    private readonly IEditHistoryStore? _historyStore;
    private readonly StableId _worldId;
    private WorldState _world;

    public WorldEditorSession(
        WorldState initialWorld,
        IRailEditValidator validator,
        IEditHistoryStore? historyStore = null,
        StableId? worldId = null)
    {
        _world = initialWorld ??
            throw new ArgumentNullException(nameof(initialWorld));
        WorldStateValidator.Validate(_world);
        _validator = validator ??
            throw new ArgumentNullException(nameof(validator));
        _historyStore = historyStore;
        _worldId = worldId ??
            new StableId($"world:{_world.BaseWorld.Parameters.Seed}");
        RestoreHistory();
    }

    public string Key => "world";

    public WorldState World
    {
        get
        {
            lock (_gate)
            {
                return _world;
            }
        }
    }

    public long Revision => World.Revision;

    public string CurrentHash => WorldContentHasher.HashState(World);

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undo.Count > 0;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _redo.Count > 0;
            }
        }
    }

    public CommandDecision Apply(
        RailGraphPatch patch,
        long baseRevision)
    {
        ArgumentNullException.ThrowIfNull(patch);
        lock (_gate)
        {
            var revisionDecision = CheckRevision(baseRevision);
            if (revisionDecision is not null)
            {
                return revisionDecision;
            }

            var validation = _validator.Validate(
                _world,
                patch);
            var error = validation.Issues.FirstOrDefault(
                issue =>
                    issue.Severity == RailValidationSeverity.Error);
            if (error is not null)
            {
                return CommandDecision.Reject(
                    error.Code,
                    "Rail edit validation failed.");
            }

            var unacknowledgedWarning = validation.Issues.FirstOrDefault(
                issue =>
                    issue.Severity == RailValidationSeverity.Warning &&
                    !patch.AcknowledgedWarningCodes.Contains(issue.Code));
            if (unacknowledgedWarning is not null)
            {
                return CommandDecision.Reject(
                    "rail.warning.unacknowledged",
                    $"Rail edit warning {unacknowledgedWarning.Code} " +
                    "requires explicit acknowledgement.");
            }

            var candidate = patch.ApplyTo(_world.RailTopology);
            var resultRevision = checked(_world.Revision + 1);
            _world = new WorldState(
                resultRevision,
                _world.BaseWorld,
                candidate,
                patch.ToChangeSet(
                    _world.RailTopology,
                    candidate,
                    _world.BaseWorld.Parameters.ChunkSizeMeters,
                    _world.Revision,
                    resultRevision));
            _undo.Push(patch.Invert());
            _redo.Clear();
            PersistHistory();
            return CommandDecision.Accept();
        }
    }

    public CommandDecision Undo(long baseRevision)
    {
        lock (_gate)
        {
            var revisionDecision = CheckRevision(baseRevision);
            if (revisionDecision is not null)
            {
                return revisionDecision;
            }

            if (_undo.Count == 0)
            {
                return CommandDecision.Reject(
                    "world.edit.undo_empty",
                    "There is no world edit to undo.");
            }

            var patch = _undo.Pop();
            var candidate = patch.ApplyTo(_world.RailTopology);
            var resultRevision = checked(_world.Revision + 1);
            _world = new WorldState(
                resultRevision,
                _world.BaseWorld,
                candidate,
                patch.ToChangeSet(
                    _world.RailTopology,
                    candidate,
                    _world.BaseWorld.Parameters.ChunkSizeMeters,
                    _world.Revision,
                    resultRevision));
            _redo.Push(patch.Invert());
            PersistHistory();
            return CommandDecision.Accept();
        }
    }

    public CommandDecision Redo(long baseRevision)
    {
        lock (_gate)
        {
            var revisionDecision = CheckRevision(baseRevision);
            if (revisionDecision is not null)
            {
                return revisionDecision;
            }

            if (_redo.Count == 0)
            {
                return CommandDecision.Reject(
                    "world.edit.redo_empty",
                    "There is no world edit to redo.");
            }

            var patch = _redo.Pop();
            var candidate = patch.ApplyTo(_world.RailTopology);
            var resultRevision = checked(_world.Revision + 1);
            _world = new WorldState(
                resultRevision,
                _world.BaseWorld,
                candidate,
                patch.ToChangeSet(
                    _world.RailTopology,
                    candidate,
                    _world.BaseWorld.Parameters.ChunkSizeMeters,
                    _world.Revision,
                    resultRevision));
            _undo.Push(patch.Invert());
            PersistHistory();
            return CommandDecision.Accept();
        }
    }

    public ISimulationSnapshotSection Capture()
    {
        return new WorldSnapshotSection(World);
    }

    private CommandDecision? CheckRevision(long baseRevision)
    {
        return baseRevision == _world.Revision
            ? null
            : CommandDecision.Reject(
                "world.edit.stale_revision",
                $"Expected world revision {_world.Revision}, received {baseRevision}.");
    }

    private void RestoreHistory()
    {
        var history = _historyStore?.Load(_worldId);
        if (history is null ||
            history.SchemaVersion != 1 ||
            history.WorldRevision != _world.Revision)
        {
            return;
        }

        foreach (var patch in history.Undo)
        {
            _undo.Push(patch);
        }

        foreach (var patch in history.Redo)
        {
            _redo.Push(patch);
        }
    }

    private void PersistHistory()
    {
        _historyStore?.Save(
            _worldId,
            new EditHistorySnapshot(
                schemaVersion: 1,
                _world.Revision,
                _undo.Reverse().ToImmutableArray(),
                _redo.Reverse().ToImmutableArray()));
    }
}
