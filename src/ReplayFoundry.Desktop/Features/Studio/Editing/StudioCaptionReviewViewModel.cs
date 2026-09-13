using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioCaptionReviewViewModel : ObservableObject
{
    private readonly DelegateCommand<StudioCaptionTimingIssue> _review;
    private readonly DelegateCommand _reviewFirst;
    private IReadOnlyList<StudioCaptionTimingIssue> _issues = [];
    private bool _busy;
    private string? _message;
    public StudioCaptionReviewViewModel()
    {
        _review = new(issue => { if (issue is not null) ReviewRequested?.Invoke(issue); },
            issue => !_busy && issue is not null && _issues.Contains(issue));
        _reviewFirst = new(() => _review.Execute(_issues[0]), () => !_busy && HasIssues);
    }
    public event Action<StudioCaptionTimingIssue>? ReviewRequested;
    public ICommand ReviewCommand => _review;
    public ICommand ReviewFirstCommand => _reviewFirst;
    public IReadOnlyList<StudioCaptionTimingIssue> Issues => _issues;
    public bool HasIssues => _issues.Count > 0;
    public bool HasBlockingIssues => _issues.Any(issue => issue.BlocksPop);
    public bool HasMessage => _message is not null;
    public string Summary => $"{_issues.Count} " + (_issues.Count == 1 ? "phrase to review" : "phrases to review");
    public string Guidance => _message ?? (HasBlockingIssues
        ? "Pop needs a time for every word. Render first tries automatic repair; unresolved phrases need review."
        : _issues.Any(issue => issue.NeedsWordTiming)
            ? "Some word times need review. Your current caption settings can still export."
            : "Listen and check these phrases. These reminders do not stop export.");
    internal void SetIssues(IReadOnlyList<StudioCaptionTimingIssue> issues)
    {
        if (_issues.SequenceEqual(issues)) return;
        _issues = issues; _message = null;
        foreach (string name in new[] { nameof(Issues), nameof(HasIssues), nameof(HasBlockingIssues), nameof(HasMessage), nameof(Summary), nameof(Guidance) })
            OnPropertyChanged(name);
        _review.RaiseCanExecuteChanged();
        _reviewFirst.RaiseCanExecuteChanged();
    }
    internal void SetBusy(bool busy) { _busy = busy; _review.RaiseCanExecuteChanged(); _reviewFirst.RaiseCanExecuteChanged(); }
    internal void ShowMessage(string message)
    {
        _message = message;
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(Guidance));
    }
}
