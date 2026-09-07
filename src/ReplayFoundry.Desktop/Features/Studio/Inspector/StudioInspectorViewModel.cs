using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Inspector;

internal sealed record StudioInspectorDraftSnapshot(
    string ProjectId,
    string AssetId,
    StudioClipEditorDraftSnapshot? Clip,
    StudioGraphicPlacementDraftSnapshot? Graphics,
    StudioCaptionTrackDraftSnapshot? Caption,
    StudioPendingEditorialDraft? Editorial,
    StudioPendingEditorialProfileDraft? EditorialProfile);

public sealed class StudioInspectorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DelegateCommand<StudioInspectorSection> _selectCommand;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _selectedAsset;
    private StudioInspectorSection _selectedSection = StudioInspectorSection.Clip;
    private bool _hasProject;
    private bool _isDisposed;

    public StudioInspectorViewModel(
        IGenerationOutputEditor? outputEditor,
        IClipEditorialMetadataGenerationService? metadataGenerator,
        IClipEditorialProfileEditor? editorialProfile,
        IStudioClipPreferenceService? preferenceService,
        IStudioCandidateDecisionStore? decisionStore = null,
        IResearchFeedbackRecorder? researchFeedback = null,
        IEditorialRerollPreference? editorialRerollPreference = null,
        IStudioEditorialMetadataCorrectionRecorder?
            editorialPreferenceRecorder =
            null,
        IGenerationGameKnowledgeService? gameKnowledge = null)
    {
        Clip = new StudioClipEditorViewModel(outputEditor);
        Output = new StudioOutputEditorViewModel(outputEditor);
        FrameEdits = new StudioFrameEditsViewModel(outputEditor);
        Graphics = new StudioGraphicOverlayEditorViewModel(outputEditor);
        Caption = new StudioCaptionTrackEditorViewModel(outputEditor);
        Preference = new StudioClipPreferenceViewModel(
            preferenceService,
            outputEditor,
            decisionStore,
            researchFeedback);
        Editorial = new StudioEditorialMetadataViewModel(
            outputEditor,
            metadataGenerator,
            editorialProfile,
            editorialRerollPreference,
            editorialPreferenceRecorder,
            gameKnowledge);
        TimelineEdits = new StudioTimelineEditViewModel(outputEditor, () => !Clip.HasPendingEdit &&
            !Caption.HasUnsavedChanges && !Graphics.HasUnsavedChanges && !Editorial.HasUnsavedChanges && !Editorial.IsGenerating);
        _selectCommand = new DelegateCommand<StudioInspectorSection>(
            value => SelectedInspector = value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SelectedAssetChanged;

    public StudioClipEditorViewModel Clip { get; }
    public StudioOutputEditorViewModel Output { get; }
    public StudioFrameEditsViewModel FrameEdits { get; }
    public StudioTimelineEditViewModel TimelineEdits { get; }
    public StudioGraphicOverlayEditorViewModel Graphics { get; }
    public StudioCaptionTrackEditorViewModel Caption { get; }
    public StudioClipPreferenceViewModel Preference { get; }
    public StudioEditorialMetadataViewModel Editorial { get; }
    public IReadOnlyList<StudioInspectorItem> InspectorSections =>
        StudioSurfaceCatalog.InspectorSections;
    public IReadOnlyList<GenerationOutputAsset> ProjectAssets =>
        _project?.Assets ?? [];
    public IReadOnlyList<StudioAudioStreamSummary> AudioStreams =>
        BuildAudioStreams(SelectedAsset);
    public StudioInspectorSection SelectedInspector
    {
        get => _selectedSection;
        set
        {
            if (_selectedSection == value)
            {
                return;
            }

            _selectedSection = value;
            if (value != StudioInspectorSection.Effects) Clip.Effects.ShowOriginal = false;
            OnPropertyChanged();
            NotifySectionProperties();
        }
    }
    public GenerationOutputAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (ReferenceEquals(_selectedAsset, value))
            {
                return;
            }
            if (value is not null &&
                !ProjectAssets.Any(asset => ReferenceEquals(asset, value)))
            {
                throw new ArgumentException(
                    "The selected Studio asset must belong to the current project.",
                    nameof(value));
            }

            BindSelectedAsset(value);
            SelectedAssetChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public string SelectedInspectorTitle =>
        StudioSurfaceCatalog.GetInspector(SelectedInspector).Label;
    public string SelectedInspectorDescription => SelectedAsset is null
        ? "Choose a clip to see what you can change."
        : IsProjectFinalized
            ? $"{SelectedAsset.DisplayName} has a finished copy in Library."
            : $"Editing {SelectedAsset.DisplayName}. You can change this again at any time.";
    public bool IsClipBoundaryEditorVisible =>
        _hasProject && SelectedInspector == StudioInspectorSection.Clip;
    public bool IsAudioEditorVisible =>
        _hasProject && SelectedInspector == StudioInspectorSection.Audio;
    public bool IsCaptionStyleEditorVisible =>
        _hasProject &&
        SelectedInspector == StudioInspectorSection.Captions &&
        SelectedAsset is not null;
    public bool IsCaptionTrackMissing =>
        _hasProject &&
        SelectedInspector == StudioInspectorSection.Captions &&
        SelectedAsset?.HasCaptions != true;
    public bool IsVideoEffectEditorVisible =>
        _hasProject && SelectedInspector == StudioInspectorSection.Effects;
    public bool IsGraphicsEditorVisible =>
        _hasProject && SelectedInspector == StudioInspectorSection.Graphics;
    public bool IsMetadataEditorVisible =>
        _hasProject && SelectedInspector == StudioInspectorSection.Metadata;
    public bool IsProjectFinalized => _project?.IsFinalized == true;
    public bool IsProjectDraft =>
        _project is not null && !_project.IsFinalized;
    public string AudioMixSummary => SelectedAsset is null
        ? "Select a clip to hear its audio."
        : SelectedAsset.SourceMedia.AudioStreams.Count switch
        {
            0 => "This video has no sound, so the finished clip will be silent.",
            1 => "The clip's audio stays audible in the finished file.",
            int count => $"All {count} audio tracks stay audible in the finished file.",
        };
    public string CaptionAudioSummary => SelectedAsset?.Captions is null
        ? "No voice audio was chosen for captions."
        : "Caption words use the voice audio you chose. All audio remains audible.";
    public System.Windows.Input.ICommand SelectInspectorCommand =>
        _selectCommand;

    public void Bind(
        bool hasProject,
        GenerationOutputProject? project,
        string? preferredAssetId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _hasProject = hasProject;
        _project = project;
        GenerationOutputAsset? selected = project?.Assets
            .FirstOrDefault(
                asset => preferredAssetId is not null &&
                         asset.Id.Equals(
                             preferredAssetId,
                             StringComparison.Ordinal)) ??
            project?.Assets.FirstOrDefault();
        BindSelectedAsset(selected);
        NotifyContextProperties();
    }

    public void SetHostBusy(bool isBusy)
    {
        Clip.SetHostBusy(isBusy);
        Output.SetHostBusy(isBusy);
        FrameEdits.SetHostBusy(isBusy);
        TimelineEdits.SetHostBusy(isBusy);
        Caption.SetHostBusy(isBusy);
        Graphics.SetHostBusy(isBusy);
        Preference.SetHostBusy(isBusy);
        Editorial.SetHostBusy(isBusy);
    }

    internal StudioInspectorDraftSnapshot? CapturePendingDrafts()
    {
        if (_project?.IsFinalized != false || _selectedAsset is null)
        {
            return null;
        }

        StudioClipEditorDraftSnapshot? clip = Clip.CapturePendingDraft();
        StudioGraphicPlacementDraftSnapshot? graphics =
            Graphics.CapturePendingDraft();
        StudioCaptionTrackDraftSnapshot? caption =
            Caption.CapturePendingDraft();
        StudioPendingEditorialDraft? editorial =
            Editorial.CapturePendingDraft();
        StudioPendingEditorialProfileDraft? profile =
            Editorial.CapturePendingProfileDraft();
        if (clip is null && graphics is null && caption is null &&
            editorial is null && profile is null)
        {
            return null;
        }

        return new StudioInspectorDraftSnapshot(
            _project.Id,
            _selectedAsset.Id,
            clip,
            graphics,
            caption,
            editorial,
            profile);
    }

    internal void RestorePendingDrafts(
        StudioInspectorDraftSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (_project?.IsFinalized != false ||
            _selectedAsset is null ||
            !_project.Id.Equals(draft.ProjectId, StringComparison.Ordinal) ||
            !_selectedAsset.Id.Equals(draft.AssetId, StringComparison.Ordinal))
        {
            return;
        }

        if (draft.Clip is not null)
        {
            Clip.RestorePendingDraft(draft.Clip);
        }
        if (draft.Graphics is not null)
        {
            Graphics.RestorePendingDraft(draft.Graphics);
        }
        if (draft.Caption is not null)
        {
            Caption.RestorePendingDraft(draft.Caption);
        }
        Editorial.RestorePendingDrafts(
            draft.Editorial,
            draft.EditorialProfile);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Preference.Dispose();
        Caption.Dispose();
        Output.Dispose();
        Editorial.Dispose();
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        await Caption.AudioAudition.StopAsync(cancellationToken);
        await Output.MixAudition.StopAsync(cancellationToken);
        await Output.Tracking.StopAsync(cancellationToken);
        await Editorial.StopAsync(cancellationToken);
    }

    private void BindSelectedAsset(GenerationOutputAsset? asset)
    {
        _selectedAsset = asset;
        Clip.Bind(_project, asset);
        Output.Bind(_project, asset);
        FrameEdits.Bind(_project, asset);
        TimelineEdits.Bind(_project, asset);
        Graphics.Bind(_project, asset);
        Caption.Bind(_project, asset);
        Preference.Bind(_project, asset);
        Editorial.Bind(_project, asset);
        OnPropertyChanged(nameof(SelectedAsset));
        NotifyContextProperties();
    }

    private void NotifySectionProperties()
    {
        foreach (string propertyName in new[]
        {
            nameof(SelectedInspectorTitle),
            nameof(SelectedInspectorDescription),
            nameof(IsClipBoundaryEditorVisible),
            nameof(IsAudioEditorVisible),
            nameof(IsCaptionStyleEditorVisible),
            nameof(IsCaptionTrackMissing),
            nameof(IsVideoEffectEditorVisible),
            nameof(IsGraphicsEditorVisible),
            nameof(IsMetadataEditorVisible),
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void NotifyContextProperties()
    {
        OnPropertyChanged(nameof(ProjectAssets));
        OnPropertyChanged(nameof(AudioStreams));
        OnPropertyChanged(nameof(AudioMixSummary));
        OnPropertyChanged(nameof(CaptionAudioSummary));
        OnPropertyChanged(nameof(IsProjectFinalized));
        OnPropertyChanged(nameof(IsProjectDraft));
        NotifySectionProperties();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static IReadOnlyList<StudioAudioStreamSummary> BuildAudioStreams(
        GenerationOutputAsset? asset) => asset?.SourceMedia.AudioStreams
        .Select(stream => new StudioAudioStreamSummary(
            string.IsNullOrWhiteSpace(stream.Title)
                ? $"Audio {stream.Index + 1}"
                : stream.Title,
            stream.Channels switch
            {
                1 => "Mono",
                2 => "Stereo",
                int channels => $"{channels} channels",
                _ => "Audio",
            },
            stream.IsDefault
                ? "Main audio · included in the finished clip"
                : "Included in the finished clip"))
        .ToArray() ?? [];
}
