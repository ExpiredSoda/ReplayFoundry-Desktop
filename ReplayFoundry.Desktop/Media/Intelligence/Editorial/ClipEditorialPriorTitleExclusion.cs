using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>
/// Retains audience copy that an editorial provider must not repeat for one
/// exact candidate cut. Prior titles are exclusion constraints only; they are
/// never evidence for what happened in the clip.
/// </summary>
public sealed record ClipEditorialPriorTitleExclusion
{
    public const int MaximumRetainedTitles = 8;

    public ClipEditorialPriorTitleExclusion(
        string candidateId,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        string title)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "A prior editorial title requires an exact candidate cut and nonblank copy.");
        }

        string normalizedTitle = title.Trim();
        if (normalizedTitle.Length > ClipEditorialMetadataDraft.MaximumTitleLength)
        {
            throw new ArgumentException(
                $"A prior editorial title cannot exceed {ClipEditorialMetadataDraft.MaximumTitleLength} characters.",
                nameof(title));
        }

        CandidateId = candidateId.Trim();
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        Title = normalizedTitle;
    }

    public string CandidateId { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public string Title { get; }

    public static ClipEditorialPriorTitleExclusion ForContext(
        ClipEditorialContext context,
        string title)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        string hashtag = context.GameContext.GameHashtag;
        string suffix = " " + hashtag;
        string body = title.Trim();
        if (body.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            body = body[..^suffix.Length].TrimEnd();
        }
        int maximumBodyLength =
            ClipEditorialMetadataDraft.MaximumTitleLength - suffix.Length;
        if (maximumBodyLength <= 0 || body.Length == 0)
        {
            throw new ArgumentException(
                "A prior editorial title requires audience copy before its game hashtag.",
                nameof(title));
        }
        if (body.Length > maximumBodyLength)
        {
            body = body[..maximumBodyLength].TrimEnd();
        }
        return new(
            context.CandidateId,
            context.SourceStart,
            context.SourceEnd,
            body + suffix);
    }

    internal static IReadOnlyList<string> MergeTitleHistory(
        IEnumerable<string>? history,
        string? currentTitle = null)
    {
        string[] values = (history ?? [])
            .Append(currentTitle)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Where(static value =>
                value.Length <= ClipEditorialMetadataDraft.MaximumTitleLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(MaximumRetainedTitles)
            .ToArray();
        return new ReadOnlyCollection<string>(values);
    }
}
