using System.Collections.Immutable;

namespace RailSim.Core.Editing;

public enum RailDrawingInputMode
{
    HybridGuided,
    MultiPointPrecision,
    DragTangent
}

public enum RailDrawingCandidateDisposition
{
    Append,
    ReplaceLast,
    Reject
}

public readonly record struct RailDrawingCandidateDecision(
    RailDrawingCandidateDisposition Disposition,
    string Code)
{
    public static RailDrawingCandidateDecision Append()
    {
        return new RailDrawingCandidateDecision(
            RailDrawingCandidateDisposition.Append,
            "ok");
    }

    public static RailDrawingCandidateDecision ReplaceLast()
    {
        return new RailDrawingCandidateDecision(
            RailDrawingCandidateDisposition.ReplaceLast,
            "ok");
    }

    public static RailDrawingCandidateDecision Reject(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new RailDrawingCandidateDecision(
            RailDrawingCandidateDisposition.Reject,
            code);
    }
}

public interface IRailDrawingInputStrategy
{
    RailDrawingInputMode Mode { get; }

    RailDrawingCandidateDecision ResolveCandidate(
        ImmutableArray<DraftWaypoint> confirmedWaypoints,
        DraftWaypoint candidate);
}

public sealed class HybridGuidedRailDrawingInputStrategy :
    IRailDrawingInputStrategy
{
    public RailDrawingInputMode Mode =>
        RailDrawingInputMode.HybridGuided;

    public RailDrawingCandidateDecision ResolveCandidate(
        ImmutableArray<DraftWaypoint> confirmedWaypoints,
        DraftWaypoint candidate)
    {
        if (!confirmedWaypoints.IsEmpty &&
            confirmedWaypoints[^1].Position == candidate.Position)
        {
            return confirmedWaypoints[^1] == candidate
                ? RailDrawingCandidateDecision.Reject(
                    "rail.drawing.duplicate_point")
                : RailDrawingCandidateDecision.ReplaceLast();
        }

        return RailDrawingCandidateDecision.Append();
    }
}

public enum RailDrawingState
{
    Idle,
    Previewing,
    DraggingWaypoint,
    ConfirmingWarning
}

public enum RailDrawingUndoResult
{
    NoChange,
    PointRemoved,
    DraftCancelled
}

public readonly record struct RailDrawingActionResult(
    bool Accepted,
    string Code)
{
    public static RailDrawingActionResult Accept()
    {
        return new RailDrawingActionResult(true, "ok");
    }

    public static RailDrawingActionResult Reject(string code)
    {
        return new RailDrawingActionResult(false, code);
    }
}

public sealed class RailDrawingSession
{
    private readonly IRailSnapResolver _snapResolver;
    private readonly IReadOnlyDictionary<
        RailDrawingInputMode,
        IRailDrawingInputStrategy> _inputStrategies;
    private readonly List<DraftWaypoint> _confirmed = [];
    private IRailDrawingInputStrategy _inputStrategy;
    private int _draggedIndex = -1;
    private DraftWaypoint _preDragWaypoint;

    public RailDrawingSession(
        IRailSnapResolver snapResolver,
        RailDrawingInputMode inputMode =
            RailDrawingInputMode.HybridGuided,
        IEnumerable<IRailDrawingInputStrategy>? inputStrategies = null)
    {
        _snapResolver = snapResolver ??
            throw new ArgumentNullException(nameof(snapResolver));
        var strategies = new Dictionary<
            RailDrawingInputMode,
            IRailDrawingInputStrategy>
        {
            [RailDrawingInputMode.HybridGuided] =
                new HybridGuidedRailDrawingInputStrategy()
        };
        if (inputStrategies is not null)
        {
            foreach (var strategy in inputStrategies)
            {
                ArgumentNullException.ThrowIfNull(strategy);
                strategies[strategy.Mode] = strategy;
            }
        }

        _inputStrategies = strategies;
        if (!strategies.TryGetValue(inputMode, out _inputStrategy!))
        {
            throw new InvalidOperationException(
                $"No rail drawing input strategy is registered for {inputMode}.");
        }
    }

    public RailDrawingInputMode InputMode => _inputStrategy.Mode;

    public RailDrawingState State { get; private set; }

    public RailSnapResult? CurrentSnap { get; private set; }

    public ImmutableArray<DraftWaypoint> ConfirmedWaypoints =>
        _confirmed.ToImmutableArray();

    public ImmutableArray<DraftWaypoint> PreviewWaypoints
    {
        get
        {
            if (CurrentSnap is null)
            {
                return ConfirmedWaypoints;
            }

            var confirmed = ConfirmedWaypoints;
            var decision = _inputStrategy.ResolveCandidate(
                confirmed,
                CurrentSnap.Waypoint);
            return decision.Disposition switch
            {
                RailDrawingCandidateDisposition.Append =>
                    confirmed.Add(CurrentSnap.Waypoint),
                RailDrawingCandidateDisposition.ReplaceLast
                    when !confirmed.IsEmpty =>
                    confirmed.SetItem(
                        confirmed.Length - 1,
                        CurrentSnap.Waypoint),
                _ => confirmed
            };
        }
    }

    public void Begin(DraftWaypoint origin)
    {
        Cancel();
        _confirmed.Add(origin);
        State = RailDrawingState.Previewing;
    }

    public void Load(ImmutableArray<DraftWaypoint> waypoints)
    {
        if (waypoints.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one waypoint is required.",
                nameof(waypoints));
        }

        Cancel();
        _confirmed.AddRange(waypoints);
        State = RailDrawingState.Previewing;
    }

    public void UpdatePreview(RailSnapRequest request)
    {
        CurrentSnap = _snapResolver.Resolve(request);
        if (State == RailDrawingState.ConfirmingWarning)
        {
            State = RailDrawingState.Previewing;
        }
    }

    public RailDrawingActionResult ConfirmPreviewPoint()
    {
        if (CurrentSnap is null)
        {
            return RailDrawingActionResult.Reject(
                "rail.drawing.preview_missing");
        }

        var waypoint = CurrentSnap.Waypoint;
        var decision = _inputStrategy.ResolveCandidate(
            ConfirmedWaypoints,
            waypoint);
        if (decision.Disposition ==
            RailDrawingCandidateDisposition.Reject)
        {
            return RailDrawingActionResult.Reject(decision.Code);
        }

        if (decision.Disposition ==
            RailDrawingCandidateDisposition.ReplaceLast)
        {
            if (_confirmed.Count == 0)
            {
                return RailDrawingActionResult.Reject(
                    "rail.drawing.strategy_invalid");
            }
            _confirmed[^1] = waypoint;
        }
        else
        {
            _confirmed.Add(waypoint);
        }

        CurrentSnap = null;
        State = RailDrawingState.Previewing;
        return RailDrawingActionResult.Accept();
    }

    public void InvalidatePreview()
    {
        CurrentSnap = null;
        if (State == RailDrawingState.ConfirmingWarning)
        {
            State = RailDrawingState.Previewing;
        }
    }

    public RailDrawingUndoResult UndoStep()
    {
        CurrentSnap = null;
        ClearDrag();
        if (_confirmed.Count > 1)
        {
            _confirmed.RemoveAt(_confirmed.Count - 1);
            State = RailDrawingState.Previewing;
            return RailDrawingUndoResult.PointRemoved;
        }

        if (_confirmed.Count == 1)
        {
            Cancel();
            return RailDrawingUndoResult.DraftCancelled;
        }

        return RailDrawingUndoResult.NoChange;
    }

    public void BeginWaypointDrag(int index)
    {
        EnsureWaypointIndex(index);
        _draggedIndex = index;
        _preDragWaypoint = _confirmed[index];
        State = RailDrawingState.DraggingWaypoint;
    }

    public void MoveWaypoint(int index, DraftWaypoint waypoint)
    {
        EnsureWaypointIndex(index);
        if ((index > 0 &&
             _confirmed[index - 1].Position == waypoint.Position) ||
            (index < _confirmed.Count - 1 &&
             _confirmed[index + 1].Position == waypoint.Position))
        {
            throw new ArgumentException(
                "A waypoint cannot share its position with a neighbor.",
                nameof(waypoint));
        }

        _confirmed[index] = waypoint;
        CurrentSnap = null;
    }

    public void FinishWaypointDrag()
    {
        ClearDrag();
        State = _confirmed.Count == 0
            ? RailDrawingState.Idle
            : RailDrawingState.Previewing;
    }

    public void CancelWaypointDrag()
    {
        if (_draggedIndex >= 0)
        {
            _confirmed[_draggedIndex] = _preDragWaypoint;
        }

        FinishWaypointDrag();
    }

    public void RemoveWaypoint(int index)
    {
        EnsureWaypointIndex(index);
        _confirmed.RemoveAt(index);
        CurrentSnap = null;
        State = _confirmed.Count == 0
            ? RailDrawingState.Idle
            : RailDrawingState.Previewing;
    }

    public void MarkWarningPending()
    {
        if (_confirmed.Count >= 2)
        {
            State = RailDrawingState.ConfirmingWarning;
        }
    }

    public void SetInputMode(RailDrawingInputMode inputMode)
    {
        if (!_inputStrategies.TryGetValue(
                inputMode,
                out var strategy))
        {
            throw new InvalidOperationException(
                $"No rail drawing input strategy is registered for {inputMode}.");
        }

        _inputStrategy = strategy;
    }

    public void ResetAfterCommit()
    {
        Cancel();
    }

    public void Cancel()
    {
        _confirmed.Clear();
        CurrentSnap = null;
        ClearDrag();
        State = RailDrawingState.Idle;
    }

    private void EnsureWaypointIndex(int index)
    {
        if (index < 0 || index >= _confirmed.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private void ClearDrag()
    {
        _draggedIndex = -1;
        _preDragWaypoint = default;
    }
}
