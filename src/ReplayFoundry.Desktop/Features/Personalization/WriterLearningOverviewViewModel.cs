using System.IO;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Personalization;

public sealed record WriterLearningOverview(int SavedExamples, int Recordings, int FactsAwaitingReview, bool SelectionPresent);

public sealed class WriterLearningOverviewViewModel : ObservableObject
{
    private readonly Func<WriterLearningOverview> _read;
    private readonly AsyncDelegateCommand _refresh;
    public WriterLearningOverviewViewModel(Func<WriterLearningOverview> read)
    {
        _read = read;
        _refresh = new(RefreshAsync);
    }
    public string Summary { get; private set; } = "Refresh to inspect the wording feedback saved on this PC.";
    public string State { get; private set; } = "Personal writing model";
    public ICommand RefreshCommand => _refresh;
    private async Task RefreshAsync()
    {
        try
        {
            var status = await Task.Run(_read);
            State = status.SelectionPresent ? "Personal selection saved · checked when loaded" : "Collecting writing feedback";
            Summary = $"{status.SavedExamples} saved examples · {status.Recordings} recordings · {status.FactsAwaitingReview} fact corrections awaiting review. " +
                "Eligibility is checked during evaluation; saved counts alone do not qualify a model.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { State = "Feedback unavailable"; Summary = "The saved feedback could not be read. Your existing data was kept."; }
        OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(Summary));
    }
}
