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

    public async Task<GenerationVisualSemanticAnalysisResult> AnalyzeAsync(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateIntelligence);
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        IReadOnlyList<CandidateSource> shortlist = CreateShortlist(
            candidateIntelligence,
            _settings.MaximumCandidateCount);
        if (shortlist.Count == 0)
        {
            return CreateFallbackResult(
                candidateIntelligence,
                elapsed.Elapsed,
                "No moment needed a closer picture check. Replay Foundry kept the original selections for you to review.",
                diagnosticDetails: null);
        }

        var materialized = new List<MaterializedVisualSemanticReviewVideo>();
        bool ownershipTransferred = false;
        try
        {
            for (int index = 0; index < shortlist.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CandidateSource item = shortlist[index];
                (TimeSpan start, TimeSpan end) = ReviewBounds(
                    item.Candidate,
                    item.Source.PreparedSource.Media.Duration,
                    _settings.VideoPolicy.MaximumReviewDuration);
                progress?.Report(new GenerationVisualSemanticProgress(
                    GenerationVisualSemanticPhase.PreparingReviewVideo,
                    "Framing the best moments",
                    $"Preparing moment {index + 1} of {shortlist.Count} for a close visual read.",
                    index,
                    shortlist.Count,
                    isIndeterminate: true));
                materialized.Add(await _materializer.MaterializeAsync(
                    new VisualSemanticReviewVideoMaterializationRequest(
                        item.Candidate.Id,
                        item.Source.PreparedSource.Media,
                        start,
                        end,
                        GameplayRegion(item.Source, start, end)),
                    cancellationToken));
            }

            VisualSemanticRequest[] requests = materialized
                .Select((video, index) => CreateRequest(
                    shortlist[index],
                    video,
                    candidateIntelligence.BaseMoments.Request.Settings.Options.OutputKind))
                .ToArray();
            var batch = new VisualSemanticBatchRequest(
                requests,
                _settings.VideoPolicy);
            progress?.Report(new GenerationVisualSemanticProgress(
                GenerationVisualSemanticPhase.ReviewingCandidates,
                "Reading the strongest moments",
                $"Comparing {shortlist.Count} promising moments to understand what visibly happened.",
                0,
                shortlist.Count,
                isIndeterminate: true));
            VisualSemanticEditorialBatchResult providerResult =
                await _provider.ObserveAsync(batch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            GenerationVisualSemanticCandidateObservation[] observations =
                providerResult.Results.Select((result, index) =>
                    new GenerationVisualSemanticCandidateObservation(
                        shortlist[index].Candidate,
                        shortlist[index].Source,
                        materialized[index].Request.SourceStart,
                        materialized[index].Request.SourceEnd,
                        materialized[index].Input.ReviewVideoSha256,
                        result.Observation,
                        result.CanonicalizationAudit,
                        result.Elapsed)).ToArray();
            progress?.Report(new GenerationVisualSemanticProgress(
                GenerationVisualSemanticPhase.Completed,
                "Picture check complete",
                $"Checked {observations.Length} promising moments without rereading the full videos.",
                observations.Length,
                observations.Length,
                isIndeterminate: false,
                overallPercentage: 100));
            var result = new GenerationVisualSemanticAnalysisResult(
                candidateIntelligence,
                _provider.Identity,
                observations,
                providerResult.Elapsed,
                providerResult.PeakAllocatedGpuBytes,
                materialized);
            ownershipTransferred = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string diagnostics = SafeDiagnostics(exception);
            progress?.Report(new GenerationVisualSemanticProgress(
                GenerationVisualSemanticPhase.Completed,
                "Moment selection kept for review",
                "The closer picture check was unavailable, so Replay Foundry kept the original moments and continued.",
                shortlist.Count,
                shortlist.Count,
                isIndeterminate: false,
                overallPercentage: 100));
            return CreateFallbackResult(
                candidateIntelligence,
                elapsed.Elapsed,
                "The closer picture check was unavailable. Replay Foundry kept the original moment selection so clip finding could continue.",
                diagnostics);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                foreach (MaterializedVisualSemanticReviewVideo video in
                         materialized.AsEnumerable().Reverse())
                {
                    video.Dispose();
                }
            }
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
        var candidates = new List<CandidateSource>();
        foreach (GenerationMomentCandidate selected in
                 candidateIntelligence.RefinedMoments.SelectedCandidates)
        {
            candidates.Add(new CandidateSource(
                selected.Candidate,
                selected.AnalyzedSource,
                selected.Refinement?.RankingScore ??
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
            if (candidates.Any(value =>
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
        MomentOutputKind outputKind)
    {
        TimeSpan duration = video.Request.Duration;
        string caseHash = Hash(
            item.Source.PreparedSource.Media.FullPath.ToUpperInvariant(),
            item.Candidate.Id,
            video.Request.SourceStart.Ticks.ToString(CultureInfo.InvariantCulture),
            video.Request.SourceEnd.Ticks.ToString(CultureInfo.InvariantCulture),
            video.Input.ReviewVideoSha256,
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
            new VisualSemanticTranscriptContext(
                VisualSemanticTranscriptContextPolicy.VisualOnlyV1,
                null,
                [],
                "Transcript context was not supplied for candidate ranking; visual observations remain bounded to sampled frames."),
            null,
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
