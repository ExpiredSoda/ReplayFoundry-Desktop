using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public sealed class GenerationVisualSemanticAnalysisService :
    IGenerationVisualSemanticAnalysisService
{
    private readonly IVisualSemanticEditorialProvider _provider;
    private readonly IVisualSemanticReviewVideoMaterializer _materializer;
    private readonly GenerationVisualSemanticSettings _settings;
    internal GenerationRecordingIndexService? RecordingIndex { get; init; }

    public Task<GenerationCandidateIntelligenceResult> IndexRecordingAsync(GenerationCandidateIntelligenceResult intelligence,
        IProgress<string>? progress, CancellationToken cancellationToken) => RecordingIndex?.AnalyzeAsync(intelligence, progress, cancellationToken)
            ?? Task.FromResult(intelligence);

    public GenerationVisualSemanticAnalysisService(
        IVisualSemanticEditorialProvider provider,
        IVisualSemanticReviewVideoMaterializer materializer,
        GenerationVisualSemanticSettings settings)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _materializer = materializer ??
            throw new ArgumentNullException(nameof(materializer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<GenerationVisualSemanticAnalysisResult> AnalyzeAsync(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateIntelligence);
        cancellationToken.ThrowIfCancellationRequested();
        int budget = ReviewBudget(candidateIntelligence);
        IReadOnlyList<CandidateSource> shortlist = CreateShortlist(candidateIntelligence, budget);
        return AnalyzeShortlistAsync(candidateIntelligence, shortlist, progress, false, cancellationToken);
    }

    public async Task<GenerationVisualSemanticAnalysisResult> ReviewPromotedAsync(
        GenerationCandidateIntelligenceResult baseline,
        IReadOnlyList<GenerationMomentCandidate> selected,
        GenerationVisualSemanticAnalysisResult previous,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(previous);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(previous.CandidateIntelligence, baseline))
            throw new ArgumentException("Promoted review requires the original pre-visual intelligence.", nameof(baseline));
        if (previous.Outcome != GenerationVisualSemanticOutcome.Completed || previous.SupplementalReviewAttempted)
            return previous;
        var owners = baseline.BaseMoments.Sources.SelectMany(source => source.Moments.Proposals.Select(candidate =>
            new { Candidate = candidate, source.AnalyzedSource })).ToDictionary(item => item.Candidate, item => item.AnalyzedSource);
        if (selected.Any(item => item is null || !owners.TryGetValue(item.Candidate, out var owner) ||
                !ReferenceEquals(owner, item.AnalyzedSource)))
            throw new ArgumentException("Promoted candidates must preserve the retained candidate and source identity.", nameof(selected));
        int remaining = Math.Max(0, _settings.MaximumCandidateCount - previous.Observations.Count);
        CandidateSource[] promoted = selected.Where(item => !previous.Observations.Any(observation =>
                ReferenceEquals(observation.Candidate, item.Candidate)))
            .DistinctBy(item => item.Candidate, ReferenceEqualityComparer.Instance)
            .Take(Math.Min(GenerationSemanticReviewBudgetPolicy.MaximumBatchSize, remaining))
            .Select(item => new CandidateSource(item.Candidate, owners[item.Candidate],
                item.Refinement?.RankingScore ?? item.Candidate.Score.RawComponentTotal, item.IsHumanPriority, true)).ToArray();
        if (promoted.Length == 0) return previous;
        GenerationVisualSemanticAnalysisResult supplemental = await AnalyzeShortlistAsync(baseline, promoted, progress, true, cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GenerationVisualSemanticAnalysisResult.Combine(previous, supplemental);
        }
        catch
        {
            supplemental.Dispose();
            throw;
        }
    }

    private int ReviewBudget(GenerationCandidateIntelligenceResult intelligence)
    {
        int budget = GenerationSemanticReviewBudgetPolicy.Resolve(intelligence.BaseMoments, _settings.MaximumCandidateCount);
        int mapped = intelligence.Refinements.Count(item => item.Components.Any(component =>
            component.Code == GenerationCandidateRefinementComponentCode.NeuralIndexCoverage && component.RawValue >= .8));
        // The full map already explored the recording; reserve close review for
        // the requested picks and a few challengers, including the existing exploration slots.
        if (intelligence.Refinements.Count > 0 && mapped >= intelligence.Refinements.Count * .9)
            budget = Math.Min(budget, Math.Clamp(intelligence.BaseMoments.Request.Setup.DesiredResultCount + 3, 8, GenerationSemanticReviewBudgetPolicy.MaximumCandidates));
        return budget;
    }

    private async Task<GenerationVisualSemanticAnalysisResult> AnalyzeShortlistAsync(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        IReadOnlyList<CandidateSource> shortlist,
        IProgress<GenerationVisualSemanticProgress>? progress,
        bool supplemental,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        if (shortlist.Count == 0)
        {
            return CreateFallbackResult(
                candidateIntelligence,
                elapsed.Elapsed,
                "No moment needed a closer picture check. Replay Foundry kept the original selections for you to review.",
                diagnosticDetails: null);
        }

        var materialized = new List<MaterializedVisualSemanticReviewVideo>();
        var prepared = new List<(CandidateSource Item, MaterializedVisualSemanticReviewVideo Video, VisualSemanticRequest Request)>();
        var observed = new List<GenerationVisualSemanticCandidateObservation>();
        var diagnostics = new List<GenerationVisualReviewDiagnostic>();
        var failedIds = new HashSet<string>(StringComparer.Ordinal);
        long? peakGpuBytes = null;
        bool ownershipTransferred = false;
        try
        {
            foreach (CandidateSource item in shortlist)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                try
                {
                    (TimeSpan start, TimeSpan end) = ReviewBounds(item.Candidate,
                        item.Source.PreparedSource.Media.Duration, _settings.VideoPolicy.MaximumReviewDuration);
                    progress?.Report(new GenerationVisualSemanticProgress(
                        GenerationVisualSemanticPhase.PreparingReviewVideo, "Preparing picture checks",
                        $"Preparing moment {prepared.Count + failedIds.Count + 1} of {shortlist.Count}.",
                        prepared.Count + failedIds.Count, shortlist.Count, isIndeterminate: true));
                    var video = await _materializer.MaterializeAsync(
                        new VisualSemanticReviewVideoMaterializationRequest(item.Candidate.Id,
                            item.Source.PreparedSource.Media, start, end, GameplayRegion(item.Source, start, end)), cancellationToken);
                    materialized.Add(video);
                    var request = CreateRequest(item, video,
                        candidateIntelligence.BaseMoments.Request.Settings.Options.OutputKind,
                        candidateIntelligence.Transcripts?.Sources.SingleOrDefault(source => source.SourceFullPath.Equals(
                            item.Source.PreparedSource.Media.FullPath, StringComparison.OrdinalIgnoreCase)));
                    prepared.Add((item, video, request));
                    diagnostics.Add(new(item.Candidate.Id, "Preparation", 1, true, timer.Elapsed.TotalSeconds, null));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    failedIds.Add(item.Candidate.Id);
                    diagnostics.Add(new(item.Candidate.Id, "Preparation", 1, false, timer.Elapsed.TotalSeconds, SafeDiagnostics(exception)));
                }
            }

            foreach (var chunk in prepared.Chunk(GenerationSemanticReviewBudgetPolicy.MaximumBatchSize))
            {
                var pending = chunk.ToArray();
                // Successful cases are never sent a second time. Only unresolved cases get one retry.
                for (int attempt = 1; attempt <= 2 && pending.Length > 0; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var timer = Stopwatch.StartNew();
                    progress?.Report(new GenerationVisualSemanticProgress(
                        GenerationVisualSemanticPhase.ReviewingCandidates,
                        attempt == 1 ? "Reading the moments" : "Retrying unfinished picture checks",
                        $"{observed.Count} of {shortlist.Count} moments checked.", observed.Count,
                        shortlist.Count, isIndeterminate: true));
                    try
                    {
                        var batch = new VisualSemanticBatchRequest(pending.Select(item => item.Request), _settings.VideoPolicy);
                        var result = await _provider.ObserveAsync(batch, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (result.PeakAllocatedGpuBytes is long peak) peakGpuBytes = Math.Max(peakGpuBytes ?? 0, peak);
                        foreach (var success in result.Results)
                        {
                            var owner = pending.Single(item => ReferenceEquals(item.Request, success.Request));
                            observed.Add(new(owner.Item.Candidate, owner.Item.Source,
                                owner.Video.Request.SourceStart, owner.Video.Request.SourceEnd,
                                owner.Video.Input.ReviewVideoSha256, success.Observation, success.CanonicalizationAudit, success.Elapsed, success.NeuralEditorialValue));
                            failedIds.Remove(owner.Item.Candidate.Id);
                            diagnostics.Add(new(owner.Item.Candidate.Id, "Inference", attempt, true, success.Elapsed.TotalSeconds, null));
                        }
                        foreach (var failure in result.Failures)
                        {
                            failedIds.Add(failure.Request.CandidateId);
                            diagnostics.Add(new(failure.Request.CandidateId, failure.Stage, attempt, false,
                                failure.Elapsed.TotalSeconds, failure.ErrorCode));
                        }
                        pending = pending.Where(item => result.Failures.Any(failure => failure.CanRetry && ReferenceEquals(failure.Request, item.Request))).ToArray();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception)
                    {
                        foreach (var item in pending)
                        {
                            failedIds.Add(item.Item.Candidate.Id);
                            diagnostics.Add(new(item.Item.Candidate.Id, "Batch", attempt, false,
                                timer.Elapsed.TotalSeconds, SafeDiagnostics(exception)));
                        }
                    }
                }
            }
            string? reason = failedIds.Count == 0 ? null :
                $"{failedIds.Count} of {shortlist.Count} picture checks could not finish. Successfully checked moments are kept; unchecked moments need your review.";
            string details = System.Text.Json.JsonSerializer.Serialize(diagnostics);
            GenerationVisualReviewDiagnostics.Save(_provider.Identity, diagnostics, elapsed.Elapsed, observed.Count, shortlist.Count);
            progress?.Report(new GenerationVisualSemanticProgress(GenerationVisualSemanticPhase.Completed,
                reason is null ? "Picture checks complete" : "Some picture checks need review",
                reason ?? $"Checked {observed.Count} {(observed.Count == 1 ? "moment" : "moments")}.", observed.Count, shortlist.Count,
                isIndeterminate: false, overallPercentage: 100));
            if (observed.Count == 0)
                return CreateFallbackResult(candidateIntelligence, elapsed.Elapsed,
                    reason ?? "No picture checks completed. Review the suggested moments before using them.", details);

            var acceptedMedia = materialized.Where(video => observed.Any(item =>
                item.Candidate.Id == video.Request.CandidateId)).ToArray();
            foreach (var unused in materialized.Except(acceptedMedia)) unused.Dispose();
            var output = new GenerationVisualSemanticAnalysisResult(candidateIntelligence, _provider.Identity,
                observed, elapsed.Elapsed, peakGpuBytes, acceptedMedia, fallbackReason: reason, diagnosticDetails: details);
            ownershipTransferred = true;
            return output;
        }
        finally
        {
            if (!ownershipTransferred)
                foreach (var video in materialized.AsEnumerable().Reverse()) video.Dispose();
        }
    }
    private GenerationVisualSemanticAnalysisResult CreateFallbackResult(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        TimeSpan elapsed,
        string reason,
        string? diagnosticDetails) =>
        new(
            candidateIntelligence,
            _provider.Identity,
            observations: [],
            elapsed,
            peakAllocatedGpuBytes: null,
            reviewVideos: [],
            GenerationVisualSemanticOutcome.RetainedDeterministicCandidates,
            reason,
            diagnosticDetails);

    private static string SafeDiagnostics(Exception exception)
    {
        string failureType = exception.GetType().Name;
        return exception switch
        {
            Qwen3VlQualifiedCaseException failure => $"failureType={failureType}; errorCode={failure.ErrorCode}; stage={failure.Stage}; exitCode={failure.ExitCode}",
            Qwen3VlInferenceException { HostFailure: { } failure } =>
                $"failureType={failureType}; errorCode={failure.Failure.ErrorCode}; stage={failure.Stage}",
            Qwen3VlInferenceException =>
                $"failureType={failureType}; reason=qualified visual provider unavailable",
            VisualSemanticReviewVideoMaterializationException =>
                $"failureType={failureType}; reason=bounded review materialization unavailable",
            _ =>
                $"failureType={failureType}; reason=focused visual review unavailable",
        };
    }

    internal static IReadOnlyList<CandidateSource> CreateShortlist(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        int maximumCandidateCount)
    {
        if (maximumCandidateCount is < 1 or > GenerationSemanticReviewBudgetPolicy.MaximumCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCandidateCount));
        }
        var refinementByCandidate = candidateIntelligence.Refinements.ToDictionary(value => value.Candidate);
        var neuralCandidates = candidateIntelligence.Refinements
            .Where(value => value.Components.Any(component => component.Code is
                GenerationCandidateRefinementComponentCode.NeuralTimelineValue or
                GenerationCandidateRefinementComponentCode.NeuralPersonalValue))
            .Select(value => value.Candidate).ToHashSet();
        var candidates = new List<CandidateSource>();
        foreach (GenerationMomentCandidate selected in
                 candidateIntelligence.RefinedMoments.SelectedCandidates)
        {
            candidates.Add(new CandidateSource(
                selected.Candidate,
                selected.AnalyzedSource,
                refinementByCandidate.GetValueOrDefault(selected.Candidate)?.RankingScore ?? selected.Refinement?.RankingScore ??
                    selected.Candidate.Score.RawComponentTotal,
                selected.IsHumanPriority,
                IsSelected: true));
        }

        foreach (GenerationCandidateRefinement refinement in
                 candidateIntelligence.Refinements
                     .OrderByDescending(static value => value.RankingScore)
                     .ThenBy(static value => value.Candidate.Window.Start)
                     .ThenBy(static value => value.Candidate.Id, StringComparer.Ordinal))
        {
            if ((!neuralCandidates.Contains(refinement.Candidate) &&
                refinement.Candidate.Disposition is MomentCandidateDisposition.RejectedBlack or
                    MomentCandidateDisposition.RejectedFreeze) ||
                candidates.Any(value =>
                    ReferenceEquals(value.Candidate, refinement.Candidate)))
            {
                continue;
            }
            GenerationSourceMomentResult source =
                candidateIntelligence.BaseMoments.Sources.Single(value =>
                    value.Moments.Proposals.Any(proposal =>
                        ReferenceEquals(proposal, refinement.Candidate)));
            candidates.Add(new CandidateSource(
                refinement.Candidate,
                source.AnalyzedSource,
                refinement.RankingScore,
                IsHumanPriority: false,
                IsSelected: false));
        }

        CandidateSource[] ordered = candidates
            .OrderByDescending(static value => value.IsHumanPriority)
            .ThenByDescending(static value => value.IsSelected)
            .ThenByDescending(static value => value.Score)
            .ThenByDescending(static value =>
                GenerationGameplayEventCoveragePolicy.Strength(
                    value.Candidate))
            .ThenBy(static value => value.Source.PreparedSource.Media.FullPath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.Candidate.Window.Start)
            .ThenBy(static value => value.Candidate.Id, StringComparer.Ordinal)
            .ToArray();
        if (!candidateIntelligence.Refinements.Any(value => value.Components.Any(component =>
                component.Code == GenerationCandidateRefinementComponentCode.NeuralPersonalValue)))
        {
            var nominated = candidates.Select(candidate => new
                {
                    Candidate = candidate,
                    Priority = refinementByCandidate.GetValueOrDefault(candidate.Candidate)?.Components.FirstOrDefault(component =>
                        component.Code == GenerationCandidateRefinementComponentCode.NeuralReviewPriority),
                    Coverage = refinementByCandidate.GetValueOrDefault(candidate.Candidate)?.Components.FirstOrDefault(component =>
                        component.Code == GenerationCandidateRefinementComponentCode.NeuralRegionCoverage)?.RawValue ?? 0
                }).Where(item => item.Priority is not null && item.Priority.EvidenceReferences.Count > 0)
                .GroupBy(item => item.Candidate.Source.PreparedSource.Media.FullPath + "\0" + item.Priority!.EvidenceReferences[0], StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.First().Priority!.RawValue)
                .Select(group => group.OrderByDescending(item => item.Coverage).ThenByDescending(item => item.Candidate.Score)
                    .ThenBy(item => item.Candidate.Candidate.Window.Start).First().Candidate).ToArray();
            if (nominated.Length > 0)
                return ordered.Where(item => item.IsHumanPriority).Concat(nominated).Concat(ordered)
                    .DistinctBy(item => item.Candidate, ReferenceEqualityComparer.Instance).Take(maximumCandidateCount).ToArray();
        }
        if (neuralCandidates.Count > 0)
        {
            // A model-reviewed quiet or dark scene may be the strongest moment.
            // Preserve explicit user priorities, then spend the close-review budget
            // according to the model's utility instead of a gameplay reservation.
            return ordered.OrderByDescending(value => value.IsHumanPriority)
                .ThenByDescending(value => value.Score)
                .ThenBy(value => value.Source.PreparedSource.Media.FullPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Candidate.Window.Start)
                .ThenBy(value => value.Candidate.Id, StringComparer.Ordinal)
                .Take(maximumCandidateCount).ToArray();
        }
        var shortlist = ordered
            .Where(static value => value.IsSelected)
            .Take(maximumCandidateCount)
            .ToList();

        if (candidateIntelligence.BaseMoments.Request.Setup.ContentEmphasis !=
            ContentEmphasis.CommentaryFocused)
        {
            CandidateSource? gameplayChallenger = ordered
                .Where(value =>
                    !shortlist.Contains(value) &&
                    GenerationGameplayEventCoveragePolicy
                        .IsDeterministicGameplayEvent(value.Candidate))
                .OrderByDescending(static value => !value.IsSelected)
                .ThenByDescending(static value =>
                    GenerationGameplayEventCoveragePolicy.Strength(
                        value.Candidate))
                .ThenByDescending(static value => value.Score)
                .FirstOrDefault();
            if (gameplayChallenger is not null)
            {
                if (shortlist.Count < maximumCandidateCount)
                {
                    shortlist.Add(gameplayChallenger);
                }
                else
                {
                    int replacementIndex = shortlist.FindLastIndex(
                        static value =>
                            !value.IsHumanPriority &&
                            !GenerationGameplayEventCoveragePolicy
                                .IsDeterministicGameplayEvent(
                                    value.Candidate));
                    if (replacementIndex < 0)
                    {
                        replacementIndex = shortlist.FindLastIndex(
                            static value => !value.IsHumanPriority);
                    }
                    if (replacementIndex >= 0)
                    {
                        shortlist[replacementIndex] = gameplayChallenger;
                    }
                }
            }
        }

        // Represent each source before spending the remaining budget on a
        // dense cluster of high scores from just one recording.
        var reserved = new HashSet<CandidateSource>();
        foreach (CandidateSource representative in ordered
                     .GroupBy(static value => value.Source)
                     .Select(static group => group.First()))
        {
            Reserve(representative);
        }
        // Review unseen parts of a recording even if their motion/audio score
        // is low. These windows cannot be selected without qualified evidence.
        CandidateSource[][] explorationBySource = ordered
            .Where(value => value.Candidate.ConstructionReason ==
                MomentCandidateConstructionReason.SemanticExploration ||
                GenerationSemanticRetrieval.Priority(candidateIntelligence.Transcripts?.SemanticRetrieval,
                    value.Source.PreparedSource.Media.FullPath, value.Candidate) > 0)
            .GroupBy(static value => value.Source)
            .Select(group => group.OrderByDescending(value =>
                GenerationSemanticRetrieval.Priority(candidateIntelligence.Transcripts?.SemanticRetrieval,
                    value.Source.PreparedSource.Media.FullPath, value.Candidate))
                .ThenByDescending(static value => value.Score)
                .ThenBy(static value =>
                value.Candidate.Window.Start).ToArray())
            .ToArray();
        int explorationBudget = Math.Max(1, maximumCandidateCount / 3);
        int explored = 0;
        for (int pass = 0; explored < explorationBudget &&
             explorationBySource.Any(group => pass < group.Length); pass++)
        {
            foreach (CandidateSource[] source in explorationBySource)
            {
                if (pass < source.Length && explored < explorationBudget && Reserve(source[pass]))
                {
                    explored++;
                }
            }
        }

        foreach (CandidateSource candidate in ordered)
        {
            if (shortlist.Count >= maximumCandidateCount)
            {
                break;
            }
            if (!shortlist.Contains(candidate))
            {
                shortlist.Add(candidate);
            }
        }

        return shortlist;

        bool Reserve(CandidateSource candidate)
        {
            if (shortlist.Contains(candidate))
            {
                reserved.Add(candidate);
                return true;
            }
            if (shortlist.Count < maximumCandidateCount)
            {
                shortlist.Add(candidate);
                reserved.Add(candidate);
                return true;
            }
            int replacement = shortlist.FindLastIndex(value =>
                !value.IsHumanPriority && !reserved.Contains(value) &&
                !GenerationGameplayEventCoveragePolicy.IsDeterministicGameplayEvent(value.Candidate));
            if (replacement < 0)
            {
                return false;
            }
            shortlist[replacement] = candidate;
            reserved.Add(candidate);
            return true;
        }
    }

    internal static (TimeSpan Start, TimeSpan End) ReviewBounds(
        MomentCandidate candidate,
        TimeSpan sourceDuration,
        TimeSpan maximumDuration)
    {
        if (candidate.Window.Duration <= maximumDuration)
        {
            return (candidate.Window.Start, candidate.Window.End);
        }

        TimeSpan focus = candidate.Episode?.PrimaryPeakTimestamp ??
            TimeSpan.FromTicks(
                candidate.Window.Start.Ticks +
                candidate.Window.Duration.Ticks / 2);
        TimeSpan start = focus - TimeSpan.FromTicks(maximumDuration.Ticks / 2);
        if (start < candidate.Window.Start)
        {
            start = candidate.Window.Start;
        }
        TimeSpan end = start + maximumDuration;
        if (end > candidate.Window.End)
        {
            end = candidate.Window.End;
            start = end - maximumDuration;
        }
        if (end > sourceDuration)
        {
            end = sourceDuration;
            start = end - maximumDuration;
        }
        return (start, end);
    }

    private VisualSemanticRequest CreateRequest(
        CandidateSource item,
        MaterializedVisualSemanticReviewVideo video,
        MomentOutputKind outputKind,
        GenerationSourceTranscript? sourceTranscript)
    {
        TimeSpan duration = video.Request.Duration;
        VisualSemanticTranscriptContext transcript = GenerationVisualTranscriptContextBuilder.Build(
            sourceTranscript, video.Request.SourceStart, video.Request.SourceEnd);
        string caseHash = Hash(
            item.Source.PreparedSource.Media.FullPath.ToUpperInvariant(),
            item.Candidate.Id,
            video.Request.SourceStart.Ticks.ToString(CultureInfo.InvariantCulture),
            video.Request.SourceEnd.Ticks.ToString(CultureInfo.InvariantCulture),
            video.Input.ReviewVideoSha256,
            Hash(string.Join("|", transcript.Spans.Select(static span =>
                $"{span.Id}:{span.ReviewRelativeStart.Ticks}:{span.ReviewRelativeEnd.Ticks}:{span.Text}"))),
            _settings.Prompt.Sha256,
            _settings.Model.ManifestSha256);
        return new VisualSemanticRequest(
            $"production-{caseHash[..20].ToLowerInvariant()}",
            caseHash,
            $"source-{Hash(item.Source.PreparedSource.Media.FullPath.ToUpperInvariant())[..20].ToLowerInvariant()}",
            video.Input,
            item.Candidate.Id,
            outputKind,
            TimeSpan.Zero,
            duration,
            // The provider reads the already-trimmed review artifact, whose
            // local timeline begins at zero. The original-source interval is
            // retained by GenerationVisualSemanticCandidateObservation.
            TimeSpan.Zero,
            Composition(item.Source, video.Request.SourceStart, video.Request.SourceEnd),
            transcript,
            transcript.TranscriptSupplied
                ? VisualSemanticDeterministicSummaryBuilder.Build(new(
                    duration,
                    item.Candidate.Anchors.Count(static anchor => anchor.Kind is MomentAnchorKind.GameplaySceneBoundary or MomentAnchorKind.GameplaySceneCluster),
                    item.Candidate.Anchors.Count(static anchor => anchor.Kind == MomentAnchorKind.GameplayActivityBurst),
                    item.Candidate.Anchors.Count(static anchor => anchor.Kind is MomentAnchorKind.AudioNovelty or MomentAnchorKind.AudioReentry),
                    item.Candidate.Anchors.Count(static anchor => anchor.Kind is MomentAnchorKind.PresenterAudioAgreement or MomentAnchorKind.PresenterGatedSupport),
                    item.Candidate.FullFrameBlackOverlapRatio > 0
                        ? item.Candidate.FullFrameFreezeOverlapRatio > 0
                            ? VisualSemanticIntegrityStatus.FullFrameBlackAndFrozen : VisualSemanticIntegrityStatus.FullFrameBlack
                        : item.Candidate.FullFrameFreezeOverlapRatio > 0
                            ? VisualSemanticIntegrityStatus.FullFrameFrozen : VisualSemanticIntegrityStatus.Clear,
                    null, null, null, outputKind,
                    Composition(item.Source, video.Request.SourceStart, video.Request.SourceEnd).Regions
                        .Where(static region => region.RoleSource == CompositionValueSource.UserConfirmed)
                        .Select(static region => region.Role).ToArray()))
                : null,
            _settings.Prompt,
            _settings.Model);
    }

    private static VisualSemanticCompositionMetadata Composition(
        AnalyzedGenerationSource source,
        TimeSpan start,
        TimeSpan end)
    {
        CompositionLayoutInterval layout = ReviewLayout(source, start, end);
        VisualSemanticCompositionRegion[] regions = new[]
            {
                CompositionRegionSelector.FindPrimary(
                    layout,
                    CompositionRegionRole.Gameplay),
                CompositionRegionSelector.FindPrimary(
                    layout,
                    CompositionRegionRole.Presenter),
            }
            .Where(static region => region is not null)
            .Select(static region => new VisualSemanticCompositionRegion(
                region!.Id,
                region.Role,
                region.Geometry,
                region.GeometrySource,
                region.RoleSource))
            .ToArray();
        double ratio = EffectiveDisplayGeometryCalculator.Calculate(
            source.PreparedSource.Media.PrimaryVideoStream).DisplayAspectRatio;
        string description = ratio < 0.95
            ? "vertical"
            : ratio > 1.05
                ? "landscape"
                : "square";
        return new VisualSemanticCompositionMetadata(
            description,
            source.CompositionPlan.Plan.CoordinateSpace,
            regions);
    }

    private static NormalizedRectangle GameplayRegion(
        AnalyzedGenerationSource source,
        TimeSpan start,
        TimeSpan end) =>
        CompositionRegionSelector.FindPrimary(
            ReviewLayout(source, start, end),
            CompositionRegionRole.Gameplay)!.Geometry;

    private static CompositionLayoutInterval ReviewLayout(
        AnalyzedGenerationSource source,
        TimeSpan start,
        TimeSpan end)
    {
        TimeSpan position = TimeSpan.FromTicks(
            start.Ticks + (end - start).Ticks / 2);
        if (position >= source.CompositionPlan.Plan.SourceDuration)
        {
            position = source.CompositionPlan.Plan.SourceDuration -
                TimeSpan.FromTicks(1);
        }
        return source.CompositionPlan.Plan.GetLayoutAt(position);
    }

    private static string Hash(params string[] values)
    {
        string canonical = string.Join("\u001f", values);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical)));
    }

    internal sealed record CandidateSource(
        MomentCandidate Candidate,
        AnalyzedGenerationSource Source,
        double Score,
        bool IsHumanPriority,
        bool IsSelected);
}
