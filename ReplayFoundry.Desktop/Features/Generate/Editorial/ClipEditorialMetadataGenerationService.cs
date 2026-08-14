using System.IO;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public interface IClipEditorialMetadataGenerationService
{
    bool IsAiAvailable { get; }

    Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var drafts = new List<ClipEditorialMetadataDraft>(requests.Count);
        foreach (ClipEditorialMetadataRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            drafts.Add(await GenerateAsync(request, cancellationToken));
        }

        return drafts.AsReadOnly();
    }
}
public sealed class ClipEditorialMetadataGenerationService :
    IClipEditorialMetadataGenerationService
{
    internal static readonly TimeSpan MaximumFocusedReviewDuration =
        TimeSpan.FromSeconds(16);

    private readonly IClipEditorialMetadataGenerator _heuristic;
    private readonly IClipEditorialMetadataGenerator? _ai;
    private readonly IVisualSemanticReviewVideoMaterializer?
        _reviewVideoMaterializer;

    public ClipEditorialMetadataGenerationService(
        IClipEditorialMetadataGenerator heuristic,
        IClipEditorialMetadataGenerator? ai = null,
        IVisualSemanticReviewVideoMaterializer? reviewVideoMaterializer = null)
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
        _reviewVideoMaterializer = reviewVideoMaterializer;
    }

    public bool IsAiAvailable =>
        _ai?.IsAvailable == true &&
        (_ai is not IClipEditorialVisualMetadataGenerator ||
         _reviewVideoMaterializer is not null);

    public async Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Preference !=
                ClipEditorialGenerationPreference.HeuristicOnly &&
            IsAiAvailable)
        {
            MaterializedVisualSemanticReviewVideo? reviewVideo = null;
            try
            {
                ClipEditorialMetadataRequest effectiveRequest = request;
                if (_ai is IClipEditorialVisualMetadataGenerator &&
                    request.ReviewVideo is null)
                {
                    reviewVideo = await MaterializeReviewVideoAsync(
                        request,
                        cancellationToken);
                    effectiveRequest = request.WithReviewVideo(
                        reviewVideo.Input);
                }
                return await _ai!.GenerateAsync(
                    effectiveRequest,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                request.Preference ==
                ClipEditorialGenerationPreference.AiWhenAvailable)
            {
                return await GenerateHeuristicFallbackAsync(
                    request,
                    ClipEditorialWarningCode.AiProviderFailed,
                    BuildProviderFailureWarning(
                        "The qualified local AI could not validate this clip, so Replay Foundry kept a deterministic working label instead.",
                        exception),
                    cancellationToken);
            }
            finally
            {
                reviewVideo?.Dispose();
            }
        }

        if (request.Preference ==
            ClipEditorialGenerationPreference.AiRequired)
        {
            throw new InvalidOperationException(
                "AI metadata generation is not available. Replay Foundry will not silently substitute a different model or PATH tool.");
        }

        ClipEditorialMetadataDraft draft = await _heuristic.GenerateAsync(
            request,
            cancellationToken);
        if (request.Preference !=
            ClipEditorialGenerationPreference.AiWhenAvailable)
        {
            return draft;
        }

        return AddWarning(
            draft,
            ClipEditorialWarningCode.AiProviderUnavailable,
            "The optional AI metadata provider is unavailable, so Replay Foundry used its deterministic grounded generator.");
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
                try
                {
                    IReadOnlyList<ClipEditorialMetadataDraft> drafts =
                        await batchGenerator.GenerateBatchAsync(
                            effectiveRequests,
                            cancellationToken);
                    if (drafts.Count != requests.Count ||
                        drafts.Any(static draft => draft is null))
                    {
                        throw new InvalidDataException(
                            "The AI editorial provider did not preserve every batch request.");
                    }

                    return drafts;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception batchException)
                {
                    return await HandleBatchFailureAsync(
                        requests,
                        batchException,
                        cancellationToken);
                }
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

        var fallback = new List<ClipEditorialMetadataDraft>(requests.Count);
        foreach (ClipEditorialMetadataRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fallback.Add(await GenerateAsync(request, cancellationToken));
        }

        return fallback.AsReadOnly();
    }

    private async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
        HandleBatchFailureAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> originalRequests,
            Exception failure,
            CancellationToken cancellationToken)
    {
        if (originalRequests.Any(static request =>
                request.Preference ==
                    ClipEditorialGenerationPreference.AiRequired))
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }

        var drafts = new List<ClipEditorialMetadataDraft>(
            originalRequests.Count);
        for (int index = 0; index < originalRequests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            drafts.Add(await GenerateHeuristicFallbackAsync(
                originalRequests[index],
                ClipEditorialWarningCode.AiProviderFailed,
                BuildProviderFailureWarning(
                    "The qualified local AI could not return a complete " +
                    "metadata batch. Replay Foundry did not repeat the same " +
                    "GPU work and kept a deterministic working label instead.",
                    failure),
                cancellationToken));
        }

        return drafts.AsReadOnly();
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

        TimeSpan maximum = MaximumFocusedReviewDuration;
        TimeSpan start = request.Context.SourceStart;
        TimeSpan end = request.Context.SourceEnd;
        if (end - start > maximum)
        {
            TimeSpan center = request.ReviewFocusSourceTimestamp ??
                TimeSpan.FromTicks(start.Ticks + (end - start).Ticks / 2);
            start = center - TimeSpan.FromTicks(maximum.Ticks / 2);
            end = start + maximum;
            if (start < request.Context.SourceStart)
            {
                start = request.Context.SourceStart;
                end = start + maximum;
            }
            if (end > request.Context.SourceEnd)
            {
                end = request.Context.SourceEnd;
                start = end - maximum;
            }
        }

        return await _reviewVideoMaterializer.MaterializeAsync(
            new VisualSemanticReviewVideoMaterializationRequest(
                request.Context.CandidateId,
                request.SourceMedia,
                start,
                end,
                request.Context.GameplayRegion),
            cancellationToken);
    }

    private async Task<ClipEditorialMetadataDraft>
        GenerateHeuristicFallbackAsync(
            ClipEditorialMetadataRequest request,
            ClipEditorialWarningCode warningCode,
            string warningMessage,
            CancellationToken cancellationToken)
    {
        ClipEditorialMetadataDraft draft = await _heuristic.GenerateAsync(
            request,
            cancellationToken);
        return AddWarning(draft, warningCode, warningMessage);
    }

    private static ClipEditorialMetadataDraft AddWarning(
        ClipEditorialMetadataDraft draft,
        ClipEditorialWarningCode warningCode,
        string warningMessage) =>
        new(
            draft.Title,
            draft.Description,
            draft.Tags,
            draft.Origin,
            draft.Generator,
            draft.Attempt,
            draft.Evidence,
            draft.Warnings.Append(
                new ClipEditorialWarning(
                    warningCode,
                    warningMessage)),
            draft.AiProvenance,
            draft.Readiness,
            draft.QualityIssues,
            draft.PriorAcceptedTitles);

    private static string BuildProviderFailureWarning(
        string explanation,
        Exception failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        ArgumentNullException.ThrowIfNull(failure);

        string detail = NormalizeFailureMessage(failure.Message);
        return detail.Length == 0
            ? explanation
            : $"{explanation} Provider detail: {detail}";
    }

    private static string NormalizeFailureMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        const int maximumLength = 320;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..(maximumLength - 1)] + "…";
    }
}
