using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal sealed record NormalizedVisualMomentSample(
    VisualSignalSample Sample,
    string RegionId,
    int IntervalIndex,
    CompositionRegionRole Role,
    LocalSignalContext Context)
{
    public double Activity => Context.NormalizedProminence;
}

internal sealed record NormalizedAudioMomentSample(
    AudioSignalSample Sample,
    LocalSignalContext? Context)
{
    public double Activity =>
        Sample.IsDigitalSilence
            ? 0
            : Context?.NormalizedProminence ?? 0;
}

internal sealed class NormalizedMomentSignals
{
    public NormalizedMomentSignals(
        IEnumerable<NormalizedVisualMomentSample> gameplay,
        IEnumerable<NormalizedVisualMomentSample> presenter,
        IEnumerable<NormalizedAudioMomentSample> audio,
        IEnumerable<AttributedGameplaySceneBoundary> gameplayScenes,
        IEnumerable<ActivityBurst> gameplayBursts,
        IEnumerable<ActivityBurst> presenterBursts,
        IEnumerable<AudioNoveltyEvent> audioNoveltyEvents)
    {
        Gameplay = Array.AsReadOnly(gameplay.ToArray());
        Presenter = Array.AsReadOnly(presenter.ToArray());
        Audio = Array.AsReadOnly(audio.ToArray());
        GameplayScenes = Array.AsReadOnly(gameplayScenes.ToArray());
        GameplayBursts = Array.AsReadOnly(gameplayBursts.ToArray());
        PresenterBursts = Array.AsReadOnly(presenterBursts.ToArray());
        AudioNoveltyEvents = Array.AsReadOnly(audioNoveltyEvents.ToArray());
    }

    public IReadOnlyList<NormalizedVisualMomentSample> Gameplay { get; }
    public IReadOnlyList<NormalizedVisualMomentSample> Presenter { get; }
    public IReadOnlyList<NormalizedAudioMomentSample> Audio { get; }
    public IReadOnlyList<AttributedGameplaySceneBoundary> GameplayScenes { get; }
    public IReadOnlyList<ActivityBurst> GameplayBursts { get; }
    public IReadOnlyList<ActivityBurst> PresenterBursts { get; }
    public IReadOnlyList<AudioNoveltyEvent> AudioNoveltyEvents { get; }
}
