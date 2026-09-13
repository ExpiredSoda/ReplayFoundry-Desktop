using System.Text.RegularExpressions;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionTextMatch(string SegmentId, int Offset, string Text);

/// <summary>Literal, reversible caption corrections. This never removes or changes the media.</summary>
public sealed class StudioCaptionTextTools : ObservableObject
{
    private readonly Func<IReadOnlyList<StudioCaptionSegmentEdit>> _snapshot;
    private readonly Action<Action<List<StudioCaptionSegmentEdit>>> _mutate;
    private readonly Action<string> _open;
    private readonly Func<bool> _canEdit;
    private readonly DelegateCommand _next, _previous, _replaceAll;
    private IReadOnlyList<StudioCaptionTextMatch> _matches = [];
    private string _query = "", _replacement = "";
    private bool _wholeWords = true, _showReplacement;
    private int _index = -1;
    private string? _message;

    internal StudioCaptionTextTools(Func<IReadOnlyList<StudioCaptionSegmentEdit>> snapshot,
        Action<Action<List<StudioCaptionSegmentEdit>>> mutate, Action<string> open, Func<bool> canEdit)
    {
        _snapshot = snapshot; _mutate = mutate; _open = open; _canEdit = canEdit;
        _next = new(() => Navigate(1), () => _canEdit() && HasMatches);
        _previous = new(() => Navigate(-1), () => _canEdit() && HasMatches);
        _replaceAll = new(ReplaceAll, () => _canEdit() && HasMatches && _query != _replacement);
        ToggleReplaceCommand = new DelegateCommand(() => ShowReplacement = !ShowReplacement);
        ClearCommand = new DelegateCommand(() => Query = "");
    }

    public string Query
    {
        get => _query;
        set { if (_query == value) return; _query = value ?? ""; _index = -1; _message = null; OnPropertyChanged(); Refresh(); }
    }
    public string Replacement
    {
        get => _replacement;
        set { if (_replacement == value) return; _replacement = value ?? ""; OnPropertyChanged(); _replaceAll.RaiseCanExecuteChanged(); }
    }
    public bool WholeWords
    {
        get => _wholeWords;
        set { if (_wholeWords == value) return; _wholeWords = value; _index = -1; _message = null; OnPropertyChanged(); Refresh(); }
    }
    public bool ShowReplacement
    {
        get => _showReplacement;
        set { if (_showReplacement == value) return; _showReplacement = value; OnPropertyChanged(); }
    }
    public bool HasQuery => !string.IsNullOrWhiteSpace(_query);
    public bool HasMatches => _matches.Count > 0;
    public string Summary => _message ?? (!HasQuery ? "Find words in this clip's captions." : !HasMatches ? "No matching caption text." :
        _index < 0 ? $"{_matches.Count} matches in this clip" : $"Match {_index + 1} of {_matches.Count}");
    public string ReplaceButtonText => $"Replace all {_matches.Count} in this clip";
    public string? SelectedExcerpt => _index >= 0 && _index < _matches.Count ? _matches[_index].Text : null;
    public IReadOnlyList<StudioCaptionTextMatch> Matches => _matches;
    public ICommand NextCommand => _next;
    public ICommand PreviousCommand => _previous;
    public ICommand ReplaceAllCommand => _replaceAll;
    public ICommand ToggleReplaceCommand { get; }
    public ICommand ClearCommand { get; }

    internal void Reset()
    {
        _query = ""; _replacement = ""; _index = -1; _message = null; _showReplacement = false;
        foreach (string name in new[] { nameof(Query), nameof(Replacement), nameof(ShowReplacement) }) OnPropertyChanged(name);
        Refresh();
    }

    internal void Refresh()
    {
        _message = null;
        var selected = _index >= 0 && _index < _matches.Count ? _matches[_index] : null;
        var pattern = StudioCaptionTextReplacement.Pattern(_query, _wholeWords);
        _matches = pattern is null ? [] : _snapshot().SelectMany(segment => pattern.Matches(segment.Text)
            .Select(match => new StudioCaptionTextMatch(segment.Id, match.Index, segment.Text))).ToArray();
        _index = selected is null ? -1 : _matches.ToList().FindIndex(match => match.SegmentId == selected.SegmentId && match.Offset == selected.Offset);
        Notify();
    }

    private void Navigate(int direction)
    {
        _index = _index < 0 ? direction > 0 ? 0 : _matches.Count - 1 : (_index + direction + _matches.Count) % _matches.Count;
        _message = null;
        _open(_matches[_index].SegmentId);
        Notify();
    }

    private void ReplaceAll()
    {
        int count = _matches.Count;
        _mutate(segments =>
        {
            for (int i = 0; i < segments.Count; i++)
                segments[i] = StudioCaptionTextReplacement.Apply(segments[i], _query, _replacement, _wholeWords);
        });
        _index = -1;
        Refresh();
        _message = $"Replaced {count} matches. Save words and timing when ready. Undo restores the entire correction. Check timing if words were added or removed.";
        Notify();
    }

    private void Notify()
    {
        foreach (string name in new[] { nameof(HasQuery), nameof(HasMatches), nameof(Summary), nameof(ReplaceButtonText), nameof(SelectedExcerpt), nameof(Matches) })
            OnPropertyChanged(name);
        _next.RaiseCanExecuteChanged(); _previous.RaiseCanExecuteChanged(); _replaceAll.RaiseCanExecuteChanged();
    }
}

internal static class StudioCaptionTextReplacement
{
    internal static Regex? Pattern(string query, bool wholeWords) => string.IsNullOrWhiteSpace(query) ? null :
        new((wholeWords ? @"(?<![\p{L}\p{N}_])" : "") + Regex.Escape(query) +
            (wholeWords ? @"(?![\p{L}\p{N}_])" : ""), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    internal static StudioCaptionSegmentEdit Apply(StudioCaptionSegmentEdit segment, string query, string replacement, bool wholeWords)
    {
        var pattern = Pattern(query, wholeWords);
        if (pattern is null) return segment;
        var matches = pattern.Matches(segment.Text).ToArray();
        if (matches.Length == 0) return segment;
        string text = pattern.Replace(segment.Text, _ => replacement);
        if (text == segment.Text) return segment;
        var tokens = Regex.Matches(segment.Text, @"\S+").Where(token => token.Value.Any(char.IsLetterOrDigit)).ToArray();
        var rows = StudioCaptionTimingReview.CreateWordRows(segment);
        var retained = new List<StudioCaptionWordEdit>();
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i]; var row = rows[i];
            bool originalRow = segment.Words?.Any(word => StudioCaptionTrackEditing.Lexical(word.Text) == StudioCaptionTrackEditing.Lexical(row.Text) &&
                word.StartSeconds.Equals(row.StartSeconds) && word.EndSeconds.Equals(row.EndSeconds)) == true;
            if (!originalRow) continue;
            var touched = matches.Where(match => match.Index < token.Index + token.Length && match.Index + match.Length > token.Index).ToArray();
            if (touched.Length == 0) { retained.Add(row); continue; }
            // A spelling correction within one word can keep its observed boundaries.
            // Cross-word edits cannot assign new word boundaries from text alone.
            if (touched.Any(match => match.Index < token.Index || match.Index + match.Length > token.Index + token.Length)) continue;
            string word = pattern.Replace(token.Value, _ => replacement);
            if (!word.Any(char.IsLetterOrDigit) || word.Any(char.IsWhiteSpace)) continue;
            retained.Add(row with { Text = word, AcousticScore = null });
        }
        return segment with { Text = text, Words = retained };
    }
}
