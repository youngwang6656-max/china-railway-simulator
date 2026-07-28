using Godot;
using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
using RailSim.Core.Time;

namespace RailSim.App;

public partial class Bootstrap : Control
{
    private SimulationHost _host = null!;
    private ISimulationQuery _query = null!;
    private Label _status = null!;
    private SimulationRate _rate =
        SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

    public override void _Ready()
    {
        var store = new SnapshotStore();
        _query = store;
        _host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            Array.Empty<ISimulationSystem>(),
            store,
            Array.Empty<ISnapshotContributor>());
        _status = GetNode<Label>("Status");

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            RunSmokeTest();
            return;
        }

        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        _host.AdvanceFrame(TimeSpan.FromSeconds(delta), _rate);
        UpdateStatus();
    }

    private void RunSmokeTest()
    {
        _host.AdvanceFrame(TimeSpan.FromSeconds(1), _rate);
        var tick = _host.CurrentTick.Value;
        GD.Print($"RAILSIM_SMOKE tick={tick}");
        GetTree().Quit(tick == 4 ? 0 : 1);
    }

    private void UpdateStatus()
    {
        var snapshot = _query.GetLatest();
        _status.Text =
            $"RailSim foundation | Tick {snapshot.Tick.Value} | Rate {_rate.Preset}";
    }
}
