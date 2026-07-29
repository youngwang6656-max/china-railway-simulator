using Godot;
using RailSim.App.Map;
using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
using RailSim.Core.Time;

namespace RailSim.App;

public partial class Bootstrap : Control
{
    private SimulationHost _host = null!;
    private ISimulationQuery _query = null!;
    private WorldEditorController _editor = null!;
    private WorldMapView? _map;
    private Label? _status;
    private bool _smokePending;
    private int _smokeFrames;
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
        _editor = WorldEditorController.CreateDefault(
            20260729,
            ProjectSettings.GlobalizePath(
                "user://regional-world.crsim-world.json"));

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            _editor.BuildDemoMainLine();
            _editor.BuildDemoSecondLine();
            _editor.BuildDemoConnector();
            BuildWorkspace();
            UpdateStatus();
            _smokePending = true;
            return;
        }

        _editor.BuildDemoMainLine();
        _editor.BuildDemoSecondLine();
        _editor.BuildDemoConnector();
        BuildWorkspace();
        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        if (_smokePending)
        {
            _smokeFrames++;
            if (_smokeFrames >= 3)
            {
                _host.AdvanceFrame(TimeSpan.FromSeconds(1), _rate);
                CompleteSmokeTest();
            }

            return;
        }

        _host.AdvanceFrame(TimeSpan.FromSeconds(delta), _rate);
        UpdateStatus();
    }

    private void CompleteSmokeTest()
    {
        _smokePending = false;
        var tick = _host.CurrentTick.Value;
        GD.Print($"RAILSIM_SMOKE tick={tick}");
        var world = _editor.World;
        var success =
            tick == 4 &&
            world.BaseWorld.Cities.Count == 64 &&
            world.BaseWorld.Stations.Count == 64 &&
            world.RailTopology.Tracks.Count >= 8;
        GD.Print(
            $"RAILSIM_WORLD_SMOKE cities={world.BaseWorld.Cities.Count} " +
            $"stations={world.BaseWorld.Stations.Count} " +
            $"tracks={world.RailTopology.Tracks.Count}");
        GetTree().Quit(success ? 0 : 1);
    }

    private void BuildWorkspace()
    {
        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 0);
        AddChild(layout);

        var toolbarPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 52)
        };
        layout.AddChild(toolbarPanel);
        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        toolbarPanel.AddChild(toolbar);

        AddButton(
            toolbar,
            "新世界",
            () =>
            {
                _editor.NewWorld(_editor.Seed + 1);
                _map?.ResetCamera();
            });
        AddButton(
            toolbar,
            "绘制双线",
            () => _editor.SetTool(WorldEditorTool.DrawDoubleTrack));
        AddButton(toolbar, "示例干线", () => _editor.BuildDemoMainLine());
        AddButton(toolbar, "线路二", () => _editor.BuildDemoSecondLine());
        AddButton(toolbar, "丁市联络线", () => _editor.BuildDemoConnector());
        AddButton(toolbar, "撤销", () => _editor.Undo());
        AddButton(toolbar, "重做", () => _editor.Redo());
        AddButton(toolbar, "保存", SaveWorld);
        AddButton(toolbar, "读取", LoadWorld);
        AddButton(
            toolbar,
            "查看",
            () => _editor.SetTool(WorldEditorTool.Inspect));

        var hint = new Label
        {
            Text = "滚轮缩放｜中/右键拖动｜绘线时左键布点、拖点调整、Enter 提交、Esc 取消",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        toolbar.AddChild(hint);

        _map = new WorldMapView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(640, 420)
        };
        layout.AddChild(_map);
        _map.Initialize(_editor);

        _status = new Label
        {
            CustomMinimumSize = new Vector2(0, 34),
            VerticalAlignment = VerticalAlignment.Center
        };
        layout.AddChild(_status);
    }

    private void SaveWorld()
    {
        try
        {
            _editor.Save();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            if (_status is not null)
            {
                _status.Text = $"保存失败：{exception.Message}";
            }
        }
    }

    private void LoadWorld()
    {
        try
        {
            _editor.Load();
            _map?.ResetCamera();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            if (_status is not null)
            {
                _status.Text = $"读取失败：{exception.Message}";
            }
        }
    }

    private void UpdateStatus()
    {
        if (_status is null)
        {
            return;
        }

        var snapshot = _query.GetLatest();
        var world = _editor.World;
        _status.Text =
            $"种子 {_editor.Seed}｜修订 {world.Revision}｜" +
            $"城市 {world.BaseWorld.Cities.Count}｜站点 {world.BaseWorld.Stations.Count}｜" +
            $"物理轨道 {world.RailTopology.Tracks.Count}｜互通 {world.RailTopology.Connections.Count}｜" +
            $"工具 {_editor.CurrentTool}｜Tick {snapshot.Tick.Value}｜{_editor.LastMessage}";
    }

    private static void AddButton(
        Container parent,
        string text,
        Action action)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(78, 40)
        };
        button.Pressed += action;
        parent.AddChild(button);
    }
}
