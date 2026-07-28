using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
using RailSim.Core.Time;
using Xunit;

namespace RailSim.Core.Tests.Query;

public sealed class SnapshotStoreTests
{
    [Fact]
    public void HostPublishesTickCommandsNoticesAndSections()
    {
        var state = new CounterState();
        var bus = new SimulationCommandBus();
        bus.Register(new IncrementHandler(state));
        var store = new SnapshotStore();
        var host = new SimulationHost(
            new SimulationClock(),
            bus,
            new[] { new CounterSystem(state) },
            store,
            new[] { new CounterContributor(state) });
        host.Submit(new IncrementCommand(3));

        host.AdvanceFrame(
            TimeSpan.FromMilliseconds(250),
            SimulationRate.FromPreset(SimulationSpeedPreset.Normal));
        var snapshot = store.GetLatest();

        Assert.Equal(new SimulationTick(1), snapshot.Tick);
        Assert.Equal(1, snapshot.LastCommandSequence);
        Assert.Equal("counter.stepped", Assert.Single(snapshot.Notices).Code);
        var section = Assert.IsType<CounterSection>(snapshot.Sections["counter"]);
        Assert.Equal(4, section.Value);
    }

    [Fact]
    public void PublishedCollectionsCannotBeMutatedThroughSourceCollections()
    {
        var notices = new List<SimulationNotice>
        {
            new("first", "First notice")
        };
        var sections = new Dictionary<string, ISimulationSnapshotSection>
        {
            ["counter"] = new CounterSection(1)
        };
        var store = new SnapshotStore();
        store.Publish(new SimulationSnapshot(
            new SimulationTick(1),
            0,
            notices,
            sections));

        notices.Add(new SimulationNotice("second", "Second notice"));
        sections["counter"] = new CounterSection(99);
        var snapshot = store.GetLatest();

        Assert.Single(snapshot.Notices);
        Assert.Equal(1, Assert.IsType<CounterSection>(snapshot.Sections["counter"]).Value);
    }

    [Fact]
    public void PublishedNoticesCannotBeMutatedThroughRetrievedSnapshot()
    {
        var store = new SnapshotStore();
        store.Publish(new SimulationSnapshot(
            new SimulationTick(1),
            0,
            new[] { new SimulationNotice("first", "First notice") },
            new Dictionary<string, ISimulationSnapshotSection>()));
        var snapshot = store.GetLatest();
        var notices = Assert.IsAssignableFrom<IList<SimulationNotice>>(snapshot.Notices);

        Assert.Throws<NotSupportedException>(() =>
            notices[0] = new SimulationNotice("changed", "Changed notice"));

        Assert.Equal("first", store.GetLatest().Notices[0].Code);
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed record IncrementCommand(int Amount) : ISimulationCommand;

    private sealed class IncrementHandler(CounterState state)
        : CommandHandler<IncrementCommand>
    {
        protected override CommandDecision Handle(
            IncrementCommand command,
            SimulationTick tick)
        {
            state.Value += command.Amount;
            return CommandDecision.Accept();
        }
    }

    private sealed class CounterSystem(CounterState state) : ISimulationSystem
    {
        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            state.Value++;
            context.Notices.Add("counter.stepped", "Counter advanced.");
        }
    }

    private sealed record CounterSection(int Value) : ISimulationSnapshotSection
    {
        public string Key => "counter";
    }

    private sealed class CounterContributor(CounterState state)
        : ISnapshotContributor
    {
        public string Key => "counter";

        public ISimulationSnapshotSection Capture()
        {
            return new CounterSection(state.Value);
        }
    }
}
