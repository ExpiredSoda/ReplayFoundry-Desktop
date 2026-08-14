namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

public static class EditorialMetadataStructuralFeatureExtractor
{
    public const int MaximumObservedTitleCharacters = 100;
    public const int MaximumObservedDescriptionCharacters = 5_000;
    public const int MaximumObservedDescriptionLines = 20;
    public const int MaximumObservedTags = 15;
    public const int MaximumObservedTagCharacters = 500;
    public const int MaximumObservedAverageTagCharacters = 50;

    public static EditorialMetadataPreferenceFeatureVector Extract(
        string title,
        string description,
        IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(tags);
        string[] tagSnapshot = tags
            .Select(tag => tag?.Trim() ?? throw new ArgumentException(
                "Editorial tags cannot contain null entries.",
                nameof(tags)))
            .Where(static tag => tag.Length > 0)
            .ToArray();

        int tagCharacters = SaturatingSum(
            tagSnapshot.Select(static tag => tag.Length));
        int tagPunctuation = SaturatingSum(
            tagSnapshot.Select(static tag => CountPunctuation(tag)));
        int descriptionLines = description.Length == 0
            ? 0
            : 1 + description.Count(static character => character == '\n');

        return new EditorialMetadataPreferenceFeatureVector(
        [
            new(
                EditorialMetadataPreferenceFeatureCode.TitleCharacterCount,
                Normalize(title.Length, MaximumObservedTitleCharacters)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .TitleUppercaseLetterRatio,
                UppercaseLetterRatio(title)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .TitleDigitCharacterRatio,
                CharacterRatio(title, char.IsDigit)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .TitlePunctuationCharacterRatio,
                CharacterRatio(title, char.IsPunctuation)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .DescriptionCharacterCount,
                Normalize(
                    description.Length,
                    MaximumObservedDescriptionCharacters)),
            new(
                EditorialMetadataPreferenceFeatureCode.DescriptionLineCount,
                Normalize(
                    descriptionLines,
                    MaximumObservedDescriptionLines)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .DescriptionDigitCharacterRatio,
                CharacterRatio(description, char.IsDigit)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .DescriptionPunctuationCharacterRatio,
                CharacterRatio(description, char.IsPunctuation)),
            new(
                EditorialMetadataPreferenceFeatureCode.TagCount,
                Normalize(tagSnapshot.Length, MaximumObservedTags)),
            new(
                EditorialMetadataPreferenceFeatureCode.TagCharacterCount,
                Normalize(
                    tagCharacters,
                    MaximumObservedTagCharacters)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .TagAverageCharacterCount,
                Normalize(
                    tagSnapshot.Length == 0
                        ? 0
                        : tagCharacters / (double)tagSnapshot.Length,
                    MaximumObservedAverageTagCharacters)),
            new(
                EditorialMetadataPreferenceFeatureCode
                    .TagPunctuationCharacterRatio,
                tagCharacters == 0
                    ? 0
                    : tagPunctuation / (double)tagCharacters),
        ]);
    }

    private static double UppercaseLetterRatio(string value)
    {
        int letters = value.Count(char.IsLetter);
        return letters == 0
            ? 0
            : value.Count(char.IsUpper) / (double)letters;
    }

    private static double CharacterRatio(
        string value,
        Func<char, bool> predicate) =>
        value.Length == 0
            ? 0
            : value.Count(predicate) / (double)value.Length;

    private static int CountPunctuation(string value) =>
        value.Count(char.IsPunctuation);

    private static int SaturatingSum(IEnumerable<int> values)
    {
        long sum = 0;
        foreach (int value in values)
        {
            sum += value;
            if (sum >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }
        return (int)sum;
    }

    private static double Normalize(double value, double maximum) =>
        Math.Clamp(value / maximum, 0, 1);
}
