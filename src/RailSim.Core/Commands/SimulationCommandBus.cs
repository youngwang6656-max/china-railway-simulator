using RailSim.Core.Time;

namespace RailSim.Core.Commands;

public sealed class SimulationCommandBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, ICommandHandler> _handlers = new();
    private readonly List<CommandEnvelope> _scheduled = new();
    private readonly List<CommandEnvelope> _log = new();
    private long _nextSequence;

    public void Register(ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryAdd(handler.CommandType, handler))
            {
                throw new InvalidOperationException(
                    $"A handler is already registered for {handler.CommandType.FullName}.");
            }
        }
    }

    public CommandEnvelope Enqueue(
        ISimulationCommand command,
        SimulationTick executeAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (executeAt.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executeAt),
                executeAt,
                "Commands must execute at tick 1 or later.");
        }

        lock (_gate)
        {
            var envelope = new CommandEnvelope(
                checked(++_nextSequence),
                executeAt,
                command);
            _scheduled.Add(envelope);
            _log.Add(envelope);
            return envelope;
        }
    }

    public IReadOnlyList<CommandResult> DispatchDue(SimulationTick tick)
    {
        CommandEnvelope[] due;
        ICommandHandler?[] handlers;

        lock (_gate)
        {
            due = _scheduled
                .Where(item => item.ExecuteAt.Value <= tick.Value)
                .OrderBy(item => item.ExecuteAt.Value)
                .ThenBy(item => item.Sequence)
                .ToArray();

            var dueIds = due
                .Select(item => item.Sequence)
                .ToHashSet();
            _scheduled.RemoveAll(item => dueIds.Contains(item.Sequence));

            handlers = due
                .Select(item => _handlers.TryGetValue(
                    item.Payload.GetType(),
                    out var handler)
                    ? handler
                    : null)
                .ToArray();
        }

        var results = new List<CommandResult>(due.Length);
        for (var index = 0; index < due.Length; index++)
        {
            var envelope = due[index];
            var handler = handlers[index];

            var decision = handler is null
                ? CommandDecision.Reject(
                    "handler.missing",
                    $"No handler is registered for {envelope.Payload.GetType().FullName}.")
                : handler.Handle(envelope.Payload, tick);

            results.Add(new CommandResult(
                envelope.Sequence,
                tick,
                decision.Status,
                decision.Code,
                decision.Message));
        }

        return results;
    }

    public IReadOnlyList<CommandEnvelope> ExportLog()
    {
        lock (_gate)
        {
            return _log
                .OrderBy(item => item.Sequence)
                .ToArray();
        }
    }
}
