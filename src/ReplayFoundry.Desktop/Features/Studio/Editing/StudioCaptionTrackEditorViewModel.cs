using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
// Shared file-format value; parsing and dialogs belong to the caption file service.
using SubtitleSidecarFormat = ReplayFoundry.Desktop.Media.Subtitles.SubtitleSidecarFormat;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public abstract class StudioCaptionDraft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Changed(name);
    }
    protected void Changed(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class StudioCaptionWordDraft : StudioCaptionDraft
{
    private string _text; private double _start, _end; private bool _emphasis;
    private double? _acousticScore;
    public StudioCaptionWordDraft(StudioCaptionWordEdit edit, double minimumSeconds = 0, double maximumSeconds = double.PositiveInfinity)
    {
        _text = edit.Text; _start = edit.StartSeconds; _end = edit.EndSeconds;
        _emphasis = edit.IsEmphasized; _acousticScore = edit.AcousticScore;
        MinimumSeconds = minimumSeconds; MaximumSeconds = maximumSeconds;
    }
    public string Text { get => _text; set { if (_text == value) return; _text = value; _acousticScore = null; Changed(nameof(Text)); Changed(nameof(AcousticReview)); } }
    public double StartSeconds
    {
        get => _start;
        set
        {
            if (_start.Equals(value)) return;
            _start = value; _acousticScore = null;
            Changed(nameof(StartSeconds)); Changed(nameof(StartTime)); TimingChanged();
        }
    }
    public double EndSeconds
    {
        get => _end;
        set
        {
            if (_end.Equals(value)) return;
            _end = value; _acousticScore = null;
            Changed(nameof(EndSeconds)); Changed(nameof(EndTime)); TimingChanged();
        }
    }
    public string StartTime
    {
        get => double.IsFinite(StartSeconds) ? StartSeconds.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) : "";
        set { StartSeconds = double.TryParse(value, out double number) ? number : double.NaN; }
    }
    public string EndTime
    {
        get => double.IsFinite(EndSeconds) ? EndSeconds.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) : "";
        set { EndSeconds = double.TryParse(value, out double number) ? number : double.NaN; }
    }
    public bool IsEmphasized { get => _emphasis; set => Set(ref _emphasis, value); }
    public double MinimumSeconds { get; private set; }
    public double MaximumSeconds { get; private set; }
    public bool CanAdjustTiming => double.IsFinite(StartSeconds) && double.IsFinite(EndSeconds) && EndSeconds > StartSeconds &&
        double.IsFinite(MinimumSeconds) && double.IsFinite(MaximumSeconds) && MaximumSeconds > MinimumSeconds;
    public string? AcousticReview => _acousticScore is { } score
        ? $"{(score < .15 ? "Weak acoustic match" : "Acoustic fit")}: {score:0.000}. Review by listening; this is not a correctness probability." : null;
    public event EventHandler? TimingEditStarted;
    public event EventHandler? TimingEditCompleted;
    public void BeginTimingEdit() => TimingEditStarted?.Invoke(this, EventArgs.Empty);
    public void CompleteTimingEdit() => TimingEditCompleted?.Invoke(this, EventArgs.Empty);
    internal void RestoreTiming(StudioCaptionWordEdit edit)
    {
        _start = edit.StartSeconds; _end = edit.EndSeconds; _acousticScore = edit.AcousticScore;
        Changed(nameof(StartSeconds)); Changed(nameof(EndSeconds)); Changed(nameof(StartTime)); Changed(nameof(EndTime)); TimingChanged();
    }
    public void UpdateTimingBounds(double minimum, double maximum)
    { MinimumSeconds = minimum; MaximumSeconds = maximum; Changed(nameof(MinimumSeconds)); Changed(nameof(MaximumSeconds)); Changed(nameof(CanAdjustTiming)); }
    public void MoveStartBy(double seconds)
    {
        if (!CanAdjustTiming || !double.IsFinite(seconds)) return;
        double upper = Math.Min(MaximumSeconds, EndSeconds) - .001;
        if (upper >= MinimumSeconds) StartSeconds = Math.Clamp(StartSeconds + seconds, MinimumSeconds, upper);
    }
    public void MoveEndBy(double seconds)
    {
        if (!CanAdjustTiming || !double.IsFinite(seconds)) return;
        double lower = Math.Max(MinimumSeconds, StartSeconds) + .001;
        if (lower <= MaximumSeconds) EndSeconds = Math.Clamp(EndSeconds + seconds, lower, MaximumSeconds);
    }
    private void TimingChanged() { Changed(nameof(CanAdjustTiming)); Changed(nameof(AcousticReview)); }
    public StudioCaptionWordEdit Snapshot() => new(Text, StartSeconds, EndSeconds, IsEmphasized, _acousticScore);
}
public sealed class StudioCaptionSegmentDraft : StudioCaptionDraft
{
    private string _text; private double _start, _end; private string? _speaker, _secondaryText;
    private readonly double? _cutDurationSeconds;
    public StudioCaptionSegmentDraft(string id, string text, double startSeconds, double endSeconds,
        IReadOnlyList<StudioCaptionWordEdit>? words = null, string? speaker = null, string? secondaryText = null,
        double? cutDurationSeconds = null, string? alignmentProvenance = null)
    {
        if (cutDurationSeconds is { } duration && (!double.IsFinite(duration) || duration <= 0))
            throw new ArgumentOutOfRangeException(nameof(cutDurationSeconds));
        _cutDurationSeconds = cutDurationSeconds;
        AlignmentProvenance = alignmentProvenance;
        Id = id; _text = text; _start = startSeconds; _end = endSeconds; _speaker = speaker; _secondaryText = secondaryText;
        foreach (var word in words ?? []) Words.Add(new StudioCaptionWordDraft(word, startSeconds, endSeconds));
        foreach (var word in Words) word.PropertyChanged += (_, _) => { Changed(nameof(Words)); Changed(nameof(AlignmentSummary)); };
        foreach (var word in Words)
        {
            word.TimingEditStarted += (_, _) => TimingEditStarted?.Invoke(this, EventArgs.Empty);
            word.TimingEditCompleted += (_, _) => TimingEditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
    public string Id { get; }
    public string Text { get => _text; set { Set(ref _text, value); UpdateReadability(); } }
    public double StartSeconds { get => _start; set { Set(ref _start, value); UpdateReadability(); UpdateWordBounds(); } }
    public double EndSeconds { get => _end; set { Set(ref _end, value); UpdateReadability(); UpdateWordBounds(); } }
    public string? Speaker { get => _speaker; set => Set(ref _speaker, value); }
    public string? SecondaryText { get => _secondaryText; set => Set(ref _secondaryText, value); }
    public ObservableCollection<StudioCaptionWordDraft> Words { get; } = [];
    public string? AlignmentProvenance { get; }
    public bool HasAlignmentProvenance => AlignmentProvenance is not null;
    public string? AlignmentSummary => StudioCaptionAlignmentProvenance.Read(AlignmentProvenance) is { } alignment
        ? "Retained alignment record: " + alignment.Summary + (alignment.TextSha256 != StudioCaptionAlignmentProvenance.TextIdentity(Text) ||
            Words.Any(word => word.Snapshot().AcousticScore is null)
            ? " Text or timing was edited after the original proposal; source details retain the original result." : "") : null;
    public string? AlignmentDetails => AlignmentProvenance is null ? null : string.Join("\n\n",
        StudioCaptionAlignmentProvenance.ReadAll(AlignmentProvenance).Select(static value => value.Details));
    public event EventHandler? TimingEditStarted;
    public event EventHandler? TimingEditCompleted;
    private void UpdateWordBounds() { foreach (var word in Words) word.UpdateTimingBounds(StartSeconds, EndSeconds); }
    public string ReadabilitySummary => StudioCaptionReadability.Assess(Text, EndSeconds - StartSeconds).Summary;
    public string? ReadabilityWarning => StudioCaptionReadability.Assess(Text, EndSeconds - StartSeconds).Warning;
    private void UpdateReadability() { Changed(nameof(ReadabilitySummary)); Changed(nameof(ReadabilityWarning)); Changed(nameof(TimingHint)); Changed(nameof(AlignmentSummary)); }
    public string TimingHint
    {
        get
        {
            if (EndSeconds <= 0) return "Before this cut; retained for boundary expansion and omitted from this export.";
            if (_cutDurationSeconds is { } duration && StartSeconds >= duration)
                return "After this cut; retained for boundary expansion and omitted from this export.";
            bool before = StartSeconds < 0, after = _cutDurationSeconds is { } cutEnd && EndSeconds > cutEnd;
            if (before && after) return "Crosses both cut boundaries; the retained phrase is projected to the current export.";
            if (before) return "Begins before this cut; timing is retained for later trims.";
            if (after) return "Ends after this cut; timing is retained for later trims.";
            return Words.Count > 0 ? "Word times are seconds from this cut. Correct the matching word text to keep animation." :
                "Phrase timing only. Regenerate captions to recover measured word timings.";
        }
    }
    public StudioCaptionSegmentEdit Snapshot() => new(Id, Text, StartSeconds, EndSeconds, Words.Select(w => w.Snapshot()).ToArray(), Speaker, SecondaryText, AlignmentProvenance);
}

internal sealed record StudioCaptionTrackDraftSnapshot(IReadOnlyList<StudioCaptionSegmentEdit> Segments);

public sealed class StudioCaptionTrackEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly DelegateCommand _saveCommand, _addCommand, _undoCommand, _redoCommand, _importCommand, _exportCommand, _saveVocabularyCommand;
    private readonly StudioCaptionFileService _captionFiles = new();
    public string VocabularyText { get; set; } = "";
    public bool RememberCorrections { get; set; }
    public ICommand SaveVocabularyCommand => _saveVocabularyCommand;
    private readonly DelegateCommand<StudioCaptionSegmentDraft> _deleteCommand, _splitCommand, _mergeCommand, _wordRowsCommand;
    private readonly AsyncDelegateCommand _regenerateCommand, _translateSecondaryCommand;
    private readonly Stack<StudioCaptionSegmentEdit[]> _undo = new(), _redo = new();
    private StudioCaptionSegmentEdit[] _last = [];
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private IGenerationCaptionPreparationService? _preparation;
    private CancellationTokenSource? _regeneration;
    private string? _status;
    private bool _isHostBusy, _loading;

    public StudioCaptionTrackEditorViewModel(IGenerationOutputEditor? outputEditor)
    {
        _outputEditor = outputEditor;
        _saveCommand = new(Save, CanEdit);
        _addCommand = new(Add, CanEdit);
        _undoCommand = new(() => RestoreHistory(_undo, _redo), () => CanEdit() && _undo.Count > 0);
        _redoCommand = new(() => RestoreHistory(_redo, _undo), () => CanEdit() && _redo.Count > 0);
        _deleteCommand = new(s => Mutate(list => list.RemoveAll(x => x.Id == s.Id)), _ => CanEdit());
        _splitCommand = new(Split, s => CanEdit() && s is not null && s.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1);
        _mergeCommand = new(Merge, s => CanEdit() && s is not null && Segments.IndexOf(s) < Segments.Count - 1);
        _wordRowsCommand = new(RebuildWordRows, _ => CanEdit());
        _importCommand = new(Import, CanEdit);
        _exportCommand = new(Export, () => _asset?.Captions is not null && !_isHostBusy && !HasUnsavedChanges);
        _regenerateCommand = new(RegenerateAsync, () => CanEdit() && HasSelectedAudioStream && _preparation is not null && !HasUnsavedChanges && LanguageUnavailableReason is null);
        _translateSecondaryCommand = new(TranslateSecondaryAsync, () => CanEdit() && HasSelectedAudioStream && _preparation is not null &&
            _asset?.Captions is not null && !HasUnsavedChanges && TranslationUnavailableReason is null);
        _saveVocabularyCommand = new(SaveVocabulary, () => !_isHostBusy);
        _alignCommand = new(AlignCorrectedTextAsync, CanAlign);
        _cancelAlignmentCommand = new(CancelAlignment, () => _alignmentCancellation is not null);
        try { VocabularyText = string.Join(Environment.NewLine, _captionFiles.LoadVocabulary()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { _status = "Saved vocabulary could not be loaded: " + e.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<StudioCaptionSegmentDraft> Segments { get; } = [];
    public ICommand SaveCommand => _saveCommand;
    public ICommand AddCommand => _addCommand;
    public ICommand DeleteCommand => _deleteCommand;
    public ICommand SplitCommand => _splitCommand;
    public ICommand MergeCommand => _mergeCommand;
    public ICommand RebuildWordRowsCommand => _wordRowsCommand;
    public ICommand UndoCommand => _undoCommand;
    public ICommand RedoCommand => _redoCommand;
    public ICommand ImportCommand => _importCommand;
    public ICommand ExportCommand => _exportCommand;
    public ICommand RegenerateCommand => _regenerateCommand;
    public ICommand TranslateSecondaryCommand => _translateSecondaryCommand;
    public IReadOnlyList<StudioCaptionAudioStreamChoice> AudioStreams { get; private set; } = [];
    private int _selectedAudioStreamIndex;
    public int SelectedAudioStreamIndex
    {
        get => _selectedAudioStreamIndex;
        set { if (_selectedAudioStreamIndex == value) return; _selectedAudioStreamIndex = value; InvalidateAlignment(); AudioAudition.Bind(_asset, value); Notify(); }
    }
    public StudioCaptionAudioAuditionViewModel AudioAudition { get; } = new();
    private StudioCaptionLanguageModel _languageModel = new(null);
    private IReadOnlyList<SelectionOption<GenerationCaptionLanguagePolicy>>? _languageOptions;
    public IReadOnlyList<SelectionOption<GenerationCaptionLanguagePolicy>> LanguageOptions
    {
        get
        {
            if (_languageOptions is not null) return _languageOptions;
            var choices = _languageModel.GetChoices(SelectedLanguage);
            // Corrected-text alignment uses its own English acoustic model. Keep
            // English selectable even when Whisper cannot transcribe it; the
            // separate regeneration blocking reason remains authoritative.
            return _languageOptions = _alignmentService is null || choices.Any(choice => choice.Value == GenerationCaptionLanguagePolicy.English)
                ? choices
                : choices.Concat(_languageModel.GetChoices(GenerationCaptionLanguagePolicy.English)
                    .Where(choice => choice.Value == GenerationCaptionLanguagePolicy.English)).ToArray();
        }
    }
    public string? LanguageUnavailableReason => _languageModel.GetUnavailableReason(SelectedLanguage);
    public string? TranslationUnavailableReason => _languageModel.GetUnavailableReason(GenerationCaptionLanguagePolicy.EnglishTranslation);
    public string? LanguageModelDescription => _languageModel.Description;
    private GenerationCaptionLanguagePolicy _selectedLanguage;
    public GenerationCaptionLanguagePolicy SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            if (_languageOptions?.Any(option => option.Value == value) != true) _languageOptions = null;
            InvalidateAlignment(); Notify();
        }
    }
    public bool HasSegments => Segments.Count > 0;
    public bool HasUnsavedChanges => _asset is not null && !Same(StudioCaptionTrackEditing.CreateDrafts(_asset), Snapshot());
    public string Status
    {
        get
        {
            string status = _status ?? "Times begin at the current cut. Retained rows outside this cut remain editable for boundary expansion. " +
                "Save corrections before regenerating; regeneration replaces the saved transcript.";
            if (_asset?.Captions is { } track)
                status += " " + string.Join(" ", StudioCaptionCutProjection.Project(track, _asset.SourceStart, _asset.SourceEnd).Warnings);
            return status.TrimEnd();
        }
    }
    public void ConfigurePreparation(IGenerationCaptionPreparationService? preparation) { _preparation = preparation; Notify(); }
    public void ConfigureLanguageCapabilities(StudioCaptionLanguageModel? model)
    {
        _regeneration?.Cancel(); _languageModel = model ?? new(null); _languageOptions = null;
        if (_asset is null) _selectedLanguage = _languageModel.DefaultLanguage;
        Notify();
    }

    public void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        bool sameClip = asset is not null && _asset is { } priorAsset && priorAsset.Id == asset.Id && _project?.Id == project?.Id &&
            string.Equals(priorAsset.SourceFullPath, asset.SourceFullPath, StringComparison.OrdinalIgnoreCase);
        _regeneration?.Cancel();
        InvalidateAlignment(); _alignmentCancellation = null;
        _project = project; _asset = asset; _status = null;
        _undo.Clear(); _redo.Clear();
        AudioStreams = asset?.SourceMedia.AudioStreams.Select(static stream => new StudioCaptionAudioStreamChoice(stream.Index,
            $"Track {stream.Index}" + (string.IsNullOrWhiteSpace(stream.Title) ? "" : $" · {stream.Title}"))).ToArray() ?? [];
        if (!sameClip || !AudioStreams.Any(stream => stream.Index == SelectedAudioStreamIndex))
            SelectedAudioStreamIndex = asset?.Captions?.SourceSelection.AbsoluteAudioStreamIndex ?? asset?.SourceMedia.AudioStreams.FirstOrDefault()?.Index ?? 0;
        AudioAudition.Bind(asset, SelectedAudioStreamIndex);
        if (!sameClip) SelectedLanguage = asset?.Captions?.SourceSelection.LanguagePolicy ?? _languageModel.DefaultLanguage;
        LoadSegments(asset is null ? [] : StudioCaptionTrackEditing.CreateDrafts(asset));
        Notify();
    }
    public void SetHostBusy(bool value) { _isHostBusy = value; if (value) { _regeneration?.Cancel(); InvalidateAlignment(); } AudioAudition.SetHostBusy(value); Notify(); }
    public void Dispose() { _isHostBusy = true; _regeneration?.Cancel(); InvalidateAlignment(); AudioAudition.Dispose(); }
    internal StudioCaptionTrackDraftSnapshot? CapturePendingDraft() => HasUnsavedChanges ? new(Snapshot()) : null;
    internal void RestorePendingDraft(StudioCaptionTrackDraftSnapshot draft)
    {
        if (_asset is null || _project?.IsFinalized != false) return;
        LoadSegments(draft.Segments); Notify();
    }
    private bool CanEdit() => _project is { IsFinalized: false } && _asset is not null &&
        _outputEditor is not null && !_isHostBusy && _regeneration is null && _alignmentCancellation is null;
    private bool HasSelectedAudioStream => _asset?.SourceMedia.AudioStreams.Any(stream => stream.Index == SelectedAudioStreamIndex) == true;
    private StudioCaptionSegmentEdit[] Snapshot() => Segments.Select(s => s.Snapshot()).ToArray();
    private void Save()
    {
        if (!CanEdit()) return;
        try
        {
            var before = StudioCaptionTrackEditing.CreateDrafts(_asset!);
            var pending = Snapshot();
            _asset = StudioCaptionTrackEditing.Apply(_outputEditor!, _project!, _asset!, Snapshot());
            LoadSegments(StudioCaptionTrackEditing.CreateDrafts(_asset));
            _status = "Caption words and timing saved. Corrected phrases without matching word timing use phrase animation.";
            if (RememberCorrections)
            {
                try
                {
                    var prior = before.SelectMany(s => Tokens(s.Text)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    string[] added = pending.SelectMany(s => Tokens(s.Text)).Where(t => !prior.Contains(t)).ToArray();
                    _captionFiles.SaveVocabulary(_captionFiles.LoadVocabulary().Concat(added));
                    VocabularyText = string.Join(Environment.NewLine, _captionFiles.LoadVocabulary());
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VocabularyText)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                { _status += " Vocabulary was not updated: " + e.Message; }
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { _status = "Not saved: " + e.Message; }
        Notify();
    }
    private void Add()
    {
        double start = 0;
        foreach (var segment in Segments)
        {
            if (segment.StartSeconds - start >= 0.5) break;
            start = Math.Max(start, segment.EndSeconds);
        }
        double end = Math.Min(_asset!.Duration.TotalSeconds, start + 1);
        var next = Segments.FirstOrDefault(s => s.StartSeconds > start);
        if (next is not null) end = Math.Min(end, next.StartSeconds);
        if (end <= start) { _status = "No empty interval remains. Shorten or split a phrase first."; Notify(); return; }
        Mutate(list => { list.Add(new(NewId(), "New caption", start, end)); list.Sort((a,b) => a.StartSeconds.CompareTo(b.StartSeconds)); });
    }
    private void Split(StudioCaptionSegmentDraft segment)
    {
        StudioCaptionSegmentEdit source = segment.Snapshot();
        string[] text = source.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int half = text.Length / 2;
        double boundary = source.StartSeconds + (source.EndSeconds - source.StartSeconds) / 2;
        IReadOnlyList<StudioCaptionWordEdit> leftWords = [], rightWords = [];
        if (source.Words is { Count: > 1 } words && StudioCaptionTrackEditing.Lexical(source.Text) == StudioCaptionTrackEditing.Lexical(string.Concat(words.Select(w => w.Text))))
        {
            int wordHalf = words.Count / 2;
            boundary = words[wordHalf].StartSeconds;
            leftWords = words.Take(wordHalf).ToArray(); rightWords = words.Skip(wordHalf).ToArray();
            // Split on measured word timing, preserving every word. Punctuation in the
            // phrase is retained using the next word's lexical location below.
            int split = FindWordSplit(source.Text, leftWords);
            Mutate(list =>
            {
                int i = list.FindIndex(s => s.Id == source.Id);
                list[i] = source with { Text = source.Text[..split].Trim(), EndSeconds = boundary, Words = leftWords };
                list.Insert(i + 1, new(NewId(), source.Text[split..].Trim(), boundary, source.EndSeconds,
                    rightWords, source.Speaker, source.SecondaryText, source.AlignmentProvenance));
            });
        }
        else Mutate(list =>
        {
            int i = list.FindIndex(s => s.Id == source.Id);
            list[i] = source with { Text = string.Join(' ', text.Take(half)), EndSeconds = boundary, Words = [] };
            list.Insert(i + 1, new(NewId(), string.Join(' ', text.Skip(half)), boundary, source.EndSeconds,
                [], source.Speaker, source.SecondaryText, source.AlignmentProvenance));
        });
        _status = "Phrase split. Measured words retain their times; phrase-only splits can be adjusted with Show times."; Notify();
    }
    private static int FindWordSplit(string text, IReadOnlyList<StudioCaptionWordEdit> words)
    {
        int target = StudioCaptionTrackEditing.Lexical(string.Concat(words.Select(w => w.Text))).Length, count = 0;
        for (int i = 0; i < text.Length; i++) if (char.IsLetterOrDigit(text[i]) && ++count > target) return i;
        return text.Length / 2;
    }
    private void Merge(StudioCaptionSegmentDraft segment) => Mutate(list =>
    {
        int i = list.FindIndex(s => s.Id == segment.Id); if (i < 0 || i + 1 >= list.Count) return;
        var a = list[i]; var b = list[i + 1];
        list[i] = a with { Text = a.Text + " " + b.Text, EndSeconds = b.EndSeconds,
            AlignmentProvenance = StudioCaptionAlignmentProvenance.Merge(a.AlignmentProvenance, b.AlignmentProvenance),
            Words = a.Words is { Count: > 0 } && b.Words is { Count: > 0 } ? a.Words.Concat(b.Words).ToArray() : [],
            SecondaryText = string.Join(" ", new[] { a.SecondaryText, b.SecondaryText }.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct()) };
        list.RemoveAt(i + 1);
    });
    private void RebuildWordRows(StudioCaptionSegmentDraft segment)
    {
        Mutate(list =>
        {
            int index = list.FindIndex(s => s.Id == segment.Id);
            var source = list[index];
            string[] tokens = source.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var words = tokens.Select((text, i) => source.Words is { } measured && i < measured.Count &&
                StudioCaptionTrackEditing.Lexical(measured[i].Text) == StudioCaptionTrackEditing.Lexical(text)
                    ? measured[i] with { Text = text }
                    : new StudioCaptionWordEdit(text, double.NaN, double.NaN)).ToArray();
            list[index] = source with { Words = words };
        });
        _status = "Word rows rebuilt. Existing matching times were preserved. Enter a start and end for every blank word before saving; no timing is guessed.";
        Notify();
    }
    private void Mutate(Action<List<StudioCaptionSegmentEdit>> action)
    {
        var before = Snapshot(); var values = before.ToList(); action(values);
        _undo.Push(before); _redo.Clear(); LoadSegments(values); Notify();
    }
    private void RestoreHistory(Stack<StudioCaptionSegmentEdit[]> source, Stack<StudioCaptionSegmentEdit[]> destination)
    { destination.Push(Snapshot()); LoadSegments(source.Pop()); Notify(); }
    private void LoadSegments(IEnumerable<StudioCaptionSegmentEdit> segments)
    {
        _timingEditBefore = null;
        _loading = true;
        foreach (var segment in Segments)
        { segment.PropertyChanged -= SegmentChanged; segment.TimingEditStarted -= BeginTimingEdit; segment.TimingEditCompleted -= CompleteTimingEdit; }
        Segments.Clear();
        foreach (var edit in segments)
        {
            var draft = new StudioCaptionSegmentDraft(edit.Id, edit.Text, edit.StartSeconds, edit.EndSeconds, edit.Words, edit.Speaker, edit.SecondaryText,
                _asset?.Duration.TotalSeconds, edit.AlignmentProvenance);
            draft.PropertyChanged += SegmentChanged; Segments.Add(draft);
            draft.TimingEditStarted += BeginTimingEdit; draft.TimingEditCompleted += CompleteTimingEdit;
        }
        _last = Snapshot(); _loading = false;
    }
    private void SegmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        var current = Snapshot(); if (Same(_last, current)) return;
        InvalidateAlignment();
        if (_timingEditBefore is null) { _undo.Push(_last); _redo.Clear(); }
        _last = current; Notify();
    }
    private void Import()
    {
        string? path = StudioCaptionFileService.ChooseImport();
        if (path is null) return;
        try
        {
            var import = StudioCaptionFileService.ReadImport(path);
            ImportText(import.Text, import.Format);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException) { _status = "Import failed: " + e.Message; Notify(); }
    }
    private static IEnumerable<string> Tokens(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"[\p{L}\p{N}][\p{L}\p{N}'-]{2,79}")
        .Select(m => m.Value);
    private void SaveVocabulary()
    {
        try
        {
            _captionFiles.SaveVocabulary(VocabularyText.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries));
            _status = "Vocabulary saved. Future transcriptions use these terms as spelling hints.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { _status = "Vocabulary was not saved: " + e.Message; }
        Notify();
    }
    public void ImportText(string text, SubtitleSidecarFormat format)
    {
        if (!CanEdit()) throw new InvalidOperationException("Select an editable clip first.");
        var segments = StudioCaptionFileService.Parse(text, format, _asset!.Duration);
        Mutate(list => { list.Clear(); list.AddRange(segments); });
        _status = "Subtitles imported into the draft. Review timings and save words and timing."; Notify();
    }
    private void Export()
    {
        string? path = StudioCaptionFileService.ChooseExport();
        if (path is null) return;
        try
        {
            StudioCaptionFileService.Export(path, _asset!);
            _status = "Subtitle file exported.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status = "Export failed: " + e.Message; }
        Notify();
    }
    public async Task RegenerateAsync()
    {
        if (!CanEdit() || !HasSelectedAudioStream || _preparation is null || HasUnsavedChanges) return;
        if (LanguageUnavailableReason is { } reason) { _status = reason; Notify(); return; }
        AudioAudition.Stop();
        var asset = _asset!; var project = _project!;
        using var cancellation = new CancellationTokenSource(); _regeneration = cancellation;
        _status = "Transcribing this cut. This replaces the saved words and restores measured word timings."; Notify();
        try
        {
            var selection = new GenerationCaptionSourceSelection(asset.SourceFullPath, SelectedAudioStreamIndex,
                CaptionAudioContentRole.CreatorCommentary, SelectedLanguage);
            var track = await _preparation.PrepareRetainedCandidateAsync(asset.Id, asset.SourceMedia, asset.SourceStart, asset.SourceEnd,
                selection, asset.Appearance.CaptionStyle, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_asset, asset) || _project?.Id != project.Id) return;
            var updated = asset.WithCaptionTrack(track);
            _outputEditor!.ReplaceAsset(project.Id, updated); _asset = updated;
            LoadSegments(StudioCaptionTrackEditing.CreateDrafts(updated));
            _status = !track.HasRenderableSegments ? "No clear speech was retained. You can import subtitles or add phrases manually." :
                track.Segments.All(static segment => segment.Words.Count > 0) ? "Captions regenerated with measured word timing." :
                "Captions regenerated. Some phrases have no measured word timings; review or time their words manually.";
        }
        catch (OperationCanceledException) { _status = "Caption regeneration cancelled."; }
        catch (Exception e) { _status = "Caption regeneration failed: " + e.Message; }
        finally { _regeneration = null; Notify(); }
    }
    public async Task TranslateSecondaryAsync()
    {
        if (!CanEdit() || !HasSelectedAudioStream || _preparation is null || _asset?.Captions is not { } original || HasUnsavedChanges) return;
        if (TranslationUnavailableReason is { } reason) { _status = reason; Notify(); return; }
        AudioAudition.Stop();
        var asset = _asset; var project = _project!;
        using var cancellation = new CancellationTokenSource(); _regeneration = cancellation;
        _status = "Translating the retained speech to English. Original words and word timings are preserved."; Notify();
        try
        {
            var selection = new GenerationCaptionSourceSelection(asset.SourceFullPath, SelectedAudioStreamIndex,
                CaptionAudioContentRole.CreatorCommentary, GenerationCaptionLanguagePolicy.EnglishTranslation);
            var translated = await _preparation.PrepareRetainedCandidateAsync(asset.Id, asset.SourceMedia,
                original.SourceWindowStart, original.SourceWindowStart + original.SourceWindowDuration,
                selection, original.RequestedStyle, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_asset, asset) || _project?.Id != project.Id) return;
            if (!translated.HasRenderableSegments)
            {
                _status = "No translated speech was returned. Existing secondary captions were kept; you can add translations manually.";
                return;
            }
            var edits = StudioCaptionTrackEditing.CreateDrafts(asset).Select(edit => edit with
            {
                SecondaryText = string.Join(" ", translated.Segments.Where(s =>
                    s.AbsoluteSourceStart < asset.SourceStart + TimeSpan.FromSeconds(edit.EndSeconds) &&
                    s.AbsoluteSourceEnd > asset.SourceStart + TimeSpan.FromSeconds(edit.StartSeconds)).Select(s => s.Text).Distinct())
            }).ToArray();
            _asset = StudioCaptionTrackEditing.Apply(_outputEditor!, project, asset, edits);
            LoadSegments(StudioCaptionTrackEditing.CreateDrafts(_asset));
            _status = "English secondary captions added. Review each translation and its phrase alignment before export.";
        }
        catch (OperationCanceledException) { _status = "Translation cancelled."; }
        catch (Exception e) { _status = "Translation failed: " + e.Message; }
        finally { _regeneration = null; Notify(); }
    }
    private void Notify()
    {
        AudioAudition.SetHostBusy(_isHostBusy || _regeneration is not null || _alignmentCancellation is not null);
        // Keep option identities stable during draft/progress changes and publish
        // a changed item source before restoring its selected value in WPF.
        foreach (string name in new[] { nameof(HasSegments), nameof(HasUnsavedChanges), nameof(Status), nameof(AudioStreams), nameof(LanguageOptions),
            nameof(SelectedAudioStreamIndex), nameof(SelectedLanguage), nameof(LanguageUnavailableReason), nameof(TranslationUnavailableReason), nameof(LanguageModelDescription) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        _saveCommand.RaiseCanExecuteChanged(); _addCommand.RaiseCanExecuteChanged(); _undoCommand.RaiseCanExecuteChanged(); _redoCommand.RaiseCanExecuteChanged();
        _deleteCommand.RaiseCanExecuteChanged(); _splitCommand.RaiseCanExecuteChanged(); _mergeCommand.RaiseCanExecuteChanged();
        _wordRowsCommand.RaiseCanExecuteChanged();
        _importCommand.RaiseCanExecuteChanged(); _exportCommand.RaiseCanExecuteChanged(); _regenerateCommand.RaiseCanExecuteChanged();
        _saveVocabularyCommand.RaiseCanExecuteChanged();
        _translateSecondaryCommand.RaiseCanExecuteChanged();
        _alignCommand.RaiseCanExecuteChanged(); _cancelAlignmentCommand.RaiseCanExecuteChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAligning)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlignmentAvailability)));
    }
    private static string NewId() => "manual-" + Guid.NewGuid().ToString("N");
    private static bool Same(IReadOnlyList<StudioCaptionSegmentEdit> a, IReadOnlyList<StudioCaptionSegmentEdit> b) => a.Count == b.Count &&
        !a.Where((x, i) => x.Id != b[i].Id || x.Text != b[i].Text || !x.StartSeconds.Equals(b[i].StartSeconds) || !x.EndSeconds.Equals(b[i].EndSeconds) ||
            x.Speaker != b[i].Speaker || x.SecondaryText != b[i].SecondaryText || x.AlignmentProvenance != b[i].AlignmentProvenance ||
            !(x.Words ?? []).SequenceEqual(b[i].Words ?? [])).Any();

    private readonly AsyncDelegateCommand<StudioCaptionSegmentDraft> _alignCommand;
    private readonly DelegateCommand _cancelAlignmentCommand;
    private ICorrectedCaptionAlignmentService? _alignmentService;
    private CancellationTokenSource? _alignmentCancellation;
    private long _draftRevision;
    private StudioCaptionSegmentEdit[]? _timingEditBefore;
    public ICommand AlignCorrectedTextCommand => _alignCommand;
    public ICommand CancelAlignmentCommand => _cancelAlignmentCommand;
    public bool IsAligning => _alignmentCancellation is not null;
    public string AlignmentAvailability => _alignmentService is null ? "Corrected-text alignment is unavailable in this session." :
        !HasSelectedAudioStream ? "This clip has no selected audio track to align. Manual captions and subtitle import remain available." :
        SelectedLanguage != GenerationCaptionLanguagePolicy.English
            ? "Choose English in the voice/language settings to align corrected text. Automatic language detection is not treated as English."
            : "Align one corrected English phrase (up to 30 seconds). First use downloads a 91 MiB model; audio and text stay on this computer. " +
                "Review the proposed times before Save.";

    public void ConfigureAlignment(ICorrectedCaptionAlignmentService? service)
    { InvalidateAlignment(); _alignmentService = service; _languageOptions = null; Notify(); }

    private bool CanAlign(StudioCaptionSegmentDraft segment) => segment is not null && _alignmentService is not null &&
        CanEdit() && HasSelectedAudioStream && SelectedLanguage == GenerationCaptionLanguagePolicy.English && Segments.Contains(segment);

    private void InvalidateAlignment()
    {
        _draftRevision++;
        if (_alignmentCancellation is not null)
        {
            _alignmentCancellation.Cancel();
            _status = "Alignment stopped because the draft or audio selection changed. Existing words and times were kept.";
        }
    }
    private void CancelAlignment()
    {
        _alignmentCancellation?.Cancel();
        _status = "Cancelling alignment; existing draft words and times are kept."; Notify();
    }

    public async Task AlignCorrectedTextAsync(StudioCaptionSegmentDraft segment)
    {
        if (!CanAlign(segment)) return;
        var asset = _asset!; var project = _project!;
        StudioCaptionSegmentEdit before = segment.Snapshot();
        long revision = _draftRevision;
        using var cancellation = new CancellationTokenSource(); _alignmentCancellation = cancellation;
        AudioAudition.Stop();
        bool Current() => ReferenceEquals(_alignmentCancellation, cancellation) && ReferenceEquals(_asset, asset) &&
            _project?.Id == project.Id && _draftRevision == revision;
        _status = "Preparing corrected-text alignment. Your draft wording will be preserved."; Notify();
        try
        {
            if (!double.IsFinite(before.StartSeconds) || !double.IsFinite(before.EndSeconds) || before.EndSeconds <= before.StartSeconds ||
                before.EndSeconds - before.StartSeconds > 30 || string.IsNullOrWhiteSpace(before.Text) || before.Text.Length > 1000)
                throw new ArgumentException("Choose a phrase of at most 30 seconds and 1,000 characters with valid start/end times.");
            var request = new CorrectedCaptionAlignmentRequest(asset.SourceFullPath, asset.SourceDuration, SelectedAudioStreamIndex,
                asset.SourceStart + TimeSpan.FromSeconds(before.StartSeconds), asset.SourceStart + TimeSpan.FromSeconds(before.EndSeconds), before.Text, "en");
            if (request.SourceStart < TimeSpan.Zero || request.SourceEnd > asset.SourceDuration ||
                !asset.SourceMedia.AudioStreams.Any(stream => stream.Index == request.AbsoluteAudioStreamIndex))
                throw new ArgumentException("The phrase and selected audio stream must belong to this recording.");
            var source = new FileInfo(asset.SourceFullPath);
            if (!source.Exists) throw new FileNotFoundException("The source recording is unavailable; existing timings were kept.");
            long length = source.Length; DateTime modified = source.LastWriteTimeUtc;
            var progress = new Progress<string>(message =>
            {
                if (Current() && !cancellation.IsCancellationRequested) { _status = message; Notify(); }
            });
            var result = await _alignmentService!.AlignAsync(request, progress, cancellation.Token);
            if (!Current()) return;
            cancellation.Token.ThrowIfCancellationRequested();
            source.Refresh();
            if (!source.Exists || source.Length != length || source.LastWriteTimeUtc != modified)
                throw new InvalidDataException("The recording changed during alignment. Existing draft timings were kept.");
            ValidateAlignment(result, request);
            string provenance = StudioCaptionAlignmentProvenance.Create(request, result, length, modified);
            var words = result.Words.Select((word, index) => new StudioCaptionWordEdit(word.Text,
                before.StartSeconds + word.RelativeStart.TotalSeconds, before.StartSeconds + word.RelativeEnd.TotalSeconds,
                before.Words is { } old && index < old.Count && old[index].Text == word.Text && old[index].IsEmphasized,
                word.AcousticScore)).ToArray();
            Mutate(list =>
            {
                int index = list.FindIndex(item => item.Id == before.Id);
                if (index < 0) throw new InvalidOperationException("The caption phrase is no longer in the draft.");
                list[index] = before with { Words = words, AlignmentProvenance = provenance };
            });
            int weak = words.Count(word => word.AcousticScore < .15);
            _status = $"Timing proposed for the corrected text; nothing has been saved. {weak} of {words.Length} words have weak acoustic matches. " +
                "Listen, adjust the handles if needed, then Save words and timing. Acoustic scores do not verify the wording.";
        }
        catch (OperationCanceledException)
        {
            if (Current()) _status = "Alignment cancelled. Existing draft words and times were kept.";
        }
        catch (Exception exception)
        {
            if (Current()) _status = "Alignment was not applied: " + exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_alignmentCancellation, cancellation)) { _alignmentCancellation = null; Notify(); }
        }
    }

    private static void ValidateAlignment(CorrectedCaptionAlignmentResult result, CorrectedCaptionAlignmentRequest request)
    {
        string[] tokens = Regex.Matches(request.CorrectedText, @"\S+").Select(static match => match.Value).ToArray();
        if (result.Words is null || result.Words.Count != tokens.Length || tokens.Length is < 1 or > 120)
            throw new InvalidDataException("Alignment did not return one timing for each unchanged word; the draft was kept.");
        TimeSpan previous = TimeSpan.Zero;
        for (int index = 0; index < tokens.Length; index++)
        {
            var word = result.Words[index];
            if (word is null || word.Text != tokens[index] || word.RelativeStart < previous || word.RelativeEnd <= word.RelativeStart ||
                word.RelativeEnd > request.SourceEnd - request.SourceStart || !double.IsFinite(word.AcousticScore) || word.AcousticScore is < 0 or > 1)
                throw new InvalidDataException("Alignment returned changed text, overlapping times, or timing outside this phrase; the draft was kept.");
            previous = word.RelativeEnd;
        }
    }

    private void BeginTimingEdit(object? sender, EventArgs args)
    { _timingEditBefore ??= Snapshot(); }
    private void CompleteTimingEdit(object? sender, EventArgs args)
    {
        if (_timingEditBefore is not { } before) return;
        _timingEditBefore = null;
        var current = Snapshot();
        if (!Same(before, current)) { _undo.Push(before); _redo.Clear(); }
        _last = current; Notify();
    }
}

public sealed record StudioCaptionAudioStreamChoice(int Index, string Label)
{
    public override string ToString() => Label;
}
