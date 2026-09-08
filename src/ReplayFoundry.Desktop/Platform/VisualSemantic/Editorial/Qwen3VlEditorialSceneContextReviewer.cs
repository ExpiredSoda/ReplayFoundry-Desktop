using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

// Existing projects and trimmed/manual clips need the same current, frame-bound
// facts as a newly generated clip before they can use the scene writer.
internal sealed class Qwen3VlEditorialSceneContextReviewer(
    IVisualSemanticEditorialProvider provider,
    VisualSemanticPromptManifest prompt,
    VisualSemanticModelManifest model) : IClipEditorialSceneContextReviewer
{
    internal Qwen3VlEditorialSceneContextReviewer(Qwen3VlQualifiedEditorialRuntime runtime)
        : this(new Qwen3VlSceneReviewProvider(runtime),
            Qwen3VlSceneReviewProvider.LoadPrompt(runtime.Host.HostScriptPath), runtime.Model) { }

    public async Task<IReadOnlyList<ClipEditorialMetadataRequest>> ReviewAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
    {
        var result = requests.ToArray();
        var policy = VisualSemanticVideoInputPolicy.CreateV05A1();
        // Longer manually edited cuts retain the full-video legacy writer;
        // a partial scene review must never authorize facts for an entire cut.
        var pending = Enumerable.Range(0, result.Length).Where(i =>
            !Qwen3VlSceneCopyGenerator.CanUse(result[i]) && result[i].Context.Duration <= policy.MaximumReviewDuration).ToArray();
        foreach (var group in pending.Chunk(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bindings = group.ToDictionary(i => i, i => SourceBinding(result[i].Context));
            var reviewRequests = group.Select(i => CreateRequest(result[i])).ToArray();
            var review = await provider.ObserveAsync(new(reviewRequests, policy), cancellationToken);
            foreach (int i in group)
            {
                var request = result[i];
                var observed = review.Results.SingleOrDefault(value => value.Request.CandidateId == request.Context.CandidateId)
                    ?? throw new InvalidDataException($"The picture review could not establish facts for {request.Context.CandidateId}.");
                if (observed.CanonicalizationAudit.WireRepresentationVersion != Qwen3VlSceneReviewProvider.Version ||
                    bindings[i] != SourceBinding(request.Context))
                    throw new InvalidDataException("The clip or its picture evidence changed during review. Retry with the current cut.");
                var context = request.Context;
                var evidence = context.Evidence.Where(item => !item.Id.StartsWith("scene-review-", StringComparison.Ordinal)).ToList();
                evidence.AddRange(observed.Observation.EvidenceIntervals.Select(interval =>
                    new ClipEditorialEvidenceReference(Qwen3VlSceneReviewProvider.Version + "-" + interval.Id,
                        ClipEditorialEvidenceKind.VisualObservation, interval.Description)));
                evidence.Add(new("scene-review-source-binding", ClipEditorialEvidenceKind.SourceIdentity, bindings[i]));
                var refreshed = new ClipEditorialContext(context.CandidateId, context.SourceFullPath, context.SourceLabel,
                    context.SourceStart, context.SourceEnd, context.SourceDuration, context.DeterministicScore,
                    context.DeterministicReason, context.Transcripts, evidence, context.GameContext, context.GameKnowledge,
                    context.GameplayRegion, context.VisualText, context.EditorialBrief);
                result[i] = new(refreshed, request.Profile, request.Attempt, request.Preference, request.SourceMedia,
                    request.ReviewVideo, request.PriorAcceptedTitleExclusions, request.VariantIntent);
                if (!Qwen3VlSceneCopyGenerator.CanUse(result[i]))
                    throw new InvalidDataException("The picture review did not supply a complete scene for this cut.");
            }
        }
        return result;
    }

    internal VisualSemanticRequest CreateRequest(ClipEditorialMetadataRequest request)
    {
        var context = request.Context;
        var mode = CandidateMode(context);
        var video = request.ReviewVideo ?? throw new InvalidOperationException("Scene writing requires bounded review media.");
        var spans = context.Transcripts.SelectMany(track => track.Spans.Select((span, i) => new VisualSemanticTranscriptSpan(
            $"audio-{track.AbsoluteAudioStreamIndex}-{i}", span.Text, span.SourceStart - context.SourceStart,
            span.SourceEnd - context.SourceStart, false, TranscriptTimingPrecision.Unknown))).ToArray();
        bool supplied = spans.Length > 0;
        var transcript = new VisualSemanticTranscriptContext(supplied ? VisualSemanticTranscriptContextPolicy.FullContextV1 :
            VisualSemanticTranscriptContextPolicy.VisualOnlyV1, supplied ? TranscriptEvidenceStatus.LexicalText : null,
            spans, "Speech recognition is approximate; dialogue does not establish visible actions or outcomes.");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceBinding(context) + video.ReviewVideoSha256 + JsonSerializer.Serialize(spans) + prompt.Sha256 + model.ManifestSha256)));
        return new("editorial-" + hash[..20], hash, "source-" + hash[..20], video, context.CandidateId,
            mode, TimeSpan.Zero, context.Duration, TimeSpan.Zero,
            new("Exact cut used for title and description", CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop),
            transcript, supplied ? VisualSemanticDeterministicSummaryBuilder.Build(new(context.Duration, 0, 0, 0, 0,
                VisualSemanticIntegrityStatus.Clear, null, null, null, mode, [])) : null, prompt, model);
    }

    private static string SourceBinding(ClipEditorialContext context)
    {
        var source = new FileInfo(context.SourceFullPath);
        if (!source.Exists) throw new FileNotFoundException("The original clip source is unavailable.", source.FullName);
        return JsonSerializer.Serialize(new { start = context.SourceStart.Ticks, end = context.SourceEnd.Ticks,
            length = source.Length, modified = source.LastWriteTimeUtc.Ticks, source = source.FullName,
            candidateMode = CandidateMode(context).ToString() });
    }

    private static MomentOutputKind CandidateMode(ClipEditorialContext context)
    {
        var binding = context.Evidence.FirstOrDefault(item => item.Id == "scene-review-source-binding");
        if (binding is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(binding.Description);
                if (document.RootElement.TryGetProperty("candidateMode", out var mode) && mode.GetString() == "MontageSegment")
                    return MomentOutputKind.MontageSegment;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException) { }
        }
        return MomentOutputKind.StandaloneClip;
    }
}
