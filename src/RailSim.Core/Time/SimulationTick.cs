namespace RailSim.Core.Time;

public readonly record struct SimulationTick(long Value)
{
    public static SimulationTick Zero { get; } = new(0);

    public SimulationTick Next()
    {
        return new SimulationTick(checked(Value + 1));
    }
}
