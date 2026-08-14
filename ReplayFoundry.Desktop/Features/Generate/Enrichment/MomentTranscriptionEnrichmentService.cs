using System.Diagnostics;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Enrichment;

public sealed class MomentTranscriptionEnrichmentService
{
    private readonly IAudioSegmentExtractor _extractor;
    private readonly IAudioTranscriptionProvider _provider;
    private readonly AudioTranscriptionOptions
        _transcriptionOptions;
    private readonly AudioTranscriptionModelSettings
        _modelSettings;

    public MomentTranscriptionEnrichmentService(
        IAudioSegmentExtractor extractor,
        IAudioTranscriptionProvider provider,
        AudioTranscriptionOptions transcriptionOptions,
        AudioTranscriptionModelSettings modelSettings)
    {
        _extractor =
            extractor ??
            throw new ArgumentNullException(nameof(extractor));
        _provider =
            provider ??
            throw new ArgumentNullException(nameof(provider));
        _transcriptionOptions =
            transcriptionOptions ??
            throw new ArgumentNullException(
                nameof(transcriptionOptions));
        _modelSettings =
            modelSettings ??
            throw new ArgumentNullException(nameof(modelSettings));
    }

    public async Task<MomentTranscriptionEnrichmentResult>
        EnrichAsync(
            MomentEnrichmentRequest request,
            IProgress<MomentEnrichmentProgressUpdate>? progress,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(
            new MomentEnrichmentProgressUpdate(
                MomentEnrichmentProgressPhase
                    .PlanningNeighborhoods,
                "Planning bounded candidate neighborhoods.",
                0,
                0));
        CandidateNeighborhoodPlan plan =
            CandidateNeighborhoodPlanner.Plan(
                request,
                cancellationToken);

        if (plan.Neighborhoods.Count == 0)
        {
            throw new InvalidOperationException(
                "The selected proposal policy produced no transcription neighborhoods.");
        }

        var results =
            new List<NeighborhoodTranscriptionEnrichment>(
                plan.Neighborhoods.Count);
        DateTimeOffset startedAtUtc =
            DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        for (int index = 0;
             index < plan.Neighborhoods.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CandidateNeighborhood neighborhood =
                plan.Neighborhoods[index];

            try
            {
                progress?.Report(
                    new MomentEnrichmentProgressUpdate(
                        MomentEnrichmentProgressPhase
                            .ExtractingAudio,
                        $"Extracting audio neighborhood {index + 1} of {plan.Neighborhoods.Count}.",
                        index + 1,
                        plan.Neighborhoods.Count));
                var extractionRequest =
                    new AudioSegmentExtractionRequest(
                        neighborhood.Id,
                        request.SourcePath,
                        request.SourceDuration,
                        request.AbsoluteAudioStreamIndex,
                        neighborhood.Start,
                        neighborhood.End,
                        _transcriptionOptions
                            .MaximumProcessDuration);

                using ExtractedAudioSegment extracted =
                    await _extractor.ExtractAsync(
                        extractionRequest,
                        cancellationToken);
                progress?.Report(
                    new MomentEnrichmentProgressUpdate(
                        MomentEnrichmentProgressPhase
                            .TranscribingAudio,
                        $"Transcribing neighborhood {index + 1} of {plan.Neighborhoods.Count}.",
                        index + 1,
                        plan.Neighborhoods.Count));
                var transcriptionRequest =
                    new AudioTranscriptionRequest(
                        neighborhood.Id,
                        extracted.Path,
                        neighborhood.Duration,
                        neighborhood.Start,
                        request.SourceDuration,
                        request.AbsoluteAudioStreamIndex,
                        _transcriptionOptions,
                        _modelSettings);
                AudioTranscriptionResult transcription =
                    await _provider.TranscribeAsync(
                        transcriptionRequest,
                        cancellationToken);

                results.Add(
                    new NeighborhoodTranscriptionEnrichment(
                        neighborhood,
                        extracted.Manifest,
                        transcription));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MomentEnrichmentException(
                    $"Candidate-neighborhood enrichment failed for '{neighborhood.Id}'.",
                    neighborhood.Id,
                    exception);
            }
        }

        progress?.Report(
            new MomentEnrichmentProgressUpdate(
                MomentEnrichmentProgressPhase
                    .BindingCandidates,
                "Binding timed transcript observations to deterministic candidates.",
                plan.Neighborhoods.Count,
                plan.Neighborhoods.Count));
        (
            CandidateTranscriptBinding[] bindings,
            MomentEnrichmentWarning[] warnings) =
            BuildBindings(
                request,
                plan,
                results);
        stopwatch.Stop();
        DateTimeOffset completedAtUtc =
            DateTimeOffset.UtcNow;
        NeighborhoodTranscriptionEnrichment first =
            results[0];
        var manifest =
            new MomentEnrichmentManifest(
                request.Options.PolicyVersion,
                _provider.Identity,
                first.Transcription.Manifest.Execution.Model,
                plan.Neighborhoods.Count,
                plan.Neighborhoods.Count,
                plan.Neighborhoods.Count,
                startedAtUtc,
                completedAtUtc,
                stopwatch.Elapsed);
        var result =
            new MomentTranscriptionEnrichmentResult(
                request,
                plan,
                results,
                bindings,
                manifest,
                warnings);

        progress?.Report(
            new MomentEnrichmentProgressUpdate(
                MomentEnrichmentProgressPhase.Complete,
                "Local candidate transcription is complete.",
                plan.Neighborhoods.Count,
                plan.Neighborhoods.Count));

        return result;
    }

    private static (
        CandidateTranscriptBinding[] Bindings,
        MomentEnrichmentWarning[] Warnings)
        BuildBindings(
            MomentEnrichmentRequest request,
            CandidateNeighborhoodPlan plan,
            IReadOnlyList<NeighborhoodTranscriptionEnrichment>
                results)
    {
        var bindings =
            new List<CandidateTranscriptBinding>();
        var warnings =
            new List<MomentEnrichmentWarning>();

        foreach (CandidateNeighborhood neighborhood in
                 plan.Neighborhoods)
        {
            NeighborhoodTranscriptionEnrichment result =
                results.Single(
                    item =>
                        string.Equals(
                            item.Neighborhood.Id,
                            neighborhood.Id,
                            StringComparison.Ordinal));

            foreach (CandidateNeighborhoodMembership membership in
                     neighborhood.Memberships)
            {
                AudioTranscriptionSegment[] segments =
                    result.Transcription.Segments
                        .Where(
                            segment =>
                                segment.AbsoluteSourceEnd >
                                    membership.CandidateStart &&
                                segment.AbsoluteSourceStart <
                                    membership.CandidateEnd)
                        .ToArray();
                bool beginsInside =
                    segments.Any(
                        segment =>
                            segment.AbsoluteSourceStart <
                                membership.CandidateStart &&
                            segment.AbsoluteSourceEnd >
                                membership.CandidateStart);
                bool endsInside =
                    segments.Any(
                        segment =>
                            segment.AbsoluteSourceStart <
                                membership.CandidateEnd &&
                            segment.AbsoluteSourceEnd >
                                membership.CandidateEnd);
                CandidateTranscriptBoundaryStatus status =
                    (beginsInside, endsInside) switch
                    {
                        (true, true) =>
                            CandidateTranscriptBoundaryStatus
                                .BeginsAndEndsInsideSegments,
                        (true, false) =>
                            CandidateTranscriptBoundaryStatus
                                .BeginsInsideSegment,
                        (false, true) =>
                            CandidateTranscriptBoundaryStatus
                                .EndsInsideSegment,
                        _ =>
                            CandidateTranscriptBoundaryStatus
                                .Complete,
                    };

                if (beginsInside || endsInside)
                {
                    warnings.Add(
                        new MomentEnrichmentWarning(
                            MomentEnrichmentWarningCode
                                .CandidateBoundaryCutsSegment,
                            "The deterministic candidate boundary intersects a provider transcript segment.",
                            neighborhood.Id,
                            membership.CandidateId));
                }

                CandidateTranscriptFeatures features =
                    BuildFeatures(
                        membership,
                        segments,
                        result.Transcription);

                bindings.Add(
                    new CandidateTranscriptBinding(
                        membership.CandidateId,
                        membership.CandidateStart,
                        membership.CandidateEnd,
                        neighborhood.Id,
                        segments,
                        status,
                        features));
            }
        }

        return (
            bindings
                .OrderBy(
                    binding =>
                        request.Candidates.Single(
                            candidate =>
                                string.Equals(
                                    candidate.CandidateId,
                                    binding.CandidateId,
                                    StringComparison.Ordinal))
                            .SourceOrder)
                .ToArray(),
            warnings.ToArray());
    }

    private static CandidateTranscriptFeatures BuildFeatures(
        CandidateNeighborhoodMembership membership,
        IReadOnlyList<AudioTranscriptionSegment> segments,
        AudioTranscriptionResult transcription)
    {
        TimeSpan candidateDuration =
            membership.CandidateEnd -
            membership.CandidateStart;
        double coveredSeconds =
            MergeCoverage(
                segments.Select(
                    segment =>
                        (
                            Start:
                                segment.AbsoluteSourceStart <
                                membership.CandidateStart
                                    ? membership.CandidateStart
                                    : segment.AbsoluteSourceStart,
                            End:
                                segment.AbsoluteSourceEnd >
                                membership.CandidateEnd
                                    ? membership.CandidateEnd
                                    : segment.AbsoluteSourceEnd
                        )));
        int words =
            segments.Sum(
                static segment =>
                    segment.Words.Count > 0
                        ? segment.Words.Count
                        : CountWords(segment.Text));
        double? wordsPerSecond =
            coveredSeconds > 0
                ? words / coveredSeconds
                : null;
        string text =
            string.Join(
                " ",
                segments.Select(
                    static segment =>
                        segment.Text));

        return new CandidateTranscriptFeatures(
            Math.Clamp(
                coveredSeconds /
                candidateDuration.TotalSeconds,
                0,
                1),
            segments.Count,
            words,
            wordsPerSecond,
            segments.Any(
                segment =>
                    segment.AbsoluteSourceStart <
                        membership.CandidateStart &&
                    segment.AbsoluteSourceEnd >
                        membership.CandidateStart),
            segments.Any(
                segment =>
                    segment.AbsoluteSourceStart <
                        membership.CandidateEnd &&
                    segment.AbsoluteSourceEnd >
                        membership.CandidateEnd),
            segments.Count(
                segment =>
                    segment.AbsoluteSourceStart >=
                        membership.CandidateStart &&
                    segment.AbsoluteSourceEnd <=
                        membership.CandidateEnd),
            text.Count(static character => character == '?'),
            text.Count(static character => character == '!'),
            transcription.DetectedLanguage is not null,
            overlappingSpeechReported: null,
            silenceOnlyNeighborhood: null);
    }

    private static int CountWords(string text) =>
        text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)
            .Length;

    private static double MergeCoverage(
        IEnumerable<(TimeSpan Start, TimeSpan End)> intervals)
    {
        (TimeSpan Start, TimeSpan End)[] ordered =
            intervals
                .Where(
                    static item =>
                        item.End > item.Start)
                .OrderBy(static item => item.Start)
                .ToArray();

        if (ordered.Length == 0)
        {
            return 0;
        }

        TimeSpan start = ordered[0].Start;
        TimeSpan end = ordered[0].End;
        long ticks = 0;

        for (int index = 1;
             index < ordered.Length;
             index++)
        {
            if (ordered[index].Start <= end)
            {
                if (ordered[index].End > end)
                {
                    end = ordered[index].End;
                }

                continue;
            }

            ticks += (end - start).Ticks;
            start = ordered[index].Start;
            end = ordered[index].End;
        }

        ticks += (end - start).Ticks;

        return TimeSpan.FromTicks(ticks)
            .TotalSeconds;
    }
}
