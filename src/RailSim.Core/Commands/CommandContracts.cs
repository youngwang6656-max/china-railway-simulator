using RailSim.Core.Time;

namespace RailSim.Core.Commands;

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
