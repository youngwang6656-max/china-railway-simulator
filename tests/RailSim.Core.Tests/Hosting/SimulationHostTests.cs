using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Time;
using Xunit;

namespace RailSim.Core.Tests.Hosting;

public sealed class SimulationHostTests
{
    [Fact]
    public void CommandRunsBeforeOrderedSystemsAtNextTick()
    {
        var events = new List<string>();
        var clock = new SimulationClock();
        var bus = new SimulationCommandBus();
        bus.Register(new RecordHandler(events));
        var host = new SimulationHost(
            clock,
            bus,
            new ISimulationSystem[]
            {
                new RecordingSystem(20, "second", events),
                new RecordingSystem(10, "first", events)
            });

        host.Submit(new RecordCommand("command"));
        var result = host.AdvanceFrame(
            TimeSpan.FromMilliseconds(250),
            SimulationRate.FromPreset(SimulationSpeedPreset.Normal));

        Assert.Equal(1, result.StepsProcessed);
        Assert.Equal(new SimulationTick(1), result.CurrentTick);
        Assert.Equal(
            new[] { "command@1", "first@1", "second@1" },
            events);
    }

    [Fact]
    public void FramePartitionDoesNotChangeTickTrace()
    {
        var splitTrace = RunTrace(Enumerable.Repeat(
            TimeSpan.FromMilliseconds(100),
            10));
        var wholeTrace = RunTrace(new[] { TimeSpan.FromSeconds(1) });

        Assert.Equal(wholeTrace, splitTrace);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, wholeTrace);
    }

    [Fact]
    public void DuplicateSystemOrderIsRejected()
    {
        var events = new List<string>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SimulationHost(
                new SimulationClock(),
                new SimulationCommandBus(),
                new ISimulationSystem[]
                {
                    new RecordingSystem(10, "A", events),
                    new RecordingSystem(10, "B", events)
                }));

        Assert.Contains("10", exception.Message);
    }

    [Fact]
    public void NullSystemElementIsRejectedWithClearArgumentError()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SimulationHost(
                new SimulationClock(),
                new SimulationCommandBus(),
                new ISimulationSystem[] { null! }));

        Assert.Equal("systems", exception.ParamName);
    }

    [Fact]
    public void PausedFrameDoesNotRunCommandsOrSystems()
    {
        var events = new List<string>();
        var bus = new SimulationCommandBus();
        bus.Register(new RecordHandler(events));
        var host = new SimulationHost(
            new SimulationClock(),
            bus,
            new[] { new RecordingSystem(10, "system", events) });
        host.Submit(new RecordCommand("command"));

        var result = host.AdvanceFrame(
            TimeSpan.FromSeconds(10),
            SimulationRate.FromPreset(SimulationSpeedPreset.Paused));

        Assert.Equal(0, result.StepsProcessed);
        Assert.Empty(result.CommandResults);
        Assert.Empty(events);
    }

    private static long[] RunTrace(IEnumerable<TimeSpan> frames)
    {
        var trace = new List<long>();
        var host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            new[] { new TickTraceSystem(trace) });
        var rate = SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

        foreach (var frame in frames)
        {
            host.AdvanceFrame(frame, rate);
        }

        return trace.ToArray();
    }

    private sealed record RecordCommand(string Value) : ISimulationCommand;

    private sealed class RecordHandler(List<string> events)
        : CommandHandler<RecordCommand>
    {
        protected override CommandDecision Handle(
            RecordCommand command,
            SimulationTick tick)
        {
            events.Add($"{command.Value}@{tick.Value}");
            return CommandDecision.Accept();
        }
    }

    private sealed class RecordingSystem(
        int order,
        string name,
        List<string> events) : ISimulationSystem
    {
        public int Order => order;

        public void Step(SimulationStepContext context)
        {
            events.Add($"{name}@{context.Tick.Value}");
        }
    }

    private sealed class TickTraceSystem(List<long> trace) : ISimulationSystem
    {
        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            trace.Add(context.Tick.Value);
        }
    }
}
