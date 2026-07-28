namespace RailSim.Core.Hosting;

public interface ISimulationSystem
{
    int Order { get; }

    void Step(SimulationStepContext context);
}
