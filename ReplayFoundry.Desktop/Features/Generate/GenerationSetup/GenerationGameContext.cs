using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public enum GenerationGameContextOrigin
{
    SourcePathHint,
    ReusedUserMemory,
    UserConfirmed,
}

public sealed class GenerationSourceGameContext
{
    public const int MaximumGameNameLength = 120;
    public const int MaximumNotesLength = 1_500;

    public GenerationSourceGameContext(
        string sourceFullPath,
        string gameName,
        string? contextNotes,
        GenerationGameContextOrigin origin,
        bool useOpenGameKnowledge = false)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "Game context requires a fully qualified source path.",
                nameof(sourceFullPath));
        }
        if (string.IsNullOrWhiteSpace(gameName) ||
            gameName.Trim().Length > MaximumGameNameLength)
        {
            throw new ArgumentException(
                $"A game name must contain at most {MaximumGameNameLength} characters.",
                nameof(gameName));
        }
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        string? notes = string.IsNullOrWhiteSpace(contextNotes)
            ? null
            : contextNotes.Trim();
        if (notes?.Length > MaximumNotesLength)
        {
            throw new ArgumentException(
                $"Game context notes cannot exceed {MaximumNotesLength} characters.",
                nameof(contextNotes));
        }

        SourceFullPath = Path.GetFullPath(sourceFullPath);
        GameName = gameName.Trim();
        ContextNotes = notes;
        Origin = origin;
        UseOpenGameKnowledge = useOpenGameKnowledge;
        GameHashtag = BuildHashtag(GameName);
    }

    public string SourceFullPath { get; }

    public string GameName { get; }

    public string? ContextNotes { get; }

    public GenerationGameContextOrigin Origin { get; }

    public bool UseOpenGameKnowledge { get; }

    public string GameHashtag { get; }

    public GenerationSourceGameContext WithUserConfirmation(
        string gameName,
        string? contextNotes,
        bool? useOpenGameKnowledge = null) =>
        new(
            SourceFullPath,
            gameName,
            contextNotes,
            GenerationGameContextOrigin.UserConfirmed,
            useOpenGameKnowledge ?? UseOpenGameKnowledge);

    public static GenerationSourceGameContext CreatePathHint(
        string sourceFullPath) =>
        new(
            sourceFullPath,
            SuggestedGameName(sourceFullPath),
            contextNotes: null,
            GenerationGameContextOrigin.SourcePathHint);

    public static string SuggestedGameName(string sourceFullPath)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "A game-name hint requires a fully qualified source path.",
                nameof(sourceFullPath));
        }

        DirectoryInfo? directory = Directory.GetParent(sourceFullPath);
        if (directory?.Name.Equals(
                "Vertical",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            directory = directory.Parent;
        }
        string value = directory?.Name ??
            Path.GetFileNameWithoutExtension(sourceFullPath);
        return string.IsNullOrWhiteSpace(value)
            ? "Gameplay"
            : value.Trim();
    }

    private static string BuildHashtag(string gameName)
    {
        var value = new StringBuilder(gameName.Length + 1);
        value.Append('#');
        foreach (char character in gameName)
        {
            if (char.IsLetterOrDigit(character))
            {
                value.Append(character);
            }
        }
        return value.Length == 1
            ? "#Gameplay"
            : value.ToString();
    }
}

public sealed class GenerationGameContextSettings
{
    private readonly ReadOnlyCollection<GenerationSourceGameContext>
        _sources;

    public GenerationGameContextSettings(
        IEnumerable<GenerationSourceGameContext>? sources = null)
    {
        GenerationSourceGameContext[] snapshot = sources?.ToArray() ?? [];
        if (snapshot.Any(static value => value is null) ||
            snapshot.GroupBy(
                    static value => value.SourceFullPath,
                    StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() != 1))
        {
            throw new ArgumentException(
                "Game context sources must be non-null and path-unique.",
                nameof(sources));
        }
        _sources = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<GenerationSourceGameContext> Sources => _sources;

    public GenerationSourceGameContext? Find(string sourceFullPath) =>
        _sources.SingleOrDefault(value => value.SourceFullPath.Equals(
            sourceFullPath,
            StringComparison.OrdinalIgnoreCase));

    public static GenerationGameContextSettings Empty { get; } = new();
}

public interface IGenerationGameContextMemory
{
    GenerationSourceGameContext? Find(string sourceFullPath);

    void Remember(IEnumerable<GenerationSourceGameContext> contexts);
}
