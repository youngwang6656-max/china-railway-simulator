using System.Collections.Immutable;
using RailSim.Core.Commands;
using RailSim.Core.Editing;
using RailSim.Core.Hosting;
using RailSim.Core.Persistence;
using RailSim.Core.Projection;
using RailSim.Core.Query;
using RailSim.Core.Rail;
using RailSim.Core.Time;
using RailSim.Core.World;

namespace RailSim.App.Map;

public enum WorldEditorTool
{
    Inspect,
    DrawDoubleTrack,
    DrawConnector
}

public sealed class WorldEditorController : ISnapshotContributor
{
    private readonly IWorldGenerator _generator;
    private readonly IWorldProjection _projection;
    private readonly IWorldRepository _repository;
    private readonly string _savePath;
    private readonly List<DraftWaypoint> _draftWaypoints = [];
    private WorldEditorSession _session = null!;
    private readonly SimulationCommandBus _commandBus;
    private ISimulationQuery? _query;
    private SimulationHost? _host;
    private long _commandTick;
    private StableId? _mainGroupId;
    private StableId? _secondLineGroupId;
    private StableId? _connectorGroupId;
    private StableId? _mainJunctionStationId;
    private StableId? _secondLineJunctionStationId;
    private string? _pendingWarningFingerprint;
    private int _connectorMovementPlan;

    private WorldEditorController(
        IWorldGenerator generator,
        IWorldProjection projection,
        IWorldRepository repository,
        SimulationCommandBus commandBus,
        string savePath,
        ulong seed)
    {
        _generator = generator;
        _projection = projection;
        _repository = repository;
        _commandBus = commandBus ??
            throw new ArgumentNullException(nameof(commandBus));
        _savePath = savePath;
        NewWorld(seed);
        WorldEditCommandRegistration.Register(
            _commandBus,
            () => _session);
    }

    public event Action? Changed;

    public string Key => "world";

    public WorldState World => _session.World;

    public WorldState DisplayWorld
    {
        get
        {
            var snapshot = _query?.GetLatest();
            if (snapshot is not null &&
                snapshot.Sections.TryGetValue(Key, out var section) &&
                section is WorldSnapshotSection worldSection)
            {
                return worldSection.World;
            }

            return World;
        }
    }

    public ulong Seed => World.BaseWorld.Parameters.Seed;

    public WorldEditorTool CurrentTool { get; private set; }

    public string LastMessage { get; private set; } = "世界已生成";

    public StableId? SelectedObjectId { get; private set; }

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
        return CreateDefault(seed, savePath, new SimulationCommandBus());
    }

    public static WorldEditorController CreateDefault(
        ulong seed,
        string savePath,
        SimulationCommandBus commandBus)
    {
        return new WorldEditorController(
            new ProceduralWorldGenerator(),
            new WorldMapProjection(new DefaultMapLodPolicy()),
            new JsonWorldRepository(),
            commandBus,
            savePath,
            seed);
    }

    public void AttachQuery(ISimulationQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public void AttachHost(SimulationHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public ISimulationSnapshotSection Capture()
    {
        return new WorldSnapshotSection(World);
    }

    public WorldMapFrame Project(MapViewport viewport)
    {
        return _projection.Project(DisplayWorld, viewport);
    }

    public void NewWorld(ulong seed)
    {
        var generated = _generator.Generate(
            WorldGenerationParameters.CreateRegional(seed));
        ResetSession(new WorldState(0, generated, RailTopology.Empty));
        _draftWaypoints.Clear();
        _pendingWarningFingerprint = null;
        _connectorMovementPlan = 0;
        _mainGroupId = null;
        _secondLineGroupId = null;
        _connectorGroupId = null;
        _mainJunctionStationId = null;
        _secondLineJunctionStationId = null;
        CurrentTool = WorldEditorTool.Inspect;
        SelectedObjectId = null;
        LastMessage = $"新世界 {seed} 已生成";
        Changed?.Invoke();
    }

    public void SetTool(WorldEditorTool tool)
    {
        if (tool != CurrentTool)
        {
            _draftWaypoints.Clear();
            _pendingWarningFingerprint = null;
            _connectorMovementPlan = 0;
        }

        CurrentTool = tool;
        LastMessage = tool == WorldEditorTool.DrawDoubleTrack
            ? "双线工具：左键布点，Enter 或双击提交，Esc 取消"
            : tool == WorldEditorTool.DrawConnector
                ? "联络线工具：依次选择两个既有节点，Enter 或双击提交"
                : "查看工具";
        Changed?.Invoke();
    }

    public void InspectAt(WorldPoint point, double radiusMeters)
    {
        var candidates = World.RailTopology.Nodes.Values
            .Select(
                node => (
                    node.Id,
                    Kind: "节点",
                    node.Position,
                    Distance: node.Position.DistanceMetersTo(point)))
            .Concat(
                World.BaseWorld.Stations.Values.Select(
                    station => (
                        station.Id,
                        Kind: "车站",
                        station.Position,
                        Distance:
                        station.Position.DistanceMetersTo(point))))
            .Concat(
                World.BaseWorld.Cities.Values.Select(
                    city => (
                        city.Id,
                        Kind: "城市",
                        city.Position,
                        Distance: city.Position.DistanceMetersTo(point))))
            .Where(candidate => candidate.Distance <= radiusMeters)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
        if (candidates.Id == default)
        {
            SelectedObjectId = null;
            LastMessage = "当前位置没有可检查对象";
        }
        else
        {
            SelectedObjectId = candidates.Id;
            LastMessage = $"已选择{candidates.Kind} {candidates.Id}";
        }

        Changed?.Invoke();
    }

    public void AddWaypoint(
        WorldPoint point,
        double snapRadiusMeters)
    {
        var waypoint = SnapWaypoint(point, snapRadiusMeters);
        _draftWaypoints.Add(waypoint);
        _pendingWarningFingerprint = null;
        _connectorMovementPlan = 0;
        LastMessage = waypoint.NodeId.HasValue
            ? $"已吸附到节点 {waypoint.NodeId.Value}"
            : waypoint.StationId.HasValue
                ? $"已吸附到车站 {waypoint.StationId.Value}"
                : $"已添加控制点 {_draftWaypoints.Count}";
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

        _draftWaypoints[index] = SnapWaypoint(point, snapRadiusMeters);
        _pendingWarningFingerprint = null;
        _connectorMovementPlan = 0;
        LastMessage = "控制点已调整";
        Changed?.Invoke();
    }

    public void CancelDraft()
    {
        _draftWaypoints.Clear();
        _pendingWarningFingerprint = null;
        _connectorMovementPlan = 0;
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

        if (CurrentTool == WorldEditorTool.DrawConnector)
        {
            return CommitConnectorDraft(draft);
        }

        return CommitDoubleTrackDraft(draft);
    }

    private CommandStatus CommitDoubleTrackDraft(AlignmentDraft draft)
    {
        var patch = new DoubleTrackCorridorStrategy(
            NewIdSource("manual"))
            .CreatePatch(
                World.RailTopology,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        var validation = new RailEditValidator().Validate(World, patch);
        var warningCodes = validation.Issues
            .Where(
                issue =>
                    issue.Severity == RailValidationSeverity.Warning)
            .Select(issue => issue.Code)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var fingerprint = DraftFingerprint();
        if (!warningCodes.IsEmpty &&
            !string.Equals(
                _pendingWarningFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            _pendingWarningFingerprint = fingerprint;
            LastMessage =
                $"工程警告：{string.Join("、", warningCodes.Order())}；" +
                "再次 Enter 或双击确认建设";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        patch = patch with
        {
            AcknowledgedWarningCodes = warningCodes
        };
        var result = Submit(
            new ApplyRailEditCommand(patch, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _draftWaypoints.Clear();
            _pendingWarningFingerprint = null;
        }

        return result.Status;
    }

    private CommandStatus CommitConnectorDraft(AlignmentDraft draft)
    {
        var firstNodeId = draft.Waypoints[0].NodeId;
        var secondNodeId = draft.Waypoints[^1].NodeId;
        if (!firstNodeId.HasValue ||
            !secondNodeId.HasValue ||
            firstNodeId == secondNodeId)
        {
            LastMessage = "联络线两端必须吸附到两个不同的既有节点";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        var planCount = CountMovementPlans(
            World.RailTopology,
            firstNodeId.Value,
            secondNodeId.Value);
        if (planCount == 0)
        {
            LastMessage = "所选节点缺少可接入的既有到达线或出发线";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        _connectorMovementPlan %= planCount;
        var linkPatch = new DoubleTrackCorridorStrategy(
            NewIdSource("manual-link"))
            .CreatePatch(
                World.RailTopology,
                draft,
                new TrackPlacementOptions(5_000, 160));
        var candidate = linkPatch.ApplyTo(World.RailTopology);
        var linkGroup = linkPatch.AddedTrackGroups.Single();
        ImmutableArray<ConnectorMovement> movements;
        try
        {
            movements = BuildBidirectionalMovements(
                World.RailTopology,
                candidate,
                linkGroup,
                firstNodeId.Value,
                secondNodeId.Value,
                _connectorMovementPlan);
        }
        catch (InvalidOperationException)
        {
            LastMessage = "所选节点缺少可接入的既有到达线或出发线";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        var movementPatch = new ExplicitConnectorStrategy(
            NewIdSource("manual-movement"))
            .CreateConnectionPatch(candidate, movements);
        var complete = linkPatch.Combine(movementPatch);
        var validation = new RailEditValidator().Validate(World, complete);
        var warningCodes = validation.Issues
            .Where(
                issue =>
                    issue.Severity == RailValidationSeverity.Warning)
            .Select(issue => issue.Code)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var fingerprint = DraftFingerprint();
        if (!warningCodes.IsEmpty &&
            !string.Equals(
                _pendingWarningFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            _pendingWarningFingerprint = fingerprint;
            LastMessage =
                $"工程警告：{string.Join("、", warningCodes.Order())}；" +
                "再次 Enter 或双击确认建设";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        complete = complete with
        {
            AcknowledgedWarningCodes = warningCodes
        };
        var result = Submit(
            new ApplyRailEditCommand(complete, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _draftWaypoints.Clear();
            _pendingWarningFingerprint = null;
        }

        return result.Status;
    }

    public void CycleConnectorMovementPlan()
    {
        if (CurrentTool != WorldEditorTool.DrawConnector ||
            _draftWaypoints.Count < 2 ||
            !_draftWaypoints[0].NodeId.HasValue ||
            !_draftWaypoints[^1].NodeId.HasValue)
        {
            LastMessage = "请先用联络线工具选择两个既有节点";
            Changed?.Invoke();
            return;
        }

        var planCount = CountMovementPlans(
            World.RailTopology,
            _draftWaypoints[0].NodeId!.Value,
            _draftWaypoints[^1].NodeId!.Value);
        if (planCount <= 0)
        {
            LastMessage = "所选节点没有完整的到达/出发轨道组合";
            Changed?.Invoke();
            return;
        }

        _connectorMovementPlan =
            (_connectorMovementPlan + 1) % planCount;
        LastMessage =
            $"联络互通方案 {_connectorMovementPlan + 1}/{planCount}；" +
            "提交时只开放该方案的四个明确方向";
        Changed?.Invoke();
    }

    public CommandStatus BuildDemoMainLine()
    {
        if (GroupExists(_mainGroupId))
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
        if (GroupExists(_secondLineGroupId))
        {
            LastMessage = "线路二已经存在";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        if (!GroupExists(_mainGroupId))
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
        patch = AcknowledgePresetWarnings(patch);
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
        if (GroupExists(_connectorGroupId))
        {
            LastMessage = "丁市双向联络线已经存在";
            Changed?.Invoke();
            return CommandStatus.Rejected;
        }

        if (!GroupExists(_mainGroupId))
        {
            BuildDemoMainLine();
        }

        if (!GroupExists(_secondLineGroupId))
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
        var completePatch = AcknowledgePresetWarnings(
            linkPatch.Combine(movementPatch));
        var result = Submit(
            new ApplyRailEditCommand(completePatch, World.Revision));
        if (result.Status == CommandStatus.Accepted)
        {
            _connectorGroupId = linkPatch.AddedTrackGroups.Single().Id;
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
        _pendingWarningFingerprint = null;
        _mainGroupId = null;
        _secondLineGroupId = null;
        _connectorGroupId = null;
        _mainJunctionStationId = null;
        _secondLineJunctionStationId = null;
        SynchronizeDemoReferencesFromTopology();
        CurrentTool = WorldEditorTool.Inspect;
        SelectedObjectId = null;
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
        CommandResult result;
        if (_host is null)
        {
            var tick = new SimulationTick(checked(++_commandTick));
            var envelope = _commandBus.Enqueue(command, tick);
            result = _commandBus.DispatchDue(tick).Single(
                item => item.Sequence == envelope.Sequence);
        }
        else
        {
            var envelope = _host.Submit(command);
            result = _host
                .AdvanceFrame(
                    SimulationClock.StepDuration,
                    SimulationRate.FromPreset(
                        SimulationSpeedPreset.Normal))
                .CommandResults
                .Single(item => item.Sequence == envelope.Sequence);
        }

        LastMessage = result.Status == CommandStatus.Accepted
            ? "操作成功"
            : $"{result.Code}: {result.Message}";
        Changed?.Invoke();
        return result;
    }

    private void ResetSession(WorldState world)
    {
        _session = new WorldEditorSession(world, new RailEditValidator());
        _commandTick = 0;
    }

    private bool GroupExists(StableId? groupId)
    {
        return groupId.HasValue &&
            World.RailTopology.TrackGroups.ContainsKey(groupId.Value);
    }

    private void SynchronizeDemoReferencesFromTopology()
    {
        _mainGroupId = FindDemoGroup("-demo-main:");
        _secondLineGroupId = FindDemoGroup("-demo-second:");
        _connectorGroupId = FindDemoGroup("-demo-link:");
        if (_mainGroupId.HasValue)
        {
            _mainJunctionStationId = DemoMainStations()[2].Id;
        }

        if (_secondLineGroupId.HasValue &&
            _mainJunctionStationId.HasValue)
        {
            _secondLineJunctionStationId = DemoSecondLineStations()[1].Id;
        }
    }

    private StableId? FindDemoGroup(string marker)
    {
        return World.RailTopology.TrackGroups.Keys
            .Where(id => id.Value.Contains(marker, StringComparison.Ordinal))
            .OrderBy(id => id)
            .Select(id => (StableId?)id)
            .FirstOrDefault();
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
        var patch = new DoubleTrackCorridorStrategy(NewIdSource(scope))
            .CreatePatch(
                World.RailTopology,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        return AcknowledgePresetWarnings(patch);
    }

    private RailGraphPatch AcknowledgePresetWarnings(
        RailGraphPatch patch)
    {
        var warningCodes = new RailEditValidator()
            .Validate(World, patch)
            .Issues
            .Where(
                issue =>
                    issue.Severity == RailValidationSeverity.Warning)
            .Select(issue => issue.Code)
            .ToImmutableHashSet(StringComparer.Ordinal);
        return patch with
        {
            AcknowledgedWarningCodes = warningCodes
        };
    }

    private string DraftFingerprint()
    {
        return string.Join(
            ";",
            _draftWaypoints.Select(
                waypoint =>
                    $"{waypoint.Position.XMillimeters}," +
                    $"{waypoint.Position.YMillimeters}," +
                    $"{waypoint.StationId?.Value}"));
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

    private DraftWaypoint SnapWaypoint(
        WorldPoint point,
        double radiusMeters)
    {
        var node = World.RailTopology.Nodes.Values
            .Select(
                item => new
                {
                    Node = item,
                    Distance = item.Position.DistanceMetersTo(point)
                })
            .Where(item => item.Distance <= radiusMeters)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Node.Id)
            .Select(item => item.Node)
            .FirstOrDefault();
        if (node is not null)
        {
            return new DraftWaypoint(
                node.Position,
                node.StationId,
                node.Id);
        }

        var station = FindNearestStation(point, radiusMeters);
        return station is null
            ? new DraftWaypoint(point, null, null)
            : new DraftWaypoint(
                station.Position,
                station.Id,
                null);
    }

    private static ImmutableArray<ConnectorMovement>
        BuildBidirectionalMovements(
            RailTopology original,
            RailTopology candidate,
            TrackGroup linkGroup,
            StableId firstNodeId,
            StableId secondNodeId,
            int movementPlan)
    {
        var movements = ImmutableArray.CreateBuilder<ConnectorMovement>(4);
        var choice = movementPlan;
        foreach (var nodeId in new[] { firstNodeId, secondNodeId })
        {
            var incomingOptions = original.Tracks.Values
                .Where(
                    track =>
                        original.Ports[track.ToPortId].NodeId == nodeId)
                .OrderBy(track => track.Id)
                .ToArray();
            var outgoingOptions = original.Tracks.Values
                .Where(
                    track =>
                        original.Ports[track.FromPortId].NodeId == nodeId)
                .OrderBy(track => track.Id)
                .ToArray();
            var existingIncoming =
                incomingOptions[choice % incomingOptions.Length];
            choice /= incomingOptions.Length;
            var existingOutgoing =
                outgoingOptions[choice % outgoingOptions.Length];
            choice /= outgoingOptions.Length;
            var linkOutgoing = OutgoingAt(candidate, linkGroup, nodeId);
            var linkIncoming = IncomingAt(candidate, linkGroup, nodeId);
            movements.Add(new ConnectorMovement(
                nodeId,
                existingIncoming.Id,
                linkOutgoing.Id));
            movements.Add(new ConnectorMovement(
                nodeId,
                linkIncoming.Id,
                existingOutgoing.Id));
        }

        return movements.MoveToImmutable();
    }

    private static int CountMovementPlans(
        RailTopology topology,
        StableId firstNodeId,
        StableId secondNodeId)
    {
        var count = 1;
        foreach (var nodeId in new[] { firstNodeId, secondNodeId })
        {
            var incoming = topology.Tracks.Values.Count(
                track =>
                    topology.Ports[track.ToPortId].NodeId == nodeId);
            var outgoing = topology.Tracks.Values.Count(
                track =>
                    topology.Ports[track.FromPortId].NodeId == nodeId);
            if (incoming == 0 || outgoing == 0)
            {
                return 0;
            }

            count = checked(count * incoming * outgoing);
        }

        return count;
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
