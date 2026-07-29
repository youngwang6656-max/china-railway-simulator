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

    public static void Register(
        SimulationCommandBus bus,
        Func<WorldEditorSession> sessionProvider)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(sessionProvider);
        bus.Register(new ApplyRailEditCommandHandler(sessionProvider));
        bus.Register(new UndoWorldEditCommandHandler(sessionProvider));
        bus.Register(new RedoWorldEditCommandHandler(sessionProvider));
    }
}

public sealed class ApplyRailEditCommandHandler
    : CommandHandler<ApplyRailEditCommand>
{
    private readonly Func<WorldEditorSession> _sessionProvider;

    public ApplyRailEditCommandHandler(WorldEditorSession session)
        : this(() => session)
    {
        ArgumentNullException.ThrowIfNull(session);
    }

    public ApplyRailEditCommandHandler(
        Func<WorldEditorSession> sessionProvider)
    {
        _sessionProvider = sessionProvider ??
            throw new ArgumentNullException(nameof(sessionProvider));
    }

    protected override CommandDecision Handle(
        ApplyRailEditCommand command,
        SimulationTick tick)
    {
        return Session.Apply(command.Patch, command.BaseRevision);
    }

    private WorldEditorSession Session =>
        _sessionProvider() ??
        throw new InvalidOperationException(
            "The world editor session provider returned null.");
}

public sealed class UndoWorldEditCommandHandler
    : CommandHandler<UndoWorldEditCommand>
{
    private readonly Func<WorldEditorSession> _sessionProvider;

    public UndoWorldEditCommandHandler(WorldEditorSession session)
        : this(() => session)
    {
        ArgumentNullException.ThrowIfNull(session);
    }

    public UndoWorldEditCommandHandler(
        Func<WorldEditorSession> sessionProvider)
    {
        _sessionProvider = sessionProvider ??
            throw new ArgumentNullException(nameof(sessionProvider));
    }

    protected override CommandDecision Handle(
        UndoWorldEditCommand command,
        SimulationTick tick)
    {
        return Session.Undo(command.BaseRevision);
    }

    private WorldEditorSession Session =>
        _sessionProvider() ??
        throw new InvalidOperationException(
            "The world editor session provider returned null.");
}

public sealed class RedoWorldEditCommandHandler
    : CommandHandler<RedoWorldEditCommand>
{
    private readonly Func<WorldEditorSession> _sessionProvider;

    public RedoWorldEditCommandHandler(WorldEditorSession session)
        : this(() => session)
    {
        ArgumentNullException.ThrowIfNull(session);
    }

    public RedoWorldEditCommandHandler(
        Func<WorldEditorSession> sessionProvider)
    {
        _sessionProvider = sessionProvider ??
            throw new ArgumentNullException(nameof(sessionProvider));
    }

    protected override CommandDecision Handle(
        RedoWorldEditCommand command,
        SimulationTick tick)
    {
        return Session.Redo(command.BaseRevision);
    }

    private WorldEditorSession Session =>
        _sessionProvider() ??
        throw new InvalidOperationException(
            "The world editor session provider returned null.");
}
