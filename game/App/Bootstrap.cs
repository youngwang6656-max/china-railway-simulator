using Godot;

namespace RailSim.App;

public partial class Bootstrap : Control
{
    public override void _Ready()
    {
        GD.Print("RAILSIM_BOOT");

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            GetTree().Quit(0);
        }
    }
}
