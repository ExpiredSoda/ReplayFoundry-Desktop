using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class CaptionLookChooserViewModel : ObservableObject
{
    private readonly ObservableCollection<StudioNamedCaptionLook> _saved;
    private readonly Func<StudioCaptionLook> _current;
    private readonly Func<string?> _selectedName;
    private readonly Action<StudioNamedCaptionLook> _applySaved;
    private readonly Action<GenerationCaptionStylePreset> _applyStyle;
    private IReadOnlyList<CaptionLookChoice>? _choices;
    private bool _applying;

    public CaptionLookChooserViewModel(ObservableCollection<StudioNamedCaptionLook> saved,
        Func<StudioCaptionLook> current, Func<string?> selectedName,
        Action<StudioNamedCaptionLook> applySaved, Action<GenerationCaptionStylePreset> applyStyle)
    {
        _saved = saved; _current = current; _selectedName = selectedName;
        _applySaved = applySaved; _applyStyle = applyStyle;
        saved.CollectionChanged += (_, _) => { _choices = null; OnPropertyChanged(nameof(Choices)); Refresh(); };
    }

    public IReadOnlyList<CaptionLookChoice> Choices => _choices ??= CaptionLookChoice.Create(_saved);
    public CaptionLookChoice SelectedChoice
    {
        get
        {
            var current = _current();
            return Choices.FirstOrDefault(choice => choice.Saved?.Name == _selectedName() && choice.Look == current)
                ?? Choices.FirstOrDefault(choice => choice.IsSaved && choice.Look == current)
                ?? Choices.First(choice => !choice.IsSaved && choice.Style == current.CaptionStyle);
        }
        set
        {
            if (value is null || !Choices.Contains(value) || _applying) return;
            _applying = true;
            try
            {
                if (value.Saved is { } saved) _applySaved(saved);
                else _applyStyle(value.Style);
            }
            finally { _applying = false; Refresh(); }
        }
    }
    public void Refresh() { if (!_applying) OnPropertyChanged(nameof(SelectedChoice)); }
}
