using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ReplayFoundry.Desktop.Features.Generate.Guidance;

public enum UserMomentGuidanceKind
{
    PriorityPoint,
    PriorityRange,
}

public sealed class UserMomentGuidance
{
    private UserMomentGuidance(
        string id,
        string sourceFullPath,
        TimeSpan sourceDuration,
        UserMomentGuidanceKind kind,
        TimeSpan start,
        TimeSpan end)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "Human moment guidance requires stable identity and a fully qualified source path.");
        }
        if (!Enum.IsDefined(kind) ||
            sourceDuration <= TimeSpan.Zero ||
            start < TimeSpan.Zero ||
            end < start ||
            end > sourceDuration ||
            (kind == UserMomentGuidanceKind.PriorityPoint && end != start) ||
            (kind == UserMomentGuidanceKind.PriorityRange && end == start))
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        Id = id.Trim();
        SourceFullPath = Path.GetFullPath(sourceFullPath);
        SourceDuration = sourceDuration;
        Kind = kind;
        Start = start;
        End = end;
    }

    public string Id { get; }
    public string SourceFullPath { get; }
    public TimeSpan SourceDuration { get; }
    public UserMomentGuidanceKind Kind { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public TimeSpan Timestamp => Start;
    public TimeSpan Duration => End - Start;
    public bool ReservesCandidateSearch =>
        Kind == UserMomentGuidanceKind.PriorityRange &&
        Duration <= GenerationMomentGuidance.ReservedRangeMaximumDuration;

    public static UserMomentGuidance CreatePoint(
        string sourceFullPath,
        TimeSpan sourceDuration,
        TimeSpan timestamp) =>
        Create(
            sourceFullPath,
            sourceDuration,
            UserMomentGuidanceKind.PriorityPoint,
            timestamp,
            timestamp);

    public static UserMomentGuidance CreateRange(
        string sourceFullPath,
        TimeSpan sourceDuration,
        TimeSpan start,
        TimeSpan end) =>
        Create(
            sourceFullPath,
            sourceDuration,
            UserMomentGuidanceKind.PriorityRange,
            start,
            end);

    private static UserMomentGuidance Create(
        string sourceFullPath,
        TimeSpan sourceDuration,
        UserMomentGuidanceKind kind,
        TimeSpan start,
        TimeSpan end)
    {
        string canonical = string.Join(
            "|",
            Path.GetFullPath(sourceFullPath).ToUpperInvariant(),
            sourceDuration.Ticks,
            kind,
            start.Ticks,
            end.Ticks);
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new UserMomentGuidance(
            $"human-{hash[..20].ToLowerInvariant()}",
            sourceFullPath,
            sourceDuration,
            kind,
            start,
            end);
    }
}

public sealed class GenerationMomentGuidance
{
    public static readonly TimeSpan ReservedRangeMaximumDuration =
        TimeSpan.FromMinutes(3);

    private readonly ReadOnlyCollection<UserMomentGuidance> _items;

    public GenerationMomentGuidance(
        IEnumerable<UserMomentGuidance>? items = null)
    {
        UserMomentGuidance[] snapshot = items?
            .OrderBy(static item => item.SourceFullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Start)
            .ThenBy(static item => item.End)
            .ThenBy(static item => item.Kind)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray() ?? [];
        if (snapshot.Any(static item => item is null) ||
            snapshot.GroupBy(static item => item.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Human moment guidance entries must be non-null with unique identities.",
                nameof(items));
        }
        _items = Array.AsReadOnly(snapshot);
    }

    public static GenerationMomentGuidance Empty { get; } = new();

    public IReadOnlyList<UserMomentGuidance> Items => _items;
    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    public IReadOnlyList<UserMomentGuidance> ForSource(
        string sourceFullPath)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "A fully qualified source path is required.",
                nameof(sourceFullPath));
        }
        string fullPath = Path.GetFullPath(sourceFullPath);
        return _items
            .Where(
                item => string.Equals(
                    item.SourceFullPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
