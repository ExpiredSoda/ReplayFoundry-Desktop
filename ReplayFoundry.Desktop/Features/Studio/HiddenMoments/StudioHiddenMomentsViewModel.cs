using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public sealed class StudioHiddenMomentAcceptedEventArgs : EventArgs
{
    public StudioHiddenMomentAcceptedEventArgs(string candidateId)
    {
        CandidateId = candidateId;
    }

    public string CandidateId { get; }
}

public sealed class StudioHiddenMomentsViewModel : ObservableObject, IDisposable
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IStudioHiddenMomentDecisionStore? _decisionStore;
    private readonly IResearchFeedbackRecorder? _researchFeedback;
    private readonly IGenerationCaptionPreparationService?
        _captionPreparation;
    private readonly IGenerationEditorialMetadataService?
        _editorialMetadata;
    private readonly DelegateCommand _openCommand;
    private readonly DelegateCommand _closeCommand;
    private readonly AsyncDelegateCommand _acceptCommand;
    private readonly DelegateCommand _skipCommand;
    private readonly DelegateCommand _resetCommand;
    private GenerationOutputProject? _project;
    private GenerationHiddenMoment[] _pending = [];
    private GenerationHiddenMoment? _current;
    private string? _projectId;
    private int _sessionTotal;
    private int _reviewedCount;
    private bool _isOpen;
    private bool _isProjectMutationBlocked;
    private bool _isDisposed;
    private string? _error;
    private CancellationTokenSource? _acceptCancellation;

    public StudioHiddenMomentsViewModel(
        IGenerationOutputEditor? outputEditor,
        IStudioPreviewMediaService? previewMediaService,
        IStudioHiddenMomentDecisionStore? decisionStore,
        IResearchFeedbackRecorder? researchFeedback = null,
        IGenerationCaptionPreparationService? captionPreparation = null,
        IGenerationEditorialMetadataService? editorialMetadata = null)
    {
        _outputEditor = outputEditor;
        _decisionStore = decisionStore;
        _researchFeedback = researchFeedback;
        _captionPreparation = captionPreparation;
        _editorialMetadata = editorialMetadata;
        Preview = new StudioPreviewViewModel(
            previewMediaService,
            showCaptionControls: false);
        _openCommand = new DelegateCommand(Open, CanOpen);
        _closeCommand = new DelegateCommand(Close, () => IsOpen);
        _acceptCommand = new AsyncDelegateCommand(AcceptAsync, CanDecide);
        _skipCommand = new DelegateCommand(Skip, CanDecide);
        _resetCommand = new DelegateCommand(ResetProject, CanResetProject);
    }

    public event EventHandler<StudioHiddenMomentAcceptedEventArgs>?
        MomentAccepted;

    public StudioPreviewViewModel Preview { get; }
    public GenerationHiddenMoment? Current => _current;
    public bool IsOpen => _isOpen;
    public bool HasAvailableMoments => _pending.Length > 0;
    public bool IsExhausted => IsOpen && _current is null;
    public int RemainingCount => _pending.Length;
    public int ReviewedCount => _reviewedCount;
    public int SessionTotal => _sessionTotal;
    public string OpenButtonText => HasAvailableMoments
        ? $"Review {RemainingCount} alternate moments"
        : "No hidden moments remaining";
    public string ProgressText => _current is null
        ? $"Reviewed {_reviewedCount} of {_sessionTotal}"
        : $"Moment {_reviewedCount + 1} of {_sessionTotal}";
    public string MomentTitle => _current is null
        ? "You reviewed every hidden moment"
        : _current.SourceName;
    public string MomentDetail => _current is null
        ? "Accepted moments are now in Studio. Skipped moments remain separate from Like, Neutral, and Dislike feedback."
        : $"{Format(_current.SourceStart)}–{Format(_current.SourceEnd)} · " +
          $"{MediaTimeFormatter.Format(_current.Duration)} · {_current.ReviewReason}";
    public string EvidenceText => _current?.Explanation ??
        "Nothing else is waiting in this review session.";
    public string? Error => _error;
    public bool HasError => !string.IsNullOrWhiteSpace(_error);
    public bool IsPreparingAcceptedMoment => _acceptCancellation is not null;
    public bool IsProjectMutationBlocked => _isProjectMutationBlocked;
    public ICommand OpenCommand => _openCommand;
    public ICommand CloseCommand => _closeCommand;
    public ICommand AcceptCommand => _acceptCommand;
    public ICommand SkipCommand => _skipCommand;
    public ICommand ReviewSkippedAgainCommand => _resetCommand;

    public void Bind(GenerationOutputProject? project)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        bool newProject = !string.Equals(
            _projectId,
            project?.Id,
            StringComparison.Ordinal);
        _project = project;
        _projectId = project?.Id;
        if (newProject)
        {
            _isOpen = false;
            _sessionTotal = project?.HiddenMomentCount ?? 0;
            _reviewedCount = 0;
            _error = null;
        }

        _pending = project?.HiddenMoments
            .Where(value => _decisionStore?.Find(project.Id, value.Id) is null)
            .OrderBy(static value => value.ReviewOrder)
            .ToArray() ?? [];
        int stored = project is null || _decisionStore is null
            ? 0
            : _decisionStore.Current.Count(value =>
                value.ProjectId.Equals(project.Id, StringComparison.Ordinal));
        _sessionTotal = Math.Max(_sessionTotal, _pending.Length + stored);
        _reviewedCount = Math.Max(0, _sessionTotal - _pending.Length);
        SetCurrent(_isOpen ? _pending.FirstOrDefault() : null);
        NotifyAll();
    }

    public void SetProjectMutationBlocked(bool value)
    {
        if (_isProjectMutationBlocked == value)
        {
            return;
        }

        _isProjectMutationBlocked = value;
        NotifyAll();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        _acceptCancellation?.Cancel();
        _acceptCancellation?.Dispose();
        Preview.Dispose();
    }

    private bool CanOpen() =>
        !IsProjectMutationBlocked &&
        _project is { IsFinalized: false } &&
        HasAvailableMoments;

    private void Open()
    {
        if (!CanOpen())
        {
            return;
        }
        _isOpen = true;
        SetCurrent(_pending.FirstOrDefault());
        NotifyAll();
    }

    private void Close()
    {
        _acceptCancellation?.Cancel();
        _isOpen = false;
        SetCurrent(null);
        NotifyAll();
    }

    private bool CanDecide() =>
        !IsProjectMutationBlocked &&
        _project is { IsFinalized: false } &&
        _current is not null &&
        _outputEditor is not null;

    private async Task AcceptAsync()
    {
        if (!CanDecide() ||
            _project is not { } project ||
            _current is not { } current ||
            _outputEditor is null)
        {
            return;
        }
        try
        {
            _acceptCancellation = new CancellationTokenSource();
            NotifyAll();
            GenerationCandidateCaptionTrack? captions = null;
            bool hasGenerationProvenance =
                current.TryGetGenerationProvenance(
                    out var analyzedSource,
                    out var candidate);
            if (current.CaptionSourceSelection is { } selection &&
                current.CaptionStyle is { } style &&
                hasGenerationProvenance)
            {
                if (_captionPreparation is null)
                {
                    throw new InvalidOperationException(
                        "This run requested captions, but the verified local transcription provider is unavailable.");
                }
                var selected = new GenerationMomentCandidate(
                    current.Id,
                    analyzedSource!,
                    candidate!,
                    current.SourceOrder,
                    current.ReviewOrder,
                    GenerationCandidateSelectionReason.HiddenMomentRecovery);
                captions = await _captionPreparation.PrepareCandidateAsync(
                    selected,
                    selection,
                    style,
                    _acceptCancellation.Token);
            }
            GenerationHiddenMoment prepared =
                _editorialMetadata is null || !hasGenerationProvenance
                ? current
                : await _editorialMetadata.PrepareAcceptedHiddenAsync(
                    current,
                    captions,
                    _acceptCancellation.Token);
            if (IsProjectMutationBlocked ||
                !ReferenceEquals(_project, project))
            {
                _error =
                    "Studio changed while this hidden moment was being prepared. Review it again before adding it.";
                NotifyAll();
                return;
            }
            _outputEditor.AcceptHiddenMoment(
                project.Id,
                current.Id,
                captions,
                prepared.EditorialContext,
                prepared.EditorialMetadata);
            Save(current, StudioHiddenMomentReviewDecision.AcceptedIntoStudio);
            RecordResearch(current, ResearchFeedbackValue.Accepted);
            _reviewedCount = Math.Min(_sessionTotal, _reviewedCount + 1);
            _error = null;
            MomentAccepted?.Invoke(
                this,
                new StudioHiddenMomentAcceptedEventArgs(current.Id));
            NotifyAll();
        }
        catch (OperationCanceledException)
        {
            _error = "Adding this hidden moment was cancelled.";
            NotifyAll();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            _error = "Replay Foundry could not add this hidden moment: " +
                exception.Message;
            NotifyAll();
        }
        finally
        {
            _acceptCancellation?.Dispose();
            _acceptCancellation = null;
            NotifyAll();
        }
    }

    private void Skip()
    {
        if (!CanDecide() || _current is not { } current)
        {
            return;
        }
        try
        {
            Save(current, StudioHiddenMomentReviewDecision.SkippedForProject);
            RecordResearch(current, ResearchFeedbackValue.Skipped);
            _pending = _pending.Where(value => !value.Id.Equals(
                current.Id,
                StringComparison.Ordinal)).ToArray();
            _reviewedCount++;
            _error = null;
            SetCurrent(_pending.FirstOrDefault());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _error = "Replay Foundry could not save this decision: " +
                exception.Message;
        }
        NotifyAll();
    }

    private bool CanResetProject() =>
        !IsProjectMutationBlocked &&
        _project is { IsFinalized: false } &&
        _decisionStore is not null &&
        _decisionStore.Current.Any(value =>
            value.ProjectId.Equals(_project.Id, StringComparison.Ordinal) &&
            value.Decision ==
                StudioHiddenMomentReviewDecision.SkippedForProject);

    private void ResetProject()
    {
        if (_project is null || _decisionStore is null)
        {
            return;
        }
        _decisionStore.ClearSkippedForProject(_project.Id);
        Bind(_project);
        Open();
    }

    private void Save(
        GenerationHiddenMoment moment,
        StudioHiddenMomentReviewDecision decision)
    {
        if (_project is null || _decisionStore is null)
        {
            return;
        }
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                moment.SourceFullPath.ToUpperInvariant() + "|" +
                moment.SourceMedia.Duration.Ticks)));
        _decisionStore.Upsert(new StudioHiddenMomentDecision(
            _project.Id,
            moment.Id,
            sourceIdentity,
            moment.SourceStart,
            moment.SourceEnd,
            decision,
            DateTimeOffset.UtcNow));
    }

    private void SetCurrent(GenerationHiddenMoment? value)
    {
        _current = value;
        GenerationOutputAsset? previewAsset = value is null || _project is null
            ? null
            : new GenerationOutputAsset(
                value.Id,
                _project.Assets.Count + 1,
                value.SourceMedia,
                outputFullPath: null,
                value.SourceStart,
                value.SourceEnd,
                value.FinalScore,
                value.QualityTarget,
                GenerationCandidateSelectionReason.HiddenMomentRecovery,
                value.Explanation,
                preferenceFeatures: value.PreferenceFeatures);
        Preview.Bind(value is not null, _project, previewAsset);
    }

    private void RecordResearch(
        GenerationHiddenMoment moment,
        ResearchFeedbackValue value) =>
        _researchFeedback?.Record(
            moment.Id,
            moment.SourceFullPath,
            moment.SourceMedia.Duration,
            moment.PreferenceFeatures,
            ResearchFeedbackChannel.HiddenMomentReview,
            value);

    private void NotifyAll()
    {
        foreach (string propertyName in new[]
        {
            nameof(Current),
            nameof(IsOpen),
            nameof(HasAvailableMoments),
            nameof(IsExhausted),
            nameof(RemainingCount),
            nameof(ReviewedCount),
            nameof(SessionTotal),
            nameof(OpenButtonText),
            nameof(ProgressText),
            nameof(MomentTitle),
            nameof(MomentDetail),
            nameof(EvidenceText),
            nameof(Error),
            nameof(HasError),
            nameof(IsPreparingAcceptedMoment),
            nameof(IsProjectMutationBlocked),
        })
        {
            OnPropertyChanged(propertyName);
        }
        _openCommand.RaiseCanExecuteChanged();
        _closeCommand.RaiseCanExecuteChanged();
        _acceptCommand.RaiseCanExecuteChanged();
        _skipCommand.RaiseCanExecuteChanged();
        _resetCommand.RaiseCanExecuteChanged();
    }

    private static string Format(TimeSpan value) =>
        MediaTimeFormatter.Format(value);
}
