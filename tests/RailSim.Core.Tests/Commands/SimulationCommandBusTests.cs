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

    [Fact]
    public async Task DispatchDueUsesHandlerSnapshotFromDispatchBoundary()
    {
        var bus = new SimulationCommandBus();
        var blockingHandler = new BlockingHandler();
        bus.Register(blockingHandler);
        bus.Enqueue(new BlockingCommand(), new SimulationTick(1));
        bus.Enqueue(new LateRegisteredCommand(), new SimulationTick(1));

        var dispatch = Task.Run(() => bus.DispatchDue(new SimulationTick(1)));
        try
        {
            Assert.True(blockingHandler.Entered.Wait(TimeSpan.FromSeconds(5)));
            bus.Register(new LateRegisteredHandler());
        }
        finally
        {
            blockingHandler.Continue.Set();
        }

        var results = await dispatch;

        Assert.Equal(CommandStatus.Accepted, results[0].Status);
        Assert.Equal(CommandStatus.Rejected, results[1].Status);
        Assert.Equal("handler.missing", results[1].Code);
    }

    [Fact]
    public void WritablePropertyCommandIsRejectedWithoutChangingSequenceOrLog()
    {
        var bus = new SimulationCommandBus();

        var exception = Assert.Throws<ArgumentException>(() =>
            bus.Enqueue(new WritablePropertyCommand(), new SimulationTick(1)));

        Assert.Equal("command", exception.ParamName);
        Assert.Empty(bus.ExportLog());
        var accepted = bus.Enqueue(new UnknownCommand(), new SimulationTick(1));
        Assert.Equal(1, accepted.Sequence);
        Assert.Equal(new[] { accepted }, bus.ExportLog());
    }

    [Fact]
    public void RecordContainingListIsRejected()
    {
        var bus = new SimulationCommandBus();

        var exception = Assert.Throws<ArgumentException>(() =>
            bus.Enqueue(
                new ListCommand(["mutable"]),
                new SimulationTick(1)));

        Assert.Equal("command", exception.ParamName);
    }

    [Fact]
    public void RecordContainingArrayIsRejected()
    {
        var bus = new SimulationCommandBus();

        var exception = Assert.Throws<ArgumentException>(() =>
            bus.Enqueue(
                new ArrayCommand(["mutable"]),
                new SimulationTick(1)));

        Assert.Equal("command", exception.ParamName);
    }

    [Fact]
    public void RecordContainingNestedMutableObjectIsRejected()
    {
        var bus = new SimulationCommandBus();

        var exception = Assert.Throws<ArgumentException>(() =>
            bus.Enqueue(
                new NestedMutableCommand(new MutableDetails()),
                new SimulationTick(1)));

        Assert.Equal("command", exception.ParamName);
    }

    [Fact]
    public void ImmutableCommandsRemainAcceptedOrderedDispatchedAndLogged()
    {
        var bus = new SimulationCommandBus();
        bus.Enqueue(new AppendCommand("string"), new SimulationTick(2));
        bus.Enqueue(new UnknownCommand(), new SimulationTick(2));
        bus.Enqueue(
            new CompositeCommand(
                42,
                new ImmutableValue(new SimulationTick(1), TimeSpan.FromSeconds(3))),
            new SimulationTick(1));

        var results = bus.DispatchDue(new SimulationTick(2));
        var log = bus.ExportLog();

        Assert.Equal(new long[] { 3, 1, 2 }, results.Select(result => result.Sequence));
        Assert.Equal(new long[] { 1, 2, 3 }, log.Select(item => item.Sequence));
    }

    private sealed record AppendCommand(string Value) : ISimulationCommand;

    private sealed record UnknownCommand : ISimulationCommand;

    private sealed record BlockingCommand : ISimulationCommand;

    private sealed record LateRegisteredCommand : ISimulationCommand;

    private sealed record WritablePropertyCommand : ISimulationCommand
    {
        public int Value { get; set; }
    }

    private sealed record ListCommand(List<string> Values) : ISimulationCommand;

    private sealed record ArrayCommand(string[] Values) : ISimulationCommand;

    private sealed record NestedMutableCommand(MutableDetails Details) : ISimulationCommand;

    private sealed record CompositeCommand(
        int Count,
        ImmutableValue Value) : ISimulationCommand;

    private readonly record struct ImmutableValue(
        SimulationTick Tick,
        TimeSpan Offset);

    private sealed class MutableDetails
    {
        public int Value { get; set; }
    }

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

    private sealed class BlockingHandler : CommandHandler<BlockingCommand>
    {
        public ManualResetEventSlim Continue { get; } = new(false);

        public ManualResetEventSlim Entered { get; } = new(false);

        protected override CommandDecision Handle(
            BlockingCommand command,
            SimulationTick tick)
        {
            Entered.Set();
            Continue.Wait();
            return CommandDecision.Accept();
        }
    }

    private sealed class LateRegisteredHandler : CommandHandler<LateRegisteredCommand>
    {
        protected override CommandDecision Handle(
            LateRegisteredCommand command,
            SimulationTick tick)
        {
            return CommandDecision.Accept();
        }
    }
}
