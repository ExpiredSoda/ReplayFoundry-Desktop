using System.Globalization;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioManualClipViewModel : ObservableObject, IDisposable
{
    private readonly IGenerationManualClipEditor? _editor;
    private readonly Func<bool> _canMutate;
    private readonly DelegateCommand _add;
    private readonly DelegateCommand _preview;
    private GenerationOutputProject? _project;
    private StudioManualSource? _source;
    private string _start = "00:00:00";
    private string _end = "00:00:30";
    private string _status = "Choose a source and a range of up to three minutes.";

    public StudioManualClipViewModel(IGenerationManualClipEditor? editor,
        IStudioPreviewMediaService? previewMedia, Func<bool> canMutate)
    {
        _editor = editor;
        _canMutate = canMutate;
        Preview = new(previewMedia, showCaptionControls: false, rangeMode: StudioPreviewRangeMode.ExactSelection);
        _add = new(Add, CanAdd);
        _preview = new(PreviewRange, ValidRange);
    }

    public event EventHandler? ClipAdded;
    public IReadOnlyList<StudioManualSource> Sources { get; private set; } = [];
    public StudioPreviewViewModel Preview { get; }
    public ICommand AddCommand => _add;
    public ICommand PreviewCommand => _preview;
    public string Status => _status;
    public StudioManualSource? SelectedSource
    {
        get => _source;
        set
        {
            if (value is null) return;
            if (_source?.FullPath.Equals(value.FullPath, StringComparison.OrdinalIgnoreCase) == true)
            { _source = value; return; }
            _source = value;
            _start = "00:00:00";
            _end = Format(TimeSpan.FromSeconds(Math.Min(30, value?.Duration.TotalSeconds ?? 30)));
            Notify();
        }
    }
    public string StartText { get => _start; set { _start = value; Notify(); } }
    public string EndText { get => _end; set { _end = value; Notify(); } }
    public double SourceMaximumSeconds => Math.Max(0, (_source?.Duration.TotalSeconds ?? 0) - 1);
    public double SourcePositionSeconds
    {
        get => TryTime(_start, out var start) ? start.TotalSeconds : 0;
        set
        {
            if (!double.IsFinite(value) || _source is null) return;
            double start = Math.Clamp(value, 0, SourceMaximumSeconds);
            _start = Format(TimeSpan.FromSeconds(start));
            _end = Format(TimeSpan.FromSeconds(Math.Min(start + 30, _source.Duration.TotalSeconds)));
            Notify();
        }
    }

    public void Bind(GenerationOutputProject? project)
    {
        bool changed = _project?.Id != project?.Id;
        _project = project;
        Sources = project?.SourceMedia.Select(static media => StudioManualSource.FromMedia(media)).ToArray() ?? [];
        if (changed || _source is null)
        {
            SelectedSource = Sources.FirstOrDefault();
            Preview.Bind(false, null, null);
        }
        else
            _source = Sources.FirstOrDefault(value => value.FullPath.Equals(_source.FullPath,
                StringComparison.OrdinalIgnoreCase));
        Notify();
    }

    public void RefreshAvailability() => _add.RaiseCanExecuteChanged();
    internal Task StopAsync(CancellationToken token) => Preview.StopAsync(token);
    public void Dispose() => Preview.Dispose();

    private bool ValidRange() => _project?.IsFinalized == false && _source is not null &&
        TryTime(_start, out var start) && TryTime(_end, out var end) &&
        start >= TimeSpan.Zero && end > start && end <= _source.Duration &&
        end - start <= TimeSpan.FromMinutes(3);
    private bool CanAdd() => _editor is not null && _canMutate() && ValidRange();

    private void PreviewRange()
    {
        if (!ValidRange()) return;
        TryTime(_start, out var start);
        TryTime(_end, out var end);
        Preview.Bind(true, _project, _project!.CreateManualSourceAsset(_source!.FullPath, start, end));
        _status = "Previewing this source range. Add it when the beginning and ending feel complete.";
        OnPropertyChanged(nameof(Status));
    }

    private void Add()
    {
        if (!CanAdd()) return;
        TryTime(_start, out var start);
        TryTime(_end, out var end);
        _editor!.AddManualSourceClip(_project!.Id, _source!.FullPath, start, end);
        _status = "Added to Studio. Review its title, captions and output layout before rendering.";
        OnPropertyChanged(nameof(Status));
        ClipAdded?.Invoke(this, EventArgs.Empty);
    }

    private void Notify()
    {
        foreach (string name in new[] { nameof(Sources), nameof(SelectedSource), nameof(StartText), nameof(EndText),
                     nameof(SourceMaximumSeconds), nameof(SourcePositionSeconds) }) OnPropertyChanged(name);
        _status = ValidRange() ? "Preview or add this range from the full recording."
            : "Enter a start and end inside the recording, no more than three minutes apart (hh:mm:ss or seconds).";
        OnPropertyChanged(nameof(Status));
        _add.RaiseCanExecuteChanged();
        _preview.RaiseCanExecuteChanged();
    }

    private static bool TryTime(string value, out TimeSpan time)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
        { time = TimeSpan.FromSeconds(seconds); return true; }
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
    }
    private static string Format(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
}
