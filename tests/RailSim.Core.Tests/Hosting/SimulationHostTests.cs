using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
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
    public void NullContributorElementIsRejectedWithClearArgumentError()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SimulationHost(
                new SimulationClock(),
                new SimulationCommandBus(),
                Array.Empty<ISimulationSystem>(),
                new SnapshotStore(),
                new ISnapshotContributor[] { null! }));

        Assert.Equal("contributors", exception.ParamName);
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

    [Fact]
    public void HandlerFailurePropagatesAndPermanentlyFaultsHost()
    {
        var failure = new TestFailureException("handler failed");
        var bus = new SimulationCommandBus();
        bus.Register(new ThrowingHandler(failure));
        var host = new SimulationHost(
            new SimulationClock(),
            bus,
            Array.Empty<ISimulationSystem>());
        host.Submit(new ThrowCommand());

        var propagated = Assert.Throws<TestFailureException>(() =>
            host.AdvanceFrame(
                TimeSpan.FromMilliseconds(250),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));

        Assert.Same(failure, propagated);
        var firstAdvanceRejection = Assert.Throws<InvalidOperationException>(() =>
            host.AdvanceFrame(
                TimeSpan.Zero,
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));
        var secondAdvanceRejection = Assert.Throws<InvalidOperationException>(() =>
            host.AdvanceFrame(
                TimeSpan.Zero,
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));
        var submitRejection = Assert.Throws<InvalidOperationException>(() =>
            host.Submit(new ThrowCommand()));
        Assert.Equal(firstAdvanceRejection.Message, secondAdvanceRejection.Message);
        Assert.Equal(firstAdvanceRejection.Message, submitRejection.Message);
    }

    [Fact]
    public void SystemFailurePropagatesAndPermanentlyFaultsHost()
    {
        var failure = new TestFailureException("system failed");
        var host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            new[] { new ThrowingSystem(failure) });

        var propagated = Assert.Throws<TestFailureException>(() =>
            host.AdvanceFrame(
                TimeSpan.FromMilliseconds(250),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));

        Assert.Same(failure, propagated);
        Assert.Throws<InvalidOperationException>(() =>
            host.AdvanceFrame(
                TimeSpan.Zero,
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));
        Assert.Throws<InvalidOperationException>(() =>
            host.Submit(new ThrowCommand()));
    }

    [Fact]
    public void ReentrantAdvanceIsRejectedAndFaultsOuterAdvance()
    {
        var store = new SnapshotStore();
        var system = new ReentrantSystem();
        var host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            new[] { system },
            store,
            Array.Empty<ISnapshotContributor>());
        system.Host = host;

        var rejection = Assert.Throws<InvalidOperationException>(() =>
            host.AdvanceFrame(
                TimeSpan.FromMilliseconds(250),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));

        Assert.Contains("advancing", rejection.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new SimulationTick(1), host.CurrentTick);
        Assert.Equal(SimulationTick.Zero, store.GetLatest().Tick);
        Assert.Throws<InvalidOperationException>(() =>
            host.AdvanceFrame(
                TimeSpan.Zero,
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));
    }

    [Fact]
    public async Task ConcurrentAdvanceIsRejectedWithoutFaultingActiveAdvance()
    {
        var system = new BlockingSystem();
        var host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            new[] { system });
        var rate = SimulationRate.FromPreset(SimulationSpeedPreset.Normal);
        var activeAdvance = Task.Run(() => host.AdvanceFrame(
            TimeSpan.FromMilliseconds(250),
            rate));
        Task<Exception>? competingAdvance = null;
        var completedPromptly = false;

        try
        {
            Assert.True(system.Entered.Wait(TimeSpan.FromSeconds(5)));
            competingAdvance = Task.Run(() => Record.Exception(() =>
                host.AdvanceFrame(TimeSpan.FromMilliseconds(250), rate)));
            completedPromptly = await Task.WhenAny(
                competingAdvance,
                Task.Delay(TimeSpan.FromSeconds(1))) == competingAdvance;
        }
        finally
        {
            system.Continue.Set();
        }

        var activeResult = await activeAdvance;
        Assert.NotNull(competingAdvance);
        var competingException = await competingAdvance;
        Assert.True(completedPromptly);
        var rejection = Assert.IsType<InvalidOperationException>(competingException);
        Assert.Contains("advancing", rejection.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new SimulationTick(1), activeResult.CurrentTick);

        var resumedResult = host.AdvanceFrame(
            TimeSpan.FromMilliseconds(250),
            rate);

        Assert.Equal(new SimulationTick(2), resumedResult.CurrentTick);
    }

    [Fact]
    public void InvalidArgumentsAreRejectedBeforeFaultedState()
    {
        var host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            new[] { new ThrowingSystem(new TestFailureException("failed")) });
        Assert.Throws<TestFailureException>(() =>
            host.AdvanceFrame(
                TimeSpan.FromMilliseconds(250),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));

        var elapsedException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            host.AdvanceFrame(
                TimeSpan.FromTicks(-1),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));
        var commandException = Assert.Throws<ArgumentNullException>(() =>
            host.Submit(null!));

        Assert.Equal("realElapsed", elapsedException.ParamName);
        Assert.Equal("command", commandException.ParamName);
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

    private sealed record ThrowCommand : ISimulationCommand;

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

    private sealed class ThrowingHandler(Exception failure)
        : CommandHandler<ThrowCommand>
    {
        protected override CommandDecision Handle(
            ThrowCommand command,
            SimulationTick tick)
        {
            throw failure;
        }
    }

    private sealed class ThrowingSystem(Exception failure) : ISimulationSystem
    {
        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            throw failure;
        }
    }

    private sealed class ReentrantSystem : ISimulationSystem
    {
        private bool _hasReentered;

        public SimulationHost Host { get; set; } = null!;

        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            if (_hasReentered)
            {
                return;
            }

            _hasReentered = true;
            Host.AdvanceFrame(
                TimeSpan.FromMilliseconds(250),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal));
        }
    }

    private sealed class BlockingSystem : ISimulationSystem
    {
        public ManualResetEventSlim Continue { get; } = new(false);

        public ManualResetEventSlim Entered { get; } = new(false);

        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            Entered.Set();
            Continue.Wait();
        }
    }

    private sealed class TestFailureException(string message) : Exception(message);
}
