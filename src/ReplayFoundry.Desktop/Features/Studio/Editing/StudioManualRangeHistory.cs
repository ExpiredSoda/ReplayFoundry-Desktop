namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal readonly record struct StudioManualRange(double Start, double End)
{
    public StudioManualRange MoveTo(double start, double sourceDuration)
    {
        double duration = Math.Clamp(End - Start, 0, sourceDuration);
        start = Math.Clamp(start, 0, Math.Max(0, sourceDuration - duration));
        return new(start, start + duration);
    }
}

internal sealed class StudioManualRangeHistory
{
    private readonly List<StudioManualRange> _undo = [];
    private readonly Stack<StudioManualRange> _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Clear() { _undo.Clear(); _redo.Clear(); }
    public void Record(StudioManualRange before, StudioManualRange after)
    {
        if (before == after) return;
        if (_undo.Count == 50) _undo.RemoveAt(0);
        _undo.Add(before); _redo.Clear();
    }
    public StudioManualRange Undo(StudioManualRange current)
    {
        if (!CanUndo) return current;
        var previous = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Push(current);
        return previous;
    }
    public StudioManualRange Redo(StudioManualRange current)
    {
        if (!CanRedo) return current;
        _undo.Add(current); return _redo.Pop();
    }
}
