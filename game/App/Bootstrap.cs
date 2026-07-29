using Godot;
using RailSim.App.Map;
using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
using RailSim.Core.Time;
using RailSim.Core.World;

namespace RailSim.App;

public partial class Bootstrap : Control
{
    private SimulationHost _host = null!;
    private ISimulationQuery _query = null!;
    private WorldEditorController _editor = null!;
    private WorldMapView? _map;
    private Label? _status;
    private bool _smokePending;
    private bool _smokeHistoryOk;
    private bool _smokePersistenceOk;
    private bool _smokeHostCommandOk;
    private bool _smokeReplacementOk;
    private bool _smokeManualWarningOk;
    private bool _smokeManualConnectorOk;
    private bool _smokeEditToolsOk;
    private long _smokeStartTick;
    private int _smokeFrames;
    private SimulationRate _rate =
        SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

    public override void _Ready()
    {
        var smokeTest =
            Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0;
        var store = new SnapshotStore();
        var commandBus = new SimulationCommandBus();
        _query = store;
        _editor = WorldEditorController.CreateDefault(
            20260729,
            ProjectSettings.GlobalizePath(
                smokeTest
                    ? "res://../.tools/smoke-world.crsim-world.json"
                    : "user://regional-world.crsim-world.json"),
            commandBus);
        _editor.AttachQuery(store);
        _host = new SimulationHost(
            new SimulationClock(),
            commandBus,
            Array.Empty<ISimulationSystem>(),
            store,
            [_editor]);
        _editor.AttachHost(_host);

        if (smokeTest)
        {
            _editor.BuildDemoMainLine();
            _editor.BuildDemoSecondLine();
            _editor.BuildDemoConnector();
            var connectorUndo = _editor.Undo();
            var secondLineUndo = _editor.Undo();
            var mainLineUndo = _editor.Undo();
            var mainLineRebuild = _editor.BuildDemoMainLine();
            var secondLineRebuild = _editor.BuildDemoSecondLine();
            var connectorRebuild = _editor.BuildDemoConnector();
            _smokeHistoryOk =
                connectorUndo == CommandStatus.Accepted &&
                secondLineUndo == CommandStatus.Accepted &&
                mainLineUndo == CommandStatus.Accepted &&
                mainLineRebuild == CommandStatus.Accepted &&
                secondLineRebuild == CommandStatus.Accepted &&
                connectorRebuild == CommandStatus.Accepted;
            var hostUndo = _editor.Undo();
            var hostRedo = _editor.Redo();
            _smokeHostCommandOk =
                hostUndo == CommandStatus.Accepted &&
                hostRedo == CommandStatus.Accepted &&
                _query.GetLatest().LastCommandSequence > 0;
            _smokeStartTick = _host.CurrentTick.Value;
            var expectedHash = WorldContentHasher.HashState(_editor.World);
            _editor.Save();
            _editor.NewWorld(_editor.Seed + 1);
            var newWorldDisplayOk =
                ReferenceEquals(_editor.DisplayWorld, _editor.World);
            _editor.Load();
            _smokePersistenceOk =
                string.Equals(
                    expectedHash,
                    WorldContentHasher.HashState(_editor.World),
                    StringComparison.Ordinal);
            _smokeReplacementOk =
                newWorldDisplayOk &&
                ReferenceEquals(_editor.DisplayWorld, _editor.World);
            (
                _smokeManualWarningOk,
                _smokeManualConnectorOk,
                _smokeEditToolsOk) =
                RunManualToolSmoke();
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
        _map?.QueueRedraw();
        UpdateStatus();
    }

    private void CompleteSmokeTest()
    {
        _smokePending = false;
        var tick = _host.CurrentTick.Value - _smokeStartTick;
        GD.Print($"RAILSIM_SMOKE tick={tick}");
        var world = _editor.World;
        var snapshot = _query.GetLatest();
        var snapshotOk =
            snapshot.Sections.TryGetValue("world", out var section) &&
            section is WorldSnapshotSection worldSection &&
            worldSection.World.Revision == world.Revision &&
            worldSection.World.RailTopology.Tracks.Count ==
            world.RailTopology.Tracks.Count;
        var success =
            tick == 4 &&
            world.BaseWorld.Cities.Count == 64 &&
            world.BaseWorld.Stations.Count == 64 &&
            world.RailTopology.Tracks.Count >= 8 &&
            _smokeHistoryOk &&
            _smokePersistenceOk &&
            _smokeHostCommandOk &&
            _smokeReplacementOk &&
            _smokeManualWarningOk &&
            _smokeManualConnectorOk &&
            _smokeEditToolsOk &&
            snapshotOk;
        GD.Print(
            $"RAILSIM_WORLD_SMOKE cities={world.BaseWorld.Cities.Count} " +
            $"stations={world.BaseWorld.Stations.Count} " +
            $"tracks={world.RailTopology.Tracks.Count}");
        GD.Print($"RAILSIM_HISTORY_SMOKE rebuild={_smokeHistoryOk}");
        GD.Print($"RAILSIM_SNAPSHOT_SMOKE published={snapshotOk}");
        GD.Print($"RAILSIM_PERSISTENCE_SMOKE roundtrip={_smokePersistenceOk}");
        GD.Print($"RAILSIM_HOST_COMMAND_SMOKE dispatched={_smokeHostCommandOk}");
        GD.Print($"RAILSIM_REPLACEMENT_SMOKE current={_smokeReplacementOk}");
        GD.Print($"RAILSIM_MANUAL_WARNING_SMOKE confirmed={_smokeManualWarningOk}");
        GD.Print($"RAILSIM_MANUAL_CONNECTOR_SMOKE built={_smokeManualConnectorOk}");
        GD.Print($"RAILSIM_EDIT_TOOLS_SMOKE complete={_smokeEditToolsOk}");
        GetTree().Quit(success ? 0 : 1);
    }

    private static (bool Warning, bool Connector, bool EditTools)
        RunManualToolSmoke()
    {
        var editor = WorldEditorController.CreateDefault(
            20260729,
            ProjectSettings.GlobalizePath(
                "res://../.tools/manual-smoke-world.crsim-world.json"),
            new SimulationCommandBus());
        editor.BuildDemoMainLine();
        editor.BuildDemoSecondLine();
        editor.SetTool(WorldEditorTool.DrawDoubleTrack);
        editor.AddWaypoint(
            new WorldPoint(100_000_000, 100_000_000),
            snapRadiusMeters: 1);
        editor.AddWaypoint(
            new WorldPoint(100_100_000, 100_000_000),
            snapRadiusMeters: 1);
        editor.AddWaypoint(
            new WorldPoint(100_100_000, 100_100_000),
            snapRadiusMeters: 1);
        var warningFirst = editor.CommitDraft();
        var warningSecond = editor.CommitDraft();
        var warningOk =
            warningFirst == CommandStatus.Rejected &&
            warningSecond == CommandStatus.Accepted;

        var connectorNodes = editor.World.RailTopology.Nodes.Values
            .Where(
                node =>
                    node.Kind ==
                    RailSim.Core.Rail.RailNodeKind.StationBoundary &&
                    node.StationId.HasValue)
            .OrderBy(node => node.Id)
            .Take(2)
            .ToArray();
        if (connectorNodes.Length < 2)
        {
            return (warningOk, false, false);
        }

        editor.SetTool(WorldEditorTool.DrawConnector);
        editor.AddWaypoint(connectorNodes[0].Position, snapRadiusMeters: 1);
        editor.AddWaypoint(connectorNodes[1].Position, snapRadiusMeters: 1);
        editor.CycleConnectorMovementPlan();
        var tracksBefore = editor.World.RailTopology.Tracks.Count;
        var connectionsBefore = editor.World.RailTopology.Connections.Count;
        var connectorResult = editor.CommitDraft();
        if (connectorResult == CommandStatus.Rejected)
        {
            connectorResult = editor.CommitDraft();
        }

        var connectorOk =
            connectorResult == CommandStatus.Accepted &&
            editor.World.RailTopology.Tracks.Count == tracksBefore + 2 &&
            editor.World.RailTopology.Connections.Count >=
            connectionsBefore + 4;

        editor.SetTool(WorldEditorTool.DrawDoubleTrack);
        editor.AddWaypoint(
            new WorldPoint(200_000_000, 200_000_000),
            snapRadiusMeters: 1);
        editor.AddWaypoint(
            new WorldPoint(210_000_000, 200_000_000),
            snapRadiusMeters: 1);
        editor.AddWaypoint(
            new WorldPoint(220_000_000, 200_000_000),
            snapRadiusMeters: 1);
        editor.RemoveWaypoint(1);
        var controlPointDeleteOk = editor.DraftWaypoints.Length == 2;
        editor.CancelDraft();

        var splitTarget = editor.World.RailTopology.Tracks.Values
            .OrderBy(track => track.Id)
            .First();
        var splitAlignment = editor.World.RailTopology.Alignments[
            splitTarget.AlignmentId];
        var splitPoint = splitAlignment.ControlPoints[
            splitAlignment.ControlPoints.Length / 2];
        var tracksBeforeSplit = editor.World.RailTopology.Tracks.Count;
        editor.SetTool(WorldEditorTool.SplitTrack);
        var splitOk =
            editor.SplitTrackAt(splitPoint, radiusMeters: 1) ==
            CommandStatus.Accepted &&
            editor.World.RailTopology.Tracks.Count ==
            tracksBeforeSplit + 2;
        var tracksBeforeDelete = editor.World.RailTopology.Tracks.Count;
        var deleteOk =
            editor.DeleteSelection() == CommandStatus.Accepted &&
            editor.World.RailTopology.Tracks.Count < tracksBeforeDelete &&
            editor.Undo() == CommandStatus.Accepted &&
            editor.World.RailTopology.Tracks.Count == tracksBeforeDelete;

        var connectedNode = editor.World.RailTopology.Nodes.Values
            .First(
                node =>
                    editor.World.RailTopology.Connections.Values.Any(
                        connection =>
                            connection.NodeId == node.Id));
        var selectionBounds = new WorldBounds(
            connectedNode.Position.OffsetMillimeters(-1, -1),
            connectedNode.Position.OffsetMillimeters(1, 1));
        editor.SelectInBounds(selectionBounds);
        var boxSelectOk = editor.SelectedObjectIds.Count > 0;
        var connectionsBeforeDisconnect =
            editor.World.RailTopology.Connections.Count;
        var disconnectOk =
            editor.DisconnectSelection() == CommandStatus.Accepted &&
            editor.World.RailTopology.Connections.Count <
            connectionsBeforeDisconnect;
        return (
            warningOk,
            connectorOk,
            controlPointDeleteOk &&
            splitOk &&
            deleteOk &&
            boxSelectOk &&
            disconnectOk);
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
            CustomMinimumSize = new Vector2(0, 96)
        };
        layout.AddChild(toolbarPanel);
        var toolbar = new HFlowContainer();
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
        AddButton(
            toolbar,
            "绘制联络线",
            () => _editor.SetTool(WorldEditorTool.DrawConnector));
        AddButton(
            toolbar,
            "框选",
            () => _editor.SetTool(WorldEditorTool.BoxSelect));
        AddButton(
            toolbar,
            "拆分轨道",
            () => _editor.SetTool(WorldEditorTool.SplitTrack));
        AddButton(
            toolbar,
            "删除所选",
            () => _editor.DeleteSelection());
        AddButton(
            toolbar,
            "断开互通",
            () => _editor.DisconnectSelection());
        AddButton(
            toolbar,
            "删控制点",
            () => _editor.RemoveLastWaypoint());
        AddButton(
            toolbar,
            "切换互通方案",
            () => _editor.CycleConnectorMovementPlan());
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
            Text = "滚轮缩放｜中/右键平移｜Delete 删除点/所选｜拆分后可接联络线",
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
        var world = _editor.DisplayWorld;
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
            CustomMinimumSize = new Vector2(68, 40)
        };
        button.Pressed += action;
        parent.AddChild(button);
    }
}
