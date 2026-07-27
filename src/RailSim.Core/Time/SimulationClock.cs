namespace RailSim.Core.Time;

public sealed class SimulationClock
{
    private const int QuarterUnitsPerNormalRate = 4;
    private long _quarterScaledRemainder;

    public static TimeSpan StepDuration { get; } =
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 4);

    public SimulationTick Current { get; private set; } = SimulationTick.Zero;

    public int Accumulate(TimeSpan realElapsed, SimulationRate rate)
    {
        if (realElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(realElapsed),
                realElapsed,
                "Elapsed time cannot be negative.");
        }

        var scaledElapsed = checked(realElapsed.Ticks * rate.QuarterUnits);
        var available = checked(_quarterScaledRemainder + scaledElapsed);
        var scaledStep = checked(StepDuration.Ticks * QuarterUnitsPerNormalRate);
        var dueSteps = available / scaledStep;

        if (dueSteps > int.MaxValue)
        {
            throw new InvalidOperationException(
                "A single frame produced more simulation steps than can be processed.");
        }

        _quarterScaledRemainder = available % scaledStep;
        return (int)dueSteps;
    }

    public SimulationTick AdvanceOne()
    {
        Current = Current.Next();
        return Current;
    }
}
