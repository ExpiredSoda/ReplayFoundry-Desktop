using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public sealed class ClipEditorialMetadataGenerationService :
    IClipEditorialMetadataGenerationService
{
    private const int MaximumNoveltyRetryCount = 2;

    private readonly IClipEditorialMetadataGenerator _heuristic;
    private readonly IClipEditorialMetadataGenerator? _ai;
    private readonly string? _aiUnavailableReason;
    private readonly IVisualSemanticReviewVideoMaterializer?
        _reviewVideoMaterializer;
    private readonly IClipEditorialSceneContextReviewer? _sceneReviewer;

    public ClipEditorialMetadataGenerationService(
        IClipEditorialMetadataGenerator heuristic,
        IClipEditorialMetadataGenerator? ai = null,
        IVisualSemanticReviewVideoMaterializer? reviewVideoMaterializer = null,
        string? aiUnavailableReason = null,
        IClipEditorialSceneContextReviewer? sceneReviewer = null)
    {
        _heuristic = heuristic ??
            throw new ArgumentNullException(nameof(heuristic));
        if (!_heuristic.IsAvailable)
        {
            throw new ArgumentException(
                "The deterministic editorial generator must always be available.",
                nameof(heuristic));
        }

        _ai = ai;
        _aiUnavailableReason = string.IsNullOrWhiteSpace(aiUnavailableReason)
            ? null : aiUnavailableReason.Trim();
        _reviewVideoMaterializer = reviewVideoMaterializer;
        _sceneReviewer = sceneReviewer;
    }

    public bool IsAiAvailable =>
        _ai?.IsAvailable == true &&
        (_ai is not IClipEditorialVisualMetadataGenerator ||
         _reviewVideoMaterializer is not null);

    public string? AiUnavailableReason =>
        IsAiAvailable ? null : _aiUnavailableReason;

    public async Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Preference ==
            ClipEditorialGenerationPreference.HeuristicOnly)
        {
            return await _heuristic.GenerateAsync(request, cancellationToken);
        }

        if (_ai is IClipEditorialMetadataBatchGenerator)
        {
            IReadOnlyList<ClipEditorialMetadataDraft> drafts =
                await GenerateBatchAsync([request], cancellationToken);
            if (drafts.Count != 1)
            {
                throw new InvalidDataException(
                    "Single-clip editorial generation did not preserve its requested clip.");
            }
            return drafts[0];
        }

        return await GenerateSingleAiProviderAsync(
            request,
            cancellationToken);
    }

    private async Task<ClipEditorialMetadataDraft>
        GenerateSingleAiProviderAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureAiAvailable(request);

        MaterializedVisualSemanticReviewVideo? reviewVideo = null;
        try
        {
            ClipEditorialMetadataRequest effectiveRequest = request;
            if (_ai is IClipEditorialVisualMetadataGenerator &&
                request.ReviewVideo is null)
            {
                reviewVideo = await MaterializeReviewVideoAsync(
                    effectiveRequest,
                    cancellationToken);
                effectiveRequest = request.WithReviewVideo(reviewVideo.Input);
            }

            if (_sceneReviewer is not null)
            {
                var reviewed = await _sceneReviewer.ReviewAsync([effectiveRequest], cancellationToken);
                if (reviewed.Count != 1) throw new InvalidDataException("Scene review did not preserve the requested clip.");
                effectiveRequest = reviewed[0];
            }

            ClipEditorialMetadataDraft aiDraft =
                await _ai!.GenerateAsync(effectiveRequest, cancellationToken);
            if (aiDraft is null)
            {
                throw new ClipEditorialAiGenerationException(
                    ClipEditorialAiFailureKind.IncompleteResult,
                    "Local AI did not return a title and description for this clip.",
                    request.Context.CandidateId);
            }

            ValidateAiDraftPostcondition(aiDraft, request);

            if (HeuristicAudienceCopyPolicy.ContainsInternalProcessText(
                    aiDraft.Title,
                    DescriptionForEditorialReview(aiDraft, request)))
            {
                throw new ClipEditorialAiGenerationException(
                    ClipEditorialAiFailureKind.UnsafeOutput,
                    "Local AI returned internal process wording instead of " +
                    "viewer-facing copy. Replay Foundry did not keep it.",
                    request.Context.CandidateId);
            }

            return aiDraft;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ClipEditorialAiGenerationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ClipEditorialAiGenerationException(
                ClipEditorialAiFailureKind.ProviderFailed,
                BuildProviderFailureMessage(
                    "Local AI could not write a title and description for this clip.",
                    exception),
                request.Context.CandidateId,
                exception);
        }
        finally
        {
            reviewVideo?.Dispose();
        }
    }

    public async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
        GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Any(static request => request is null))
        {
            throw new ArgumentException(
                "Editorial metadata batches cannot contain null requests.",
                nameof(requests));
        }
        if (requests.Count == 0)
        {
            return Array.Empty<ClipEditorialMetadataDraft>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        ClipEditorialMetadataRequest? firstAiRequest = requests.FirstOrDefault(
            static request => request.Preference !=
                ClipEditorialGenerationPreference.HeuristicOnly);
        if (firstAiRequest is not null && !IsAiAvailable)
        {
            EnsureAiAvailable(firstAiRequest);
        }

        if (IsAiAvailable &&
            requests.All(static request =>
                request.Preference !=
                ClipEditorialGenerationPreference.HeuristicOnly) &&
            _ai is IClipEditorialMetadataBatchGenerator batchGenerator)
        {
            var reviewVideos =
                new List<MaterializedVisualSemanticReviewVideo>();
            IReadOnlyList<ClipEditorialMetadataRequest> effectiveRequests =
                requests;
            try
            {
                if (_ai is IClipEditorialVisualMetadataGenerator)
                {
                    var prepared = new List<ClipEditorialMetadataRequest>(
                        requests.Count);
                    foreach (ClipEditorialMetadataRequest request in requests)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (request.ReviewVideo is not null)
                        {
                            prepared.Add(request);
                            continue;
                        }

                        MaterializedVisualSemanticReviewVideo video =
                            await MaterializeReviewVideoAsync(
                                request,
                                cancellationToken);
                        reviewVideos.Add(video);
                        prepared.Add(request.WithReviewVideo(video.Input));
                    }
                    effectiveRequests = prepared.AsReadOnly();
                }
                if (_sceneReviewer is not null)
                    effectiveRequests = await _sceneReviewer.ReviewAsync(effectiveRequests, cancellationToken);
                return await GenerateNovelBatchAsync(
                    requests,
                    effectiveRequests,
                    batchGenerator,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ClipEditorialAiGenerationException)
            {
                throw;
            }
            catch (Exception batchException)
            {
                throw new ClipEditorialAiGenerationException(
                    ClipEditorialAiFailureKind.ProviderFailed,
                    BuildProviderFailureMessage(
                        "Local AI could not finish the titles and descriptions for these clips.",
                        batchException),
                    innerException: batchException);
            }
            finally
            {
                foreach (MaterializedVisualSemanticReviewVideo video in
                         reviewVideos.AsEnumerable().Reverse())
                {
                    video.Dispose();
                }
            }
        }

        var sequentialDrafts =
            new List<ClipEditorialMetadataDraft>(requests.Count);
        foreach (ClipEditorialMetadataRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sequentialDrafts.Add(await GenerateAsync(request, cancellationToken));
        }

        return sequentialDrafts.AsReadOnly();
    }

    private async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
        GenerateNovelBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> originalRequests,
            IReadOnlyList<ClipEditorialMetadataRequest> effectiveRequests,
            IClipEditorialMetadataBatchGenerator generator,
            CancellationToken cancellationToken)
    {
        using IClipEditorialMetadataBatchSession? session =
            (generator as IClipEditorialMetadataBatchSessionFactory)?.CreateBatchSession();
        generator = session ?? generator;
        long batchStarted = Stopwatch.GetTimestamp();
        string diagnosticBatchId = Guid.NewGuid().ToString("N");
        IReadOnlyList<ClipEditorialMetadataRequest> currentRequests = effectiveRequests;
        IReadOnlyList<ClipEditorialMetadataDraft> currentDrafts =
            await GenerateProviderBatchAsync(
                originalRequests,
                effectiveRequests,
                generator,
                cancellationToken);
        int[] currentIndexes = Enumerable.Range(
            0,
            originalRequests.Count).ToArray();
        var acceptedTitles = new List<string>(originalRequests.Count);
        var rejectedTitles = Enumerable.Range(0, originalRequests.Count)
            .Select(static _ => new List<string>())
            .ToArray();
        var attemptedDrafts = Enumerable.Range(0, originalRequests.Count)
            .Select(static _ => new List<ClipEditorialMetadataDraft>())
            .ToArray();
        var resolved = new ClipEditorialMetadataDraft?[originalRequests.Count];

        for (int retryCount = 0;
             retryCount <= MaximumNoveltyRetryCount;
             retryCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pendingIndexes = new List<int>();
            var pendingDecisions = new List<ClipEditorialRetryCaseDiagnostic>();
            for (int position = 0;
                 position < currentIndexes.Length;
                 position++)
            {
                int originalIndex = currentIndexes[position];
                ClipEditorialMetadataDraft draft = currentDrafts[position];
                attemptedDrafts[originalIndex].Add(draft);
                ClipEditorialMetadataRequest originalRequest =
                    originalRequests[originalIndex];
                string[] comparisons = originalRequest
                    .PriorAcceptedTitleExclusions
                    .Select(static exclusion => exclusion.Title)
                    .Concat(acceptedTitles)
                    .Concat(rejectedTitles[originalIndex])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                bool titleRejected = HasNeuralCopyReview(draft)
                    ? comparisons.Any(title => title.Equals(draft.Title, StringComparison.OrdinalIgnoreCase))
                    : ClipEditorialBatchNoveltyPolicy.Rejects(draft.Title, comparisons);
                IReadOnlyList<ClipEditorialMetadataQualityIssue> qualityIssues =
                    EditorialRetryIssues(draft, originalRequest);
                if (titleRejected || qualityIssues.Count > 0)
                {
                    bool differentTitleRequired = titleRejected || RequiresDifferentTitle(
                            draft,
                            originalRequest,
                            qualityIssues);
                    if (differentTitleRequired)
                    {
                        rejectedTitles[originalIndex].Add(draft.Title);
                    }
                    pendingIndexes.Add(originalIndex);
                    ClipEditorialMetadataRequest currentRequest = currentRequests[position];
                    pendingDecisions.Add(new ClipEditorialRetryCaseDiagnostic(
                        currentRequest.Context.CandidateId,
                        currentRequest.Attempt,
                        currentRequest.VariantIntent.ToString(),
                        currentRequest.Attempt,
                        currentRequest.VariantIntent.ToString(),
                        titleRejected,
                        titleRejected
                            ? ClipEditorialBatchNoveltyPolicy.IsBannedAbstractFamily(draft.Title)
                                ? "BannedAbstractFamily" : "TitleCollision"
                            : null,
                        differentTitleRequired,
                        Array.AsReadOnly(qualityIssues.Select(static issue =>
                            issue.Code.ToString()).Distinct(StringComparer.Ordinal).ToArray()),
                        Array.AsReadOnly(qualityIssues.Select(static issue =>
                            issue.SourceRuleCode).OfType<string>()
                            .Distinct(StringComparer.Ordinal).ToArray())));
                    continue;
                }

                resolved[originalIndex] = draft;
                acceptedTitles.Add(draft.Title);
            }

            if (pendingIndexes.Count == 0)
            {
                return resolved.Select(static draft => draft ??
                        throw new InvalidDataException(
                            "AI batch novelty validation lost a resolved draft."))
                    .ToArray();
            }

            if (retryCount == MaximumNoveltyRetryCount)
            {
                return RetainBestGroundedAiDrafts(
                    originalRequests,
                    pendingIndexes,
                    attemptedDrafts,
                    resolved,
                    acceptedTitles);
            }

            currentIndexes = pendingIndexes.ToArray();
            ClipEditorialMetadataRequest[] retryRequests = currentIndexes
                .Select(index => BuildNoveltyRetryRequest(
                    effectiveRequests[index],
                    acceptedTitles,
                    rejectedTitles[index],
                    retryCount + 1))
                .ToArray();
            IReadOnlyList<ClipEditorialRetryCaseDiagnostic> retryCases = Array.AsReadOnly(
                retryRequests.Select((request, index) => pendingDecisions[index] with
                {
                    NextAttempt = request.Attempt,
                    NextVariant = request.VariantIntent.ToString(),
                }).ToArray());
            long retryStarted = Stopwatch.GetTimestamp();
            var retryDiagnostic = new ClipEditorialRetryDiagnostic(
                "Started", diagnosticBatchId, retryCount + 1,
                Stopwatch.GetElapsedTime(batchStarted).TotalSeconds,
                null, retryCases);
            ClipEditorialRetryDiagnostics.Report(retryDiagnostic);
            try
            {
                currentRequests = retryRequests;
                currentDrafts = await GenerateProviderBatchAsync(
                    retryRequests,
                    retryRequests,
                    generator,
                    cancellationToken);
                ClipEditorialRetryDiagnostics.Report(retryDiagnostic with
                {
                    Event = "Completed",
                    ElapsedSeconds = Stopwatch.GetElapsedTime(batchStarted).TotalSeconds,
                    RetryElapsedSeconds = Stopwatch.GetElapsedTime(retryStarted).TotalSeconds,
                });
            }
            catch (OperationCanceledException)
            {
                ClipEditorialRetryDiagnostics.Report(retryDiagnostic with
                {
                    Event = "Cancelled",
                    ElapsedSeconds = Stopwatch.GetElapsedTime(batchStarted).TotalSeconds,
                    RetryElapsedSeconds = Stopwatch.GetElapsedTime(retryStarted).TotalSeconds,
                });
                throw;
            }
            catch (Exception exception)
            {
                ClipEditorialRetryDiagnostics.Report(retryDiagnostic with
                {
                    Event = "Failed",
                    ElapsedSeconds = Stopwatch.GetElapsedTime(batchStarted).TotalSeconds,
                    RetryElapsedSeconds = Stopwatch.GetElapsedTime(retryStarted).TotalSeconds,
                    FailureType = exception.GetType().Name,
                });
                // Every pending row already has at least one schema- and
                // provenance-valid AI attempt. A failed corrective rewrite
                // must not erase that work or switch authorship to heuristics.
                return RetainBestGroundedAiDrafts(
                    originalRequests,
                    pendingIndexes,
                    attemptedDrafts,
                    resolved,
                    acceptedTitles);
            }
        }

        throw new InvalidOperationException(
            "AI batch novelty validation exceeded its bounded retry loop.");
    }

    private static IReadOnlyList<ClipEditorialMetadataQualityIssue>
        EditorialRetryIssues(
        ClipEditorialMetadataDraft draft,
        ClipEditorialMetadataRequest request)
        // Word lists and phrasing preferences remain advisory after a verified
        // neural judgment. They must not trigger another generation decision.
        => HasNeuralCopyReview(draft) ? [] : MergedAdvisoryIssues(draft, request)
            .Where(static issue => issue.SourceRuleCode is not
                ("RerollDiversityProvenanceRecomputed" or "BalanceNotSatisfied"))
            .ToArray();

    private static bool HasNeuralCopyReview(ClipEditorialMetadataDraft draft) =>
        draft.AiProvenance?.NeuralCopyReview?.Accepted == true;

    private static bool RequiresDifferentTitle(
        ClipEditorialMetadataDraft draft,
        ClipEditorialMetadataRequest request,
        IReadOnlyList<ClipEditorialMetadataQualityIssue> issues)
    {
        int preferredTitleMaximum = Math.Min(
            ClipEditorialMetadataDraft.MaximumTitleLength,
            Math.Max(
                ClipEditorialMetadataQuality.PreferredMaximumTitleLength,
                request.Context.GameContext.AudienceGameHashtag.Length + 12));
        return draft.Title.Length > preferredTitleMaximum ||
            issues.Any(static issue => issue.Code ==
                ClipEditorialMetadataQualityIssueCode.RedundantGameIdentity) ||
            issues.Any(static issue =>
                issue.SourceRuleCode is
                    "RerollTitleTooSimilar" or
                    "IncompleteTitle" or
                    "FirstPersonTitleSubject");
    }

    private static IReadOnlyList<ClipEditorialMetadataDraft>
        RetainBestGroundedAiDrafts(
            IReadOnlyList<ClipEditorialMetadataRequest> requests,
            IReadOnlyList<int> pendingIndexes,
            IReadOnlyList<List<ClipEditorialMetadataDraft>> attemptedDrafts,
            ClipEditorialMetadataDraft?[] resolved,
            IReadOnlyList<string> acceptedTitles)
    {
        var retainedTitles = new List<string>(acceptedTitles);
        foreach (int index in pendingIndexes.Order())
        {
            ClipEditorialMetadataRequest request = requests[index];
            string[] comparisons = request.PriorAcceptedTitleExclusions
                .Select(static exclusion => exclusion.Title)
                .Concat(retainedTitles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ClipEditorialMetadataDraft best = SelectBestGroundedAiDraft(
                attemptedDrafts[index],
                request,
                comparisons);

            ClipEditorialMetadataDraft retained = WithExhaustedRewriteReview(
                best,
                request,
                attemptedDrafts[index]);
            resolved[index] = retained;
            retainedTitles.Add(retained.Title);
        }

        return resolved.Select(static draft => draft ??
                throw new InvalidDataException(
                    "AI batch review retention lost a resolved draft."))
            .ToArray();
    }

    private static ClipEditorialMetadataDraft SelectBestGroundedAiDraft(
        IReadOnlyList<ClipEditorialMetadataDraft> attempts,
        ClipEditorialMetadataRequest request,
        IReadOnlyList<string> comparisonTitles)
    {
        if (attempts.Count == 0)
        {
            throw new InvalidDataException(
                "AI batch review retention requires at least one validated draft.");
        }

        return attempts
            .Select((draft, index) => new
            {
                Draft = draft,
                Index = index,
                Issues = MergedAdvisoryIssues(draft, request),
            })
            .OrderBy(candidate => RetentionPenalty(
                candidate.Draft,
                candidate.Issues,
                comparisonTitles))
            .ThenBy(candidate => ClipAudiencePackagingAssessment.Evaluate(
                candidate.Draft.Title,
                candidate.Draft.Description).Penalty)
            .ThenByDescending(static candidate => candidate.Index)
            .Select(static candidate => candidate.Draft)
            .First();
    }

    private static long RetentionPenalty(
        ClipEditorialMetadataDraft draft,
        IReadOnlyList<ClipEditorialMetadataQualityIssue> issues,
        IReadOnlyList<string> comparisonTitles)
    {
        long penalty = issues.Sum(RetentionIssuePenalty);
        if (ClipEditorialBatchNoveltyPolicy.IsBannedAbstractFamily(draft.Title))
        {
            penalty += 20;
        }
        if (comparisonTitles.Any(title =>
                ClipEditorialBatchNoveltyPolicy.Collides(draft.Title, title)))
        {
            penalty += 15;
        }
        return penalty;
    }

    private static long RetentionIssuePenalty(
        ClipEditorialMetadataQualityIssue issue)
    {
        string? rule = issue.SourceRuleCode;
        if (rule is "IncompleteTitle" or "GameHashtag" or
            "EmbeddedHashtag")
        {
            // These are provider review findings on a complete contract, not
            // malformed output. Prefer every other validated draft, but keep
            // one when every bounded AI rewrite shares the finding.
            return 1_000_000_000;
        }

        if (rule is
                "AnalysisBookkeeping" or
                "CaseLocalFactRetention" or
                "CrossDraftTitleContamination" or
                "OutputLanguage" or
                "StrictOutputValidation" or
                "UncoupledKnowledgeReference" or
                "UnresolvedVisualGrounding" or
                "UnreviewedTranscriptReuse" or
                "UnsupportedCreatorEmbodiment" or
                "UnsupportedInterfaceAttribution" or
                "UnsupportedKnowledgeGrounding" or
                "UnstableReadableTextReuse" ||
            issue.Code == ClipEditorialMetadataQualityIssueCode
                .UnreviewedTranscriptReuse)
        {
            return 1_000_000;
        }

        if (rule == "UnsupportedMentalState" ||
            issue.Code == ClipEditorialMetadataQualityIssueCode
                .UnsupportedMentalState)
        {
            return 10_000;
        }

        if (rule is
                "BalanceNotSatisfied" or
                "EditorialFrameDrift" or
                "FirstPersonTitleSubject" or
                "GenericOpening" or
                "LiteralSceneReport" or
                "LocalAudienceCopyBoundary" or
                "NonRetrospectiveVoice" or
                "ThirdPersonCreatorFraming" ||
            issue.Code is
                ClipEditorialMetadataQualityIssueCode
                    .ThirdPersonCreatorFraming or
                ClipEditorialMetadataQualityIssueCode.GenericOpening or
                ClipEditorialMetadataQualityIssueCode.LiteralSceneReport or
                ClipEditorialMetadataQualityIssueCode
                    .TitleDescriptionRepetition or
                ClipEditorialMetadataQualityIssueCode.OverlongAudienceCopy or
                ClipEditorialMetadataQualityIssueCode.RedundantGameIdentity)
        {
            return 100;
        }

        return 1;
    }

    private static ClipEditorialMetadataDraft WithExhaustedRewriteReview(
        ClipEditorialMetadataDraft draft,
        ClipEditorialMetadataRequest request,
        IReadOnlyList<ClipEditorialMetadataDraft> attempts)
    {
        var issues = MergedAdvisoryIssues(draft, request).ToList();
        if (issues.Count == 0)
        {
            issues.Add(new ClipEditorialMetadataQualityIssue(
                ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
                "Local AI completed grounded copy, but bounded rewrites did not " +
                "produce a sufficiently distinct title. Review it or reroll."));
        }

        var warnings = draft.Warnings.ToList();
        if (!warnings.Any(static warning => warning.Code ==
                ClipEditorialWarningCode.MetadataReviewRequired))
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.MetadataReviewRequired,
                "Replay Foundry kept the best grounded local AI draft after " +
                "bounded editorial rewrites. Review it or request another AI reroll."));
        }
        if (attempts.Count > 1 &&
            !warnings.Any(static warning => warning.Code ==
                ClipEditorialWarningCode.AiDraftRegenerated))
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.AiDraftRegenerated,
                "Local AI tried more than one editorial structure before Replay " +
                "Foundry kept the strongest grounded draft."));
        }

        IReadOnlyList<string> priorTitles =
            ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                draft.PriorAcceptedTitles.Concat(attempts
                    .Select(static attempt => attempt.Title)
                    .Where(title => !title.Equals(
                        draft.Title,
                        StringComparison.OrdinalIgnoreCase))));
        return new ClipEditorialMetadataDraft(
            draft.Title,
            draft.Description,
            draft.Tags,
            draft.Origin,
            draft.Generator,
            draft.Attempt,
            draft.Evidence,
            warnings,
            draft.AiProvenance,
            draft.Readiness,
            issues,
            priorTitles,
            draft.GroundingAudit);
    }

    private static IReadOnlyList<ClipEditorialMetadataQualityIssue>
        MergedAdvisoryIssues(
            ClipEditorialMetadataDraft draft,
            ClipEditorialMetadataRequest request)
    {
        string description = DescriptionForEditorialReview(draft, request);
        IEnumerable<ClipEditorialMetadataQualityIssue> issues =
            draft.QualityIssues
            .Concat(ClipEditorialMetadataQuality.Evaluate(
                draft.Title,
                description,
                request.Context));
        if (HeuristicAudienceCopyPolicy.RequiresAudienceCopyFallback(
                draft.Title,
                description,
                request.Context))
        {
            issues = issues.Append(new ClipEditorialMetadataQualityIssue(
                ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
                "This wording stayed too close to raw evidence or generic " +
                "filler. It remains usable, but review it or reroll for more " +
                "natural audience copy.",
                sourceRuleCode: "LocalAudienceCopyBoundary"));
        }

        return issues
            .DistinctBy(static issue =>
                (issue.Code, issue.Message, issue.SourceRuleCode))
            .ToArray();
    }

    private static string DescriptionForEditorialReview(
        ClipEditorialMetadataDraft draft,
        ClipEditorialMetadataRequest request)
    {
        string? signature = request.Profile.ReusableDescriptionSignature;
        if (string.IsNullOrWhiteSpace(signature))
        {
            return draft.Description;
        }

        string normalizedSignature = signature.Trim();
        if (!draft.Description.EndsWith(
                normalizedSignature,
                StringComparison.Ordinal))
        {
            return draft.Description;
        }

        int signatureStart = draft.Description.Length -
            normalizedSignature.Length;
        string prefix = draft.Description[..signatureStart];
        if (!prefix.EndsWith('\n') && !prefix.EndsWith('\r'))
        {
            return draft.Description;
        }

        string audienceDescription = prefix.TrimEnd();
        return audienceDescription.Length == 0
            ? draft.Description
            : audienceDescription;
    }

    private async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
        GenerateProviderBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> originalRequests,
            IReadOnlyList<ClipEditorialMetadataRequest> effectiveRequests,
            IClipEditorialMetadataBatchGenerator generator,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<ClipEditorialMetadataDraft> drafts = generator is
            IClipEditorialMetadataFailSoftBatchGenerator failSoftBatchGenerator
                ? await GenerateFailSoftBatchAsync(
                    originalRequests,
                    effectiveRequests,
                    failSoftBatchGenerator,
                    cancellationToken)
                : await generator.GenerateBatchAsync(
                    effectiveRequests,
                    cancellationToken);
        if (drafts.Count != originalRequests.Count ||
            drafts.Any(static draft => draft is null))
        {
            throw new ClipEditorialAiGenerationException(
                ClipEditorialAiFailureKind.IncompleteResult,
                "Local AI did not finish every requested title and description.");
        }

        return ValidateAiDrafts(
            originalRequests,
            drafts,
            cancellationToken);
    }

    private static ClipEditorialMetadataRequest BuildNoveltyRetryRequest(
        ClipEditorialMetadataRequest request,
        IReadOnlyList<string> acceptedTitles,
        IReadOnlyList<string> rejectedTitles,
        int retryCount)
    {
        ClipEditorialPriorTitleExclusion[] exclusions = request
            .PriorAcceptedTitleExclusions
            .Select(static exclusion => exclusion.Title)
            .Concat(acceptedTitles)
            .Concat(rejectedTitles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(ClipEditorialPriorTitleExclusion.MaximumRetainedTitles)
            .Select(title => ClipEditorialPriorTitleExclusion.ForContext(
                request.Context,
                title))
            .ToArray();
        return request
            .WithPriorAcceptedTitleExclusions(exclusions)
            .WithAttempt(checked(request.Attempt + retryCount))
            .WithVariantIntent(RetryVariantIntent(
                request.VariantIntent,
                retryCount));
    }

    private static ClipEditorialVariantIntent RetryVariantIntent(
        ClipEditorialVariantIntent initial,
        int retryCount)
    {
        ClipEditorialVariantIntent[] rotation =
        [
            ClipEditorialVariantIntent.DirectAction,
            ClipEditorialVariantIntent.SpecificCuriosity,
            ClipEditorialVariantIntent.OutcomeFocused,
        ];
        int initialIndex = Array.IndexOf(rotation, initial);
        if (initialIndex < 0)
        {
            initialIndex = -1;
        }
        return rotation[(initialIndex + retryCount) % rotation.Length];
    }

    private async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
        GenerateFailSoftBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> originalRequests,
            IReadOnlyList<ClipEditorialMetadataRequest> effectiveRequests,
            IClipEditorialMetadataFailSoftBatchGenerator generator,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<ClipEditorialMetadataBatchOutcome> outcomes =
            await generator.GenerateBatchOutcomesAsync(
                effectiveRequests,
                cancellationToken);
        if (outcomes.Count != originalRequests.Count ||
            outcomes.Any(static outcome =>
                outcome is null ||
                (!outcome.IsAccepted && !outcome.IsFailed)))
        {
            throw new InvalidDataException(
                "The AI editorial provider returned an invalid fail-soft batch.");
        }

        var drafts = new List<ClipEditorialMetadataDraft>(outcomes.Count);
        for (int index = 0; index < outcomes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataBatchOutcome outcome = outcomes[index];
            if (outcome.IsAccepted)
            {
                drafts.Add(outcome.Draft!);
                continue;
            }

            throw new ClipEditorialAiGenerationException(
                ClipEditorialAiFailureKind.CaseRejected,
                BuildCaseFailureMessage(outcome.Failure!),
                originalRequests[index].Context.CandidateId);
        }
        return drafts.AsReadOnly();
    }

    private IReadOnlyList<ClipEditorialMetadataDraft>
        ValidateAiDrafts(
            IReadOnlyList<ClipEditorialMetadataRequest> requests,
            IReadOnlyList<ClipEditorialMetadataDraft> drafts,
            CancellationToken cancellationToken)
    {
        for (int index = 0; index < drafts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataDraft draft = drafts[index];
            ValidateAiDraftPostcondition(draft, requests[index]);
            if (!HeuristicAudienceCopyPolicy.ContainsInternalProcessText(
                    draft.Title,
                    DescriptionForEditorialReview(draft, requests[index])))
            {
                continue;
            }

            throw new ClipEditorialAiGenerationException(
                ClipEditorialAiFailureKind.UnsafeOutput,
                "Local AI returned internal process wording for one clip " +
                "instead of viewer-facing copy. Replay Foundry did not keep it.",
                requests[index].Context.CandidateId);
        }
        return drafts;
    }

    private void ValidateAiDraftPostcondition(
        ClipEditorialMetadataDraft draft,
        ClipEditorialMetadataRequest request)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(request);
        if (draft.Origin == ClipEditorialMetadataOrigin.AiAssisted &&
            draft.AiProvenance is not null &&
            draft.Readiness == ClipEditorialMetadataReadiness.GroundedDraft &&
            draft.Generator.Equals(_ai!.Identity))
        {
            return;
        }

        throw new ClipEditorialAiGenerationException(
            ClipEditorialAiFailureKind.IncompleteResult,
            "Local AI returned an incomplete title or description. " +
            "Replay Foundry stopped the rewrite so you can try again.",
            request.Context.CandidateId);
    }

    private async Task<MaterializedVisualSemanticReviewVideo>
        MaterializeReviewVideoAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
    {
        if (_reviewVideoMaterializer is null ||
            request.SourceMedia is null)
        {
            throw new InvalidOperationException(
                "AI metadata requires retained source inspection and the approved bounded review-video materializer.");
        }

        TimeSpan start = request.Context.SourceStart;
        TimeSpan end = request.Context.SourceEnd;

        return await _reviewVideoMaterializer.MaterializeAsync(
            new VisualSemanticReviewVideoMaterializationRequest(
                request.Context.CandidateId,
                request.SourceMedia,
                start,
                end,
                request.Context.GameplayRegion),
            cancellationToken);
    }

    private void EnsureAiAvailable(ClipEditorialMetadataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsAiAvailable)
        {
            return;
        }

        throw new ClipEditorialAiGenerationException(
            ClipEditorialAiFailureKind.ProviderUnavailable,
            "Local AI is not ready to write titles and descriptions. " +
            "Repair Advanced AI in Settings or use the built-in writer.",
            request.Context.CandidateId);
    }

    private static string BuildProviderFailureMessage(
        string explanation,
        Exception failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        ArgumentNullException.ThrowIfNull(failure);

        string state = failure switch
        {
            Qwen3VlInferenceException { HostFailure: { } hostFailure } =>
                $"{hostFailure.Failure.ErrorCode}/{hostFailure.Stage}",
            Qwen3VlOutputParseException => "StructuredOutputUnavailable",
            InvalidDataException => "IncompleteStructuredResult",
            IOException => "LocalInputOutputUnavailable",
            _ => failure.GetType().Name,
        };
        return $"{explanation} Error: {state}.";
    }

    private static string BuildCaseFailureMessage(
        ClipEditorialMetadataCaseFailure failure) =>
        failure.Code switch
        {
            ClipEditorialMetadataCaseFailureCode
                    .NoDistinctPrimaryVisualEvent =>
                "Local AI could not identify one clear main event in this " +
                "clip. Try another angle or use the built-in writer. " +
                "Error: NoDistinctPrimaryVisualEvent.",
            _ => throw new InvalidDataException(
                "The AI editorial provider returned an unknown per-case failure."),
        };
}
