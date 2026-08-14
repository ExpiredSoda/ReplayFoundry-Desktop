using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class CompositionReviewSourceViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private PreparedSourceCompositionPlan?
        _confirmedPlan;
    private string? _validationError;
    private bool _canReuseConfirmedWithoutPreview;
    private bool _isConfirmed;
    private bool _isDirty;
    private bool _isDisposed;

    public CompositionReviewSourceViewModel(
        PreparedGenerationSource preparedSource,
        bool isReference,
        IVideoPreviewFrameProvider previewFrameProvider,
        PreparedSourceCompositionPlan? initialPlan = null)
    {
        ArgumentNullException.ThrowIfNull(preparedSource);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);

        if (initialPlan is not null &&
            !ReferenceEquals(
                initialPlan.PreparedSource,
                preparedSource))
        {
            throw new ArgumentException(
                "The initial composition plan must belong to the prepared source.",
                nameof(initialPlan));
        }

        PreparedSource = preparedSource;
        IsReference = isReference;
        Preview = new CompositionPreviewViewModel(
            preparedSource,
            previewFrameProvider);
        Preview.PropertyChanged += OnPreviewPropertyChanged;
        RegionEditor = new CompositionRegionCollectionViewModel(
            MarkUnconfirmed);
        RegionEditor.PropertyChanged += OnRegionEditorPropertyChanged;

        if (initialPlan is null)
        {
            RegionEditor.InitializeFullFrameGameplay();
        }
        else
        {
            CompositionPlan plan =
                initialPlan.Plan;

            if (plan.Intervals.Count != 1)
            {
                throw new ArgumentException(
                    "Video Layout Review can only restore single-interval manual plans.",
                    nameof(initialPlan));
            }

            RegionEditor.Restore(
                plan.Intervals[0].Regions);

            _confirmedPlan = initialPlan;
            _isConfirmed = true;
            _canReuseConfirmedWithoutPreview = true;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PreparedGenerationSource PreparedSource { get; }

    public bool IsReference { get; }

    public string FileName =>
        PreparedSource.Source.FileName;

    public string FullPath =>
        PreparedSource.Source.FullPath;

    public TimeSpan Duration =>
        PreparedSource.Media.Duration;

    public string DurationText => MediaTimeFormatter.Format(Duration);

    public string ReferenceLabel =>
        IsReference
            ? "REFERENCE"
            : string.Empty;

    public ReadOnlyObservableCollection<
        CompositionRegionDraftViewModel> Regions
        => RegionEditor.Regions;

    public CompositionPreviewViewModel Preview { get; }

    public CompositionRegionCollectionViewModel RegionEditor { get; }

    public CompositionRegionDraftViewModel?
        SelectedRegion
    {
        get => RegionEditor.SelectedRegion;
        set => RegionEditor.SelectedRegion = value;
    }

    public bool HasSelectedRegion =>
        RegionEditor.HasSelectedRegion;

    public bool CanRemoveSelectedRegion =>
        RegionEditor.CanRemoveSelectedRegion;

    public TimeSpan RequestedTimestamp =>
        Preview.RequestedTimestamp;

    public double RequestedTimestampSeconds
    {
        get => Preview.RequestedTimestampSeconds;
        set => Preview.RequestedTimestampSeconds = value;
    }

    public double MaximumPreviewTimestampSeconds =>
        Preview.MaximumTimestampSeconds;

    public string RequestedTimestampText =>
        Preview.RequestedTimestampText;

    public VideoPreviewFrame? PreviewFrame => Preview.Frame;

    public bool HasPreviewFrame =>
        Preview.HasFrame;

    public bool IsPreviewCurrent =>
        Preview.IsCurrent;

    public int PreviewWidth => Preview.Width;

    public int PreviewHeight => Preview.Height;

    public bool IsLoadingPreview => Preview.IsLoading;

    public string? PreviewError => Preview.Error;

    public bool HasPreviewError =>
        Preview.HasError;

    public bool CanRetryPreview =>
        Preview.CanRetry;

    public string PreviewStatusText
    {
        get
        {
            if (IsLoadingPreview)
            {
                return "Loading preview";
            }

            if (HasPreviewError)
            {
                return "Preview failed";
            }

            if (IsConfirmed)
            {
                return "Confirmed";
            }

            return "Needs review";
        }
    }

    public string? ActualDecodedTimestampText =>
        Preview.ActualDecodedTimestampText;

    public string? ValidationError
    {
        get => _validationError;

        private set
        {
            if (string.Equals(
                    _validationError,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            _validationError = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError =>
        !string.IsNullOrWhiteSpace(
            ValidationError);

    public bool IsConfirmed
    {
        get => _isConfirmed;

        private set
        {
            if (_isConfirmed == value)
            {
                return;
            }

            _isConfirmed = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewStatusText));
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public bool IsDirty
    {
        get => _isDirty;

        private set
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            OnPropertyChanged();
        }
    }

    public bool CanConfirm =>
        !IsConfirmed &&
        (Preview.WasSuccessfullyLoaded ||
         _canReuseConfirmedWithoutPreview) &&
        HasGameplayRegion &&
        Regions.Count > 0;

    public bool HasGameplayRegion =>
        RegionEditor.HasGameplayRegion;

    public PreparedSourceCompositionPlan?
        ConfirmedPlan =>
        _confirmedPlan;

    public Task LoadPreviewAsync()
    {
        ThrowIfDisposed();
        return Preview.LoadAsync();
    }

    public CompositionRegionDraftViewModel AddRegion(
        CompositionRegionRole role)
    {
        ThrowIfDisposed();
        return RegionEditor.AddRegion(role);
    }

    public void UseFullFrameGameplay()
    {
        ThrowIfDisposed();
        RegionEditor.UseFullFrameGameplay();
    }

    public void RemoveSelectedRegion()
    {
        ThrowIfDisposed();
        RegionEditor.RemoveSelectedRegion();
    }

    public bool TryConfirm(
        DateTimeOffset createdAtUtc)
    {
        ThrowIfDisposed();

        ValidationError = null;

        if (!Preview.WasSuccessfullyLoaded &&
            !_canReuseConfirmedWithoutPreview)
        {
            ValidationError =
                "Load a representative preview frame before confirming this source.";

            return false;
        }

        try
        {
            PreparedSourceCompositionPlan sourcePlan =
                BuildSourcePlan(
                    createdAtUtc);

            _confirmedPlan = sourcePlan;
            _canReuseConfirmedWithoutPreview = true;
            IsConfirmed = true;
            IsDirty = false;

            OnPropertyChanged(nameof(ConfirmedPlan));

            return true;
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                InvalidOperationException)
        {
            ValidationError =
                exception.Message;

            return false;
        }
    }

    internal void ApplyCopiedLayout(
        IEnumerable<CompositionRegionDraftViewModel> sourceRegions,
        DateTimeOffset createdAtUtc)
    {
        ThrowIfDisposed();
        RegionEditor.ApplyCopied(sourceRegions);

        _confirmedPlan =
            BuildSourcePlan(
                createdAtUtc);

        _canReuseConfirmedWithoutPreview = true;
        IsConfirmed = true;
        IsDirty = false;
        ValidationError = null;

        OnPropertyChanged(nameof(ConfirmedPlan));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        RegionEditor.PropertyChanged -= OnRegionEditorPropertyChanged;
        Preview.PropertyChanged -= OnPreviewPropertyChanged;
        Preview.Dispose();
    }

    internal void ReportUnexpectedPreviewFailure(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_isDisposed)
        {
            return;
        }

        Preview.ReportUnexpectedFailure(exception);
    }

    private PreparedSourceCompositionPlan BuildSourcePlan(
        DateTimeOffset createdAtUtc)
    {
        CompositionPlan plan =
            ManualCompositionPlanFactory
                .CreateUserConfirmedSingleInterval(
                    FullPath,
                    Duration,
                    RegionEditor.CreateRegions(),
                    createdAtUtc);

        return new PreparedSourceCompositionPlan(
            PreparedSource,
            plan);
    }

    private void OnPreviewPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        string? projectionName = eventArgs.PropertyName switch
        {
            nameof(CompositionPreviewViewModel.RequestedTimestamp) =>
                nameof(RequestedTimestamp),
            nameof(CompositionPreviewViewModel.RequestedTimestampSeconds) =>
                nameof(RequestedTimestampSeconds),
            nameof(CompositionPreviewViewModel.MaximumTimestampSeconds) =>
                nameof(MaximumPreviewTimestampSeconds),
            nameof(CompositionPreviewViewModel.RequestedTimestampText) =>
                nameof(RequestedTimestampText),
            nameof(CompositionPreviewViewModel.Frame) =>
                nameof(PreviewFrame),
            nameof(CompositionPreviewViewModel.HasFrame) =>
                nameof(HasPreviewFrame),
            nameof(CompositionPreviewViewModel.IsCurrent) =>
                nameof(IsPreviewCurrent),
            nameof(CompositionPreviewViewModel.Width) =>
                nameof(PreviewWidth),
            nameof(CompositionPreviewViewModel.Height) =>
                nameof(PreviewHeight),
            nameof(CompositionPreviewViewModel.IsLoading) =>
                nameof(IsLoadingPreview),
            nameof(CompositionPreviewViewModel.Error) =>
                nameof(PreviewError),
            nameof(CompositionPreviewViewModel.HasError) =>
                nameof(HasPreviewError),
            nameof(CompositionPreviewViewModel.CanRetry) =>
                nameof(CanRetryPreview),
            nameof(CompositionPreviewViewModel.ActualDecodedTimestampText) =>
                nameof(ActualDecodedTimestampText),
            _ => null,
        };

        if (projectionName is not null)
        {
            OnPropertyChanged(projectionName);
        }

        if (eventArgs.PropertyName is
            nameof(CompositionPreviewViewModel.IsLoading) or
            nameof(CompositionPreviewViewModel.Error))
        {
            OnPropertyChanged(nameof(PreviewStatusText));
        }

        if (eventArgs.PropertyName ==
            nameof(CompositionPreviewViewModel.Frame))
        {
            RegionEditor.SetPreviewDimensions(
                Preview.Width,
                Preview.Height);
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    private void OnRegionEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not null)
        {
            OnPropertyChanged(eventArgs.PropertyName);
        }

        if (eventArgs.PropertyName is
            nameof(CompositionRegionCollectionViewModel.Regions) or
            nameof(CompositionRegionCollectionViewModel.HasGameplayRegion))
        {
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    private void MarkUnconfirmed()
    {
        _confirmedPlan = null;
        IsConfirmed = false;
        IsDirty = true;
        ValidationError = null;

        OnPropertyChanged(nameof(ConfirmedPlan));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(PreviewStatusText));
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(CompositionReviewSourceViewModel));
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
