using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.Progress;

internal sealed class GenerationReadyClipsControl
{
    private readonly Func<bool> _running;
    private readonly Action<string> _setDetail;
    private readonly Action _notify;
    private Action? _finish;
    private bool _requested;
    private int _count;

    internal GenerationReadyClipsControl(Func<bool> running, Action<string> setDetail, Action notify)
    {
        _running = running;
        _setDetail = setDetail;
        _notify = notify;
        Command = new DelegateCommand(Finish, () => CanFinish);
    }

    internal DelegateCommand Command { get; }
    internal bool CanFinish => _running() && !_requested && _finish is not null;
    internal string Label => $"Finish with {_count} ready {(_count == 1 ? "clip" : "clips")}";

    internal void Report(GenerationProgressUpdate update)
    {
        if (update.UseReadyClips is null && !update.ClearReadyClips) return;
        _finish = update.UseReadyClips;
        _count = update.ReadyClipCount;
        Refresh();
    }

    internal void Reset()
    {
        _finish = null;
        _requested = false;
        _count = 0;
    }

    internal void Refresh()
    {
        _notify();
        Command.RaiseCanExecuteChanged();
    }

    private void Finish()
    {
        if (!CanFinish) return;
        _requested = true;
        _finish!();
        _setDetail("Finishing the current check, then preparing captions, titles and descriptions for your ready clips.");
        Refresh();
    }
}
