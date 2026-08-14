using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Captions;
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
            Current.VoicePerspective);
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
        CancellationToken cancellationToken);
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
            moments.Request.Setup.AnalysisDepth ==
            GenerationAnalysisDepth.Thorough
                ? ClipEditorialGenerationPreference.AiRequired
                : ClipEditorialGenerationPreference.HeuristicOnly;
        var prepared =
            new List<(GenerationMomentCandidate Candidate, ClipEditorialContext Context, ClipEditorialMetadataRequest Request)>(
                moments.SelectedCandidates.Count);
        foreach (GenerationMomentCandidate candidate in
                 moments.SelectedCandidates)
        {
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
            if (_gameKnowledge is not null)
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
                        reviewVideo: RetainedFocusedReview(
                            visualResult,
                            visualObservation),
                        reviewFocusSourceTimestamp:
                            GenerationEditorialReviewFocus.Resolve(
                                candidate.Candidate,
                                visualObservation))));
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hiddenMoment);
        if (captions is not null &&
            !captions.Candidate.Id.Equals(
                hiddenMoment.Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Accepted Hidden Moment captions must belong to the same candidate.",
                nameof(captions));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var selected = new GenerationMomentCandidate(
            hiddenMoment.Id,
            hiddenMoment.AnalyzedSource,
            hiddenMoment.Candidate,
            hiddenMoment.SourceOrder,
            hiddenMoment.ReviewOrder,
            GenerationCandidateSelectionReason.HiddenMomentRecovery);
        ClipEditorialContext context = hiddenMoment.EditorialContext is
        { } retained
                ? new ClipEditorialContext(
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
                        : BuildTranscriptContexts(captions),
                    retained.Evidence,
                    retained.GameContext,
                    retained.GameKnowledge,
                    retained.GameplayRegion,
                    retained.VisualText)
                : BuildContext(
                    selected,
                    captions,
                    gameContext: null,
                    visualObservation: null);
        if (_visualText is not null && context.VisualText is null)
        {
            context = await _visualText.EnrichAsync(
                VisualTextRequest(context, selected),
                cancellationToken);
        }
        if (_gameKnowledge is not null)
        {
            context = await _gameKnowledge.EnrichAsync(
                context,
                cancellationToken);
        }
        ClipEditorialMetadataDraft draft =
            await _generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    _profileSource.Current,
                    attempt: 0,
                    hiddenMoment.EditorialPreference,
                    hiddenMoment.SourceMedia),
                cancellationToken);
        return hiddenMoment.WithEditorialMetadata(context, draft);
    }

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
            : BuildTranscriptContexts(captions);
        ClipEditorialEvidenceReference[] evidence =
            BuildEvidence(selected, gameContext, visualObservation);
        ClipEditorialGameContext editorialGame = gameContext is null
            ? new ClipEditorialGameContext(
                BuildSourceLabel(sourcePath),
                BuildGameHashtag(BuildSourceLabel(sourcePath)),
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
                gameContext.UseOpenGameKnowledge);

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
                "Selected from deterministic evidence.",
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

    private static VisualSemanticInputManifest? RetainedFocusedReview(
        GenerationVisualSemanticAnalysisResult? visualResult,
        GenerationVisualSemanticCandidateObservation? observation)
    {
        if (visualResult is null || observation is null ||
            observation.ReviewedSourceEnd - observation.ReviewedSourceStart >
                ClipEditorialMetadataGenerationService
                    .MaximumFocusedReviewDuration)
        {
            return null;
        }

        return visualResult.FindReviewVideo(observation.Candidate.Id);
    }

    private static ClipEditorialTranscriptContext[]
        BuildTranscriptContexts(
            GenerationCandidateCaptionTrack captions)
    {
        string text = string.Join(
            " ",
            captions.Segments
                .Where(
                    segment =>
                        segment.AbsoluteSourceEnd >
                            captions.Candidate.Candidate.Window.Start &&
                        segment.AbsoluteSourceStart <
                            captions.Candidate.Candidate.Window.End)
                .Select(static segment => segment.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        if (text.Length >
            ClipEditorialTranscriptContext.MaximumTextLength)
        {
            text = text[..ClipEditorialTranscriptContext.MaximumTextLength];
        }

        return
        [
            new ClipEditorialTranscriptContext(
                captions.SourceSelection.AbsoluteAudioStreamIndex,
                MapRole(captions.SourceSelection.ContentRole),
                text,
                captions.IsUserEdited
                    ? ClipEditorialTranscriptAuthority.UserCorrected
                    : ClipEditorialTranscriptAuthority.AutomaticUnreviewed),
        ];
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
                    ? $"Folder-name game hint: {gameContext.GameName}."
                    : $"User-grounded game identity: {gameContext.GameName}."));
        }
        if (visualObservation is not null)
        {
            evidence.AddRange(
                visualObservation.Observation.ObservedChanges.Select(
                    (change, index) => new ClipEditorialEvidenceReference(
                        $"visual-change-{index + 1}",
                        ClipEditorialEvidenceKind.VisualObservation,
                        change.Description)));
            evidence.AddRange(
                visualObservation.Observation.EvidenceIntervals.Select(
                    interval => new ClipEditorialEvidenceReference(
                        $"visual-interval-{interval.Id}",
                        ClipEditorialEvidenceKind.VisualObservation,
                        interval.Description)));
        }
        return evidence.ToArray();
    }

    private static AudioContentRoleAssignment MapRole(
        CaptionAudioContentRole role) =>
        role switch
        {
            CaptionAudioContentRole.CreatorCommentary =>
                new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.GameDialogue =>
                new AudioContentRoleAssignment(
                    AudioContentRole.GameDialogue,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.MixedSpeech =>
                new AudioContentRoleAssignment(
                    AudioContentRole.MixedSpeech,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.OtherKnownSpeech =>
                AudioContentRoleAssignment.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

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

    private static string BuildGameHashtag(string gameName)
    {
        string value = string.Concat(gameName.Where(char.IsLetterOrDigit));
        return value.Length == 0 ? "#Gameplay" : "#" + value;
    }
}
