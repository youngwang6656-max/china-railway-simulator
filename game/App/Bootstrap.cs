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
    private bool _smokeDrawingFeelOk;
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
            _smokeDrawingFeelOk = RunDrawingFeelSmoke();
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
            _smokeDrawingFeelOk &&
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
        GD.Print(
            $"RAILSIM_DRAWING_FEEL_SMOKE complete={_smokeDrawingFeelOk}");
        GetTree().Quit(success ? 0 : 1);
    }

    private static (bool Warning, bool Connector, bool EditTools)
        RunManualToolSmoke()
    {
        var commandBus = new SimulationCommandBus();
        var editor = WorldEditorController.CreateDefault(
            20260729,
            ProjectSettings.GlobalizePath(
                "res://../.tools/manual-smoke-world.crsim-world.json"),
            commandBus);
        var store = new SnapshotStore();
        var host = new SimulationHost(
            new SimulationClock(),
            commandBus,
            Array.Empty<ISimulationSystem>(),
            store,
            [editor]);
        editor.AttachQuery(store);
        editor.AttachHost(host);
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

        var demoMainGroup = editor.World.RailTopology.TrackGroups.Values
            .Single(
                group =>
                    group.Id.Value.Contains(
                        "-demo-main:",
                        StringComparison.Ordinal));
        var splitTarget = editor.World.RailTopology.Tracks[
            demoMainGroup.TrackIds[0]];
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
        var demoReferenceSyncOk =
            editor.BuildDemoMainLine() == CommandStatus.Rejected;

        demoMainGroup = editor.World.RailTopology.TrackGroups[
            demoMainGroup.Id];
        var realignTarget = editor.World.RailTopology.Tracks[
            demoMainGroup.TrackIds[0]];
        var realignAlignment = editor.World.RailTopology.Alignments[
            realignTarget.AlignmentId];
        var originalGeometry = realignAlignment.ControlPoints;
        var stableTrackIds = editor.World.RailTopology.Tracks.Keys
            .ToHashSet();
        var realignPoint = originalGeometry[
            originalGeometry.Length / 2];
        editor.SetTool(WorldEditorTool.RealignTrack);
        var realignLoaded =
            editor.BeginRealignAt(realignPoint, radiusMeters: 1) ==
            CommandStatus.Accepted;
        editor.AddWaypoint(
            realignPoint.OffsetMillimeters(0, 2_000_000),
            snapRadiusMeters: 1);
        var realignResult = editor.CommitDraft();
        if (realignResult == CommandStatus.Rejected)
        {
            realignResult = editor.CommitDraft();
        }

        var realignCommitted =
            realignResult == CommandStatus.Accepted &&
            stableTrackIds.SetEquals(
                editor.World.RailTopology.Tracks.Keys) &&
            !originalGeometry.SequenceEqual(
                editor.World.RailTopology.Alignments[
                    realignAlignment.Id].ControlPoints);
        var realignUndo =
            editor.Undo() == CommandStatus.Accepted &&
            originalGeometry.SequenceEqual(
                editor.World.RailTopology.Alignments[
                    realignAlignment.Id].ControlPoints);
        var realignOk =
            realignLoaded &&
            realignCommitted &&
            realignUndo;

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
            demoReferenceSyncOk &&
            realignOk &&
            boxSelectOk &&
            disconnectOk);
    }

    private bool RunDrawingFeelSmoke()
    {
        var commandBus = new SimulationCommandBus();
        var editor = WorldEditorController.CreateDefault(
            20260730,
            ProjectSettings.GlobalizePath(
                "res://../.tools/drawing-smoke-world.crsim-world.json"),
            commandBus);
        var store = new SnapshotStore();
        var host = new SimulationHost(
            new SimulationClock(),
            commandBus,
            Array.Empty<ISimulationSystem>(),
            store,
            [editor]);
        editor.AttachQuery(store);
        editor.AttachHost(host);
        editor.BuildDemoMainLine();
        editor.SetTool(WorldEditorTool.DrawDoubleTrack);
        var unchangedRevision = editor.World.Revision;
        var origin = new WorldPoint(
            120_000_000,
            110_000_000);
        var second = new WorldPoint(
            145_000_000,
            114_000_000);
        var third = new WorldPoint(
            170_000_000,
            125_000_000);
        editor.UpdateDrawingPreview(
            origin,
            metersPerPixel: 100,
            snappingDisabled: true);
        var originConfirmed =
            editor.ConfirmDrawingPoint().Accepted;
        editor.UpdateDrawingPreview(
            second,
            metersPerPixel: 100,
            snappingDisabled: true);
        var preview = editor.DrawingPreview;
        var livePreviewOk =
            editor.World.Revision == unchangedRevision &&
            preview is not null &&
            preview.UpTrackPoints.Length ==
            preview.Draft.SampledPoints.Length &&
            preview.DownTrackPoints.Length ==
            preview.Draft.SampledPoints.Length &&
            preview.UpTrackPoints[1] !=
            preview.DownTrackPoints[1];
        var secondConfirmed =
            editor.ConfirmDrawingPoint().Accepted;
        editor.UpdateDrawingPreview(
            third,
            metersPerPixel: 100,
            snappingDisabled: true);
        var thirdConfirmed =
            editor.ConfirmDrawingPoint().Accepted;
        var stepBackOk =
            editor.DraftWaypoints.Length == 3 &&
            editor.UndoDrawingStep() ==
            RailSim.Core.Editing.RailDrawingUndoResult.PointRemoved &&
            editor.DraftWaypoints.Length == 2;

        var node = editor.World.RailTopology.Nodes.Values
            .OrderBy(item => item.Id)
            .First();
        var nearNode = node.Position.OffsetMillimeters(
            500_000,
            0);
        editor.UpdateDrawingPreview(
            nearNode,
            metersPerPixel: 100,
            snappingDisabled: false);
        var snappedToNode =
            editor.CurrentSnap?.Kind ==
            RailSim.Core.Editing.RailSnapKind.RailNode;
        editor.UpdateDrawingPreview(
            nearNode,
            metersPerPixel: 100,
            snappingDisabled: true);
        var shiftFree =
            editor.CurrentSnap?.Kind ==
            RailSim.Core.Editing.RailSnapKind.Free &&
            editor.CurrentSnap.Waypoint.Position == nearNode;
        var expectedEndpoint =
            editor.CurrentSnap?.Waypoint.Position;
        var commit = editor.CommitDrawingAtPreview();
        if (commit == CommandStatus.Rejected)
        {
            commit = editor.CommitDraft();
        }

        var endpointIncluded =
            expectedEndpoint.HasValue &&
            editor.World.RailTopology.Alignments.Values.Any(
                alignment =>
                    alignment.ControlPoints[0] ==
                        expectedEndpoint.Value ||
                    alignment.ControlPoints[^1] ==
                        expectedEndpoint.Value);
        editor.UpdateDrawingPreview(
            origin,
            metersPerPixel: 100,
            snappingDisabled: true);
        editor.ConfirmDrawingPoint();
        editor.UpdateDrawingPreview(
            second,
            metersPerPixel: 100,
            snappingDisabled: true);
        editor.ConfirmDrawingPoint();
        editor.UpdateDrawingPreview(
            origin,
            metersPerPixel: 100,
            snappingDisabled: true);
        var invalidPreviewRejected =
            editor.DrawingPreview is null &&
            !editor.ConfirmDrawingPoint().Accepted &&
            editor.DraftWaypoints.Length == 2;
        GD.Print(
            "RAILSIM_DRAWING_INVALID_PREVIEW_SMOKE " +
            $"rejected={invalidPreviewRejected}");
        editor.CancelDraft();
        editor.UpdateDrawingPreview(
            origin,
            metersPerPixel: 5_000,
            snappingDisabled: true);
        editor.ConfirmDrawingPoint();
        editor.UpdateDrawingPreview(
            second,
            metersPerPixel: 5_000,
            snappingDisabled: true);
        editor.ConfirmDrawingPoint();
        editor.UpdateDrawingPreview(
            third,
            metersPerPixel: 5_000,
            snappingDisabled: true);
        editor.ConfirmDrawingPoint();
        editor.UpdateDrawingPreview(
            third,
            metersPerPixel: 1,
            snappingDisabled: true);
        var previewCommitIdentity =
            editor.DrawingPreview is not null &&
            editor.Draft is not null &&
            editor.DrawingPreview.Draft.SampledPoints.SequenceEqual(
                editor.Draft.SampledPoints);
        GD.Print(
            "RAILSIM_DRAWING_PREVIEW_IDENTITY_SMOKE " +
            $"equal={previewCommitIdentity}");
        var dragNode = editor.World.RailTopology.Nodes.Values
            .OrderBy(node => node.Id)
            .First(
                node =>
                    editor.DraftWaypoints.All(
                        waypoint =>
                            waypoint.Position != node.Position));
        var nearDragNode = dragNode.Position.OffsetMillimeters(
            500_000,
            0);
        editor.BeginWaypointDrag(1);
        editor.MoveDrawingWaypoint(
            1,
            nearDragNode,
            metersPerPixel: 100,
            snappingDisabled: false);
        var indexedDragSnap =
            editor.DraftWaypoints[1].NodeId.HasValue;
        editor.MoveDrawingWaypoint(
            1,
            nearDragNode,
            metersPerPixel: 100,
            snappingDisabled: true);
        var shiftDragFree =
            editor.DraftWaypoints[1].Position == nearDragNode &&
            !editor.DraftWaypoints[1].NodeId.HasValue &&
            !editor.DraftWaypoints[1].StationId.HasValue &&
            !editor.DraftWaypoints[1].SplitTrackId.HasValue;
        var beforeDuplicateDrag = editor.DraftWaypoints[1];
        editor.MoveDrawingWaypoint(
            1,
            editor.DraftWaypoints[0].Position,
            metersPerPixel: 100,
            snappingDisabled: true);
        var duplicateDragRejected =
            editor.DraftWaypoints[1] == beforeDuplicateDrag;
        GD.Print(
            "RAILSIM_DRAWING_DRAG_SMOKE " +
            $"indexedSnap={indexedDragSnap} " +
            $"shiftFree={shiftDragFree} " +
            $"duplicateRejected={duplicateDragRejected}");
        editor.CancelWaypointDrag();
        editor.CancelDraft();
        var pointerCommandBus = new SimulationCommandBus();
        var pointerEditor = WorldEditorController.CreateDefault(
            20260731,
            ProjectSettings.GlobalizePath(
                "res://../.tools/drawing-pointer-smoke-world.crsim-world.json"),
            pointerCommandBus);
        var pointerStore = new SnapshotStore();
        var pointerHost = new SimulationHost(
            new SimulationClock(),
            pointerCommandBus,
            Array.Empty<ISimulationSystem>(),
            pointerStore,
            [pointerEditor]);
        pointerEditor.AttachQuery(pointerStore);
        pointerEditor.AttachHost(pointerHost);
        pointerEditor.SetTool(WorldEditorTool.DrawDoubleTrack);
        var pointerView = new WorldMapView
        {
            Size = new Vector2(1_000, 500)
        };
        AddChild(pointerView);
        pointerView.Initialize(pointerEditor);
        var pointerRevision = pointerEditor.World.Revision;
        var pointerPoints = new[]
        {
            new Vector2(400, 250),
            new Vector2(450, 250),
            new Vector2(500, 250)
        };
        foreach (var pointerPoint in pointerPoints)
        {
            pointerView._GuiInput(
                new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = pointerPoint,
                    ShiftPressed = true
                });
            pointerView._GuiInput(
                new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = false,
                    Position = pointerPoint,
                    ShiftPressed = true
                });
        }

        pointerView._GuiInput(
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                DoubleClick = true,
                Pressed = true,
                Position = pointerPoints[^1],
                ShiftPressed = true
            });
        if (pointerEditor.DrawingState ==
            RailSim.Core.Editing.RailDrawingState.ConfirmingWarning)
        {
            pointerView._GuiInput(
                new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    DoubleClick = true,
                    Pressed = true,
                    Position = pointerPoints[^1],
                    ShiftPressed = true
                });
        }

        var doubleClickCommitted =
            pointerEditor.World.Revision > pointerRevision &&
            pointerEditor.DraftWaypoints.IsEmpty;
        GD.Print(
            "RAILSIM_DRAWING_POINTER_SMOKE " +
            $"committed={doubleClickCommitted}");
        pointerView.QueueFree();

        return
            originConfirmed &&
            livePreviewOk &&
            secondConfirmed &&
            thirdConfirmed &&
            stepBackOk &&
            snappedToNode &&
            shiftFree &&
            commit == CommandStatus.Accepted &&
            endpointIncluded &&
            invalidPreviewRejected &&
            previewCommitIdentity &&
            indexedDragSnap &&
            shiftDragFree &&
            duplicateDragRejected &&
            doubleClickCommitted &&
            editor.CurrentTool == WorldEditorTool.DrawDoubleTrack &&
            editor.DraftWaypoints.IsEmpty &&
            editor.DrawingState ==
            RailSim.Core.Editing.RailDrawingState.Idle;
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
            "既有线改线",
            () => _editor.SetTool(WorldEditorTool.RealignTrack));
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
            Text = "左键确认｜右键退点｜中键平移｜Shift 关闭吸附｜Enter 完成",
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
