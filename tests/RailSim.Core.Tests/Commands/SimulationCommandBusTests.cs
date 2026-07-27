using RailSim.Core.Commands;
using RailSim.Core.Time;
using Xunit;

namespace RailSim.Core.Tests.Commands;

public sealed class SimulationCommandBusTests
{
    [Fact]
    public void CommandsAtSameTickRunInEnqueueOrder()
    {
        var values = new List<string>();
        var bus = new SimulationCommandBus();
        bus.Register(new AppendHandler(values));

        bus.Enqueue(new AppendCommand("A"), new SimulationTick(2));
        bus.Enqueue(new AppendCommand("B"), new SimulationTick(2));
        bus.Enqueue(new AppendCommand("C"), new SimulationTick(2));

        var results = bus.DispatchDue(new SimulationTick(2));

        Assert.Equal(new[] { "A", "B", "C" }, values);
        Assert.All(results, result => Assert.Equal(CommandStatus.Accepted, result.Status));
        Assert.Equal(new long[] { 1, 2, 3 }, results.Select(result => result.Sequence));
    }

    [Fact]
    public void FutureCommandRemainsQueued()
    {
        var values = new List<string>();
        var bus = new SimulationCommandBus();
        bus.Register(new AppendHandler(values));
        bus.Enqueue(new AppendCommand("future"), new SimulationTick(4));

        var early = bus.DispatchDue(new SimulationTick(3));
        var onTime = bus.DispatchDue(new SimulationTick(4));

        Assert.Empty(early);
        Assert.Single(onTime);
        Assert.Equal(new[] { "future" }, values);
    }

    [Fact]
    public void MissingHandlerReturnsStableRejection()
    {
        var bus = new SimulationCommandBus();
        var envelope = bus.Enqueue(new UnknownCommand(), new SimulationTick(1));

        var result = Assert.Single(bus.DispatchDue(new SimulationTick(1)));

        Assert.Equal(envelope.Sequence, result.Sequence);
        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal("handler.missing", result.Code);
    }

    [Fact]
    public void DuplicateHandlerRegistrationIsRejected()
    {
        var bus = new SimulationCommandBus();
        bus.Register(new AppendHandler(new List<string>()));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            bus.Register(new AppendHandler(new List<string>())));

        Assert.Contains(typeof(AppendCommand).FullName!, exception.Message);
    }

    [Fact]
    public void ExportLogReturnsIndependentSequenceOrderedCopy()
    {
        var bus = new SimulationCommandBus();
        bus.Enqueue(new UnknownCommand(), new SimulationTick(3));
        bus.Enqueue(new UnknownCommand(), new SimulationTick(1));

        var log = bus.ExportLog();

        Assert.Equal(new long[] { 1, 2 }, log.Select(item => item.Sequence));
        Assert.NotSame(log, bus.ExportLog());
    }

    private sealed record AppendCommand(string Value) : ISimulationCommand;

    private sealed record UnknownCommand : ISimulationCommand;

    private sealed class AppendHandler(List<string> values)
        : CommandHandler<AppendCommand>
    {
        protected override CommandDecision Handle(
            AppendCommand command,
            SimulationTick tick)
        {
            values.Add(command.Value);
            return CommandDecision.Accept();
        }
    }
}
