using RailSim.Core.Time;

namespace RailSim.Core.Hosting;

public sealed record SimulationStepContext(
    SimulationTick Tick,
    TimeSpan StepDuration);
