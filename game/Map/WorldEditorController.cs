using System.Collections.Immutable;
using RailSim.Core.Commands;
using RailSim.Core.Editing;
using RailSim.Core.Persistence;
using RailSim.Core.Projection;
using RailSim.Core.Rail;
using RailSim.Core.Time;
using RailSim.Core.World;

namespace RailSim.App.Map;

public enum WorldEditorTool
{
    Inspect,
    DrawDoubleTrack
}

public sealed class WorldEditorController
{
    private readonly IWorldGenerator _generator;
    private readonly IWorldProjection _projection;
    private readonly IWorldRepository _repository;
    private readonly string _savePath;
    private readonly List<DraftWaypoint> _draftWaypoints = [];
    private WorldEditorSession _session = null!;
    private SimulationCommandBus _commandBus = null!;
    private long _commandTick;
    private StableId? _mainGroupId;
    private StableId? _secondLineGroupId;
    private StableId? _mainJunctionStationId;
    private StableId? _secondLineJunctionStationId;
    private bool _connectorBuilt;

    private WorldEditorController(
        IWorldGenerator generator,
        IWorldProjection projection,
        IWorldRepository repository,
        string savePath,
        ulong seed)
    {
        _generator = generator;
        _projection = projection;
        _repository = repository;
        _savePath = savePath;
        NewWorld(seed);
    }

    public event Action? Changed;

    public WorldState World => _session.World;

    public ulong Seed => World.BaseWorld.Parameters.Seed;

    public WorldEditorTool CurrentTool { get; private set; }

    public string LastMessage { get; private set; } = "世界已生成";

    public ImmutableArray<DraftWaypoint> DraftWaypoints =>
        _draftWaypoints.ToImmutableArray();

    public AlignmentDraft? Draft =>
        _draftWaypoints.Count >= 2
            ? new ControlPointSplineStrategy().Build(
                _draftWaypoints.ToImmutableArray())
            : null;

    public static WorldEditorController CreateDefault(
        ulong seed,
        string savePath)
    {
        return new WorldEditorController(
            new ProceduralWorldGenerator(),
            new WorldMapProjection(new DefaultMapLodPolicy()),
            new JsonWorldRepository(),
            savePath,
            seed);
    }

    public WorldMapFrame Project(MapViewport viewport)
    {
        return _projection.Project(World, viewport);
    }

    public void NewWorld(ulong seed)
    {
        var generated = _generator.Generate(
            WorldGenerationParameters.CreateRegional(seed));
        ResetSession(new WorldState(0, generated, RailTopology.Empty));
        _draftWaypoints.Clear();
        _mainGroupId = null;
        _secondLineGroupId = null;
        _mainJunctionStationId = null;
        _secondLineJunctionStationId = null;
        _connectorBuilt = false;
        CurrentTool = WorldEditorTool.Inspect;
        LastMessage = $"新世界 {seed} 已生成";
        Changed?.Invoke();
    }

    public void SetTool(WorldEditorTool tool)
    {
        CurrentTool = tool;
        if (tool != WorldEditorTool.DrawDoubleTrack)
        {
            _draftWaypoints.Clear();
        }

        LastMessage = tool == WorldEditorTool.DrawDoubleTrack
            ? "双线工具：左键布点，Enter 或双击提交，Esc 取消"
            : "查看工具";
        Changed?.Invoke();
    }

    public void AddWaypoint(
        WorldPoint point,
        double snapRadiusMeters)
    {
        var snapped = FindNearestStation(point, snapRadiusMeters);
        _draftWaypoints.Add(
            snapped is null
                ? new DraftWaypoint(point, null)
                : new DraftWaypoint(snapped.Position, snapped.Id));
        LastMessage = snapped is null
            ? $"已添加控制点 {_draftWaypoints.Count}"
            : $"已吸附到 {snapped.Name}";
        Changed?.Invoke();
    }

    public void MoveWaypoint(
        int index,
        WorldPoint point,
        double snapRadiusMeters)
    {
        if (index < 0 || index >= _draftWaypoints.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var snapped = FindNearestStation(point, snapRadiusMeters);
        _draftWaypoints[index] = snapped is null
            ? new DraftWaypoint(point, null)
            : new DraftWaypoint(snapped.Position, snapped.Id);
        LastMessage = "控制点已调整";
        Changed?.Invoke();
    }

    public void CancelDraft()
    {
        _draftWaypoints.Clear();
        LastMessage = "已取消线路草案";
        Changed?.Invoke();
    }

    public CommandStatus CommitDraft()
    {
        if (Draft is not { } draft)
        {
            LastMessage = "至少需要两个控制点";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        var patch = new DoubleTrackCorridorStrategy(
            NewIdSource("manual"))
            .CreatePatch(
                World.RailTopology,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var result = Submit(
            new ApplyRailEditCommand(patch, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _draftWaypoints.Clear();
        }

        return result.Status;
    }

    public CommandStatus BuildDemoMainLine()
    {
        if (_mainGroupId.HasValue)
        {
            LastMessage = "示例干线已经存在";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        var stations = DemoMainStations();
        var patch = BuildDoubleTrackPatch("demo-main", stations);
        var result = Submit(
            new ApplyRailEditCommand(patch, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _mainGroupId = patch.AddedTrackGroups.Single().Id;
            _mainJunctionStationId = stations[2].Id;
            LastMessage = "A—C—D—B 示例双线已建设";
            Changed?.Invoke();
        }

        return result.Status;
    }

    public CommandStatus BuildDemoSecondLine()
    {
        if (_secondLineGroupId.HasValue)
        {
            LastMessage = "线路二已经存在";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        if (!_mainJunctionStationId.HasValue)
        {
            BuildDemoMainLine();
        }

        var stations = DemoSecondLineStations();
        var waypoints = stations
            .Select(station => new DraftWaypoint(station.Position, station.Id))
            .ToImmutableArray();
        var draft = new ControlPointSplineStrategy().Build(waypoints);
        var patch = new DoubleTrackCorridorStrategy(
            NewIdSource("demo-second"))
            .CreatePatch(
                World.RailTopology,
                draft,
                new TrackPlacementOptions(5_000, 200));
        var result = Submit(
            new ApplyRailEditCommand(patch, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _secondLineGroupId = patch.AddedTrackGroups.Single().Id;
            _secondLineJunctionStationId = stations[1].Id;
            LastMessage = "线路二已建设；几何交叉默认不互通";
            Changed?.Invoke();
        }

        return result.Status;
    }

    public CommandStatus BuildDemoConnector()
    {
        if (_connectorBuilt)
        {
            LastMessage = "丁市双向联络线已经存在";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        if (!_mainGroupId.HasValue)
        {
            BuildDemoMainLine();
        }

        if (!_secondLineGroupId.HasValue)
        {
            BuildDemoSecondLine();
        }

        var mainGroupId = _mainGroupId ??
            throw new InvalidOperationException("The demo main line is missing.");
        var secondLineGroupId = _secondLineGroupId ??
            throw new InvalidOperationException("The demo second line is missing.");
        var mainStation = World.BaseWorld.Stations[
            _mainJunctionStationId!.Value];
        var secondStation = World.BaseWorld.Stations[
            _secondLineJunctionStationId!.Value];
        var linkDraft = new ControlPointSplineStrategy().Build(
        [
            new DraftWaypoint(mainStation.Position, mainStation.Id),
            new DraftWaypoint(secondStation.Position, secondStation.Id)
        ]);
        var linkPatch = new DoubleTrackCorridorStrategy(
            NewIdSource("demo-link"))
            .CreatePatch(
                World.RailTopology,
                linkDraft,
                new TrackPlacementOptions(5_000, 160));
        var candidate = linkPatch.ApplyTo(World.RailTopology);
        var movementPatch = BuildConnectorMovements(
            candidate,
            linkPatch.AddedTrackGroups.Single(),
            candidate.TrackGroups[mainGroupId],
            candidate.TrackGroups[secondLineGroupId],
            mainStation.Id,
            secondStation.Id);
        var completePatch = linkPatch.Combine(movementPatch);
        var result = Submit(
            new ApplyRailEditCommand(completePatch, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _connectorBuilt = true;
            LastMessage = "丁市双向联络线已建设并开放四个明确行车方向";
            Changed?.Invoke();
        }

        return result.Status;
    }

    public CommandStatus Undo()
    {
        return Submit(new UndoWorldEditCommand(World.Revision)).Status;
    }

    public CommandStatus Redo()
    {
        return Submit(new RedoWorldEditCommand(World.Revision)).Status;
    }

    public void Save()
    {
        _repository.Save(_savePath, World);
        LastMessage = $"已保存：{_savePath}";
        Changed?.Invoke();
    }

    public void Load()
    {
        ResetSession(_repository.Load(_savePath));
        _draftWaypoints.Clear();
        _mainGroupId = null;
        _secondLineGroupId = null;
        _mainJunctionStationId = null;
        _secondLineJunctionStationId = null;
        _connectorBuilt = false;
        CurrentTool = WorldEditorTool.Inspect;
        LastMessage = $"已读取：{_savePath}";
        Changed?.Invoke();
    }

    private RailGraphPatch BuildConnectorMovements(
        RailTopology topology,
        TrackGroup linkGroup,
        TrackGroup mainGroup,
        TrackGroup secondGroup,
        StableId mainStationId,
        StableId secondStationId)
    {
        var mainNode = FindNodeForStation(topology, mainStationId);
        var secondNode = FindNodeForStation(topology, secondStationId);
        var linkOutgoingMain = OutgoingAt(topology, linkGroup, mainNode.Id);
        var linkIncomingMain = IncomingAt(topology, linkGroup, mainNode.Id);
        var linkIncomingSecond = IncomingAt(
            topology,
            linkGroup,
            secondNode.Id);
        var linkOutgoingSecond = OutgoingAt(
            topology,
            linkGroup,
            secondNode.Id);
        var movements = ImmutableArray.Create(
            new ConnectorMovement(
                mainNode.Id,
                IncomingAt(topology, mainGroup, mainNode.Id).Id,
                linkOutgoingMain.Id),
            new ConnectorMovement(
                mainNode.Id,
                linkIncomingMain.Id,
                OutgoingAt(topology, mainGroup, mainNode.Id).Id),
            new ConnectorMovement(
                secondNode.Id,
                linkIncomingSecond.Id,
                OutgoingAt(topology, secondGroup, secondNode.Id).Id),
            new ConnectorMovement(
                secondNode.Id,
                IncomingAt(topology, secondGroup, secondNode.Id).Id,
                linkOutgoingSecond.Id));
        return new ExplicitConnectorStrategy(NewIdSource("demo-movement"))
            .CreateConnectionPatch(topology, movements);
    }

    private CommandResult Submit(ISimulationCommand command)
    {
        var tick = new SimulationTick(checked(++_commandTick));
        var envelope = _commandBus.Enqueue(command, tick);
        var result = _commandBus.DispatchDue(tick).Single(
            item => item.Sequence == envelope.Sequence);
        LastMessage = result.Status == CommandStatus.Accepted
            ? "操作成功"
            : $"{result.Code}: {result.Message}";
        Changed?.Invoke();
        return result;
    }

    private void ResetSession(WorldState world)
    {
        _session = new WorldEditorSession(world, new RailEditValidator());
        _commandBus = new SimulationCommandBus();
        WorldEditCommandRegistration.Register(_commandBus, _session);
        _commandTick = 0;
    }

    private RailGraphPatch BuildDoubleTrackPatch(
        string scope,
        ImmutableArray<StationAnchor> stations)
    {
        var draft = new ControlPointSplineStrategy().Build(
            stations
                .Select(
                    station => new DraftWaypoint(
                        station.Position,
                        station.Id))
                .ToImmutableArray());
        return new DoubleTrackCorridorStrategy(NewIdSource(scope))
            .CreatePatch(
                World.RailTopology,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
    }

    private ImmutableArray<StationAnchor> DemoMainStations()
    {
        var ordered = World.BaseWorld.Stations.Values
            .OrderBy(station => station.Position.XMillimeters)
            .ThenBy(station => station.Id)
            .ToArray();
        return
        [
            ordered[4],
            ordered[20],
            ordered[38],
            ordered[58]
        ];
    }

    private ImmutableArray<StationAnchor> DemoSecondLineStations()
    {
        var mainIds = DemoMainStations()
            .Select(station => station.Id)
            .ToHashSet();
        var junction = World.BaseWorld.Stations[
            _mainJunctionStationId!.Value];
        var nearest = World.BaseWorld.Stations.Values
            .Where(station => !mainIds.Contains(station.Id))
            .OrderBy(
                station =>
                    station.Position.DistanceMetersTo(junction.Position))
            .ThenBy(station => station.Id)
            .Take(3)
            .OrderBy(station => station.Position.YMillimeters)
            .ToArray();
        return [nearest[0], nearest[1], nearest[2]];
    }

    private StationAnchor? FindNearestStation(
        WorldPoint point,
        double radiusMeters)
    {
        return World.BaseWorld.Stations.Values
            .Select(
                station => new
                {
                    Station = station,
                    Distance = station.Position.DistanceMetersTo(point)
                })
            .Where(item => item.Distance <= radiusMeters)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Station.Id)
            .Select(item => item.Station)
            .FirstOrDefault();
    }

    private DeterministicStableIdSource NewIdSource(string purpose)
    {
        return new DeterministicStableIdSource(
            $"world-{Seed}-rev-{World.Revision}-{purpose}");
    }

    private static RailNode FindNodeForStation(
        RailTopology topology,
        StableId stationId)
    {
        return topology.Nodes.Values.Single(
            node => node.StationId == stationId);
    }

    private static TrackEdge IncomingAt(
        RailTopology topology,
        TrackGroup group,
        StableId nodeId)
    {
        return group.TrackIds
            .Select(trackId => topology.Tracks[trackId])
            .Where(
                track =>
                    topology.Ports[track.ToPortId].NodeId == nodeId)
            .OrderBy(track => track.Id)
            .First();
    }

    private static TrackEdge OutgoingAt(
        RailTopology topology,
        TrackGroup group,
        StableId nodeId)
    {
        return group.TrackIds
            .Select(trackId => topology.Tracks[trackId])
            .Where(
                track =>
                    topology.Ports[track.FromPortId].NodeId == nodeId)
            .OrderBy(track => track.Id)
            .First();
    }
}
