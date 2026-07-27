namespace RailSim.Core.Time;

public enum SimulationSpeedPreset
{
    Paused,
    Quarter,
    Half,
    Normal,
    X2,
    X4,
    X8,
    X16,
    X32,
    X64
}

public readonly record struct SimulationRate
{
    private SimulationRate(SimulationSpeedPreset preset, int quarterUnits)
    {
        Preset = preset;
        QuarterUnits = quarterUnits;
    }

    public SimulationSpeedPreset Preset { get; }

    public int QuarterUnits { get; }

    public bool IsPaused => QuarterUnits == 0;

    public static SimulationRate FromPreset(SimulationSpeedPreset preset)
    {
        return preset switch
        {
            SimulationSpeedPreset.Paused => new(preset, 0),
            SimulationSpeedPreset.Quarter => new(preset, 1),
            SimulationSpeedPreset.Half => new(preset, 2),
            SimulationSpeedPreset.Normal => new(preset, 4),
            SimulationSpeedPreset.X2 => new(preset, 8),
            SimulationSpeedPreset.X4 => new(preset, 16),
            SimulationSpeedPreset.X8 => new(preset, 32),
            SimulationSpeedPreset.X16 => new(preset, 64),
            SimulationSpeedPreset.X32 => new(preset, 128),
            SimulationSpeedPreset.X64 => new(preset, 256),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };
    }
}
