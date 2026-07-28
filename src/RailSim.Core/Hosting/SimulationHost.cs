using RailSim.Core.Commands;
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

    public SimulationHost(
        SimulationClock clock,
        SimulationCommandBus commandBus,
        IEnumerable<ISimulationSystem> systems)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        ArgumentNullException.ThrowIfNull(systems);

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

        var duplicateOrder = _systems
            .GroupBy(system => system.Order)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOrder is not null)
        {
            throw new InvalidOperationException(
                $"Simulation system order {duplicateOrder.Key} is registered more than once.");
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
            commandResults.AddRange(_commandBus.DispatchDue(tick));

            var context = new SimulationStepContext(
                tick,
                SimulationClock.StepDuration);
            foreach (var system in _systems)
            {
                system.Step(context);
            }
        }

        return new FrameAdvanceResult(
            dueSteps,
            _clock.Current,
            commandResults);
    }
}
