using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public sealed class MomentContextBundleBuilder :
    IMomentContextBundleBuilder
{
    public const string SchemaVersion = "1.0";

    public MomentContextBundle Build(
        MomentContextBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ValidateRequest(request);

        var candidates =
            new List<MomentContextCandidate>(
                request.Deterministic.Candidates.Count);

        foreach (
            MomentDeterministicCandidateSnapshot deterministic in
            request.Deterministic.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MomentTranscriptMembership membership =
                request.Transcription.Memberships.Single(
                    item =>
                        string.Equals(
                            item.CandidateId,
                            deterministic.Id,
                            StringComparison.Ordinal));
            MomentTranscriptNeighborhoodContext neighborhood =
                request.Transcription.Neighborhoods.Single(
                    item =>
                        string.Equals(
                            item.Id,
                            membership.NeighborhoodId,
                            StringComparison.Ordinal));

            TranscriptSpan[] allNeighborhoodSpans =
                neighborhood.Segments
                    .Select(TranscriptSpan.FromProviderSegment)
                    .ToArray();
            CandidateTranscriptRelation[] relationships =
                allNeighborhoodSpans.Length == 0
                    ?
                    [
                        CandidateTranscriptRelationshipClassifier
                            .NoEvidence(deterministic.Id),
                    ]
                    : allNeighborhoodSpans
                        .Select(
                            span =>
                                CandidateTranscriptRelationshipClassifier
                                    .Classify(
                                        deterministic.Id,
                                        deterministic.Start,
                                        deterministic.End,
                                        span,
                                        request.Options
                                            .RelationshipTolerance))
                        .ToArray();
            string[] intersectingIds =
                relationships
                    .Where(
                        relation =>
                            relation.OverlapDuration >
                            TimeSpan.Zero)
                    .Select(
                        static relation =>
                            relation.TranscriptSpanId!)
                    .ToArray();
            TranscriptSpan[] intersectingSpans =
                allNeighborhoodSpans
                    .Where(
                        span =>
                            intersectingIds.Contains(
                                span.Id,
                                StringComparer.Ordinal))
                    .ToArray();
            AudioTranscriptionSegment[] intersectingSegments =
                neighborhood.Segments
                    .Where(
                        segment =>
                            intersectingSpans.Any(
                                span =>
                                    string.Equals(
                                        span.ProviderSegmentId,
                                        segment.Id,
                                        StringComparison.Ordinal)))
                    .ToArray();
            TranscriptEvidenceAssessment assessment =
                TranscriptEvidenceAssessment.FromProviderResult(
                    intersectingSegments,
                    neighborhood.DetectedLanguage,
                    neighborhood.Warnings,
                    neighborhood.Options.RequestWordTimestamps);
            MomentContextObservations observations =
                BuildObservations(
                    deterministic,
                    neighborhood,
                    intersectingSpans,
                    relationships,
                    request);
            MomentContextWarning[] warnings =
                BuildCandidateWarnings(
                    deterministic,
                    intersectingSpans,
                    assessment,
                    request.AudioRole);

            candidates.Add(
                new MomentContextCandidate(
                    deterministic,
                    neighborhood.Id,
                    request.AbsoluteAudioStreamIndex,
                    request.Transcription.MetadataTitleHint,
                    request.AudioRole,
                    intersectingSpans,
                    relationships,
                    assessment,
                    observations,
                    warnings));
        }

        stopwatch.Stop();

        MomentContextWarning[] rootWarnings =
            BuildRootWarnings(candidates, request.AudioRole);
        var manifest =
            new MomentContextManifest(
                SchemaVersion,
                request.Options.PolicyVersion,
                DateTimeOffset.UtcNow,
                candidates.Count,
                stopwatch.Elapsed,
                request.ModelLock,
                request.InputArtifacts,
                mediaProcessesInvoked: false,
                inferenceProcessesInvoked: false,
                deterministicFinderInvoked: false);

        return new MomentContextBundle(
            request,
            candidates,
            manifest,
            rootWarnings);
    }

    private static void ValidateRequest(
        MomentContextBundleRequest request)
    {
        MomentDeterministicContext deterministic =
            request.Deterministic;
        MomentTranscriptContext transcription =
            request.Transcription;

        if (!PathsMatch(
                deterministic.SourcePath,
                transcription.SourcePath) ||
            deterministic.SourceDuration !=
                transcription.SourceDuration ||
            !string.Equals(
                deterministic.FinderName,
                transcription.FinderName,
                StringComparison.Ordinal) ||
            !string.Equals(
                deterministic.FinderVersion,
                transcription.FinderVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                deterministic.PolicyHash,
                transcription.PolicyHash,
                StringComparison.Ordinal) ||
            request.AbsoluteAudioStreamIndex !=
                transcription.AbsoluteAudioStreamIndex)
        {
            throw new ArgumentException(
                "Deterministic and transcription contexts must describe the same source, finder, policy, duration, and exact absolute audio stream.");
        }

        if (transcription.Memberships.Count !=
                deterministic.Candidates.Count ||
            transcription.Memberships
                .Select(static item => item.CandidateId)
                .Distinct(StringComparer.Ordinal)
                .Count() != deterministic.Candidates.Count)
        {
            throw new ArgumentException(
                "Transcription membership must contain exactly one entry for every deterministic candidate.");
        }

        foreach (
            MomentDeterministicCandidateSnapshot candidate in
            deterministic.Candidates)
        {
            MomentTranscriptMembership? membership =
                transcription.Memberships.SingleOrDefault(
                    item =>
                        string.Equals(
                            item.CandidateId,
                            candidate.Id,
                            StringComparison.Ordinal));

            if (membership is null ||
                membership.CandidateStart != candidate.Start ||
                membership.CandidateEnd != candidate.End ||
                membership.CandidateSourceOrder !=
                    candidate.ProposalOrder)
            {
                throw new ArgumentException(
                    $"Transcript membership for candidate '{candidate.Id}' is missing or has a foreign window/order.");
            }
        }

        MomentContextModelLock modelLock = request.ModelLock;

        foreach (
            MomentTranscriptNeighborhoodContext neighborhood in
            transcription.Neighborhoods)
        {
            InferenceExecutionManifest execution =
                neighborhood.Execution;

            if (execution.WasCancelled ||
                !ProviderMatches(
                    execution.Provider,
                    modelLock.ProviderIdentity) ||
                !string.Equals(
                    execution.ExecutableSha256,
                    modelLock.ExecutableSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    execution.Model.Sha256,
                    modelLock.ModelSha256,
                    StringComparison.Ordinal) ||
                !OptionsMatch(
                    neighborhood.Options,
                    modelLock.Options))
            {
                throw new ArgumentException(
                    $"Transcription neighborhood '{neighborhood.Id}' does not match the frozen v0.2B provider/model/options lock.");
            }
        }
    }

    private static MomentContextObservations BuildObservations(
        MomentDeterministicCandidateSnapshot candidate,
        MomentTranscriptNeighborhoodContext neighborhood,
        IReadOnlyList<TranscriptSpan> intersectingSpans,
        IReadOnlyList<CandidateTranscriptRelation> relationships,
        MomentContextBundleRequest request)
    {
        TranscriptSpan[] lexical =
            intersectingSpans
                .Where(
                    span =>
                        TranscriptTextClassifier.Classify(span.Text) ==
                        TranscriptTextKind.Lexical)
                .ToArray();
        TranscriptSpan[] nonSpeech =
            intersectingSpans
                .Where(
                    span =>
                        TranscriptTextClassifier.Classify(span.Text) ==
                        TranscriptTextKind.NonSpeechToken)
                .ToArray();
        int lexicalCharacters =
            lexical.Sum(
                static span =>
                    span.Text.Count(
                        static character =>
                            !char.IsWhiteSpace(character)));
        int lexicalWords =
            lexical.Sum(
                static span =>
                    span.Text.Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Length);
        TimeSpan neighborhoodCovered =
            neighborhood.Segments.Aggregate(
                TimeSpan.Zero,
                static (total, segment) =>
                    total +
                    (segment.AbsoluteSourceEnd -
                     segment.AbsoluteSourceStart));
        TimeSpan candidateCovered =
            relationships.Aggregate(
                TimeSpan.Zero,
                static (total, relation) =>
                    total + relation.OverlapDuration);

        return new MomentContextObservations(
            lexical.Length,
            nonSpeech.Length,
            lexicalCharacters,
            lexicalWords,
            Ratio(neighborhoodCovered, neighborhood.Duration),
            Ratio(candidateCovered, candidate.Duration),
            relationships.Count(
                static relation =>
                    relation.Kind ==
                    CandidateTranscriptRelationKind
                        .FullyInsideCandidate),
            relationships.Count(
                static relation =>
                    relation.Kind ==
                    CandidateTranscriptRelationKind
                        .CrossesCandidateStart),
            relationships.Count(
                static relation =>
                    relation.Kind ==
                    CandidateTranscriptRelationKind
                        .CrossesCandidateEnd),
            intersectingSpans.Count(
                static span =>
                    span.TimingPrecision ==
                    TranscriptTimingPrecision
                        .SegmentBoundaryClamped),
            intersectingSpans.Count == 0,
            neighborhood.DetectedLanguage is not null,
            request.AbsoluteAudioStreamIndex,
            request.AudioRole.Role,
            request.AudioRole.Source,
            intersectingSpans.Select(
                static span => span.TimingPrecision));
    }

    private static MomentContextWarning[] BuildCandidateWarnings(
        MomentDeterministicCandidateSnapshot candidate,
        IReadOnlyList<TranscriptSpan> spans,
        TranscriptEvidenceAssessment assessment,
        AudioContentRoleAssignment role)
    {
        var warnings = new List<MomentContextWarning>
        {
            new(
                MomentContextWarningCode.ApproximateSegmentTiming,
                "Provider segment timestamps are approximate observations and must not adjust this candidate window.",
                candidate.Id),
            new(
                MomentContextWarningCode.WordTimestampsUnavailable,
                "The locked v0.2B configuration did not request word timestamps.",
                candidate.Id),
        };

        if (assessment.Status ==
            TranscriptEvidenceStatus.EmptyProviderOutput)
        {
            warnings.Add(
                new MomentContextWarning(
                    MomentContextWarningCode
                        .EmptyOutputDoesNotProveSilence,
                    "Empty provider output does not prove that the candidate contains no speech.",
                    candidate.Id));
        }
        else if (assessment.Status ==
                 TranscriptEvidenceStatus.LexicalText)
        {
            warnings.Add(
                new MomentContextWarning(
                    MomentContextWarningCode
                        .LexicalTextIsNotGroundTruth,
                    "Lexical provider output is not authoritative semantic truth.",
                    candidate.Id));
        }

        foreach (
            TranscriptSpan span in
            spans.Where(
                static span =>
                    span.TimingPrecision ==
                    TranscriptTimingPrecision
                        .SegmentBoundaryClamped))
        {
            warnings.Add(
                new MomentContextWarning(
                    MomentContextWarningCode.BoundaryClamped,
                    "Provider timing exceeded the bounded neighborhood and was clamped.",
                    candidate.Id,
                    span.Id));
        }

        if (role.Role == AudioContentRole.Unknown)
        {
            warnings.Add(
                new MomentContextWarning(
                    MomentContextWarningCode.AudioRoleUnknown,
                    "Audio content role is unavailable; stream title and transcript text were not used to infer it.",
                    candidate.Id));
        }

        return warnings.ToArray();
    }

    private static MomentContextWarning[] BuildRootWarnings(
        IEnumerable<MomentContextCandidate> candidates,
        AudioContentRoleAssignment role)
    {
        var warnings = new List<MomentContextWarning>
        {
            new(
                MomentContextWarningCode.ApproximateSegmentTiming,
                "Moment Context v0.3 records transcript/candidate relationships for diagnosis only and never recommends clip-boundary changes."),
            new(
                MomentContextWarningCode.WordTimestampsUnavailable,
                "The locked Base configuration provides segment timestamps only."),
        };

        if (candidates.Any(
                candidate =>
                    candidate.TranscriptEvidence.Status ==
                    TranscriptEvidenceStatus.EmptyProviderOutput))
        {
            warnings.Add(
                new MomentContextWarning(
                    MomentContextWarningCode
                        .EmptyOutputDoesNotProveSilence,
                    "At least one candidate has no provider text; this remains an uncertainty state rather than a silence claim."));
        }

        if (role.Role == AudioContentRole.Unknown)
        {
            warnings.Add(
                new MomentContextWarning(
                    MomentContextWarningCode.AudioRoleUnknown,
                    "The exact audio stream is preserved, but its content role is not available."));
        }

        return warnings.ToArray();
    }

    private static bool ProviderMatches(
        ReplayFoundry.Desktop.Media.Intelligence.InferenceProviderIdentity left,
        ReplayFoundry.Desktop.Media.Intelligence.InferenceProviderIdentity right) =>
        string.Equals(
            left.ProviderName,
            right.ProviderName,
            StringComparison.Ordinal) &&
        string.Equals(
            left.ProviderSemanticVersion,
            right.ProviderSemanticVersion,
            StringComparison.Ordinal) &&
        string.Equals(
            left.AdapterVersion,
            right.AdapterVersion,
            StringComparison.Ordinal);

    private static bool OptionsMatch(
        AudioTranscriptionOptions left,
        AudioTranscriptionOptions right) =>
        left.LanguageMode == right.LanguageMode &&
        Equals(left.RequestedLanguage?.Code, right.RequestedLanguage?.Code) &&
        left.TranslateToEnglish == right.TranslateToEnglish &&
        left.RequireSegmentTimestamps == right.RequireSegmentTimestamps &&
        left.RequestWordTimestamps == right.RequestWordTimestamps &&
        left.Temperature == right.Temperature &&
        left.ThreadCount == right.ThreadCount &&
        left.ProcessorHint == right.ProcessorHint &&
        left.MaximumProcessDuration == right.MaximumProcessDuration &&
        left.OutputFormatPolicy == right.OutputFormatPolicy &&
        string.Equals(
            left.PolicyVersion,
            right.PolicyVersion,
            StringComparison.Ordinal);

    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static double Ratio(
        TimeSpan numerator,
        TimeSpan denominator) =>
        denominator <= TimeSpan.Zero
            ? 0
            : Math.Clamp(
                numerator.TotalSeconds /
                denominator.TotalSeconds,
                0,
                1);
}
