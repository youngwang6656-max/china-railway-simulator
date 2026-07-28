using RailSim.Core.Time;

namespace RailSim.Core.Commands;

/// <summary>
/// Marks a payload that may enter the simulation only at a command boundary.
/// </summary>
/// <remarks>
/// Implementations must be deeply immutable: sealed reference types or readonly
/// value types whose complete instance state is recursively immutable. The
/// command bus validates this contract before retaining a payload.
/// </remarks>
public interface ISimulationCommand;

public enum CommandStatus
{
    Accepted,
    Rejected
}

public sealed record CommandDecision(
    CommandStatus Status,
    string Code,
    string Message)
{
    public static CommandDecision Accept()
    {
        return new(CommandStatus.Accepted, "accepted", string.Empty);
    }

    public static CommandDecision Reject(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(CommandStatus.Rejected, code, message);
    }
}

/// <summary>
/// Associates a validated command payload with its deterministic sequence and
/// scheduled simulation tick.
/// </summary>
/// <remarks>
/// Envelopes emitted by <see cref="SimulationCommandBus"/> retain payload
/// references safely because the payload type passed structural deep-immutability
/// validation at enqueue time. Callers constructing envelopes directly remain
/// responsible for satisfying the <see cref="ISimulationCommand"/> contract.
/// </remarks>
public sealed record CommandEnvelope(
    long Sequence,
    SimulationTick ExecuteAt,
    ISimulationCommand Payload);

public sealed record CommandResult(
    long Sequence,
    SimulationTick ExecutedAt,
    CommandStatus Status,
    string Code,
    string Message);

public interface ICommandHandler
{
    Type CommandType { get; }

    CommandDecision Handle(
        ISimulationCommand command,
        SimulationTick tick);
}

public abstract class CommandHandler<TCommand> : ICommandHandler
    where TCommand : ISimulationCommand
{
    public Type CommandType => typeof(TCommand);

    public CommandDecision Handle(
        ISimulationCommand command,
        SimulationTick tick)
    {
        if (command is not TCommand typedCommand)
        {
            return CommandDecision.Reject(
                "handler.type_mismatch",
                $"Handler for {typeof(TCommand).FullName} received {command.GetType().FullName}.");
        }

        return Handle(typedCommand, tick);
    }

    protected abstract CommandDecision Handle(
        TCommand command,
        SimulationTick tick);
}
