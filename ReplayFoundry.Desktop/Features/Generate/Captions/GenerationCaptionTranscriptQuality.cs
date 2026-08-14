using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Captions;

internal static partial class GenerationCaptionTranscriptQuality
{
    private static readonly HashSet<string> StopWords = new(
        [
            "a", "an", "and", "are", "at", "for", "from", "in", "is",
            "it", "of", "on", "or", "that", "the", "this", "to", "was",
            "were", "with",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static GenerationCaptionSuppressionReason Assess(
        AudioTranscriptionResult transcription)
    {
        ArgumentNullException.ThrowIfNull(transcription);
        AudioTranscriptionSegment[] lexicalSegments =
            SelectRenderableSegments(transcription);
        if (transcription.Segments.Count > 0 && lexicalSegments.Length == 0)
        {
            return GenerationCaptionSuppressionReason
                .NonSpeechOnlyTranscript;
        }
        if (transcription.Manifest.InputDuration < TimeSpan.FromSeconds(20) ||
            lexicalSegments.Length == 0)
        {
            return GenerationCaptionSuppressionReason.None;
        }

        string[] normalizedSegments = lexicalSegments
            .Select(static segment => Normalize(segment.Text))
            .Where(static text => text.Length > 0)
            .ToArray();
        bool repeatedSegment = normalizedSegments
            .GroupBy(static text => text, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() >= 3);

        string[] tokens = normalizedSegments
            .SelectMany(static text => TokenRegex().Matches(text))
            .Select(static match => match.Value.ToLowerInvariant())
            .ToArray();
        string[] contentTokens = tokens
            .Where(static token => !StopWords.Contains(token))
            .ToArray();
        bool dominatedByOneToken =
            contentTokens.Length >= 6 &&
            contentTokens
                .GroupBy(static token => token, StringComparer.Ordinal)
                .Max(static group => group.Count()) * 2 >=
                    contentTokens.Length &&
            contentTokens.Distinct(StringComparer.Ordinal).Count() <= 4;

        string[] bigrams = tokens
            .Zip(
                tokens.Skip(1),
                static (left, right) => left + "\u001F" + right)
            .ToArray();
        bool repeatedShortPhrase =
            tokens.Length >= 8 &&
            bigrams
                .GroupBy(static value => value, StringComparer.Ordinal)
                .Any(static group => group.Count() >= 3) &&
            tokens.Distinct(StringComparer.Ordinal).Count() <= 6;

        return repeatedSegment ||
               dominatedByOneToken ||
               repeatedShortPhrase
            ? GenerationCaptionSuppressionReason
                .RepetitiveLowInformationTranscript
            : GenerationCaptionSuppressionReason.None;
    }

    public static AudioTranscriptionSegment[] SelectRenderableSegments(
        AudioTranscriptionResult transcription)
    {
        ArgumentNullException.ThrowIfNull(transcription);
        AudioTranscriptionSegment[] plausibleSegments = transcription.Segments
            .Where(static segment =>
                TranscriptTextClassifier.Classify(segment.Text) ==
                TranscriptTextKind.Lexical &&
                IsPlausiblyReportedSpeech(segment))
            .ToArray();

        if (plausibleSegments.Length < 3)
        {
            return plausibleSegments;
        }

        var renderable = new List<AudioTranscriptionSegment>(
            plausibleSegments.Length);
        for (int index = 0; index < plausibleSegments.Length; index++)
        {
            if (!IsAggregateFollowedByFragments(
                    plausibleSegments,
                    index))
            {
                renderable.Add(plausibleSegments[index]);
            }
        }

        return renderable.ToArray();
    }

    private static bool IsAggregateFollowedByFragments(
        IReadOnlyList<AudioTranscriptionSegment> segments,
        int aggregateIndex)
    {
        AudioTranscriptionSegment aggregate = segments[aggregateIndex];
        TimeSpan aggregateDuration =
            aggregate.RelativeEnd - aggregate.RelativeStart;
        string[] aggregateTokens = Tokenize(aggregate.Text);
        if (aggregateDuration < TimeSpan.FromSeconds(6) ||
            aggregateTokens.Length < 5)
        {
            return false;
        }

        var fragmentTokens = new List<string>(aggregateTokens.Length);
        TimeSpan? fragmentStart = null;
        TimeSpan fragmentEnd = aggregate.RelativeEnd;
        for (int fragmentCount = 1;
             fragmentCount <= 4 &&
             aggregateIndex + fragmentCount < segments.Count;
             fragmentCount++)
        {
            AudioTranscriptionSegment fragment =
                segments[aggregateIndex + fragmentCount];
            TimeSpan gap = fragment.RelativeStart - fragmentEnd;
            if (!string.Equals(
                    aggregate.NeighborhoodId,
                    fragment.NeighborhoodId,
                    StringComparison.Ordinal) ||
                gap < TimeSpan.FromMilliseconds(-100) ||
                gap > TimeSpan.FromMilliseconds(250))
            {
                return false;
            }

            fragmentStart ??= fragment.RelativeStart;
            fragmentEnd = fragment.RelativeEnd;
            fragmentTokens.AddRange(Tokenize(fragment.Text));
            if (fragmentTokens.Count > aggregateTokens.Length)
            {
                return false;
            }
            if (fragmentCount < 2 ||
                fragmentTokens.Count < aggregateTokens.Length)
            {
                continue;
            }

            TimeSpan fragmentDuration =
                fragmentEnd - fragmentStart.Value;
            return fragmentDuration.TotalSeconds <=
                       aggregateDuration.TotalSeconds * 0.60 &&
                   aggregateTokens.SequenceEqual(
                       fragmentTokens,
                       StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsPlausiblyReportedSpeech(
        AudioTranscriptionSegment segment)
    {
        double[] reportedProbabilities = segment.Words
            .Select(static word => word.ProviderReportedProbability)
            .Where(static probability => probability.HasValue)
            .Select(static probability => probability!.Value)
            .ToArray();
        if (reportedProbabilities.Length == 0)
        {
            return true;
        }

        double average = reportedProbabilities.Average();
        int lexicalWordCount = TokenRegex().Matches(segment.Text).Count;
        double durationSeconds = segment.RelativeEnd
            .Subtract(segment.RelativeStart)
            .TotalSeconds;
        bool implausiblySparse =
            durationSeconds >= 4 &&
            lexicalWordCount / durationSeconds < 0.35;

        return average >= 0.20 && !implausiblySparse;
    }

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    private static string[] Tokenize(string value) =>
        TokenRegex().Matches(value)
            .Select(static match => match.Value)
            .ToArray();

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
