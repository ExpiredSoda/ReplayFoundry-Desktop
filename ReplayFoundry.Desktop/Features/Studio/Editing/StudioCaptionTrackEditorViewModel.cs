using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioCaptionSegmentDraft : INotifyPropertyChanged
{
    private string _text;
    private double _startSeconds;
    private double _endSeconds;

    public StudioCaptionSegmentDraft(
        string id,
        string text,
        double startSeconds,
        double endSeconds)
    {
        Id = id;
        _text = text;
        _startSeconds = startSeconds;
        _endSeconds = endSeconds;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }
    public double StartSeconds
    {
        get => _startSeconds;
        set => Set(ref _startSeconds, value);
    }
    public double EndSeconds
    {
        get => _endSeconds;
        set => Set(ref _endSeconds, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal sealed record StudioCaptionTrackDraftSnapshot(
    IReadOnlyList<StudioCaptionSegmentEdit> Segments);

public sealed class StudioCaptionTrackEditorViewModel : INotifyPropertyChanged
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly DelegateCommand _saveCommand;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private string? _status;
    private bool _isHostBusy;

    public StudioCaptionTrackEditorViewModel(
        IGenerationOutputEditor? outputEditor)
    {
        _outputEditor = outputEditor;
        _saveCommand = new DelegateCommand(Save, CanSave);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<StudioCaptionSegmentDraft> Segments { get; } = [];
    public ICommand SaveCommand => _saveCommand;
    public bool HasSegments => Segments.Count > 0;
    public bool HasUnsavedChanges
    {
        get
        {
            if (_asset is null)
            {
                return false;
            }

            StudioCaptionSegmentEdit[] saved =
                StudioCaptionTrackEditing.CreateDrafts(_asset).ToArray();
            return saved.Length != Segments.Count ||
                   saved.Where((segment, index) =>
                           !Matches(segment, Segments[index]))
                       .Any();
        }
    }
    public string Status => _status ??
        "Edit words or segment timing. Changed segments intentionally fall back to segment-timed caption effects instead of inventing new word timestamps.";

    public void Bind(
        GenerationOutputProject? project,
        GenerationOutputAsset? asset)
    {
        _project = project;
        _asset = asset;
        _status = null;
        LoadSegments(asset is null
            ? []
            : StudioCaptionTrackEditing.CreateDrafts(asset));
        Notify();
    }

    public void SetHostBusy(bool value)
    {
        _isHostBusy = value;
        _saveCommand.RaiseCanExecuteChanged();
    }

    internal StudioCaptionTrackDraftSnapshot? CapturePendingDraft() =>
        HasUnsavedChanges
            ? new StudioCaptionTrackDraftSnapshot(
                Array.AsReadOnly(Segments
                    .Select(static draft => new StudioCaptionSegmentEdit(
                        draft.Id,
                        draft.Text,
                        draft.StartSeconds,
                        draft.EndSeconds))
                    .ToArray()))
            : null;

    internal void RestorePendingDraft(
        StudioCaptionTrackDraftSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (_asset?.Captions is null ||
            _project?.IsFinalized != false ||
            draft.Segments.Count != _asset.Captions.Segments.Count)
        {
            return;
        }

        StudioCaptionSegmentEdit[] current =
            StudioCaptionTrackEditing.CreateDrafts(_asset).ToArray();
        if (current.Where((segment, index) =>
                !segment.Id.Equals(
                    draft.Segments[index].Id,
                    StringComparison.Ordinal)).Any())
        {
            return;
        }

        LoadSegments(draft.Segments);
        Notify();
    }

    private bool CanSave() =>
        _project is { IsFinalized: false } &&
        _asset?.Captions is not null &&
        _outputEditor is not null &&
        !_isHostBusy &&
        Segments.Count > 0;

    private void Save()
    {
        if (!CanSave() ||
            _project is not { } project ||
            _asset is not { Captions: { } track } asset ||
            _outputEditor is null)
        {
            return;
        }

        try
        {
            for (int index = 1; index < Segments.Count; index++)
            {
                if (Segments[index].StartSeconds <
                    Segments[index - 1].EndSeconds)
                {
                    throw new ArgumentException(
                        $"Caption segment {index + 1} overlaps the preceding segment.");
                }
            }
            StudioCaptionSegmentEdit[] segments = Segments
                .Select(static draft => new StudioCaptionSegmentEdit(
                    draft.Id,
                    draft.Text,
                    draft.StartSeconds,
                    draft.EndSeconds))
                .ToArray();
            GenerationOutputAsset replacement =
                StudioCaptionTrackEditing.Apply(
                    _outputEditor,
                    project,
                    asset,
                    segments);
            _asset = replacement;
            _status =
                "Caption text and timing saved. You can keep editing or return to the preview.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            _status = "Caption edits were not saved: " + exception.Message;
        }
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSegments)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnsavedChanges)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        _saveCommand.RaiseCanExecuteChanged();
    }

    private void LoadSegments(
        IEnumerable<StudioCaptionSegmentEdit> segments)
    {
        foreach (StudioCaptionSegmentDraft segment in Segments)
        {
            segment.PropertyChanged -= Segment_PropertyChanged;
        }
        Segments.Clear();
        foreach (StudioCaptionSegmentEdit segment in segments)
        {
            var draft = new StudioCaptionSegmentDraft(
                segment.Id,
                segment.Text,
                segment.StartSeconds,
                segment.EndSeconds);
            draft.PropertyChanged += Segment_PropertyChanged;
            Segments.Add(draft);
        }
    }

    private void Segment_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(HasUnsavedChanges)));

    private static bool Matches(
        StudioCaptionSegmentEdit saved,
        StudioCaptionSegmentDraft draft) =>
        saved.Id.Equals(draft.Id, StringComparison.Ordinal) &&
        saved.Text.Equals(draft.Text, StringComparison.Ordinal) &&
        saved.StartSeconds.Equals(draft.StartSeconds) &&
        saved.EndSeconds.Equals(draft.EndSeconds);
}
