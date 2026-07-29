# Fluid Railway Drawing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace click-only railway drafting with a continuous hybrid drawing flow featuring live ghost double-track preview, deterministic snapping, step-back, construction metrics, and endpoint-safe submission.

**Architecture:** Keep pointer state and preview calculations in pure `RailSim.Core` services so the behavior is deterministic and xUnit-testable. `WorldEditorController` adapts world entities into snap candidates and composes the existing atomic rail patches, while `WorldMapView` only translates Godot input events and renders the returned preview. Preview and committed geometry share the same `AlignmentDraft`, preventing visual/transaction divergence.

**Tech Stack:** C# 12, .NET 8, Godot 4.6.3 .NET, xUnit, immutable domain records

## Global Constraints

- Windows 11 64-bit, .NET SDK 8.x, Godot 4.6.3 stable .NET.
- Preserve deterministic stable IDs, revision checks, atomic `RailGraphPatch` submission, undo/redo, persistence, and incremental chunk-index correctness.
- Preview updates must not create stable IDs, advance world revision, write history, write disk, or update spatial indexes.
- Default interaction is A hybrid mode; B multi-point precision and C drag-tangent modes retain strategy interfaces but receive no complete UI in this plan.
- Shift disables all automatic snapping while held.
- Preview geometry and submitted `Alignment.ControlPoints` must be identical.
- Keep detailed-yard, signal, timetable, dispatch, and professional engineering option ports intact.

---

### Task 1: Deterministic snap resolver

**Files:**
- Create: `src/RailSim.Core/Editing/RailDrawingSnapping.cs`
- Create: `tests/RailSim.Core.Tests/Editing/RailDrawingSnappingTests.cs`
- Modify: `src/RailSim.Core/Editing/AlignmentDraft.cs`

**Interfaces:**
- Consumes: `WorldPoint`, `StableId`, `DraftWaypoint`.
- Produces:
  - `RailSnapKind { Free, AngleGuide, TrackSplit, Station, RailNode }`
  - `RailSnapCandidate(StableId? Id, RailSnapKind Kind, DraftWaypoint Waypoint, double ScreenDistancePixels, double? GuideAngleDegrees = null)`
  - `RailSnapRequest(WorldPoint FreePosition, ImmutableArray<RailSnapCandidate> Candidates, bool SnappingDisabled, double MaximumDistancePixels)`
  - `RailSnapResult(DraftWaypoint Waypoint, RailSnapKind Kind, StableId? TargetId, double? GuideAngleDegrees)`
  - `IRailSnapResolver.Resolve(RailSnapRequest request)`
  - `DraftWaypoint.SplitTrackId`

- [ ] **Step 1: Write failing priority and Shift tests**

```csharp
[Fact]
public void RailNodeWinsOverCloserLowerPriorityCandidates()
{
    var request = Request(
        Candidate("station", RailSnapKind.Station, 2),
        Candidate("node", RailSnapKind.RailNode, 7));

    var result = new RailSnapResolver().Resolve(request);

    Assert.Equal(RailSnapKind.RailNode, result.Kind);
    Assert.Equal(new StableId("node"), result.TargetId);
}

[Fact]
public void ShiftReturnsTheFreeCursorPosition()
{
    var request = Request(
        snappingDisabled: true,
        Candidate("node", RailSnapKind.RailNode, 1));

    var result = new RailSnapResolver().Resolve(request);

    Assert.Equal(RailSnapKind.Free, result.Kind);
    Assert.Equal(request.FreePosition, result.Waypoint.Position);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~RailDrawingSnappingTests
```

Expected: compilation fails because `RailSnapResolver` and related records do not exist.

- [ ] **Step 3: Implement minimal deterministic resolution**

Implement `RailSnapResolver.Resolve` so disabled snapping returns `FreePosition`; otherwise filter candidates by `MaximumDistancePixels`, order by descending semantic priority (`RailNode`, `Station`, `TrackSplit`, `AngleGuide`, `Free`), then by screen distance, then nullable stable ID. Map `TrackSplit` to `DraftWaypoint.SplitTrackId`, `Station` to `StationId`, and `RailNode` to `NodeId`.

- [ ] **Step 4: Add threshold and deterministic tie tests**

```csharp
[Fact]
public void CandidateOutsidePixelThresholdDoesNotSnap()
{
    var result = new RailSnapResolver().Resolve(
        Request(maximumDistancePixels: 16, Candidate("node", RailSnapKind.RailNode, 16.01)));

    Assert.Equal(RailSnapKind.Free, result.Kind);
}

[Fact]
public void StableIdBreaksEqualKindAndDistanceTies()
{
    var result = new RailSnapResolver().Resolve(
        Request(
            Candidate("z", RailSnapKind.Station, 4),
            Candidate("a", RailSnapKind.Station, 4)));

    Assert.Equal(new StableId("a"), result.TargetId);
}
```

- [ ] **Step 5: Run the focused tests and verify GREEN**

Run the Task 1 test command. Expected: all `RailDrawingSnappingTests` pass.

- [ ] **Step 6: Commit**

```powershell
git add src/RailSim.Core/Editing/AlignmentDraft.cs src/RailSim.Core/Editing/RailDrawingSnapping.cs tests/RailSim.Core.Tests/Editing/RailDrawingSnappingTests.cs
git commit -m "feat: add deterministic railway drawing snaps"
```

---

### Task 2: Hybrid drawing session state machine

**Files:**
- Create: `src/RailSim.Core/Editing/RailDrawingSession.cs`
- Create: `tests/RailSim.Core.Tests/Editing/RailDrawingSessionTests.cs`

**Interfaces:**
- Consumes: `IRailSnapResolver`, `RailSnapRequest`, `DraftWaypoint`.
- Produces:
  - `RailDrawingInputMode { HybridGuided, MultiPointPrecision, DragTangent }`
  - `RailDrawingState { Idle, Previewing, DraggingWaypoint, ConfirmingWarning }`
  - `RailDrawingSession.ConfirmedWaypoints`
  - `RailDrawingSession.CurrentSnap`
  - `RailDrawingSession.PreviewWaypoints`
  - `Begin`, `UpdatePreview`, `ConfirmPreviewPoint`, `UndoStep`, `Cancel`, `MoveWaypoint`, `RemoveWaypoint`, and `ResetAfterCommit`.

- [ ] **Step 1: Write failing live-preview isolation test**

```csharp
[Fact]
public void CursorCandidateAppearsInPreviewButNotConfirmedPoints()
{
    var session = NewSession();
    session.Begin(Waypoint(0, 0));
    session.UpdatePreview(FreeRequest(100, 20));

    Assert.Single(session.ConfirmedWaypoints);
    Assert.Equal(2, session.PreviewWaypoints.Length);
    Assert.Equal(Point(100, 20), session.PreviewWaypoints[^1].Position);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~RailDrawingSessionTests
```

Expected: compilation fails because `RailDrawingSession` does not exist.

- [ ] **Step 3: Implement the minimal immutable-facing session**

Use mutable private builders internally but expose immutable arrays. `UpdatePreview` replaces only `CurrentSnap`; `ConfirmPreviewPoint` appends the current candidate once and clears it; repeated identical points are rejected with code `rail.drawing.duplicate_point`.

- [ ] **Step 4: Add step-back, cancel, drag rollback, and reset tests**

```csharp
[Fact]
public void RightClickStepBackRemovesOnePointThenCancelsOrigin()
{
    var session = SessionWithConfirmedPoints(Point(0, 0), Point(100, 0));

    Assert.Equal(RailDrawingUndoResult.PointRemoved, session.UndoStep());
    Assert.Single(session.ConfirmedWaypoints);
    Assert.Equal(RailDrawingUndoResult.DraftCancelled, session.UndoStep());
    Assert.Equal(RailDrawingState.Idle, session.State);
}

[Fact]
public void ResetAfterCommitKeepsHybridModeButClearsDraft()
{
    var session = SessionWithConfirmedPoints(Point(0, 0), Point(100, 0));

    session.ResetAfterCommit();

    Assert.Equal(RailDrawingInputMode.HybridGuided, session.InputMode);
    Assert.Empty(session.ConfirmedWaypoints);
}
```

- [ ] **Step 5: Run the focused tests and verify GREEN**

Run the Task 2 test command. Expected: all state-machine tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/RailSim.Core/Editing/RailDrawingSession.cs tests/RailSim.Core.Tests/Editing/RailDrawingSessionTests.cs
git commit -m "feat: add hybrid railway drawing session"
```

---

### Task 3: Ghost double-track geometry and engineering metrics

**Files:**
- Create: `src/RailSim.Core/Editing/RailDrawingPreview.cs`
- Create: `tests/RailSim.Core.Tests/Editing/RailDrawingPreviewTests.cs`
- Modify: `src/RailSim.Core/Editing/AlignmentDraft.cs`

**Interfaces:**
- Consumes: `IAlignmentSketchStrategy`, preview waypoints, track spacing, design speed.
- Produces:
  - `AlignmentEndpointHints(double? StartHeadingRadians, double? EndHeadingRadians)`
  - `IGuidedAlignmentSketchStrategy.Build(ImmutableArray<DraftWaypoint> waypoints, AlignmentEndpointHints hints)`
  - `RailDrawingMetrics(long CurrentSegmentLengthMillimeters, long TotalLengthMillimeters, double? MinimumRadiusMeters, int EstimatedSpeedKph, bool VerticalProfilePending)`
  - `RailDrawingPreview(AlignmentDraft Draft, ImmutableArray<WorldPoint> UpTrackPoints, ImmutableArray<WorldPoint> DownTrackPoints, RailDrawingMetrics Metrics, RailSnapResult Snap)`
  - `ICurveSpeedEstimator.EstimateKph(double radiusMeters, int designSpeedKph)`
  - `ConservativeCurveSpeedEstimator`, using `floor(sqrt(radiusMeters * 200 / 11.8))` capped by design speed.
  - `RailDrawingPreviewBuilder.Build(ImmutableArray<DraftWaypoint> waypoints, RailSnapResult snap, TrackPlacementOptions options, AlignmentEndpointHints endpointHints, double metersPerPixel)`.

- [ ] **Step 1: Write failing dual-track and metric tests**

```csharp
[Fact]
public void PreviewContainsOffsetTracksAndLengths()
{
    var preview = BuildPreview(
        Waypoint(0, 0),
        Waypoint(10_000, 0),
        trackSpacingMillimeters: 5_000);

    Assert.Equal(preview.Draft.SampledPoints.Length, preview.UpTrackPoints.Length);
    Assert.Equal(preview.Draft.SampledPoints.Length, preview.DownTrackPoints.Length);
    Assert.NotEqual(preview.UpTrackPoints[4], preview.DownTrackPoints[4]);
    Assert.Equal(10_000_000, preview.Metrics.TotalLengthMillimeters);
    Assert.True(preview.Metrics.VerticalProfilePending);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~RailDrawingPreviewTests
```

Expected: compilation fails because preview types do not exist.

- [ ] **Step 3: Implement offset geometry and radius estimation**

Choose a deterministic subdivision count between 8 and 64 samples per segment
so the estimated chord error is no more than 0.75 screen pixels at the supplied
`metersPerPixel`; include that sampled `AlignmentDraft` in the preview and use
it unchanged at submission. Calculate a normalized local tangent at each
sample, average adjacent segment normals at interior points, and offset by half
the track spacing. Estimate radius from each non-collinear three-point
circumcircle, take the minimum, and pass it to `ICurveSpeedEstimator`. Straight
geometry returns no finite minimum radius and uses design speed.

- [ ] **Step 4: Add tangent continuity, preview/submission identity, and curve-speed tests**

```csharp
[Fact]
public void SnappedEndpointHeadingControlsTheFinalCurveTangent()
{
    var draft = new ControlPointSplineStrategy().Build(
        ImmutableArray.Create(
            Waypoint(0, 0),
            Waypoint(10_000, 5_000)),
        new AlignmentEndpointHints(null, EndHeadingRadians: 0));
    var previous = draft.SampledPoints[^2];
    var final = draft.SampledPoints[^1];

    Assert.True(Math.Abs(final.YMillimeters - previous.YMillimeters) <= 1);
    Assert.True(final.XMillimeters > previous.XMillimeters);
}

[Fact]
public void PreviewDraftIsTheExactObjectUsedForPlacement()
{
    var preview = BuildCurvedPreview();
    var patch = new DoubleTrackCorridorStrategy(
        new DeterministicStableIdSource("preview-match"))
        .CreatePatch(
            RailTopology.Empty,
            preview.Draft,
            TrackPlacementOptions.HighSpeedDoubleTrack);

    Assert.Equal(
        preview.Draft.SampledPoints,
        Assert.Single(patch.AddedAlignments).ControlPoints);
}

[Theory]
[InlineData(7_000, 344)]
[InlineData(20_000, 350)]
public void CurveSpeedIsConservativeAndDesignSpeedCapped(
    double radiusMeters,
    int expectedKph)
{
    Assert.Equal(
        expectedKph,
        new ConservativeCurveSpeedEstimator().EstimateKph(radiusMeters, 350));
}
```

Implement endpoint hints by creating virtual Catmull–Rom neighbors in the
requested heading, using the adjacent confirmed segment length. Preserve the
existing `IAlignmentSketchStrategy.Build(waypoints)` method by forwarding with
empty hints, so existing callers and future B/C strategies remain compatible.

- [ ] **Step 5: Run the focused tests and verify GREEN**

Run the Task 3 test command. Expected: all preview tests pass with deterministic points and metrics.

- [ ] **Step 6: Commit**

```powershell
git add src/RailSim.Core/Editing/AlignmentDraft.cs src/RailSim.Core/Editing/RailDrawingPreview.cs tests/RailSim.Core.Tests/Editing/RailDrawingPreviewTests.cs
git commit -m "feat: preview ghost railway geometry and speed"
```

---

### Task 4: Atomic snapping to existing track and controller integration

**Files:**
- Create: `src/RailSim.Core/Editing/RailDrawingCommitComposer.cs`
- Create: `tests/RailSim.Core.Tests/Editing/RailDrawingCommitComposerTests.cs`
- Modify: `game/Map/WorldEditorController.cs`
- Modify: `tests/RailSim.Core.Tests/Editing/AlignmentStrategyTests.cs`

**Interfaces:**
- Consumes: `RailDrawingSession`, `RailDrawingPreviewBuilder`, `RailSpatialIndex`, `WorldRegionIndex`, `RailTopologyEditStrategy`, `DoubleTrackCorridorStrategy`.
- Produces:
  - `RailDrawingCommitComposer.CreateDoubleTrackPatch(WorldState world, AlignmentDraft draft, TrackPlacementOptions options, IStableIdSource ids)`
  - controller properties `DrawingState`, `DrawingPreview`, `CurrentSnap`
  - controller methods `UpdateDrawingPreview`, `ConfirmDrawingPoint`, `UndoDrawingStep`, `CommitDrawingAtPreview`.

- [ ] **Step 1: Write failing atomic track-split attachment test**

```csharp
[Fact]
public void TrackSplitSnapAndNewCorridorAreOneReversiblePatch()
{
    var world = WorldWithExistingDoubleTrack();
    var target = FirstTrack(world);
    var splitPoint = Midpoint(world, target);
    var draft = DraftEndingAtTrack(splitPoint, target.Id);

    var patch = NewComposer().CreateDoubleTrackPatch(
        world,
        draft,
        TrackPlacementOptions.HighSpeedDoubleTrack,
        new DeterministicStableIdSource("attached"));
    var changed = patch.ApplyTo(world.RailTopology);
    var restored = patch.Invert().ApplyTo(changed);

    Assert.Contains(
        changed.Connections.Values,
        connection => connection.NodeId == patch.AddedNodes.Single(
            node => node.Position == splitPoint).Id);
    Assert.Equal(
        Hash(world.RailTopology),
        Hash(restored));
}
```

- [ ] **Step 2: Run the focused composer test and verify RED**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~RailDrawingCommitComposerTests
```

Expected: compilation fails because `RailDrawingCommitComposer` does not exist.

- [ ] **Step 3: Implement atomic composition**

For each `DraftWaypoint.SplitTrackId`, create a split patch against the current candidate topology, replace that waypoint with the new split node ID, apply the split to the candidate, then build the double-track corridor and combine all patches. Reject two split waypoints referencing the same original track with `rail.drawing.repeated_split_target`, because the first split removes that stable track ID.
Reject a target that disappeared after preview with
`rail.drawing.snap_target_missing`; the controller keeps the draft and
re-resolves the cursor as a free candidate on its next preview update.

- [ ] **Step 4: Integrate the session and indexed snap candidate lookup**

Replace controller-owned raw list mutation with `RailDrawingSession` proxies. Cache `RailSpatialIndex` by world revision; query only the cursor-radius bounds and query `BaseWorld.RegionIndex` for stations. Generate projected nearest positions for candidate tracks, add `0°`, `±15°`, `±30°`, `±45°`, and `±90°` guide candidates, resolve with a 16 px snap radius and 8 px angle-guide threshold, then build `DrawingPreview`.
When snapping to an existing node or track, derive the target heading from the
nearest incident alignment segment and pass it as `AlignmentEndpointHints`;
the same preview draft is later consumed by the commit composer.

- [ ] **Step 5: Preserve warnings and continuous construction**

Route `CommitDoubleTrackDraft` through the composer. On success call `ResetAfterCommit` without switching tools; on validation rejection retain both confirmed points and current candidate; any candidate change clears `_pendingWarningFingerprint`.

- [ ] **Step 6: Run core tests and build the Godot app**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test RailSim.sln --configuration Release --no-restore -p:NuGetAudit=false
& "$env:USERPROFILE\.dotnet\dotnet.exe" build game/RailSim.App.csproj --configuration Debug --no-restore -p:NuGetAudit=false
```

Expected: all tests pass and Godot app builds with zero errors.

- [ ] **Step 7: Commit**

```powershell
git add src/RailSim.Core/Editing/RailDrawingCommitComposer.cs tests/RailSim.Core.Tests/Editing/RailDrawingCommitComposerTests.cs tests/RailSim.Core.Tests/Editing/AlignmentStrategyTests.cs game/Map/WorldEditorController.cs
git commit -m "feat: integrate continuous railway drafting"
```

---

### Task 5: Godot pointer semantics and visual guides

**Files:**
- Modify: `game/Map/WorldMapView.cs`
- Modify: `game/App/Bootstrap.cs`
- Modify: `scripts/verify.ps1`

**Interfaces:**
- Consumes: controller `DrawingState`, `DrawingPreview`, `CurrentSnap`, `UpdateDrawingPreview`, `ConfirmDrawingPoint`, `UndoDrawingStep`, `CommitDrawingAtPreview`.
- Produces: cursor-following preview, middle-button pan, drawing-state right-click step-back, Shift snap suppression, endpoint-safe double-click, dual-track rendering, snap/guide labels, and marker `RAILSIM_DRAWING_FEEL_SMOKE complete=True`.

- [ ] **Step 1: Add a failing Godot drawing-feel smoke marker**

Extend `RunManualToolSmoke` to:

1. select `DrawDoubleTrack`;
2. confirm an origin;
3. update the cursor without confirming and assert world revision is unchanged while preview has two offset tracks;
4. confirm three points;
5. call step-back and assert one point is removed;
6. resolve the same cursor with and without Shift and assert the Shift result is free;
7. commit at the current preview candidate and assert the resulting alignment includes the candidate endpoint;
8. assert the drawing tool remains selected with an empty new session.

Print `RAILSIM_DRAWING_FEEL_SMOKE complete=True`, add it to the overall smoke success condition, and make `scripts/verify.ps1` reject a missing or false marker.

- [ ] **Step 2: Run verification and confirm RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
```

Expected: Godot smoke fails because `WorldMapView` and controller input behavior are not yet wired to satisfy the new marker.

- [ ] **Step 3: Implement pointer state translation**

Store only the latest pointer position and modifier state from mouse-motion
events, then call `UpdateDrawingPreview` at most once per rendered frame. On
left press confirm the current preview point. On double-click, confirm that
location exactly once and then commit. Use middle button for pan. Use right
button for step-back only when `DrawingState != Idle`; otherwise retain
right-button pan. Forward Shift state on every preview update. Switching tools
calls `Cancel`, while Esc during waypoint drag restores the pre-drag position.

- [ ] **Step 4: Render construction feedback**

Draw confirmed centerline as opaque yellow dashed guidance, candidate centerline as translucent yellow, and `UpTrackPoints`/`DownTrackPoints` as cyan lines. Draw blue snap halo and type label, angle-guide line and degrees, plus a cursor metric label:

```text
本段 42.6 km｜总长 118.2 km｜R 7,500 m｜预计 350 km/h｜纵断面待详细设计
```

Invalid candidates use red and display the controller rejection message.

- [ ] **Step 5: Run verification and confirm GREEN**

Run the Task 5 verification command. Expected: build is clean, all xUnit tests pass, and `RAILSIM_DRAWING_FEEL_SMOKE complete=True` appears before `VERIFY_OK`.

- [ ] **Step 6: Commit**

```powershell
git add game/Map/WorldMapView.cs game/App/Bootstrap.cs scripts/verify.ps1
git commit -m "feat: make railway drawing fluid in Godot"
```

---

### Task 6: Documentation, regression gate, review, and deployment

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-07-29-railway-drawing-interaction-design.md`
- Modify: `scripts/verify.ps1`

**Interfaces:**
- Consumes: completed interaction behavior and verification outputs.
- Produces: accurate controls documentation, final test-count floor, accepted coverage result, independent review result, and a runnable desktop deployment.

- [ ] **Step 1: Update user controls and implementation status**

Document left-click continuous placement, live ghost lines, middle-button pan, drawing-state right-click step-back, Shift snap suppression, double-click endpoint submission, metrics, and continuous post-commit drawing. Change the design status to implemented only after verification passes.

- [ ] **Step 2: Raise the test-count floor to the observed total**

After the full test run, set `scripts/verify.ps1` minimum total and passed counts to the exact new suite count so future regressions cannot silently remove tests.

- [ ] **Step 3: Run formatting and full clean verification**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" format RailSim.sln --no-restore
git diff --check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
```

Expected: formatting produces no remaining diff, `git diff --check` exits zero, Release/Debug builds report zero errors, all tests pass, line coverage remains at least 75%, every Godot marker is true, and output ends with `VERIFY_OK`.

- [ ] **Step 4: Request independent blocking review**

Ask the existing milestone reviewer to inspect the branch against every design completion criterion, reporting only Critical/Important findings and `Ready to merge`.

- [ ] **Step 5: Fix each blocking review item with a red-green test**

For each reported blocker, add the smallest reproducing test, verify RED, implement the fix, verify GREEN, and rerun the full verification command. Do not merge with an unresolved Critical or Important item.

- [ ] **Step 6: Commit final documentation and review fixes**

```powershell
git add README.md docs/superpowers/specs/2026-07-29-railway-drawing-interaction-design.md scripts/verify.ps1
git commit -m "docs: complete fluid railway drawing milestone"
```

- [ ] **Step 7: Merge, verify from the main checkout, and launch**

Fast-forward merge the isolated branch into `master`, copy the already-verified local Godot runtime only if the main checkout lacks it, rerun `scripts/verify.ps1` from the main checkout, then start:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1
```

Confirm the Godot process is responding and `.tools/logs/godot-runtime.log` contains no `ERROR`, `FATAL`, `CRASH`, or unhandled exception.
