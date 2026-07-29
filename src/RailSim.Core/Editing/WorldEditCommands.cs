using RailSim.Core.Commands;
using RailSim.Core.Time;

namespace RailSim.Core.Editing;

public sealed record ApplyRailEditCommand(
    RailGraphPatch Patch,
    long BaseRevision) : ISimulationCommand;

public sealed record UndoWorldEditCommand(
    long BaseRevision) : ISimulationCommand;

public sealed record RedoWorldEditCommand(
    long BaseRevision) : ISimulationCommand;

public static class WorldEditCommandRegistration
{
    public static void Register(
        SimulationCommandBus bus,
        WorldEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(session);
        bus.Register(new ApplyRailEditCommandHandler(session));
        bus.Register(new UndoWorldEditCommandHandler(session));
        bus.Register(new RedoWorldEditCommandHandler(session));
    }
}

public sealed class ApplyRailEditCommandHandler
    : CommandHandler<ApplyRailEditCommand>
{
    private readonly WorldEditorSession _session;

    public ApplyRailEditCommandHandler(WorldEditorSession session)
    {
        _session = session ??
            throw new ArgumentNullException(nameof(session));
    }

    protected override CommandDecision Handle(
        ApplyRailEditCommand command,
        SimulationTick tick)
    {
        return _session.Apply(command.Patch, command.BaseRevision);
    }
}

public sealed class UndoWorldEditCommandHandler
    : CommandHandler<UndoWorldEditCommand>
{
    private readonly WorldEditorSession _session;

    public UndoWorldEditCommandHandler(WorldEditorSession session)
    {
        _session = session ??
            throw new ArgumentNullException(nameof(session));
    }

    protected override CommandDecision Handle(
        UndoWorldEditCommand command,
        SimulationTick tick)
    {
        return _session.Undo(command.BaseRevision);
    }
}

public sealed class RedoWorldEditCommandHandler
    : CommandHandler<RedoWorldEditCommand>
{
    private readonly WorldEditorSession _session;

    public RedoWorldEditCommandHandler(WorldEditorSession session)
    {
        _session = session ??
            throw new ArgumentNullException(nameof(session));
    }

    protected override CommandDecision Handle(
        RedoWorldEditCommand command,
        SimulationTick tick)
    {
        return _session.Redo(command.BaseRevision);
    }
}
