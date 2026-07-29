using System.Collections.Immutable;

namespace RailSim.Core.Editing;

public enum RailDrawingInputMode
{
    HybridGuided,
    MultiPointPrecision,
    DragTangent
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
    private readonly List<DraftWaypoint> _confirmed = [];
    private int _draggedIndex = -1;
    private DraftWaypoint _preDragWaypoint;

    public RailDrawingSession(
        IRailSnapResolver snapResolver,
        RailDrawingInputMode inputMode =
            RailDrawingInputMode.HybridGuided)
    {
        _snapResolver = snapResolver ??
            throw new ArgumentNullException(nameof(snapResolver));
        InputMode = inputMode;
    }

    public RailDrawingInputMode InputMode { get; private set; }

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

            if (_confirmed.Count > 0 &&
                _confirmed[^1].Position ==
                CurrentSnap.Waypoint.Position)
            {
                return ConfirmedWaypoints;
            }

            return _confirmed
                .Append(CurrentSnap.Waypoint)
                .ToImmutableArray();
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
        if (_confirmed.Count > 0 &&
            _confirmed[^1].Position == waypoint.Position)
        {
            return RailDrawingActionResult.Reject(
                "rail.drawing.duplicate_point");
        }

        _confirmed.Add(waypoint);
        CurrentSnap = null;
        State = RailDrawingState.Previewing;
        return RailDrawingActionResult.Accept();
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
        InputMode = inputMode;
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
