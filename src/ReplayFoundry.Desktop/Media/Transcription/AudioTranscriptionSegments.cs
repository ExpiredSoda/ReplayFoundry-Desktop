using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Media.Transcription;

public sealed class AudioTranscriptionWord
{
    public AudioTranscriptionWord(
        string text,
        TimeSpan relativeStart,
        TimeSpan relativeEnd,
        TimeSpan absoluteSourceStart,
        TimeSpan absoluteSourceEnd,
        double? providerReportedProbability = null)
    {
        Text = RequiredText(text, nameof(text));
        ValidateTimes(
            relativeStart,
            relativeEnd,
            absoluteSourceStart,
            absoluteSourceEnd);
        ValidateOptionalRatio(
            providerReportedProbability,
            nameof(providerReportedProbability));

        RelativeStart = relativeStart;
        RelativeEnd = relativeEnd;
        AbsoluteSourceStart = absoluteSourceStart;
        AbsoluteSourceEnd = absoluteSourceEnd;
        ProviderReportedProbability =
            providerReportedProbability;
    }

    public string Text { get; }

    public TimeSpan RelativeStart { get; }

    public TimeSpan RelativeEnd { get; }

    public TimeSpan AbsoluteSourceStart { get; }

    public TimeSpan AbsoluteSourceEnd { get; }

    public double? ProviderReportedProbability { get; }

    internal static string RequiredText(
        string text,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Transcription text cannot be blank.",
                parameterName);
        }

        return text.Trim();
    }

    internal static void ValidateTimes(
        TimeSpan relativeStart,
        TimeSpan relativeEnd,
        TimeSpan absoluteSourceStart,
        TimeSpan absoluteSourceEnd)
    {
        if (relativeStart < TimeSpan.Zero ||
            relativeEnd < relativeStart ||
            absoluteSourceStart < TimeSpan.Zero ||
            absoluteSourceEnd < absoluteSourceStart ||
            relativeEnd - relativeStart !=
            absoluteSourceEnd - absoluteSourceStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativeEnd),
                "Relative and absolute transcription timestamps must be ordered and span the same duration.");
        }
    }

    internal static void ValidateOptionalRatio(
        double? value,
        string parameterName)
    {
        if (value is double actual &&
            (!double.IsFinite(actual) ||
             actual is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed class AudioTranscriptionSegment
{
    private readonly ReadOnlyCollection<AudioTranscriptionWord>
        _words;

    private readonly ReadOnlyCollection<AudioTranscriptionWarning>
        _warnings;

    public AudioTranscriptionSegment(
        string id,
        string neighborhoodId,
        string text,
        TimeSpan relativeStart,
        TimeSpan relativeEnd,
        TimeSpan absoluteSourceStart,
        TimeSpan absoluteSourceEnd,
        IEnumerable<AudioTranscriptionWord>? words = null,
        double? providerReportedConfidence = null,
        AudioTranscriptionLanguage? language = null,
        IEnumerable<AudioTranscriptionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(neighborhoodId))
        {
            throw new ArgumentException(
                "Transcription segments require stable segment and neighborhood identifiers.");
        }

        Text =
            AudioTranscriptionWord.RequiredText(
                text,
                nameof(text));
        AudioTranscriptionWord.ValidateTimes(
            relativeStart,
            relativeEnd,
            absoluteSourceStart,
            absoluteSourceEnd);
        AudioTranscriptionWord.ValidateOptionalRatio(
            providerReportedConfidence,
            nameof(providerReportedConfidence));

        AudioTranscriptionWord[] wordSnapshot =
            words?.ToArray() ??
            [];
        AudioTranscriptionWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (wordSnapshot.Any(static word => word is null) ||
            warningSnapshot.Any(static warning => warning is null))
        {
            throw new ArgumentException(
                "Transcription segment collections cannot contain null values.");
        }

        for (int index = 0;
             index < wordSnapshot.Length;
             index++)
        {
            AudioTranscriptionWord word =
                wordSnapshot[index];

            if (word.RelativeStart < relativeStart ||
                word.RelativeEnd > relativeEnd ||
                word.AbsoluteSourceStart < absoluteSourceStart ||
                word.AbsoluteSourceEnd > absoluteSourceEnd ||
                index > 0 &&
                word.RelativeStart <
                wordSnapshot[index - 1].RelativeEnd)
            {
                throw new ArgumentException(
                    "Words must be ordered and remain inside their segment.",
                    nameof(words));
            }
        }

        Id = id.Trim();
        NeighborhoodId = neighborhoodId.Trim();
        RelativeStart = relativeStart;
        RelativeEnd = relativeEnd;
        AbsoluteSourceStart = absoluteSourceStart;
        AbsoluteSourceEnd = absoluteSourceEnd;
        ProviderReportedConfidence =
            providerReportedConfidence;
        Language = language;
        _words = Array.AsReadOnly(wordSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string Id { get; }

    public string NeighborhoodId { get; }

    public string Text { get; }

    public TimeSpan RelativeStart { get; }

    public TimeSpan RelativeEnd { get; }

    public TimeSpan AbsoluteSourceStart { get; }

    public TimeSpan AbsoluteSourceEnd { get; }

    public double? ProviderReportedConfidence { get; }

    public AudioTranscriptionLanguage? Language { get; }

    public IReadOnlyList<AudioTranscriptionWord> Words =>
        _words;

    public IReadOnlyList<AudioTranscriptionWarning> Warnings =>
        _warnings;
}
