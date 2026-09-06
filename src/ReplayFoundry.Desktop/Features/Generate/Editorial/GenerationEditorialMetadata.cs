using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public interface IClipEditorialProfileSource
{
    ClipEditorialProfile Current { get; }
}

public interface IClipEditorialProfileEditor :
    IClipEditorialProfileSource
{
    void Update(ClipEditorialProfile profile);
}

public sealed class ClipEditorialProfileSession :
    IClipEditorialProfileEditor,
    ICreatorVoiceSettingsEditor
{
    public ClipEditorialProfile Current { get; private set; } =
        ClipEditorialProfile.Default;

    public void Update(ClipEditorialProfile profile)
    {
        Current = profile ??
            throw new ArgumentNullException(nameof(profile));
    }

    public CreatorVoiceSettings CurrentCreatorVoice =>
        CreatorVoiceSettings.FromProfile(Current);

    public CreatorVoiceSettings UpdateCreatorVoice(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        IEnumerable<string> defaultTags)
    {
        var profile = new ClipEditorialProfile(
            audienceAddress,
            namingGuidance,
            descriptionSignature,
            defaultTags,
            Current.VoicePerspective,
            Current.CopyObjective);
        Update(profile);
        return CreatorVoiceSettings.FromProfile(profile);
    }
}

public sealed class GenerationCandidateEditorialMetadata
{
    public GenerationCandidateEditorialMetadata(
        GenerationMomentCandidate candidate,
        ClipEditorialContext context,
        ClipEditorialMetadataDraft draft)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(draft);
        if (!candidate.Id.Equals(
                context.CandidateId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Editorial context must belong to the selected generation candidate.",
                nameof(context));
        }

        Candidate = candidate;
        Context = context;
        Draft = draft;
    }

    public GenerationMomentCandidate Candidate { get; }

    public ClipEditorialContext Context { get; }

    public ClipEditorialMetadataDraft Draft { get; }
}

public sealed class GenerationEditorialMetadataResult
{
    private readonly ReadOnlyCollection<GenerationCandidateEditorialMetadata>
        _candidates;

    public GenerationEditorialMetadataResult(
        GenerationMomentFindingResult moments,
        ClipEditorialProfile profile,
        IEnumerable<GenerationCandidateEditorialMetadata> candidates)
    {
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(candidates);
        GenerationCandidateEditorialMetadata[] snapshot =
            candidates.ToArray();
        if (snapshot.Length != moments.SelectedCandidates.Count ||
            snapshot.Any(static item => item is null) ||
            snapshot.Where(
                    (item, index) =>
                        !ReferenceEquals(
                            item.Candidate,
                            moments.SelectedCandidates[index]))
                .Any())
        {
            throw new ArgumentException(
                "Editorial metadata requires one ordered result per selected moment.",
                nameof(candidates));
        }
        ClipEditorialGenerationPreference preference =
            moments.Request.Setup.MetadataAuthoringMode ==
                GenerationMetadataAuthoringMode.AiRequired
                ? ClipEditorialGenerationPreference.AiRequired
                : ClipEditorialGenerationPreference.HeuristicOnly;
        if (snapshot.Any(item =>
                !ClipEditorialMetadataGenerationPolicy.IsCompatible(
                    preference,
                    item.Draft)))
        {
            throw new ArgumentException(
                "Editorial metadata must match the generation run's explicit AI or heuristic provider choice.",
                nameof(candidates));
        }

        Moments = moments;
        Profile = profile;
        _candidates = Array.AsReadOnly(snapshot);
    }

    public GenerationMomentFindingResult Moments { get; }

    public ClipEditorialProfile Profile { get; }

    public IReadOnlyList<GenerationCandidateEditorialMetadata> Candidates =>
        _candidates;

    public GenerationCandidateEditorialMetadata Find(string candidateId) =>
        _candidates.Single(
            item => item.Candidate.Id.Equals(
                candidateId,
                StringComparison.Ordinal));
}

public interface IGenerationEditorialMetadataService
{
    Task<GenerationEditorialMetadataResult> GenerateAsync(
        GenerationMomentFindingResult moments,
        GenerationCaptionPreparationResult? captions,
        CancellationToken cancellationToken,
        GenerationCandidateIntelligenceResult? candidateIntelligence = null);

    Task<GenerationHiddenMomentDeck> GenerateHiddenAsync(
        GenerationHiddenMomentDeck hiddenMoments,
        GenerationCandidateIntelligenceResult? candidateIntelligence,
        CancellationToken cancellationToken);

    Task<GenerationHiddenMoment> PrepareAcceptedHiddenAsync(
        GenerationHiddenMoment hiddenMoment,
        GenerationCandidateCaptionTrack? captions,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? existingProjectTitles = null);
}

public sealed class GenerationEditorialMetadataService :
    IGenerationEditorialMetadataService
{
    private readonly IClipEditorialMetadataGenerationService _generator;
    private readonly IClipEditorialProfileSource _profileSource;
    private readonly IGenerationVisualTextAnalysisService? _visualText;
    private readonly IGenerationGameKnowledgeService? _gameKnowledge;

    public GenerationEditorialMetadataService(
        IClipEditorialMetadataGenerationService generator,
        IClipEditorialProfileSource profileSource,
        IGenerationGameKnowledgeService? gameKnowledge = null,
        IGenerationVisualTextAnalysisService? visualText = null)
    {
        _generator = generator ??
            throw new ArgumentNullException(nameof(generator));
        _profileSource = profileSource ??
            throw new ArgumentNullException(nameof(profileSource));
        _gameKnowledge = gameKnowledge;
        _visualText = visualText;
    }

    public async Task<GenerationEditorialMetadataResult> GenerateAsync(
        GenerationMomentFindingResult moments,
        GenerationCaptionPreparationResult? captions,
        CancellationToken cancellationToken,
        GenerationCandidateIntelligenceResult? candidateIntelligence = null)
    {
        ArgumentNullException.ThrowIfNull(moments);
        if (captions is not null &&
            !ReferenceEquals(captions.Moments, moments))
        {
            throw new ArgumentException(
                "Editorial captions must belong to the selected moments.",
                nameof(captions));
        }
        if (candidateIntelligence is not null &&
            !ReferenceEquals(candidateIntelligence.RefinedMoments, moments))
        {
            throw new ArgumentException(
                "Editorial intelligence must belong to the selected moments.",
                nameof(candidateIntelligence));
        }

        ClipEditorialProfile profile = _profileSource.Current;
        ClipEditorialGenerationPreference preference =
            moments.Request.Setup.MetadataAuthoringMode ==
            GenerationMetadataAuthoringMode.AiRequired
                ? ClipEditorialGenerationPreference.AiRequired
                : ClipEditorialGenerationPreference.HeuristicOnly;
        var prepared =
            new List<(GenerationMomentCandidate Candidate, ClipEditorialContext Context, ClipEditorialMetadataRequest Request)>(
                moments.SelectedCandidates.Count);
        for (int candidateIndex = 0;
             candidateIndex < moments.SelectedCandidates.Count;
             candidateIndex++)
        {
            GenerationMomentCandidate candidate =
                moments.SelectedCandidates[candidateIndex];
            cancellationToken.ThrowIfCancellationRequested();
            GenerationVisualSemanticAnalysisResult? visualResult =
                candidateIntelligence?.VisualSemantic;
            GenerationVisualSemanticCandidateObservation? visualObservation =
                visualResult?.Observations.SingleOrDefault(value =>
                    ReferenceEquals(value.Candidate, candidate.Candidate));
            ClipEditorialContext context = BuildContext(
                candidate,
                captions?.Tracks.SingleOrDefault(
                    track => track.Candidate.Id.Equals(
                        candidate.Id,
                        StringComparison.Ordinal)),
                moments.Request.Setup.GameContextSettings.Find(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath),
                visualObservation);
            if (_visualText is not null)
            {
                context = await _visualText.EnrichAsync(
                    VisualTextRequest(context, candidate),
                    cancellationToken);
            }
            if (_gameKnowledge is not null &&
                preference == ClipEditorialGenerationPreference.AiRequired)
            {
                context = await _gameKnowledge.EnrichAsync(
                    context,
                    cancellationToken);
            }
            prepared.Add((
                candidate,
                context,
                new ClipEditorialMetadataRequest(
                        context,
                        profile,
                        attempt: 0,
                        preference,
                        candidate.AnalyzedSource.PreparedSource.Media,
                        reviewVideo: RetainedCandidateReview(
                            visualResult,
                            visualObservation,
                            candidate.Candidate.Window))
                    .WithVariantIntent(InitialVariantIntent(candidateIndex))));
        }

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await _generator.GenerateBatchAsync(
                prepared.Select(static item => item.Request).ToArray(),
                cancellationToken);
        if (drafts.Count != prepared.Count)
        {
            throw new InvalidDataException(
                "Editorial metadata generation did not preserve every selected candidate.");
        }

        var results =
            new List<GenerationCandidateEditorialMetadata>(prepared.Count);
        for (int index = 0; index < prepared.Count; index++)
        {
            results.Add(
                new GenerationCandidateEditorialMetadata(
                    prepared[index].Candidate,
                    prepared[index].Context,
                    drafts[index]));
        }

        return new GenerationEditorialMetadataResult(
            moments,
            profile,
            results);
    }

    private static ClipEditorialVariantIntent InitialVariantIntent(
        int candidateIndex) =>
        (candidateIndex % 3) switch
        {
            0 => ClipEditorialVariantIntent.DirectAction,
            1 => ClipEditorialVariantIntent.SpecificCuriosity,
            _ => ClipEditorialVariantIntent.OutcomeFocused,
        };

    public async Task<GenerationHiddenMomentDeck> GenerateHiddenAsync(
        GenerationHiddenMomentDeck hiddenMoments,
        GenerationCandidateIntelligenceResult? candidateIntelligence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hiddenMoments);
        if (candidateIntelligence is not null &&
            !ReferenceEquals(
                candidateIntelligence.RefinedMoments,
                hiddenMoments.SelectedMoments))
        {
            throw new ArgumentException(
                "Hidden-moment intelligence must belong to the retained selected result.",
                nameof(candidateIntelligence));
        }
        if (hiddenMoments.Count == 0)
        {
            return hiddenMoments;
        }

        // Hidden Moments are not publishing candidates until the user accepts
        // one. Retain their grounded context now, but in AI mode defer the
        // audience copy so a normal five-clip run does not author dozens of
        // unused alternates. Promotion performs the required AI generation.
        bool deferAiMetadata =
            hiddenMoments.SelectedMoments.Request.Setup.MetadataAuthoringMode ==
            GenerationMetadataAuthoringMode.AiRequired;

        ClipEditorialProfile profile = _profileSource.Current;
        var prepared = new List<(
            GenerationHiddenMoment Hidden,
            ClipEditorialContext Context,
            ClipEditorialMetadataRequest Request)>(hiddenMoments.Count);
        foreach (GenerationHiddenMoment hidden in hiddenMoments.Moments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sourceOrder = hiddenMoments.SelectedMoments.Sources
                .Select((source, index) => (source, index))
                .Single(item => ReferenceEquals(
                    item.source.AnalyzedSource,
                    hidden.AnalyzedSource)).index;
            GenerationCandidateRefinement? refinement =
                candidateIntelligence?.Refinements.SingleOrDefault(value =>
                    ReferenceEquals(value.Candidate, hidden.Candidate));
            var selected = new GenerationMomentCandidate(
                hidden.Id,
                hidden.AnalyzedSource,
                hidden.Candidate,
                sourceOrder,
                hidden.ReviewOrder,
                GenerationCandidateSelectionReason.HiddenMomentRecovery,
                refinement);
            GenerationVisualSemanticAnalysisResult? visualResult =
                candidateIntelligence?.VisualSemantic;
            ClipEditorialContext context = BuildContext(
                selected,
                captions: null,
                hiddenMoments.SelectedMoments.Request.Setup.GameContextSettings.Find(
                    hidden.SourceFullPath),
                visualResult?.Observations.SingleOrDefault(value =>
                    ReferenceEquals(value.Candidate, hidden.Candidate)));
            if (_visualText is not null)
            {
                context = await _visualText.EnrichAsync(
                    VisualTextRequest(context, selected),
                    cancellationToken);
            }
            prepared.Add((
                hidden,
                context,
                new ClipEditorialMetadataRequest(
                    context,
                    profile,
                    attempt: 0,
                    ClipEditorialGenerationPreference.HeuristicOnly,
                    hidden.SourceMedia)));
        }

        if (deferAiMetadata)
        {
            return hiddenMoments.WithEditorialMetadata(
                prepared.Select(static item =>
                    item.Hidden.WithEditorialContext(item.Context)));
        }

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await _generator.GenerateBatchAsync(
                prepared.Select(static value => value.Request).ToArray(),
                cancellationToken);
        if (drafts.Count != prepared.Count)
        {
            throw new InvalidDataException(
                "Hidden-moment metadata did not preserve every safe alternate.");
        }
        return hiddenMoments.WithEditorialMetadata(
            prepared.Select((item, index) =>
                item.Hidden.WithEditorialMetadata(
                    item.Context,
                    drafts[index])));
    }

    public async Task<GenerationHiddenMoment> PrepareAcceptedHiddenAsync(
        GenerationHiddenMoment hiddenMoment,
        GenerationCandidateCaptionTrack? captions,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? existingProjectTitles = null)
    {
        ArgumentNullException.ThrowIfNull(hiddenMoment);
        if (captions is not null &&
            !captions.CandidateId.Equals(
                hiddenMoment.Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Accepted Hidden Moment captions must belong to the same candidate.",
                nameof(captions));
        }

        cancellationToken.ThrowIfCancellationRequested();
        GenerationMomentCandidate? selected =
            CreateRetainedHiddenCandidate(hiddenMoment);
        ClipEditorialContext context = hiddenMoment.EditorialContext is
            { } retained
                ? BuildRetainedHiddenContext(
                    hiddenMoment,
                    retained,
                    captions)
                : selected is not null
                    ? BuildContext(
                    selected,
                    captions,
                    gameContext: null,
                    visualObservation: null)
                    : throw new InvalidOperationException(
                        "A persisted Hidden Moment requires retained editorial context before its provisional metadata can be accepted.");
        if (_visualText is not null &&
            context.VisualText is null &&
            selected is not null)
        {
            context = await _visualText.EnrichAsync(
                VisualTextRequest(context, selected),
                cancellationToken);
        }
        if (_gameKnowledge is not null &&
            hiddenMoment.EditorialPreference ==
                ClipEditorialGenerationPreference.AiRequired)
        {
            context = await _gameKnowledge.EnrichAsync(
                context,
                cancellationToken);
        }
        ClipEditorialPriorTitleExclusion[] titleExclusions =
            ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                    existingProjectTitles)
                .Where(title =>
                    !title.Equals(
                        context.GameContext.AudienceGameHashtag,
                        StringComparison.OrdinalIgnoreCase) &&
                    !title.Equals(
                        context.GameContext.GameHashtag,
                        StringComparison.OrdinalIgnoreCase))
                .Select(title => ClipEditorialPriorTitleExclusion.ForContext(
                    context,
                    title))
                .ToArray();
        ClipEditorialMetadataRequest request =
            new ClipEditorialMetadataRequest(
                    context,
                    _profileSource.Current,
                    attempt: 0,
                    hiddenMoment.EditorialPreference,
                    hiddenMoment.SourceMedia,
                    priorAcceptedTitleExclusions: titleExclusions)
                .WithVariantIntent(InitialVariantIntent(
                    hiddenMoment.ReviewOrder - 1));
        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await _generator.GenerateBatchAsync(
                [request],
                cancellationToken);
        if (drafts.Count != 1)
        {
            throw new InvalidDataException(
                "Accepted Hidden Moment metadata did not preserve its single requested clip.");
        }
        ClipEditorialMetadataDraft draft = drafts[0];
        return hiddenMoment.WithEditorialMetadata(context, draft);
    }

    private static GenerationMomentCandidate? CreateRetainedHiddenCandidate(
        GenerationHiddenMoment hiddenMoment)
    {
        if (!hiddenMoment.TryGetGenerationProvenance(
                out AnalyzedGenerationSource? analyzedSource,
                out MomentCandidate? candidate))
        {
            return null;
        }

        return new GenerationMomentCandidate(
            hiddenMoment.Id,
            analyzedSource!,
            candidate!,
            hiddenMoment.SourceOrder,
            hiddenMoment.ReviewOrder,
            GenerationCandidateSelectionReason.HiddenMomentRecovery);
    }

    private static ClipEditorialContext BuildRetainedHiddenContext(
        GenerationHiddenMoment hiddenMoment,
        ClipEditorialContext retained,
        GenerationCandidateCaptionTrack? captions) =>
        new(
            hiddenMoment.Id,
            retained.SourceFullPath,
            retained.SourceLabel,
            hiddenMoment.SourceStart,
            hiddenMoment.SourceEnd,
            hiddenMoment.SourceMedia.Duration,
            hiddenMoment.FinalScore,
            retained.DeterministicReason,
            captions is null
                ? retained.Transcripts
                : RetainedCaptionEditorialTranscriptProjector.Project(
                    captions,
                    hiddenMoment.SourceStart,
                    hiddenMoment.SourceEnd),
            retained.Evidence,
            retained.GameContext,
            retained.GameKnowledge,
            retained.GameplayRegion,
            retained.VisualText);

    private static ClipEditorialContext BuildContext(
        GenerationMomentCandidate selected,
        GenerationCandidateCaptionTrack? captions,
        GenerationSourceGameContext? gameContext,
        GenerationVisualSemanticCandidateObservation? visualObservation)
    {
        string sourcePath = selected.AnalyzedSource.PreparedSource
            .Media.FullPath;
        ClipEditorialTranscriptContext[] transcripts = captions is null
            ? []
            : RetainedCaptionEditorialTranscriptProjector.Project(
                captions,
                selected.Candidate.Window.Start,
                selected.Candidate.Window.End);
        ClipEditorialEvidenceReference[] evidence =
            BuildEvidence(selected, gameContext, visualObservation);
        ClipEditorialGameContext editorialGame = gameContext is null
            ? new ClipEditorialGameContext(
                ClipEditorialGameContext.UnconfirmedGameName,
                ClipEditorialGameContext.UnconfirmedGameHashtag,
                contextNotes: null,
                ClipEditorialGameContextSource.SourcePathHint)
            : new ClipEditorialGameContext(
                gameContext.GameName,
                gameContext.GameHashtag,
                gameContext.ContextNotes,
                gameContext.Origin switch
                {
                    GenerationGameContextOrigin.SourcePathHint =>
                        ClipEditorialGameContextSource.SourcePathHint,
                    GenerationGameContextOrigin.ReusedUserMemory =>
                        ClipEditorialGameContextSource.ReusedUserMemory,
                    GenerationGameContextOrigin.UserConfirmed =>
                        ClipEditorialGameContextSource.UserConfirmed,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(gameContext),
                        gameContext.Origin,
                        "The game-context origin is not supported."),
                },
                gameContext.UseOpenGameKnowledge,
                gameContext.ConfirmedIdentity);

        return new ClipEditorialContext(
            selected.Id,
            sourcePath,
            BuildSourceLabel(sourcePath),
            selected.Candidate.Window.Start,
            selected.Candidate.Window.End,
            selected.AnalyzedSource.PreparedSource.Media.Duration,
            selected.FinalScore,
            selected.Candidate.Score.Components
                .OrderByDescending(
                    static component =>
                        component.SignedContribution)
                .Select(static component => component.Explanation)
                .FirstOrDefault() ??
                "Chosen based on the video scan.",
            transcripts,
            evidence,
            editorialGame,
            gameKnowledge: null,
            gameplayRegion: GameplayRegion(selected));
    }

    private static NormalizedRectangle GameplayRegion(
        GenerationMomentCandidate selected)
    {
        TimeSpan position = TimeSpan.FromTicks(
            selected.Candidate.Window.Start.Ticks +
            selected.Candidate.Window.Duration.Ticks / 2);
        CompositionPlan plan = selected.AnalyzedSource.CompositionPlan.Plan;
        if (position >= plan.SourceDuration)
        {
            position = plan.SourceDuration - TimeSpan.FromTicks(1);
        }
        CompositionRegion region =
            CompositionRegionSelector.FindPrimary(
                plan.GetLayoutAt(position),
                CompositionRegionRole.Gameplay) ??
            throw new InvalidDataException(
                "Editorial metadata requires the confirmed Gameplay region.");
        return region.Geometry;
    }

    private static GenerationVisualTextAnalysisRequest VisualTextRequest(
        ClipEditorialContext context,
        GenerationMomentCandidate selected)
    {
        IEnumerable<TimeSpan> priorityTimestamps = selected.Candidate.Anchors
            .Select(static anchor => anchor.Timestamp)
            .Append(selected.Candidate.EventNeighborhood.PeakTimestamp);
        if (selected.Candidate.Episode is not null)
        {
            priorityTimestamps = priorityTimestamps.Append(
                selected.Candidate.Episode.PrimaryPeakTimestamp);
        }
        return new GenerationVisualTextAnalysisRequest(
            context,
            selected.AnalyzedSource.PreparedSource.Media,
            priorityTimestamps);
    }

    private static VisualSemanticInputManifest? RetainedCandidateReview(
        GenerationVisualSemanticAnalysisResult? visualResult,
        GenerationVisualSemanticCandidateObservation? observation,
        MomentCandidateWindow selectedWindow)
    {
        if (visualResult is null || observation is null ||
            observation.ReviewedSourceStart != selectedWindow.Start ||
            observation.ReviewedSourceEnd != selectedWindow.End)
        {
            return null;
        }

        return visualResult.FindReviewVideo(observation.Candidate.Id);
    }

    private static ClipEditorialEvidenceReference[] BuildEvidence(
        GenerationMomentCandidate selected,
        GenerationSourceGameContext? gameContext,
        GenerationVisualSemanticCandidateObservation? visualObservation)
    {
        var evidence = new List<ClipEditorialEvidenceReference>
        {
            new(
                "source",
                ClipEditorialEvidenceKind.SourceIdentity,
                "The title and description retain the selected source identity and exact clip window."),
        };
        evidence.AddRange(
            selected.Candidate.Score.Components
                .Where(
                    static component =>
                        component.SignedContribution > 0)
                .OrderByDescending(
                    static component =>
                        component.SignedContribution)
                .ThenBy(static component => component.Code)
                .Take(5)
                .Select(
                    component =>
                        new ClipEditorialEvidenceReference(
                            $"moment-{component.Code}",
                            ClipEditorialEvidenceKind.DeterministicMoment,
                            component.Explanation)));
        if (gameContext is not null)
        {
            evidence.Add(new ClipEditorialEvidenceReference(
                "game-context",
                ClipEditorialEvidenceKind.UserGameContext,
                gameContext.Origin == GenerationGameContextOrigin.SourcePathHint
                    ? "An unconfirmed folder name was not used in the title or description."
                    : $"User-grounded game identity: {gameContext.GameName}."));
        }
        if (visualObservation is not null)
        {
            // Prompt 2.3 observations qualify and rank a candidate; their
            // compact evidence text is not descriptive audience-copy prose.
            evidence.AddRange(
                visualObservation.Observation.ObservedChanges.Select(
                    (change, index) => new ClipEditorialEvidenceReference(
                        $"visual-change-{index + 1}",
                        ClipEditorialEvidenceKind.CandidateQualification,
                        change.Description)));
            evidence.AddRange(
                visualObservation.Observation.EvidenceIntervals.Select(
                    interval => new ClipEditorialEvidenceReference(
                        $"visual-interval-{interval.Id}",
                        ClipEditorialEvidenceKind.CandidateQualification,
                        interval.Description)));
        }
        return evidence.ToArray();
    }

    private static string BuildSourceLabel(string sourceFullPath)
    {
        DirectoryInfo? parent =
            Directory.GetParent(sourceFullPath);
        if (parent is not null &&
            parent.Name.Equals(
                "Vertical",
                StringComparison.OrdinalIgnoreCase))
        {
            parent = parent.Parent;
        }

        string label = parent?.Name ??
            Path.GetFileNameWithoutExtension(sourceFullPath);
        return string.IsNullOrWhiteSpace(label)
            ? "Gameplay"
            : label.Trim();
    }

}
