using Godot;
using RailSim.Core.Projection;
using RailSim.Core.Rail;
using RailSim.Core.World;

namespace RailSim.App.Map;

public partial class WorldMapView : Control
{
    private const double MinimumMetersPerPixel = 5;
    private const double MaximumMetersPerPixel = 2_000;

    private WorldEditorController _controller = null!;
    private ImageTexture? _terrainTexture;
    private string? _terrainHash;
    private double _centerXMillimeters;
    private double _centerYMillimeters;
    private double _metersPerPixel = 800;
    private bool _initialized;
    private bool _panning;
    private bool _boxSelecting;
    private Vector2 _lastPointer;
    private Vector2 _boxStart;
    private Vector2 _boxEnd;
    private int _draggedWaypoint = -1;
    private int _selectedWaypoint = -1;
    private Vector2 _latestPointer;
    private bool _previewDirty;
    private bool _snappingDisabled;

    public double MetersPerPixel => _metersPerPixel;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_initialized ||
            !_previewDirty ||
            !IsContinuousDrawingTool())
        {
            return;
        }

        _previewDirty = false;
        _controller.UpdateDrawingPreview(
            ScreenToWorld(_latestPointer),
            _metersPerPixel,
            _snappingDisabled);
    }

    public override void _ExitTree()
    {
        if (_initialized)
        {
            _controller.Changed -= HandleControllerChanged;
        }
    }

    public void Initialize(WorldEditorController controller)
    {
        _controller = controller ??
            throw new ArgumentNullException(nameof(controller));
        _controller.Changed += HandleControllerChanged;
        _initialized = true;
        ResetCamera();
        QueueRedraw();
    }

    public void ResetCamera()
    {
        if (!_initialized)
        {
            return;
        }

        var bounds = _controller.DisplayWorld.BaseWorld.Parameters.Bounds;
        _centerXMillimeters =
            bounds.Minimum.XMillimeters + (bounds.WidthMillimeters / 2d);
        _centerYMillimeters =
            bounds.Minimum.YMillimeters + (bounds.HeightMillimeters / 2d);
        var width = Size.X > 1 ? Size.X : 1280;
        var height = Size.Y > 1 ? Size.Y : 640;
        _metersPerPixel = Math.Clamp(
            Math.Max(
                bounds.WidthMillimeters / 1000d / width,
                bounds.HeightMillimeters / 1000d / height) * 1.08,
            MinimumMetersPerPixel,
            MaximumMetersPerPixel);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(
            new Rect2(Vector2.Zero, Size),
            new Color(0.035f, 0.055f, 0.075f));
        if (!_initialized || Size.X <= 1 || Size.Y <= 1)
        {
            return;
        }

        EnsureTerrainTexture();
        var world = _controller.DisplayWorld;
        var viewport = new MapViewport(
            VisibleWorldBounds(),
            _metersPerPixel);
        var frame = _controller.Project(viewport);
        DrawTerrain(world.BaseWorld.Parameters.Bounds);
        DrawRivers(frame);
        DrawTracks(frame);
        DrawCities(frame);
        DrawStations(frame);
        DrawNodesAndPorts(frame);
        DrawConnections(frame);
        DrawDraft();
        DrawSelectionBox();
        DrawOverlay(frame);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!_initialized)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton mouse:
                HandleMouseButton(mouse);
                break;
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
            case InputEventKey key when !key.Echo:
                HandleKey(key);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
        {
            ZoomAt(mouse.Position, 0.82);
            AcceptEvent();
            return;
        }

        if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
        {
            ZoomAt(mouse.Position, 1.22);
            AcceptEvent();
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Right &&
            mouse.Pressed &&
            IsContinuousDrawingTool() &&
            _controller.DrawingState !=
            RailSim.Core.Editing.RailDrawingState.Idle)
        {
            _controller.UndoDrawingStep();
            _selectedWaypoint = Math.Min(
                _selectedWaypoint,
                _controller.DraftWaypoints.Length - 1);
            AcceptEvent();
            return;
        }

        if (mouse.ButtonIndex is MouseButton.Middle or MouseButton.Right)
        {
            _panning = mouse.Pressed;
            _lastPointer = mouse.Position;
            AcceptEvent();
            return;
        }

        if (mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (!mouse.Pressed)
        {
            if (_boxSelecting)
            {
                _boxSelecting = false;
                _boxEnd = mouse.Position;
                SelectBoxContents();
                AcceptEvent();
            }

            if (_draggedWaypoint >= 0)
            {
                _controller.FinishWaypointDrag();
                _draggedWaypoint = -1;
            }

            return;
        }

        GrabFocus();
        if (_controller.CurrentTool == WorldEditorTool.BoxSelect)
        {
            _boxSelecting = true;
            _boxStart = mouse.Position;
            _boxEnd = mouse.Position;
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (_controller.CurrentTool == WorldEditorTool.SplitTrack)
        {
            _controller.SplitTrackAt(
                ScreenToWorld(mouse.Position),
                _metersPerPixel * 15);
            AcceptEvent();
            return;
        }

        if (_controller.CurrentTool == WorldEditorTool.RealignTrack &&
            _controller.DraftWaypoints.IsEmpty)
        {
            _controller.BeginRealignAt(
                ScreenToWorld(mouse.Position),
                _metersPerPixel * 15);
            _selectedWaypoint = -1;
            AcceptEvent();
            return;
        }

        if (_controller.CurrentTool == WorldEditorTool.Inspect)
        {
            _controller.InspectAt(
                ScreenToWorld(mouse.Position),
                _metersPerPixel * 15);
            AcceptEvent();
            return;
        }

        if (IsContinuousDrawingTool())
        {
            _latestPointer = mouse.Position;
            _snappingDisabled = mouse.ShiftPressed;
            _previewDirty = false;
            _controller.UpdateDrawingPreview(
                ScreenToWorld(mouse.Position),
                _metersPerPixel,
                _snappingDisabled);
            if (mouse.DoubleClick)
            {
                _controller.CommitDrawingAtPreview();
                _selectedWaypoint = -1;
                AcceptEvent();
                return;
            }

            _draggedWaypoint = HitDraftWaypoint(mouse.Position);
            _selectedWaypoint = _draggedWaypoint;
            if (_draggedWaypoint >= 0)
            {
                _controller.BeginWaypointDrag(_draggedWaypoint);
            }
            else
            {
                var result = _controller.ConfirmDrawingPoint();
                if (result.Accepted)
                {
                    _selectedWaypoint =
                        _controller.DraftWaypoints.Length - 1;
                }
            }

            AcceptEvent();
            return;
        }

        if (mouse.DoubleClick)
        {
            _controller.CommitDraft();
            AcceptEvent();
            return;
        }

        _draggedWaypoint = HitDraftWaypoint(mouse.Position);
        _selectedWaypoint = _draggedWaypoint;
        if (_draggedWaypoint < 0)
        {
            _selectedWaypoint = _controller.AddWaypoint(
                ScreenToWorld(mouse.Position),
                _metersPerPixel * 15);
        }

        AcceptEvent();
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        if (_panning)
        {
            var delta = motion.Position - _lastPointer;
            _centerXMillimeters -= delta.X * _metersPerPixel * 1000d;
            _centerYMillimeters -= delta.Y * _metersPerPixel * 1000d;
            _lastPointer = motion.Position;
            _latestPointer = motion.Position;
            _previewDirty = IsContinuousDrawingTool();
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (_boxSelecting)
        {
            _boxEnd = motion.Position;
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (_draggedWaypoint >= 0 &&
            motion.ButtonMask.HasFlag(MouseButtonMask.Left))
        {
            _controller.MoveDrawingWaypoint(
                _draggedWaypoint,
                ScreenToWorld(motion.Position),
                _metersPerPixel,
                motion.ShiftPressed);
            AcceptEvent();
            return;
        }

        if (IsContinuousDrawingTool())
        {
            _latestPointer = motion.Position;
            _snappingDisabled = motion.ShiftPressed;
            _previewDirty = true;
            AcceptEvent();
        }
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Keycode == Key.Shift)
        {
            _snappingDisabled = key.Pressed;
            _previewDirty = true;
            AcceptEvent();
            return;
        }

        if (!key.Pressed)
        {
            return;
        }

        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            _controller.CommitDraft();
            AcceptEvent();
        }
        else if (key.Keycode == Key.Escape)
        {
            if (_draggedWaypoint >= 0)
            {
                _controller.CancelWaypointDrag();
                _draggedWaypoint = -1;
            }
            else
            {
                _controller.CancelDraft();
            }

            _selectedWaypoint = -1;
            AcceptEvent();
        }
        else if (key.Keycode is Key.Delete or Key.Backspace)
        {
            if ((_controller.CurrentTool is
                     WorldEditorTool.DrawDoubleTrack or
                     WorldEditorTool.DrawConnector or
                     WorldEditorTool.RealignTrack) &&
                _controller.DraftWaypoints.Length > 0)
            {
                var index = _selectedWaypoint >= 0
                    ? _selectedWaypoint
                    : _controller.DraftWaypoints.Length - 1;
                _controller.RemoveWaypoint(index);
                _selectedWaypoint = Math.Min(
                    index,
                    _controller.DraftWaypoints.Length - 1);
            }
            else
            {
                _controller.DeleteSelection();
            }

            AcceptEvent();
        }
    }

    private void ZoomAt(Vector2 pointer, double factor)
    {
        var before = ScreenToWorld(pointer);
        _metersPerPixel = Math.Clamp(
            _metersPerPixel * factor,
            MinimumMetersPerPixel,
            MaximumMetersPerPixel);
        var after = ScreenToWorld(pointer);
        _centerXMillimeters +=
            before.XMillimeters - after.XMillimeters;
        _centerYMillimeters +=
            before.YMillimeters - after.YMillimeters;
        _latestPointer = pointer;
        _previewDirty = IsContinuousDrawingTool();
        QueueRedraw();
    }

    private void EnsureTerrainTexture()
    {
        var terrain = _controller.DisplayWorld.BaseWorld.Terrain;
        if (_terrainHash == terrain.ContentHash && _terrainTexture is not null)
        {
            return;
        }

        var pixels = new byte[checked(terrain.Width * terrain.Height * 4)];
        for (var index = 0; index < terrain.ElevationMeters.Length; index++)
        {
            var color = TerrainColor(terrain.ElevationMeters[index]);
            var offset = index * 4;
            pixels[offset] = checked((byte)color.R8);
            pixels[offset + 1] = checked((byte)color.G8);
            pixels[offset + 2] = checked((byte)color.B8);
            pixels[offset + 3] = byte.MaxValue;
        }

        var image = Image.CreateFromData(
            terrain.Width,
            terrain.Height,
            false,
            Image.Format.Rgba8,
            pixels);
        _terrainTexture = ImageTexture.CreateFromImage(image);
        _terrainHash = terrain.ContentHash;
    }

    private void DrawTerrain(WorldBounds bounds)
    {
        if (_terrainTexture is null)
        {
            return;
        }

        var minimum = WorldToScreen(bounds.Minimum);
        var maximum = WorldToScreen(bounds.Maximum);
        DrawTextureRect(
            _terrainTexture,
            new Rect2(minimum, maximum - minimum),
            false,
            new Color(1, 1, 1, 0.92f));
    }

    private void DrawRivers(WorldMapFrame frame)
    {
        foreach (var river in frame.Rivers)
        {
            var points = river.Points.Select(WorldToScreen).ToArray();
            DrawPolyline(
                points,
                river.WidthClass == RiverWidthClass.Major
                    ? new Color(0.16f, 0.55f, 0.88f, 0.9f)
                    : new Color(0.24f, 0.62f, 0.86f, 0.72f),
                river.WidthClass == RiverWidthClass.Major ? 3.5f : 2f,
                true);
        }
    }

    private void DrawTracks(WorldMapFrame frame)
    {
        foreach (var track in frame.Tracks)
        {
            var points = track.Points
                .Select(WorldToScreen)
                .ToArray();
            var selected = _controller.IsSelected(track.SourceId);
            var color = selected
                ? new Color(1f, 0.3f, 0.12f)
                : track.IsAggregate
                    ? new Color(0.96f, 0.68f, 0.25f)
                    : track.LateralOffsetMillimeters < 0
                        ? new Color(0.18f, 0.82f, 0.98f)
                        : new Color(0.36f, 0.92f, 0.57f);
            DrawPolyline(
                points,
                color,
                track.IsAggregate ? 4.5f : 3f,
                true);
            if (!track.IsAggregate)
            {
                DrawDirectionMarker(points, color);
            }
        }
    }

    private void DrawDirectionMarker(
        IReadOnlyList<Vector2> points,
        Color color)
    {
        if (points.Count < 2)
        {
            return;
        }

        var segment = Math.Clamp(points.Count / 2, 1, points.Count - 1);
        var start = points[segment - 1];
        var end = points[segment];
        var vector = end - start;
        if (vector.LengthSquared() <= 0.001f)
        {
            return;
        }

        var direction = vector.Normalized();
        var normal = new Vector2(-direction.Y, direction.X);
        var tip = start.Lerp(end, 0.55f);
        DrawLine(
            tip,
            tip - (direction * 8f) + (normal * 4f),
            color,
            2f,
            true);
        DrawLine(
            tip,
            tip - (direction * 8f) - (normal * 4f),
            color,
            2f,
            true);
    }

    private void DrawCities(WorldMapFrame frame)
    {
        foreach (var city in frame.Cities)
        {
            var position = WorldToScreen(city.Position);
            var selected =
                _controller.IsSelected(city.SourceId);
            var radius = selected ? 9f : city.Tier switch
            {
                CityTier.Mega => 7f,
                CityTier.Large => 5.5f,
                CityTier.Medium => 4f,
                CityTier.Small => 2.8f,
                _ => 3f
            };
            DrawCircle(
                position,
                radius,
                selected
                    ? new Color(1f, 0.34f, 0.12f)
                    : new Color(0.98f, 0.75f, 0.31f),
                true,
                -1,
                true);
            if (frame.Lod != MapLod.Regional || city.Tier <= CityTier.Large)
            {
                DrawString(
                    ThemeDB.FallbackFont,
                    position + new Vector2(8, -5),
                    city.Name,
                    HorizontalAlignment.Left,
                    -1,
                    13,
                    new Color(0.96f, 0.97f, 1f));
            }
        }
    }

    private void DrawStations(WorldMapFrame frame)
    {
        if (frame.Lod == MapLod.Regional)
        {
            return;
        }

        foreach (var station in frame.Stations)
        {
            var position = WorldToScreen(station.Position);
            var selected =
                _controller.IsSelected(station.SourceId);
            DrawCircle(
                position,
                selected ? 6f : 3.5f,
                selected
                    ? new Color(1f, 0.42f, 0.16f)
                    : new Color(0.98f, 0.98f, 1f),
                true,
                -1,
                true);
            if (frame.Lod == MapLod.Edit)
            {
                DrawString(
                    ThemeDB.FallbackFont,
                    position + new Vector2(6, 15),
                    station.Name,
                    HorizontalAlignment.Left,
                    -1,
                    11,
                    new Color(0.88f, 0.91f, 0.96f));
            }
        }
    }

    private void DrawConnections(WorldMapFrame frame)
    {
        foreach (var connection in frame.Connections)
        {
            var selected =
                _controller.IsSelected(connection.SourceId);
            DrawCircle(
                WorldToScreen(connection.Position),
                selected ? 10f : 7f,
                selected
                    ? new Color(1f, 0.2f, 0.1f, 0.85f)
                    : new Color(0.96f, 0.28f, 0.6f, 0.32f),
                false,
                2f,
                true);
        }
    }

    private void DrawNodesAndPorts(WorldMapFrame frame)
    {
        foreach (var node in frame.Nodes)
        {
            var selected =
                _controller.IsSelected(node.SourceId);
            DrawCircle(
                WorldToScreen(node.Position),
                selected
                    ? 8f
                    : node.Kind == RailNodeKind.Junction ? 6f : 4.5f,
                selected
                    ? new Color(1f, 0.3f, 0.14f, 1f)
                    : new Color(1f, 0.82f, 0.25f, 0.92f),
                false,
                2f,
                true);
        }

        foreach (var port in frame.Ports)
        {
            var color = port.Direction switch
            {
                RailPortDirection.Inbound =>
                    new Color(0.95f, 0.3f, 0.42f, 0.8f),
                RailPortDirection.Outbound =>
                    new Color(0.25f, 0.86f, 1f, 0.8f),
                _ => new Color(0.72f, 0.5f, 1f, 0.8f)
            };
            DrawCircle(
                WorldToScreen(port.Position),
                2.3f,
                color,
                true,
                -1,
                true);
        }
    }

    private void DrawDraft()
    {
        var preview = _controller.DrawingPreview;
        if (preview is not null)
        {
            var center = preview.Draft.SampledPoints
                .Select(WorldToScreen)
                .ToArray();
            var up = preview.UpTrackPoints
                .Select(WorldToScreen)
                .ToArray();
            var down = preview.DownTrackPoints
                .Select(WorldToScreen)
                .ToArray();
            DrawPolyline(
                up,
                new Color(0.2f, 0.9f, 1f, 0.52f),
                2.6f,
                true);
            DrawPolyline(
                down,
                new Color(0.2f, 0.9f, 1f, 0.52f),
                2.6f,
                true);
            DrawDashedPolyline(
                center,
                new Color(1f, 0.88f, 0.25f, 0.8f),
                1.4f);

            var waypointSegments =
                preview.Draft.Waypoints.Length - 1;
            var samplesPerSegment = waypointSegments > 0
                ? (center.Length - 1) / waypointSegments
                : 0;
            var confirmedPointCount = Math.Min(
                center.Length,
                Math.Max(
                    0,
                    ((_controller.DraftWaypoints.Length - 1) *
                     samplesPerSegment) + 1));
            if (confirmedPointCount >= 2)
            {
                DrawPolyline(
                    up[..confirmedPointCount],
                    new Color(0.18f, 0.94f, 1f, 0.96f),
                    3.2f,
                    true);
                DrawPolyline(
                    down[..confirmedPointCount],
                    new Color(0.18f, 0.94f, 1f, 0.96f),
                    3.2f,
                    true);
            }

            DrawSnapFeedback(preview);
        }
        else if (_controller.Draft is { } draft)
        {
            DrawPolyline(
                draft.SampledPoints.Select(WorldToScreen).ToArray(),
                new Color(1f, 0.93f, 0.36f),
                3f,
                true);
        }

        DrawInvalidSnapFeedback();

        var waypoints = _controller.DraftWaypoints;
        for (var index = 0; index < waypoints.Length; index++)
        {
            DrawCircle(
                WorldToScreen(waypoints[index].Position),
                index == _selectedWaypoint ? 7f : 5f,
                index == _selectedWaypoint
                    ? new Color(1f, 0.22f, 0.1f)
                    : new Color(1f, 0.52f, 0.18f),
                true,
                -1,
                true);
        }
    }

    private void DrawInvalidSnapFeedback()
    {
        if (_controller.InvalidDrawingPreview is not { } invalid)
        {
            return;
        }

        var position = WorldToScreen(invalid.Snap.Waypoint.Position);
        var color = new Color(1f, 0.18f, 0.12f, 0.98f);
        DrawArc(
            position,
            11,
            0,
            MathF.Tau,
            32,
            color,
            3f,
            true);
        DrawLine(
            position + new Vector2(-7, -7),
            position + new Vector2(7, 7),
            color,
            2.4f,
            true);
        DrawLine(
            position + new Vector2(-7, 7),
            position + new Vector2(7, -7),
            color,
            2.4f,
            true);
        DrawString(
            ThemeDB.FallbackFont,
            position + new Vector2(15, -12),
            "无效候选",
            HorizontalAlignment.Left,
            -1,
            13,
            color);
    }

    private void DrawSnapFeedback(RailSim.Core.Editing.RailDrawingPreview preview)
    {
        var snap = preview.Snap;
        var position = WorldToScreen(snap.Waypoint.Position);
        if (snap.Kind == RailSim.Core.Editing.RailSnapKind.Free)
        {
            DrawLine(
                position + new Vector2(-6, 0),
                position + new Vector2(6, 0),
                Colors.White,
                1.5f,
                true);
            DrawLine(
                position + new Vector2(0, -6),
                position + new Vector2(0, 6),
                Colors.White,
                1.5f,
                true);
        }
        else
        {
            DrawArc(
                position,
                9,
                0,
                MathF.Tau,
                32,
                new Color(0.25f, 0.78f, 1f),
                2.2f,
                true);
        }

        var snapLabel = snap.Kind switch
        {
            RailSim.Core.Editing.RailSnapKind.RailNode => "节点",
            RailSim.Core.Editing.RailSnapKind.Station => "车站",
            RailSim.Core.Editing.RailSnapKind.TrackSplit => "接入轨道",
            RailSim.Core.Editing.RailSnapKind.AngleGuide =>
                $"{snap.GuideAngleDegrees:+0;-0;0}°",
            _ => "自由"
        };
        DrawString(
            ThemeDB.FallbackFont,
            position + new Vector2(12, -10),
            snapLabel,
            HorizontalAlignment.Left,
            -1,
            13,
            new Color(0.82f, 0.94f, 1f));

        var metrics = preview.Metrics;
        var radius = metrics.MinimumRadiusMeters.HasValue
            ? $"R {metrics.MinimumRadiusMeters.Value:N0}m"
            : "R ∞";
        var metricText =
            $"本段 {metrics.CurrentSegmentLengthMillimeters / 1_000_000d:N1}km  " +
            $"总长 {metrics.TotalLengthMillimeters / 1_000_000d:N1}km  " +
            $"{radius}  预计 {metrics.EstimatedSpeedKph}km/h  " +
            "纵断面待详细设计";
        var labelPosition = new Vector2(
            Math.Clamp(_latestPointer.X + 18, 12, Size.X - 420),
            Math.Clamp(_latestPointer.Y + 28, 24, Size.Y - 14));
        DrawString(
            ThemeDB.FallbackFont,
            labelPosition,
            metricText,
            HorizontalAlignment.Left,
            -1,
            13,
            new Color(1f, 0.92f, 0.55f));
    }

    private void DrawDashedPolyline(
        Vector2[] points,
        Color color,
        float width)
    {
        for (var index = 0; index < points.Length - 1; index++)
        {
            if (index % 2 == 0)
            {
                DrawLine(
                    points[index],
                    points[index + 1],
                    color,
                    width,
                    true);
            }
        }
    }

    private void DrawSelectionBox()
    {
        if (!_boxSelecting)
        {
            return;
        }

        var position = new Vector2(
            Math.Min(_boxStart.X, _boxEnd.X),
            Math.Min(_boxStart.Y, _boxEnd.Y));
        var size = new Vector2(
            Math.Abs(_boxEnd.X - _boxStart.X),
            Math.Abs(_boxEnd.Y - _boxStart.Y));
        DrawRect(
            new Rect2(position, size),
            new Color(0.2f, 0.72f, 1f, 0.18f),
            true);
        DrawRect(
            new Rect2(position, size),
            new Color(0.3f, 0.82f, 1f, 0.95f),
            false,
            2f);
    }

    private void SelectBoxContents()
    {
        var first = ScreenToWorld(_boxStart);
        var second = ScreenToWorld(_boxEnd);
        var minimum = new WorldPoint(
            Math.Min(first.XMillimeters, second.XMillimeters),
            Math.Min(first.YMillimeters, second.YMillimeters));
        var maximum = new WorldPoint(
            Math.Max(first.XMillimeters, second.XMillimeters),
            Math.Max(first.YMillimeters, second.YMillimeters));
        if (minimum.XMillimeters == maximum.XMillimeters)
        {
            maximum = maximum.OffsetMillimeters(1, 0);
        }

        if (minimum.YMillimeters == maximum.YMillimeters)
        {
            maximum = maximum.OffsetMillimeters(0, 1);
        }

        _controller.SelectInBounds(
            new WorldBounds(minimum, maximum));
        QueueRedraw();
    }

    private void DrawOverlay(WorldMapFrame frame)
    {
        var text =
            $"LOD {frame.Lod} | {Math.Round(_metersPerPixel)} m/px | " +
            $"城市 {frame.Cities.Length} | 轨道 {frame.Tracks.Length}";
        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(16, 24),
            text,
            HorizontalAlignment.Left,
            -1,
            14,
            new Color(0.95f, 0.97f, 1f));
    }

    private WorldBounds VisibleWorldBounds()
    {
        var halfWidth = Size.X * _metersPerPixel * 500d;
        var halfHeight = Size.Y * _metersPerPixel * 500d;
        return new WorldBounds(
            new WorldPoint(
                checked((long)Math.Round(_centerXMillimeters - halfWidth)),
                checked((long)Math.Round(_centerYMillimeters - halfHeight))),
            new WorldPoint(
                checked((long)Math.Round(_centerXMillimeters + halfWidth)),
                checked((long)Math.Round(_centerYMillimeters + halfHeight))));
    }

    private Vector2 WorldToScreen(WorldPoint point)
    {
        return new Vector2(
            (float)(((point.XMillimeters - _centerXMillimeters) / 1000d /
                     _metersPerPixel) +
                    (Size.X / 2d)),
            (float)(((point.YMillimeters - _centerYMillimeters) / 1000d /
                     _metersPerPixel) +
                    (Size.Y / 2d)));
    }

    private WorldPoint ScreenToWorld(Vector2 point)
    {
        return new WorldPoint(
            checked((long)Math.Round(
                _centerXMillimeters +
                ((point.X - (Size.X / 2d)) *
                 _metersPerPixel *
                 1000d))),
            checked((long)Math.Round(
                _centerYMillimeters +
                ((point.Y - (Size.Y / 2d)) *
                 _metersPerPixel *
                 1000d))));
    }

    private int HitDraftWaypoint(Vector2 pointer)
    {
        var waypoints = _controller.DraftWaypoints;
        for (var index = 0; index < waypoints.Length; index++)
        {
            if (WorldToScreen(waypoints[index].Position).DistanceTo(pointer) <= 10)
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsContinuousDrawingTool()
    {
        return _controller.CurrentTool is
            WorldEditorTool.DrawDoubleTrack or
            WorldEditorTool.DrawConnector;
    }

    private void HandleControllerChanged()
    {
        QueueRedraw();
    }

    private static Color TerrainColor(int elevationMeters)
    {
        if (elevationMeters < 260)
        {
            return new Color(0.18f, 0.34f, 0.27f);
        }

        if (elevationMeters < 620)
        {
            return new Color(0.27f, 0.42f, 0.28f);
        }

        if (elevationMeters < 1_050)
        {
            return new Color(0.42f, 0.39f, 0.29f);
        }

        return new Color(0.58f, 0.57f, 0.54f);
    }
}
