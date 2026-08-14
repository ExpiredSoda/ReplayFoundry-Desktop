using System.Globalization;
using System.Text;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;

public sealed class GenerationVisualTextAnalysisRequest
{
    private readonly IReadOnlyList<TimeSpan> _priorityTimestamps;

    public GenerationVisualTextAnalysisRequest(
        ClipEditorialContext context,
        MediaProbeResult media,
        IEnumerable<TimeSpan>? priorityTimestamps = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Media = media ?? throw new ArgumentNullException(nameof(media));
        if (!context.SourceFullPath.Equals(
                media.FullPath,
                StringComparison.OrdinalIgnoreCase) ||
            context.SourceDuration != media.Duration ||
            context.GameplayRegion is null)
        {
            throw new ArgumentException(
                "Visual-text analysis requires matching retained media and confirmed Gameplay geometry.");
        }

        TimeSpan[] priorities = (priorityTimestamps ?? [])
            .Where(value => value >= context.SourceStart &&
                            value < context.SourceEnd)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();
        _priorityTimestamps = Array.AsReadOnly(priorities);
    }

    public ClipEditorialContext Context { get; }
    public MediaProbeResult Media { get; }
    public IReadOnlyList<TimeSpan> PriorityTimestamps => _priorityTimestamps;
}

public interface IGenerationVisualTextAnalysisService
{
    bool IsAvailable { get; }

    Task<ClipEditorialContext> EnrichAsync(
        GenerationVisualTextAnalysisRequest request,
        CancellationToken cancellationToken);
}

public sealed class GenerationVisualTextAnalysisService :
    IGenerationVisualTextAnalysisService
{
    public const int MaximumSampleCount = 8;
    public const int MaximumGroundingAnchorCount = 24;
    public const int MaximumDiagnosticAnchorCount = 32;
    private const int OcrMaximumDimension = 1920;

    private readonly IVideoPreviewFrameProvider _frames;
    private readonly IVisualTextProvider _provider;

    public GenerationVisualTextAnalysisService(
        IVideoPreviewFrameProvider frames,
        IVisualTextProvider provider)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public bool IsAvailable => _provider.IsAvailable;

    public async Task<ClipEditorialContext> EnrichAsync(
        GenerationVisualTextAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var observations = new List<VisualTextFrameObservation>();
        var warnings = new List<VisualTextWarning>();
        if (!IsAvailable)
        {
            warnings.Add(new VisualTextWarning(
                VisualTextWarningCode.NoCompatibleLanguage,
                "No compatible Windows OCR language is installed; visual text was left unavailable."));
        }
        else
        {
            foreach (TimeSpan timestamp in BuildSampleTimestamps(request))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    VideoPreviewFrame frame = await _frames.GetFrameAsync(
                        new VideoPreviewFrameRequest(
                            request.Media,
                            timestamp,
                            OcrMaximumDimension,
                            OcrMaximumDimension,
                            request.Context.GameplayRegion),
                        cancellationToken);
                    observations.Add(await _provider.RecognizeAsync(
                        new VisualTextFrameRequest(frame),
                        cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    warnings.Add(new VisualTextWarning(
                        VisualTextWarningCode.FrameRecognitionFailed,
                        $"Visual text could not be read at {timestamp:c}: {exception.Message}",
                        timestamp));
                }
            }
        }

        VisualTextAnchor[] anchors = BuildAnchors(observations);
        if (observations.Count > 0 && anchors.Length == 0)
        {
            warnings.Add(new VisualTextWarning(
                VisualTextWarningCode.NoTextObserved,
                "No bounded text was observed in the sampled Gameplay frames."));
        }
        return request.Context.WithVisualText(
            new ClipVisualTextContext(
                request.Context.CandidateId,
                request.Context.SourceFullPath,
                request.Context.GameplayRegion!,
                observations,
                anchors,
                warnings));
    }

    internal static IReadOnlyList<TimeSpan> BuildSampleTimestamps(
        GenerationVisualTextAnalysisRequest request)
    {
        TimeSpan start = request.Context.SourceStart;
        TimeSpan end = request.Context.SourceEnd;
        long duration = end.Ticks - start.Ticks;
        var candidates = new List<(TimeSpan Time, int Priority)>();
        foreach (TimeSpan timestamp in request.PriorityTimestamps)
        {
            candidates.Add((Clamp(timestamp, start, end), 0));
        }
        int[] fractions = [0, 1, 2, 3, 4];
        foreach (int fraction in fractions)
        {
            long ticks = start.Ticks + duration * fraction / 4;
            candidates.Add((Clamp(TimeSpan.FromTicks(ticks), start, end), 1));
        }

        TimeSpan[] selected = candidates
            .OrderBy(static value => value.Priority)
            .ThenBy(static value => value.Time)
            .Select(static value => value.Time)
            .Distinct()
            .Take(MaximumSampleCount)
            .OrderBy(static value => value)
            .ToArray();
        return Array.AsReadOnly(selected);
    }

    internal static VisualTextAnchor[] BuildAnchors(
        IEnumerable<VisualTextFrameObservation> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var occurrences = new List<TextOccurrence>();
        foreach (VisualTextFrameObservation frame in frames)
        {
            TimeSpan timestamp = frame.Request.Frame.RequestedTimestamp;
            foreach (VisualTextLine line in frame.Lines)
            {
                AddOccurrence(occurrences, line.Text, timestamp, isLine: true);
                foreach (VisualTextWord word in line.Words)
                {
                    AddOccurrence(occurrences, word.Text, timestamp, isLine: false);
                }
            }
        }

        VisualTextAnchor[] all = occurrences
            .GroupBy(static value => value.Normalized, StringComparer.Ordinal)
            .Select(group =>
            {
                TimeSpan[] timestamps = group
                    .Select(static value => value.Timestamp)
                    .Distinct()
                    .OrderBy(static value => value)
                    .ToArray();
                TextOccurrence display = group
                    .OrderByDescending(static value => value.IsLine)
                    .ThenByDescending(static value => value.Display.Length)
                    .ThenBy(static value => value.Display, StringComparer.Ordinal)
                    .First();
                return new VisualTextAnchor(
                    group.Key,
                    display.Display,
                    timestamps.Length >= 2
                        ? VisualTextAnchorAuthority.RepeatedAcrossFrames
                        : VisualTextAnchorAuthority.SingleFrameDiagnostic,
                    timestamps,
                    display.IsLine
                        ? VisualTextAnchorSourceKind.Line
                        : VisualTextAnchorSourceKind.Word);
            })
            .OrderByDescending(static value => value.MayGroundAudienceCopy)
            .ThenByDescending(static value => value.OccurrenceCount)
            .ThenByDescending(static value => value.DisplayText.Length)
            .ThenBy(static value => value.NormalizedText, StringComparer.Ordinal)
            .Take(MaximumGroundingAnchorCount + MaximumDiagnosticAnchorCount)
            .ToArray();
        return all
            .Where((anchor, index) =>
                anchor.MayGroundAudienceCopy
                    ? all.Take(index + 1).Count(static value => value.MayGroundAudienceCopy) <=
                      MaximumGroundingAnchorCount
                    : all.Take(index + 1).Count(static value => !value.MayGroundAudienceCopy) <=
                      MaximumDiagnosticAnchorCount)
            .ToArray();
    }

    private static void AddOccurrence(
        ICollection<TextOccurrence> target,
        string display,
        TimeSpan timestamp,
        bool isLine)
    {
        string normalized = Normalize(display);
        int letterCount = normalized.Count(char.IsLetter);
        if (normalized.Length < 3 || letterCount < 2 ||
            (!isLine && normalized.Contains(' ')))
        {
            return;
        }
        target.Add(new TextOccurrence(normalized, display.Trim(), timestamp, isLine));
    }

    internal static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        bool priorSpace = false;
        foreach (char character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                priorSpace = false;
            }
            else if (!priorSpace && result.Length > 0)
            {
                result.Append(' ');
                priorSpace = true;
            }
        }
        return result.ToString().Trim();
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan start, TimeSpan end)
    {
        TimeSpan last = end - TimeSpan.FromTicks(1);
        return value < start ? start : value > last ? last : value;
    }

    private sealed record TextOccurrence(
        string Normalized,
        string Display,
        TimeSpan Timestamp,
        bool IsLine);
}
