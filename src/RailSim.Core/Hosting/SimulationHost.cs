using RailSim.Core.Commands;
using RailSim.Core.Query;
using RailSim.Core.Time;

namespace RailSim.Core.Hosting;

public sealed record FrameAdvanceResult(
    int StepsProcessed,
    SimulationTick CurrentTick,
    IReadOnlyList<CommandResult> CommandResults);

public sealed class SimulationHost
{
    private readonly SimulationClock _clock;
    private readonly SimulationCommandBus _commandBus;
    private readonly ISimulationSystem[] _systems;
    private readonly SnapshotStore _snapshotStore;
    private readonly ISnapshotContributor[] _contributors;
    private long _lastCommandSequence;

    public SimulationHost(
        SimulationClock clock,
        SimulationCommandBus commandBus,
        IEnumerable<ISimulationSystem> systems)
        : this(
            clock,
            commandBus,
            systems,
            new SnapshotStore(),
            Array.Empty<ISnapshotContributor>())
    {
    }

    public SimulationHost(
        SimulationClock clock,
        SimulationCommandBus commandBus,
        IEnumerable<ISimulationSystem> systems,
        SnapshotStore snapshotStore,
        IEnumerable<ISnapshotContributor> contributors)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(contributors);

        var registeredSystems = systems.ToArray();
        if (registeredSystems.Any(system => system is null))
        {
            throw new ArgumentException(
                "Simulation systems cannot contain null elements.",
                nameof(systems));
        }

        _systems = registeredSystems
            .OrderBy(system => system.Order)
            .ToArray();

        _contributors = contributors.ToArray();

        var duplicateOrder = _systems
            .GroupBy(system => system.Order)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOrder is not null)
        {
            throw new InvalidOperationException(
                $"Simulation system order {duplicateOrder.Key} is registered more than once.");
        }

        var duplicateSection = _contributors
            .Select(contributor => contributor.Key)
            .GroupBy(key => key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSection is not null)
        {
            throw new InvalidOperationException(
                $"Snapshot section key {duplicateSection.Key} is registered more than once.");
        }
    }

    public SimulationTick CurrentTick => _clock.Current;

    public CommandEnvelope Submit(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _commandBus.Enqueue(command, _clock.Current.Next());
    }

    public FrameAdvanceResult AdvanceFrame(
        TimeSpan realElapsed,
        SimulationRate rate)
    {
        var dueSteps = _clock.Accumulate(realElapsed, rate);
        var commandResults = new List<CommandResult>();

        for (var index = 0; index < dueSteps; index++)
        {
            var tick = _clock.AdvanceOne();
            var tickResults = _commandBus.DispatchDue(tick);
            commandResults.AddRange(tickResults);
            if (tickResults.Count > 0)
            {
                _lastCommandSequence = tickResults.Max(result => result.Sequence);
            }

            var notices = new SimulationNoticeSink();
            var context = new SimulationStepContext(
                tick,
                SimulationClock.StepDuration,
                notices);
            foreach (var system in _systems)
            {
                system.Step(context);
            }

            var sections = _contributors
                .Select(contributor =>
                {
                    var section = contributor.Capture();
                    if (!string.Equals(contributor.Key, section.Key, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Snapshot contributor key {contributor.Key} does not match captured section key {section.Key}.");
                    }

                    return section;
                })
                .ToDictionary(section => section.Key, StringComparer.Ordinal);
            _snapshotStore.Publish(new SimulationSnapshot(
                tick,
                _lastCommandSequence,
                notices.Snapshot(),
                sections));
        }

        return new FrameAdvanceResult(
            dueSteps,
            _clock.Current,
            commandResults);
    }
}
