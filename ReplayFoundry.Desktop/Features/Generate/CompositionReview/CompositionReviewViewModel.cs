using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public enum CompositionReviewInitializationOutcome
{
    Completed,
    LifecycleCancelled,
}

public sealed class CompositionReviewViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly DelegateCommand
        _fullFrameGameplayCommand;
    private readonly DelegateCommand
        _addGameplayCommand;
    private readonly DelegateCommand
        _addPresenterCommand;
    private readonly DelegateCommand
        _addChatOrTextCommand;
    private readonly DelegateCommand
        _addOverlayCommand;
    private readonly DelegateCommand
        _removeSelectedRegionCommand;
    private readonly DelegateCommand
        _confirmCurrentSourceCommand;
    private readonly DelegateCommand
        _applyCurrentLayoutCommand;
    private readonly DelegateCommand
        _cancelCommand;
    private readonly DelegateCommand
        _continueCommand;
    private readonly AsyncDelegateCommand
        _refreshPreviewCommand;

#pragma warning disable CA2213 // Alias to an item owned and disposed through Sources.
    private CompositionReviewSourceViewModel
        _selectedSource;
#pragma warning restore CA2213
    private bool _isInitialized;
    private bool _isDisposed;

    public CompositionReviewViewModel(
        GenerationCompositionReviewRequest request,
        IVideoPreviewFrameProvider previewFrameProvider,
        GenerationCompositionReviewResult? initialResult = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);

        if (initialResult is not null &&
            !ReferenceEquals(
                initialResult.Preparation,
                request.Preparation))
        {
            throw new ArgumentException(
                "The prior composition review result belongs to different prepared sources.",
                nameof(initialResult));
        }

        Request = request;

        var sources =
            new CompositionReviewSourceViewModel[
                request.SourceCount];

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            PreparedSourceCompositionPlan? initialPlan =
                initialResult?.SourcePlans[index];

            sources[index] =
                new CompositionReviewSourceViewModel(
                    request.Sources[index],
                    isReference:
                        ReferenceEquals(
                            request.Sources[index],
                            request.ReferenceSource),
                    previewFrameProvider,
                    initialPlan);

            sources[index].PropertyChanged +=
                Source_PropertyChanged;
        }

        Sources =
            Array.AsReadOnly(sources);

        _selectedSource =
            sources.Single(
                static source =>
                    source.IsReference);

        AvailableRoles =
            Array.AsReadOnly(
            [
                CompositionRegionRole.Gameplay,
                CompositionRegionRole.Presenter,
                CompositionRegionRole.ChatOrText,
                CompositionRegionRole.Overlay,
                CompositionRegionRole.Unknown,
            ]);

        _fullFrameGameplayCommand =
            new DelegateCommand(
                () =>
                    SelectedSource
                        .UseFullFrameGameplay());

        _addGameplayCommand =
            new DelegateCommand(
                () =>
                    SelectedSource.AddRegion(
                        CompositionRegionRole.Gameplay));

        _addPresenterCommand =
            new DelegateCommand(
                () =>
                    SelectedSource.AddRegion(
                        CompositionRegionRole.Presenter));

        _addChatOrTextCommand =
            new DelegateCommand(
                () =>
                    SelectedSource.AddRegion(
                        CompositionRegionRole.ChatOrText));

        _addOverlayCommand =
            new DelegateCommand(
                () =>
                    SelectedSource.AddRegion(
                        CompositionRegionRole.Overlay));

        _removeSelectedRegionCommand =
            new DelegateCommand(
                () =>
                    SelectedSource
                        .RemoveSelectedRegion(),
                () =>
                    SelectedSource
                        .CanRemoveSelectedRegion);

        _confirmCurrentSourceCommand =
            new DelegateCommand(
                ConfirmCurrentSource,
                () =>
                    SelectedSource.CanConfirm);

        _applyCurrentLayoutCommand =
            new DelegateCommand(
                ApplyCurrentLayoutToRemainingSources,
                CanApplyCurrentLayoutToRemainingSources);

        _cancelCommand =
            new DelegateCommand(
                RequestCancel);

        _continueCommand =
            new DelegateCommand(
                Complete,
                () =>
                    CanContinue);

        _refreshPreviewCommand =
            new AsyncDelegateCommand(
                LoadSelectedPreviewAsync,
                () =>
                    !SelectedSource.IsLoadingPreview);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CancelRequested;

    public event EventHandler<
        CompositionReviewCompletedEventArgs>?
        FinishRequested;

    public GenerationCompositionReviewRequest Request { get; }

    public IReadOnlyList<CompositionReviewSourceViewModel>
        Sources
    {
        get;
    }

    public IReadOnlyList<CompositionRegionRole>
        AvailableRoles
    {
        get;
    }

    public CompositionReviewSourceViewModel SelectedSource
    {
        get => _selectedSource;

        set
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(value);

            if (!Sources.Contains(value))
            {
                throw new ArgumentException(
                    "The selected review source must belong to this request.",
                    nameof(value));
            }

            if (ReferenceEquals(
                    _selectedSource,
                    value))
            {
                return;
            }

            _selectedSource = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRegion));

            RaiseCommandStateChanged();

            if (_isInitialized)
            {
                _ = ObservePreviewLoadAsync(
                    value);
            }
        }
    }

    public CompositionRegionDraftViewModel?
        SelectedRegion =>
        SelectedSource.SelectedRegion;

    public int ConfirmedSourceCount =>
        Sources.Count(
            static source =>
                source.IsConfirmed);

    public string ConfirmationSummary =>
        ConfirmedSourceCount == 1
            ? "1 source layout confirmed"
            : $"{ConfirmedSourceCount} source layouts confirmed";

    public bool CanContinue =>
        Sources.All(
            static source =>
                source.IsConfirmed &&
                source.ConfirmedPlan is not null);

    public bool CanApplyCurrentLayout =>
        CanApplyCurrentLayoutToRemainingSources();

    public ICommand FullFrameGameplayCommand =>
        _fullFrameGameplayCommand;

    public ICommand AddGameplayCommand =>
        _addGameplayCommand;

    public ICommand AddPresenterCommand =>
        _addPresenterCommand;

    public ICommand AddChatOrTextCommand =>
        _addChatOrTextCommand;

    public ICommand AddOverlayCommand =>
        _addOverlayCommand;

    public ICommand RemoveSelectedRegionCommand =>
        _removeSelectedRegionCommand;

    public ICommand ConfirmCurrentSourceCommand =>
        _confirmCurrentSourceCommand;

    public ICommand ApplyCurrentLayoutCommand =>
        _applyCurrentLayoutCommand;

    public ICommand RefreshPreviewCommand =>
        _refreshPreviewCommand;

    public ICommand CancelCommand =>
        _cancelCommand;

    public ICommand ContinueCommand =>
        _continueCommand;

    public async Task<CompositionReviewInitializationOutcome>
        InitializeAsync()
    {
        ThrowIfDisposed();

        if (_isInitialized)
        {
            return CompositionReviewInitializationOutcome
                .Completed;
        }

        _isInitialized = true;

        try
        {
            await SelectedSource.LoadPreviewAsync();

            return CompositionReviewInitializationOutcome
                .Completed;
        }
        catch (OperationCanceledException)
            when (_isDisposed)
        {
            return CompositionReviewInitializationOutcome
                .LifecycleCancelled;
        }
        catch (ObjectDisposedException)
            when (_isDisposed)
        {
            return CompositionReviewInitializationOutcome
                .LifecycleCancelled;
        }
    }

    public Task LoadSelectedPreviewAsync()
    {
        ThrowIfDisposed();

        return SelectedSource
            .LoadPreviewAsync();
    }

    public GenerationCompositionReviewResult CreateResult()
    {
        ThrowIfDisposed();

        if (!CanContinue)
        {
            throw new InvalidOperationException(
                "Every source must have a confirmed composition plan before continuing.");
        }

        return new GenerationCompositionReviewResult(
            Request,
            Sources.Select(
                static source =>
                    source.ConfirmedPlan!));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (CompositionReviewSourceViewModel source in
                 Sources)
        {
            source.PropertyChanged -=
                Source_PropertyChanged;

            source.Dispose();
        }
    }

    private void ConfirmCurrentSource()
    {
        if (!SelectedSource.TryConfirm(
                DateTimeOffset.UtcNow))
        {
            RaiseCommandStateChanged();
            return;
        }

        RaiseReviewStateChanged();
    }

    private bool CanApplyCurrentLayoutToRemainingSources()
    {
        return Sources.Count > 1 &&
               Sources.Any(
                   source =>
                       !ReferenceEquals(
                           source,
                           SelectedSource)) &&
               (SelectedSource.IsConfirmed ||
                SelectedSource.CanConfirm);
    }

    private void ApplyCurrentLayoutToRemainingSources()
    {
        if (!SelectedSource.IsConfirmed &&
            !SelectedSource.TryConfirm(
                DateTimeOffset.UtcNow))
        {
            RaiseCommandStateChanged();
            return;
        }

        DateTimeOffset createdAtUtc =
            DateTimeOffset.UtcNow;

        CompositionRegionDraftViewModel[] sourceRegions =
            SelectedSource.Regions.ToArray();

        foreach (CompositionReviewSourceViewModel target in
                 Sources)
        {
            if (ReferenceEquals(
                    target,
                    SelectedSource))
            {
                continue;
            }

            target.ApplyCopiedLayout(
                sourceRegions,
                createdAtUtc);
        }

        RaiseReviewStateChanged();
    }

    private void RequestCancel()
    {
        CancelRequested?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void Complete()
    {
        GenerationCompositionReviewResult result =
            CreateResult();

        FinishRequested?.Invoke(
            this,
            new CompositionReviewCompletedEventArgs(
                result));
    }

    private void Source_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(
                sender,
                SelectedSource) &&
            e.PropertyName ==
            nameof(
                CompositionReviewSourceViewModel
                    .SelectedRegion))
        {
            OnPropertyChanged(
                nameof(SelectedRegion));
        }

        if (e.PropertyName is
            nameof(
                CompositionReviewSourceViewModel
                    .IsConfirmed) or
            nameof(
                CompositionReviewSourceViewModel
                    .CanConfirm) or
            nameof(
                CompositionReviewSourceViewModel
                    .SelectedRegion) or
            nameof(
                CompositionReviewSourceViewModel
                    .CanRemoveSelectedRegion) or
            nameof(
                CompositionReviewSourceViewModel
                    .IsLoadingPreview))
        {
            RaiseReviewStateChanged();
        }
    }

    private void RaiseReviewStateChanged()
    {
        OnPropertyChanged(nameof(ConfirmedSourceCount));
        OnPropertyChanged(nameof(ConfirmationSummary));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanApplyCurrentLayout));
        OnPropertyChanged(nameof(SelectedRegion));

        RaiseCommandStateChanged();
    }

    private void RaiseCommandStateChanged()
    {
        _removeSelectedRegionCommand
            .RaiseCanExecuteChanged();
        _confirmCurrentSourceCommand
            .RaiseCanExecuteChanged();
        _applyCurrentLayoutCommand
            .RaiseCanExecuteChanged();
        _continueCommand
            .RaiseCanExecuteChanged();
        _refreshPreviewCommand
            .RaiseCanExecuteChanged();
    }

    private async Task ObservePreviewLoadAsync(
        CompositionReviewSourceViewModel source)
    {
        try
        {
            await source.LoadPreviewAsync();
        }
        catch (OperationCanceledException)
            when (_isDisposed)
        {
            return;
        }
        catch (ObjectDisposedException)
            when (_isDisposed)
        {
            return;
        }
        catch (Exception exception)
        {
            source.ReportUnexpectedPreviewFailure(exception);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(CompositionReviewViewModel));
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}

public sealed class CompositionReviewCompletedEventArgs :
    EventArgs
{
    public CompositionReviewCompletedEventArgs(
        GenerationCompositionReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
    }

    public GenerationCompositionReviewResult Result { get; }
}
