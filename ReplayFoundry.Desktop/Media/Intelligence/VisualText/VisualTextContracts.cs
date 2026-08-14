using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualText;

public enum VisualTextAnchorAuthority
{
    SingleFrameDiagnostic,
    RepeatedAcrossFrames,
}

public enum VisualTextAnchorSourceKind
{
    Line,
    Word,
}

public enum VisualTextWarningCode
{
    NoCompatibleLanguage,
    FrameRecognitionFailed,
    NoTextObserved,
}

public sealed record VisualTextWarning
{
    public VisualTextWarning(
        VisualTextWarningCode code,
        string message,
        TimeSpan? sourceTimestamp = null)
    {
        if (!Enum.IsDefined(code) || string.IsNullOrWhiteSpace(message) ||
            sourceTimestamp is TimeSpan timestamp && timestamp < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Visual-text warnings require a defined code, message, and valid timestamp.");
        }

        Code = code;
        Message = message.Trim();
        SourceTimestamp = sourceTimestamp;
    }

    public VisualTextWarningCode Code { get; }
    public string Message { get; }
    public TimeSpan? SourceTimestamp { get; }
}

public sealed record VisualTextProviderIdentity
{
    public VisualTextProviderIdentity(
        string name,
        string version,
        string backend,
        string runtimeVersion,
        string languageTag)
    {
        Name = Required(name, nameof(name));
        Version = Required(version, nameof(version));
        Backend = Required(backend, nameof(backend));
        RuntimeVersion = Required(runtimeVersion, nameof(runtimeVersion));
        LanguageTag = Required(languageTag, nameof(languageTag));
    }

    public string Name { get; }
    public string Version { get; }
    public string Backend { get; }
    public string RuntimeVersion { get; }
    public string LanguageTag { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > 256
            ? throw new ArgumentException(
                "Visual-text provider identity values must contain at most 256 characters.",
                parameterName)
            : value.Trim();
}

public sealed record VisualTextBoundingBox
{
    public VisualTextBoundingBox(
        double x,
        double y,
        double width,
        double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            x is < 0 or > 1 || y is < 0 or > 1 ||
            width is <= 0 or > 1 || height is <= 0 or > 1 ||
            x + width > 1.000001 || y + height > 1.000001)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Visual-text bounds must be finite normalized coordinates inside the sampled frame.");
        }

        X = x;
        Y = y;
        Width = Math.Min(width, 1 - x);
        Height = Math.Min(height, 1 - y);
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}

public sealed record VisualTextWord
{
    public VisualTextWord(string text, VisualTextBoundingBox bounds)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 256)
        {
            throw new ArgumentException(
                "An OCR word must contain at most 256 characters.",
                nameof(text));
        }
        Text = text.Trim();
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
    }

    public string Text { get; }
    public VisualTextBoundingBox Bounds { get; }
}

public sealed class VisualTextLine
{
    private readonly ReadOnlyCollection<VisualTextWord> _words;

    public VisualTextLine(string text, IEnumerable<VisualTextWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        VisualTextWord[] snapshot = words.ToArray();
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 1_000 ||
            snapshot.Length == 0 || snapshot.Any(static word => word is null))
        {
            throw new ArgumentException(
                "An OCR line requires bounded text and at least one word.");
        }
        Text = text.Trim();
        _words = Array.AsReadOnly(snapshot);
    }

    public string Text { get; }
    public IReadOnlyList<VisualTextWord> Words => _words;
}

public sealed class VisualTextFrameRequest
{
    public VisualTextFrameRequest(VideoPreviewFrame frame)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public VideoPreviewFrame Frame { get; }
}

public sealed class VisualTextFrameObservation
{
    private readonly ReadOnlyCollection<VisualTextLine> _lines;

    public VisualTextFrameObservation(
        VisualTextFrameRequest request,
        VisualTextProviderIdentity provider,
        IEnumerable<VisualTextLine> lines,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(lines);
        VisualTextLine[] snapshot = lines.ToArray();
        if (snapshot.Any(static line => line is null) || elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A visual-text observation requires valid lines and elapsed time.");
        }
        Request = request;
        Provider = provider;
        Elapsed = elapsed;
        _lines = Array.AsReadOnly(snapshot);
    }

    public VisualTextFrameRequest Request { get; }
    public VisualTextProviderIdentity Provider { get; }
    public IReadOnlyList<VisualTextLine> Lines => _lines;
    public TimeSpan Elapsed { get; }
}

public interface IVisualTextProvider
{
    string Name { get; }
    string Version { get; }
    bool IsAvailable { get; }

    Task<VisualTextFrameObservation> RecognizeAsync(
        VisualTextFrameRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisualTextAnchor
{
    private readonly ReadOnlyCollection<TimeSpan> _sourceTimestamps;

    public VisualTextAnchor(
        string normalizedText,
        string displayText,
        VisualTextAnchorAuthority authority,
        IReadOnlyList<TimeSpan> sourceTimestamps,
        VisualTextAnchorSourceKind sourceKind = VisualTextAnchorSourceKind.Line)
    {
        ArgumentNullException.ThrowIfNull(sourceTimestamps);
        TimeSpan[] timestamps = sourceTimestamps
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();
        if (string.IsNullOrWhiteSpace(normalizedText) ||
            normalizedText.Trim().Length > 1_000 ||
            string.IsNullOrWhiteSpace(displayText) ||
            displayText.Trim().Length > 1_000 ||
            !Enum.IsDefined(authority) ||
            !Enum.IsDefined(sourceKind) ||
            timestamps.Length == 0 ||
            timestamps.Any(static value => value < TimeSpan.Zero) ||
            authority == VisualTextAnchorAuthority.RepeatedAcrossFrames &&
            timestamps.Length < 2)
        {
            throw new ArgumentException(
                "A visual-text anchor requires bounded text and authority-consistent timestamps.");
        }
        NormalizedText = normalizedText.Trim();
        DisplayText = displayText.Trim();
        Authority = authority;
        SourceKind = sourceKind;
        _sourceTimestamps = Array.AsReadOnly(timestamps);
        EvidenceId = "visual-text-" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{SourceKind}\n{NormalizedText}")))
            .ToLowerInvariant()[..24];
    }

    public string NormalizedText { get; }
    public string DisplayText { get; }
    public VisualTextAnchorAuthority Authority { get; }
    public VisualTextAnchorSourceKind SourceKind { get; }
    public IReadOnlyList<TimeSpan> SourceTimestamps => _sourceTimestamps;
    public int OccurrenceCount => _sourceTimestamps.Count;
    public bool MayGroundAudienceCopy =>
        Authority == VisualTextAnchorAuthority.RepeatedAcrossFrames &&
        SourceKind == VisualTextAnchorSourceKind.Line &&
        HasAtLeastTwoWhitespaceSeparatedWords(DisplayText) &&
        char.IsLetterOrDigit(DisplayText[0]) &&
        char.IsLetterOrDigit(DisplayText[^1]);
    public string EvidenceId { get; }

    private static bool HasAtLeastTwoWhitespaceSeparatedWords(string value)
    {
        bool insideWord = false;
        int wordCount = 0;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                insideWord = false;
                continue;
            }

            if (!insideWord && ++wordCount >= 2)
            {
                return true;
            }

            insideWord = true;
        }

        return false;
    }
}

public sealed class ClipVisualTextContext
{
    public const string SamplingPolicyVersion = "visual-text-sampling-1.0";
    public const string StabilityPolicyVersion = "visual-text-stability-1.1";

    private readonly ReadOnlyCollection<VisualTextFrameObservation> _frames;
    private readonly ReadOnlyCollection<VisualTextAnchor> _anchors;
    private readonly ReadOnlyCollection<VisualTextWarning> _warnings;

    public ClipVisualTextContext(
        string candidateId,
        string sourceFullPath,
        NormalizedRectangle contentRegion,
        IEnumerable<VisualTextFrameObservation> frames,
        IEnumerable<VisualTextAnchor> anchors,
        IEnumerable<VisualTextWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "Clip visual text requires candidate and absolute source identity.");
        }
        ArgumentNullException.ThrowIfNull(contentRegion);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(anchors);
        VisualTextFrameObservation[] frameSnapshot = frames.ToArray();
        VisualTextAnchor[] anchorSnapshot = anchors.ToArray();
        VisualTextWarning[] warningSnapshot = warnings?.ToArray() ?? [];
        if (frameSnapshot.Any(static value => value is null) ||
            anchorSnapshot.Any(static value => value is null) ||
            warningSnapshot.Any(static value => value is null) ||
            frameSnapshot.Any(value => !value.Request.Frame.SourcePath.Equals(
                sourceFullPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Clip visual-text collections must be non-null and bound to one source.");
        }

        CandidateId = candidateId.Trim();
        SourceFullPath = Path.GetFullPath(sourceFullPath);
        ContentRegion = contentRegion;
        _frames = Array.AsReadOnly(frameSnapshot
            .OrderBy(static value => value.Request.Frame.RequestedTimestamp)
            .ToArray());
        _anchors = Array.AsReadOnly(anchorSnapshot
            .OrderByDescending(static value => value.MayGroundAudienceCopy)
            .ThenByDescending(static value => value.OccurrenceCount)
            .ThenBy(static value => value.NormalizedText, StringComparer.Ordinal)
            .ToArray());
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string CandidateId { get; }
    public string SourceFullPath { get; }
    public NormalizedRectangle ContentRegion { get; }
    public IReadOnlyList<VisualTextFrameObservation> Frames => _frames;
    public IReadOnlyList<VisualTextAnchor> Anchors => _anchors;
    public IReadOnlyList<VisualTextWarning> Warnings => _warnings;
    public IReadOnlyList<VisualTextAnchor> GroundingAnchors =>
        _anchors.Where(static value => value.MayGroundAudienceCopy).ToArray();
}
