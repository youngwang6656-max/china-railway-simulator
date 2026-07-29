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
    private Vector2 _lastPointer;
    private int _draggedWaypoint = -1;

    public double MetersPerPixel => _metersPerPixel;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
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
            case InputEventKey key when key.Pressed && !key.Echo:
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
            _draggedWaypoint = -1;
            return;
        }

        GrabFocus();
        if (_controller.CurrentTool == WorldEditorTool.Inspect)
        {
            _controller.InspectAt(
                ScreenToWorld(mouse.Position),
                _metersPerPixel * 15);
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
        if (_draggedWaypoint < 0)
        {
            _controller.AddWaypoint(
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
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (_draggedWaypoint >= 0 &&
            motion.ButtonMask.HasFlag(MouseButtonMask.Left))
        {
            _controller.MoveWaypoint(
                _draggedWaypoint,
                ScreenToWorld(motion.Position),
                _metersPerPixel * 15);
            AcceptEvent();
        }
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            _controller.CommitDraft();
            AcceptEvent();
        }
        else if (key.Keycode == Key.Escape)
        {
            _controller.CancelDraft();
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
            var color = track.IsAggregate
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
                _controller.SelectedObjectId == city.SourceId;
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
                _controller.SelectedObjectId == station.SourceId;
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
            DrawCircle(
                WorldToScreen(connection.Position),
                7f,
                new Color(0.96f, 0.28f, 0.6f, 0.32f),
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
                _controller.SelectedObjectId == node.SourceId;
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
        var draft = _controller.Draft;
        if (draft is not null)
        {
            DrawPolyline(
                draft.SampledPoints.Select(WorldToScreen).ToArray(),
                new Color(1f, 0.93f, 0.36f),
                3f,
                true);
        }

        var waypoints = _controller.DraftWaypoints;
        for (var index = 0; index < waypoints.Length; index++)
        {
            DrawCircle(
                WorldToScreen(waypoints[index].Position),
                5f,
                new Color(1f, 0.52f, 0.18f),
                true,
                -1,
                true);
        }
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
