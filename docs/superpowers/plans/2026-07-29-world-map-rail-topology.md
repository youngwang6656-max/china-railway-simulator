# World Map and Rail Topology Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic regional world generator and a playable Godot 2D railway editor that creates independent double-track topology, explicit connector movements, undo/redo, and local save/load.

**Architecture:** `RailSim.Core` owns immutable world and railway state, deterministic generation, validation, reversible patches, history, persistence contracts, and projection DTOs. Godot owns input and drawing only, consumes immutable projections, and submits world-edit commands through the existing command boundary. World coordinates and storage are chunk-ready from the first version so a later continental profile and multiple major stations do not require a data-model rewrite.

**Tech Stack:** Godot 4.6.3 .NET, C# 12, .NET 8, `System.Collections.Immutable`, `System.Text.Json`, xUnit 2.9.2.

## Global Constraints

- Keep `RailSim.Core` free of Godot types and scene-tree dependencies.
- Use 64-bit fixed-point millimetres for persisted global coordinates and local floating-point values only behind explicit quantisation boundaries.
- All entities use stable IDs; display names and collection indices are never reference keys.
- A macro double-track stroke creates two independent physical `TrackEdge` values.
- Crossings do not imply connectivity; only explicit `TrackConnection` values permit movements.
- Every accepted edit is atomic and reversible; rejected edits change neither state nor history.
- Preserve stable strategy ports for freehand input, terrain-aware autorouting, single-track placement, abstract macro projection, manual/external terrain, alternate history stores, and alternate frontends.
- Default content targets 50–100 station anchors; coordinate, chunk, persistence, and city/station cardinality tests must support a future China-scale continental profile with multiple major cities and multiple major stations per city.
- Do not implement detailed station yards, turnouts, signals, trains, timetables, or dispatch in this plan.
- Keep Release and Debug builds warning-free under `TreatWarningsAsErrors`.

---

## File Structure

### Core world and generation

- `src/RailSim.Core/World/StableId.cs`: validated stable identifiers and deterministic ID derivation.
- `src/RailSim.Core/World/WorldCoordinates.cs`: fixed-point coordinates, bounds, chunks, and local conversion.
- `src/RailSim.Core/World/WorldGenerationParameters.cs`: regional, continental, and custom profiles.
- `src/RailSim.Core/World/WorldEntities.cs`: cities, stations, tiers, rivers, generated-world container.
- `src/RailSim.Core/World/HeightField.cs`: immutable quantised terrain grid.
- `src/RailSim.Core/World/DeterministicRandom.cs`: fixed SplitMix64 random stream and keyed seed derivation.
- `src/RailSim.Core/World/ProceduralWorldGenerator.cs`: deterministic terrain, river, city, station, and name generation.
- `src/RailSim.Core/World/WorldContentHasher.cs`: canonical SHA-256 content fingerprint.

### Core railway and editing

- `src/RailSim.Core/Rail/RailTopology.cs`: immutable topology aggregate.
- `src/RailSim.Core/Rail/RailEntities.cs`: nodes, ports, alignments, physical tracks, groups, and connections.
- `src/RailSim.Core/Editing/AlignmentDraft.cs`: standard control-point draft.
- `src/RailSim.Core/Editing/AlignmentStrategies.cs`: sketch and placement interfaces plus default strategies.
- `src/RailSim.Core/Editing/RailGraphPatch.cs`: typed additions, removals, replacements, and inverse generation.
- `src/RailSim.Core/Editing/RailEditValidator.cs`: hard-error and engineering-warning validation.
- `src/RailSim.Core/Editing/WorldEditorSession.cs`: immutable state store, revision checks, history, undo, and redo.
- `src/RailSim.Core/Editing/WorldEditCommands.cs`: immutable command payloads and handlers.
- `src/RailSim.Core/World/WorldState.cs`: generated world plus railway topology.
- `src/RailSim.Core/Query/WorldSnapshotSection.cs`: `world` snapshot contribution.

### Persistence and projection

- `src/RailSim.Core/Persistence/IWorldRepository.cs`: storage boundary.
- `src/RailSim.Core/Persistence/JsonWorldRepository.cs`: atomic local document round-trip.
- `src/RailSim.Core/Projection/WorldMapProjection.cs`: viewport culling and LOD DTOs.
- `src/RailSim.Core/Projection/ProjectionContracts.cs`: `IWorldProjection`, `IMapLodPolicy`, and immutable projection values.

### Godot application

- `game/App/Bootstrap.cs`: composition root, smoke scenario, toolbar wiring, and status.
- `game/Map/WorldMapView.cs`: camera, terrain texture, world drawing, hit testing, and draft input.
- `game/Map/WorldEditorController.cs`: turns view actions into core drafts and edit commands.
- `game/Main.tscn`: map, toolbar, inspector/help panel, and status layout.

### Tests and documentation

- `tests/RailSim.Core.Tests/World/WorldCoordinateTests.cs`
- `tests/RailSim.Core.Tests/World/ProceduralWorldGeneratorTests.cs`
- `tests/RailSim.Core.Tests/Rail/RailTopologyTests.cs`
- `tests/RailSim.Core.Tests/Editing/AlignmentStrategyTests.cs`
- `tests/RailSim.Core.Tests/Editing/WorldEditorSessionTests.cs`
- `tests/RailSim.Core.Tests/Persistence/JsonWorldRepositoryTests.cs`
- `tests/RailSim.Core.Tests/Projection/WorldMapProjectionTests.cs`
- `scripts/verify.ps1`
- `README.md`

---

### Task 1: World Coordinates, Scale Profiles, and City/Station Cardinality

**Files:**
- Create: `src/RailSim.Core/World/StableId.cs`
- Create: `src/RailSim.Core/World/WorldCoordinates.cs`
- Create: `src/RailSim.Core/World/WorldGenerationParameters.cs`
- Create: `src/RailSim.Core/World/WorldEntities.cs`
- Test: `tests/RailSim.Core.Tests/World/WorldCoordinateTests.cs`

**Interfaces:**
- Produces: `StableId`, `WorldPoint`, `WorldBounds`, `WorldChunkId`, `WorldScaleProfile`, `WorldGenerationParameters`, `CitySite`, and `StationAnchor`.
- `WorldPoint` stores `long XMillimeters` and `long YMillimeters`.
- `WorldGenerationParameters.CreateRegional(ulong seed)` returns a `1_000_000 × 800_000` metre world with 64 default city and station anchors.
- `WorldGenerationParameters.CreateContinental(ulong seed)` returns a chunk-compatible continental bounds contract without allocating terrain.

- [ ] **Step 1: Write failing coordinate and cardinality tests**

```csharp
[Fact]
public void ContinentalProfileSupportsDistantPreciseLocalCoordinates()
{
    var parameters = WorldGenerationParameters.CreateContinental(42);
    var origin = new WorldPoint(5_000_000_000, 4_000_000_000);
    var nearby = origin.OffsetMillimeters(1_435, -620);

    Assert.True(parameters.Bounds.Contains(origin));
    Assert.Equal(1_435, nearby.XMillimeters - origin.XMillimeters);
    Assert.Equal(-620, nearby.YMillimeters - origin.YMillimeters);
}

[Fact]
public void OneCityCanOwnMultipleMajorStations()
{
    var cityId = new StableId("city:hub");
    var stations = new[]
    {
        new StationAnchor(new StableId("station:east"), "东站", StationTier.Mega, WorldPoint.Zero, cityId, null),
        new StationAnchor(new StableId("station:south"), "南站", StationTier.Mega, WorldPoint.Zero, cityId, null)
    };

    Assert.All(stations, station => Assert.Equal(cityId, station.CityId));
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldCoordinateTests
```

Expected: compilation fails because the world contracts do not exist.

- [ ] **Step 3: Implement immutable coordinate and generation contracts**

Implement these exact public shapes:

```csharp
public readonly record struct StableId(string Value);
public readonly record struct WorldPoint(long XMillimeters, long YMillimeters)
{
    public static WorldPoint Zero => new(0, 0);
    public WorldPoint OffsetMillimeters(long x, long y) =>
        new(checked(XMillimeters + x), checked(YMillimeters + y));
}

public readonly record struct WorldBounds(WorldPoint Minimum, WorldPoint Maximum)
{
    public bool Contains(WorldPoint point);
}

public enum WorldScaleProfile { Regional, Continental, Custom }
public enum CityTier { Mega, Large, Medium, Small }
public enum StationTier { Mega, Large, Medium, Small }

public sealed record WorldGenerationParameters(
    ulong Seed,
    int GeneratorVersion,
    WorldScaleProfile ScaleProfile,
    WorldBounds Bounds,
    int ChunkSizeMeters,
    int HeightSamplesX,
    int HeightSamplesY,
    int MegaCityCount,
    int LargeCityCount,
    int MediumCityCount,
    int SmallCityCount,
    int StationAnchorCount,
    int MajorRiverCount);
```

Validate non-empty IDs, positive sizes, ordered bounds, non-negative counts, and station counts between 1 and the selected profile’s supported maximum. Keep station tier independent from city tier and make `CityId` nullable on `StationAnchor`.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldCoordinateTests
```

Expected: all `WorldCoordinateTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/World tests/RailSim.Core.Tests/World/WorldCoordinateTests.cs
git commit -m "feat: add scalable world coordinate contracts"
```

---

### Task 2: Deterministic Terrain, Rivers, Cities, and Stations

**Files:**
- Create: `src/RailSim.Core/World/HeightField.cs`
- Create: `src/RailSim.Core/World/DeterministicRandom.cs`
- Create: `src/RailSim.Core/World/ProceduralWorldGenerator.cs`
- Create: `src/RailSim.Core/World/WorldContentHasher.cs`
- Modify: `src/RailSim.Core/World/WorldEntities.cs`
- Test: `tests/RailSim.Core.Tests/World/ProceduralWorldGeneratorTests.cs`

**Interfaces:**
- Consumes: `WorldGenerationParameters`, `WorldPoint`, `CitySite`, and `StationAnchor`.
- Produces:

```csharp
public interface IWorldGenerator
{
    GeneratedWorld Generate(WorldGenerationParameters parameters);
}

public sealed record GeneratedWorld(
    WorldGenerationParameters Parameters,
    HeightField Terrain,
    ImmutableArray<RiverPath> Rivers,
    ImmutableDictionary<StableId, CitySite> Cities,
    ImmutableDictionary<StableId, StationAnchor> Stations,
    string ContentHash);
```

- [ ] **Step 1: Write deterministic generation tests**

```csharp
[Fact]
public void SameSeedAndParametersProduceSameWorldHash()
{
    var parameters = WorldGenerationParameters.CreateRegional(20260729);
    var generator = new ProceduralWorldGenerator();

    var first = generator.Generate(parameters);
    var second = generator.Generate(parameters);

    Assert.Equal(first.ContentHash, second.ContentHash);
    Assert.Equal(64, first.Cities.Count);
    Assert.Equal(64, first.Stations.Count);
    Assert.Equal(parameters.MajorRiverCount, first.Rivers.Length);
}

[Fact]
public void DifferentNameSeedStreamDoesNotMoveTerrain()
{
    var seed = SeedDeriver.Derive(88, "terrain");
    var first = HeightField.Generate(32, 24, seed);
    var second = HeightField.Generate(32, 24, seed);

    Assert.Equal(first.ContentHash, second.ContentHash);
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~ProceduralWorldGeneratorTests
```

Expected: compilation fails because the generator does not exist.

- [ ] **Step 3: Implement deterministic generation**

Use fixed SplitMix64:

```csharp
public ulong NextUInt64()
{
    _state += 0x9E3779B97F4A7C15UL;
    var value = _state;
    value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
    value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
    return value ^ (value >> 31);
}
```

Generate terrain with integer lattice noise and explicit integer octave weights, then quantise every sample to integer metres. Generate rivers as deterministic downhill-biased polylines. Place cities by deterministic candidates, terrain suitability, tier spacing, and controlled spacing relaxation until the exact configured count is reached. Give every city a unique fictional Chinese name from fixed prefix/suffix tables. Generate one default station anchor per city without encoding that relationship as a domain invariant.

Compute SHA-256 from canonical generator version, parameters, row-major heights, ordered rivers, ordered cities, and ordered stations.

- [ ] **Step 4: Run world tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~World
```

Expected: all coordinate and procedural world tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/World tests/RailSim.Core.Tests/World
git commit -m "feat: generate deterministic regional worlds"
```

---

### Task 3: Directional Physical Rail Topology

**Files:**
- Create: `src/RailSim.Core/Rail/RailEntities.cs`
- Create: `src/RailSim.Core/Rail/RailTopology.cs`
- Create: `src/RailSim.Core/World/WorldState.cs`
- Test: `tests/RailSim.Core.Tests/Rail/RailTopologyTests.cs`

**Interfaces:**
- Consumes: `StableId`, `WorldPoint`, and `GeneratedWorld`.
- Produces:

```csharp
public sealed record RailNode(StableId Id, RailNodeKind Kind, WorldPoint Position, StableId? StationId);
public sealed record RailPort(StableId Id, StableId NodeId, RailPortDirection Direction);
public sealed record Alignment(StableId Id, ImmutableArray<WorldPoint> ControlPoints, long LengthMillimeters);
public sealed record TrackEdge(StableId Id, StableId FromPortId, StableId ToPortId, StableId AlignmentId, int LateralOffsetMillimeters, int DesignSpeedKph);
public sealed record TrackGroup(StableId Id, ImmutableArray<StableId> TrackIds);
public sealed record TrackConnection(StableId Id, StableId NodeId, StableId FromTrackId, StableId ToTrackId);
public sealed record RailTopology(...);
public sealed record WorldState(long Revision, GeneratedWorld BaseWorld, RailTopology RailTopology);
```

- [ ] **Step 1: Write topology tests**

```csharp
[Fact]
public void CrossingAlignmentsDoNotCreateConnections()
{
    var topology = RailTopology.Empty
        .AddAlignment(TestRail.HorizontalAlignment)
        .AddAlignment(TestRail.VerticalAlignment);

    Assert.Empty(topology.Connections);
}

[Fact]
public void ConnectionsMustReferenceACommonNode()
{
    var topology = TestRail.TwoDisconnectedTracks();
    var invalid = new TrackConnection(
        new StableId("connection:invalid"),
        new StableId("node:a"),
        new StableId("track:first"),
        new StableId("track:second"));

    Assert.Throws<RailTopologyException>(() => topology.AddConnection(invalid));
}
```

- [ ] **Step 2: Run and verify failure**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~RailTopologyTests
```

Expected: compilation fails because rail topology types do not exist.

- [ ] **Step 3: Implement immutable topology with explicit invariants**

Use `ImmutableDictionary<StableId, T>` collections. Every add and replace method returns a new `RailTopology`. Reject duplicate IDs, missing references, degenerate alignments, incompatible ports, and connections whose tracks do not share the declared node. Do not infer connections by geometric intersection.

- [ ] **Step 4: Run topology tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~RailTopologyTests
```

Expected: all topology tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/Rail src/RailSim.Core/World/WorldState.cs tests/RailSim.Core.Tests/Rail
git commit -m "feat: add directional physical rail topology"
```

---

### Task 4: Control-Point Drafts, Double-Track Placement, and Explicit Connectors

**Files:**
- Create: `src/RailSim.Core/Editing/AlignmentDraft.cs`
- Create: `src/RailSim.Core/Editing/AlignmentStrategies.cs`
- Create: `src/RailSim.Core/Editing/RailGraphPatch.cs`
- Create: `src/RailSim.Core/Editing/RailEditValidator.cs`
- Test: `tests/RailSim.Core.Tests/Editing/AlignmentStrategyTests.cs`

**Interfaces:**
- Consumes: `RailTopology`, `WorldPoint`, station anchors, and stable IDs.
- Produces:

```csharp
public interface IAlignmentSketchStrategy
{
    AlignmentDraft Build(ImmutableArray<DraftWaypoint> waypoints);
}

public interface ITrackPlacementStrategy
{
    RailGraphPatch CreatePatch(RailTopology topology, AlignmentDraft draft, TrackPlacementOptions options);
}

public interface IRailEditValidator
{
    RailValidationResult Validate(RailTopology topology, RailGraphPatch patch);
}
```

- [ ] **Step 1: Write failing placement tests**

```csharp
[Fact]
public void DoubleTrackStrokeCreatesTwoIndependentOpposingTracksPerSegment()
{
    var draft = TestDrafts.MainLine();
    var patch = new DoubleTrackCorridorStrategy(new DeterministicStableIdSource("test"))
        .CreatePatch(RailTopology.Empty, draft, TrackPlacementOptions.HighSpeedDoubleTrack);

    Assert.Equal(6, patch.AddedTracks.Length);
    Assert.Equal(3, patch.AddedTracks.Count(track => track.LateralOffsetMillimeters < 0));
    Assert.Equal(3, patch.AddedTracks.Count(track => track.LateralOffsetMillimeters > 0));
    Assert.Equal(6, patch.AddedTracks.Select(track => track.Id).Distinct().Count());
}

[Fact]
public void ConnectorAddsOnlyRequestedMovements()
{
    var patch = TestDrafts.BuildBidirectionalDConnector();

    Assert.Equal(2, patch.AddedConnections.Length);
    Assert.DoesNotContain(
        patch.AddedConnections,
        connection => connection.FromTrackId == TestIds.UnrequestedTrack);
}
```

- [ ] **Step 2: Run and verify failure**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~AlignmentStrategyTests
```

Expected: compilation fails because editing strategies do not exist.

- [ ] **Step 3: Implement strategies and validator**

`ControlPointSplineStrategy` keeps the selected waypoints stable and produces a quantised Catmull–Rom-to-cubic path for display sampling. `DoubleTrackCorridorStrategy` creates one shared centreline `Alignment`, two independent tracks per station-to-station segment, separate ports, a grouping value, and only straight-through intermediate connections. Connector construction receives explicit `(fromTrackId, toTrackId)` movement pairs and never derives all-to-all movements.

Hard errors block missing references, stale IDs, zero length, partial double track, and incompatible connection endpoints. Warnings report low radius, steep sampled grade, close spacing, major river crossing, and steep terrain crossing with stable codes.

- [ ] **Step 4: Run editing strategy tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~AlignmentStrategyTests
```

Expected: all strategy and validator tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/Editing tests/RailSim.Core.Tests/Editing/AlignmentStrategyTests.cs
git commit -m "feat: add double-track and connector editing strategies"
```

---

### Task 5: Atomic World Edits, Snapshot Publication, Undo, and Redo

**Files:**
- Create: `src/RailSim.Core/Editing/WorldEditorSession.cs`
- Create: `src/RailSim.Core/Editing/WorldEditCommands.cs`
- Create: `src/RailSim.Core/Query/WorldSnapshotSection.cs`
- Modify: `src/RailSim.Core/Commands/ImmutableCommandTypeValidator.cs`
- Test: `tests/RailSim.Core.Tests/Editing/WorldEditorSessionTests.cs`
- Modify: `tests/RailSim.Core.Tests/Commands/SimulationCommandBusTests.cs`

**Interfaces:**
- Consumes: `WorldState`, `RailGraphPatch`, existing `ISimulationCommand`, `CommandHandler<TCommand>`, and `ISnapshotContributor`.
- Produces:

```csharp
public sealed record ApplyRailEditCommand(RailGraphPatch Patch, long BaseRevision) : ISimulationCommand;
public sealed record UndoWorldEditCommand(long BaseRevision) : ISimulationCommand;
public sealed record RedoWorldEditCommand(long BaseRevision) : ISimulationCommand;
public sealed class WorldEditorSession : ISnapshotContributor;
public sealed record WorldSnapshotSection(WorldState World) : ISimulationSnapshotSection;
```

- [ ] **Step 1: Write failing atomicity and history tests**

```csharp
[Fact]
public void ApplyUndoRedoRoundTripsWorldHash()
{
    var session = TestWorlds.EditorSession();
    var before = session.CurrentHash;
    var accepted = session.Apply(TestPatches.MainLine(), session.Revision);
    var after = session.CurrentHash;

    Assert.Equal(CommandStatus.Accepted, accepted.Status);
    Assert.Equal(CommandStatus.Accepted, session.Undo(session.Revision).Status);
    Assert.Equal(before, session.CurrentHash);
    Assert.Equal(CommandStatus.Accepted, session.Redo(session.Revision).Status);
    Assert.Equal(after, session.CurrentHash);
}

[Fact]
public void RejectedPatchChangesNeitherWorldNorHistory()
{
    var session = TestWorlds.EditorSession();
    var before = session.CurrentHash;

    var result = session.Apply(TestPatches.PartialDoubleTrack(), session.Revision);

    Assert.Equal(CommandStatus.Rejected, result.Status);
    Assert.Equal(before, session.CurrentHash);
    Assert.False(session.CanUndo);
}
```

- [ ] **Step 2: Run and verify failure**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldEditorSessionTests
```

Expected: compilation fails because the editor session does not exist.

- [ ] **Step 3: Implement command-safe immutable history**

Teach `ImmutableCommandTypeValidator` to recursively accept only constructed
`ImmutableArray<T>`, `ImmutableList<T>`, and `ImmutableDictionary<TKey,TValue>`
when every key and value type is deeply immutable. Do not accept mutable
`IReadOnlyList<T>` implementations by interface alone.

`WorldEditorSession.Apply` validates the entire patch, applies it to a candidate topology, verifies the candidate, then swaps one `WorldState` reference and pushes one inverse patch. Undo and redo use the same candidate-and-swap path. New accepted edits clear redo. `Capture()` returns key `world` and the current immutable state.

Register typed handlers that translate session decisions into existing stable `CommandDecision` values.

- [ ] **Step 4: Run command, editor, and snapshot tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter "FullyQualifiedName~WorldEditorSessionTests|FullyQualifiedName~SimulationCommandBusTests|FullyQualifiedName~Snapshot"
```

Expected: all selected tests pass and mutable command payload tests still fail validation.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/Editing src/RailSim.Core/Query/WorldSnapshotSection.cs src/RailSim.Core/Commands/ImmutableCommandTypeValidator.cs tests/RailSim.Core.Tests
git commit -m "feat: add atomic world edit history"
```

---

### Task 6: Atomic Save and Load

**Files:**
- Create: `src/RailSim.Core/Persistence/IWorldRepository.cs`
- Create: `src/RailSim.Core/Persistence/JsonWorldRepository.cs`
- Test: `tests/RailSim.Core.Tests/Persistence/JsonWorldRepositoryTests.cs`

**Interfaces:**
- Consumes: `WorldState` and all world/rail immutable values.
- Produces:

```csharp
public interface IWorldRepository
{
    void Save(string path, WorldState world);
    WorldState Load(string path);
}

public sealed class JsonWorldRepository : IWorldRepository;
```

- [ ] **Step 1: Write failing persistence tests**

```csharp
[Fact]
public void SaveLoadPreservesWorldAndTopologyHash()
{
    var path = Path.Combine(_temporaryDirectory, "world.crsim-world.json");
    var expected = TestWorlds.WithMainLineAndConnector();
    var repository = new JsonWorldRepository();

    repository.Save(path, expected);
    var actual = repository.Load(path);

    Assert.Equal(expected.Revision, actual.Revision);
    Assert.Equal(expected.BaseWorld.ContentHash, actual.BaseWorld.ContentHash);
    Assert.Equal(
        WorldContentHasher.HashState(expected),
        WorldContentHasher.HashState(actual));
}
```

- [ ] **Step 2: Run and verify failure**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~JsonWorldRepositoryTests
```

Expected: compilation fails because the repository does not exist.

- [ ] **Step 3: Implement versioned atomic JSON persistence**

Write a versioned DTO with `SchemaVersion = 1`, generation parameters, terrain samples, ordered rivers, cities, stations, railway values, and revision. Save to a sibling `*.tmp` file, flush and close it, load it back for structural validation, then replace the target atomically. Reject unknown schema versions, missing IDs, invalid topology, and content-hash mismatches without changing the original target.

- [ ] **Step 4: Run persistence tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~JsonWorldRepositoryTests
```

Expected: round-trip, invalid-schema, truncated-file, and original-file-preservation tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/Persistence tests/RailSim.Core.Tests/Persistence
git commit -m "feat: persist generated worlds and rail edits"
```

---

### Task 7: Viewport Culling and LOD Projection

**Files:**
- Create: `src/RailSim.Core/Projection/ProjectionContracts.cs`
- Create: `src/RailSim.Core/Projection/WorldMapProjection.cs`
- Test: `tests/RailSim.Core.Tests/Projection/WorldMapProjectionTests.cs`

**Interfaces:**
- Consumes: immutable `WorldState`.
- Produces:

```csharp
public interface IWorldProjection
{
    WorldMapFrame Project(WorldState world, MapViewport viewport);
}

public interface IMapLodPolicy
{
    MapLod Select(double metersPerPixel);
}

public sealed record WorldMapFrame(
    long WorldRevision,
    MapLod Lod,
    ImmutableArray<ProjectedCity> Cities,
    ImmutableArray<ProjectedStation> Stations,
    ImmutableArray<ProjectedTrack> Tracks,
    ImmutableArray<ProjectedConnection> Connections);
```

- [ ] **Step 1: Write failing projection tests**

```csharp
[Fact]
public void RegionalLodMergesTrackGroupWithoutChangingWorld()
{
    var world = TestWorlds.WithMainLineAndConnector();
    var before = WorldContentHasher.HashState(world);
    var frame = new WorldMapProjection(new DefaultMapLodPolicy())
        .Project(world, MapViewport.Regional(world.BaseWorld.Parameters.Bounds));

    Assert.Equal(MapLod.Regional, frame.Lod);
    Assert.True(frame.Tracks.Length < world.RailTopology.Tracks.Count);
    Assert.Equal(before, WorldContentHasher.HashState(world));
}
```

- [ ] **Step 2: Run and verify failure**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldMapProjectionTests
```

Expected: compilation fails because projection types do not exist.

- [ ] **Step 3: Implement immutable projections**

Regional LOD emits one display line per visible `TrackGroup`. Line LOD emits independent tracks and directions. Edit LOD additionally emits control points, ports, and explicit connections. Cull cities, stations, alignments, and connections by viewport bounds plus a one-chunk margin. Include world revision and stable source IDs in every DTO.

- [ ] **Step 4: Run projection tests**

Run:

```powershell
dotnet test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldMapProjectionTests
```

Expected: LOD, culling, source-ID, and snapshot non-mutation tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/RailSim.Core/Projection tests/RailSim.Core.Tests/Projection
git commit -m "feat: project world map with scalable lod"
```

---

### Task 8: Playable Godot Map Editor

**Files:**
- Create: `game/Map/WorldMapView.cs`
- Create: `game/Map/WorldEditorController.cs`
- Modify: `game/App/Bootstrap.cs`
- Modify: `game/Main.tscn`
- Modify: `game/project.godot`

**Interfaces:**
- Consumes: `IWorldGenerator`, `WorldEditorSession`, `IWorldProjection`, `IWorldRepository`, and existing `SimulationHost`.
- Produces: a 1280×720 playable map editor and a smoke-test composition root.

- [ ] **Step 1: Add a smoke assertion before changing the UI**

Modify smoke mode to require:

```csharp
var world = _editor.World;
var success =
    world.BaseWorld.Cities.Count == 64 &&
    world.BaseWorld.Stations.Count == 64 &&
    world.RailTopology.Tracks.Count >= 8;
GD.Print(
    $"RAILSIM_WORLD_SMOKE cities={world.BaseWorld.Cities.Count} " +
    $"stations={world.BaseWorld.Stations.Count} tracks={world.RailTopology.Tracks.Count}");
GetTree().Quit(success ? 0 : 1);
```

- [ ] **Step 2: Run smoke and verify failure**

Run:

```powershell
& .\scripts\verify.ps1
```

Expected: build or smoke fails because the new map composition types and sample topology are not implemented.

- [ ] **Step 3: Implement the Godot editor**

`WorldMapView` must:

- Cache one terrain `ImageTexture` per generated terrain revision.
- Draw rivers, city/station symbols, grouped or physical tracks by current LOD, direction markers, connections, selection, and active draft.
- Convert coordinates with a camera-relative origin.
- Support mouse-wheel zoom, middle/right drag pan, left-click waypoint placement, waypoint dragging, `Enter` commit, and `Escape` cancel.
- Emit only view actions; never mutate `WorldState`.

`WorldEditorController` must:

- Snap clicks to nearby station anchors or existing node ports.
- Build `AlignmentDraft` through `IAlignmentSketchStrategy`.
- Submit a double-track patch through the command handler/session.
- Build the D connector from explicitly selected endpoint movements.
- Expose `Undo`, `Redo`, `Save`, `Load`, and `Regenerate`.

`Main.tscn` must provide buttons labelled:

```text
新世界  绘制双线  示例干线  线路二  丁市联络线  撤销  重做  保存  读取
```

It must also show seed, current tool, zoom/LOD, world revision, object counts, and the latest validation result.

- [ ] **Step 4: Run app build and headless smoke**

Run:

```powershell
dotnet build game/RailSim.App.csproj -c Debug
& .\scripts\verify.ps1
```

Expected: Debug build has zero warnings/errors and smoke prints
`RAILSIM_WORLD_SMOKE cities=64 stations=64` with at least 8 tracks.

- [ ] **Step 5: Commit**

```powershell
git add game
git commit -m "feat: add playable macro railway editor"
```

---

### Task 9: Reproducible Verification, Documentation, and Local Deployment

**Files:**
- Modify: `scripts/verify.ps1`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-07-29-world-map-rail-topology-design.md`

**Interfaces:**
- Consumes: the complete world map/editor increment.
- Produces: one command that restores, builds, tests, checks coverage, runs the world smoke scenario, and a documented local launch command.

- [ ] **Step 1: Extend verification expectations**

Require the script to fail unless Godot output contains both:

```text
RAILSIM_SMOKE tick=4
RAILSIM_WORLD_SMOKE cities=64 stations=64
```

Also keep Release solution build, Debug app build, xUnit, and coverage checks.

- [ ] **Step 2: Run verification and record any failure**

Run:

```powershell
& .\scripts\verify.ps1
```

Expected: all builds, tests, coverage, foundation smoke, and world smoke pass.

- [ ] **Step 3: Document the playable controls and deployment**

Add exact README commands:

```powershell
.\scripts\bootstrap-toolchain.ps1
.\scripts\verify.ps1
.\.tools\godot\Godot_v4.6.3-stable_mono_win64.exe --path .\game --editor
```

Document map controls, line-building workflow, explicit crossing rule, undo/redo, save location, default 64-city seed, and continental-scale architectural limits.

Update the design status to `已实现并通过验证` only after the verification command succeeds.

- [ ] **Step 4: Run final clean verification**

Run:

```powershell
git status --short
& .\scripts\verify.ps1
git diff --check
```

Expected: only intentional documentation and verification changes are present before the final commit; verification prints `VERIFY_OK`.

- [ ] **Step 5: Commit**

```powershell
git add scripts/verify.ps1 README.md docs/superpowers/specs/2026-07-29-world-map-rail-topology-design.md
git commit -m "docs: verify world map editor milestone"
```

---

## Plan Self-Review

- **Spec coverage:** Tasks 1–9 cover regional generation, 50–100 station contracts, continental coordinates and chunk seams, multiple major stations per city, terrain, rivers, names, physical double tracks, explicit connectors, control-point input, validation, atomic edits, undo/redo, persistence, LOD, Godot interaction, smoke verification, and documentation.
- **Deferred scope:** Detailed station yards, turnouts, signals, trains, timetables, and dispatch remain outside this plan exactly as specified.
- **Reserved alternatives:** Strategy and repository interfaces explicitly preserve every unselected option named in the specification.
- **Placeholder scan:** Every implementation step names exact files, commands, public contracts, expected failures, and expected passing evidence.
- **Type consistency:** `WorldState`, `RailTopology`, `RailGraphPatch`, `WorldEditorSession`, and projection signatures are defined once and consumed under the same names in later tasks.
