using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionAlignmentProvenance(int SchemaVersion, string ModelIdentity, string ModelSha256,
    string ExtractedPcmSha256, string SourceIdentitySha256, long SourceLengthBytes, DateTime SourceLastWriteUtc,
    int AudioStreamIndex, TimeSpan SourceStart, TimeSpan SourceEnd, string TextSha256,
    IReadOnlyList<CorrectedCaptionAlignedWord> Words)
{
    public static string TextIdentity(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string Create(CorrectedCaptionAlignmentRequest request, CorrectedCaptionAlignmentResult result,
        long sourceLength, DateTime sourceLastWriteUtc)
    {
        if (string.IsNullOrWhiteSpace(result.ModelIdentity) || result.ModelIdentity.Length > 300 ||
            !IsHash(result.ModelSha256) || !IsHash(result.ExtractedPcmSha256))
            throw new InvalidDataException("Alignment returned incomplete model or audio identity; existing word times were kept.");
        return JsonSerializer.Serialize(new StudioCaptionAlignmentProvenance(1, result.ModelIdentity, result.ModelSha256,
            result.ExtractedPcmSha256!, TextIdentity(Path.GetFullPath(request.SourceFullPath).ToUpperInvariant()), sourceLength,
            sourceLastWriteUtc, request.AbsoluteAudioStreamIndex, request.SourceStart, request.SourceEnd,
            TextIdentity(request.CorrectedText), result.Words));
    }

    public static StudioCaptionAlignmentProvenance? Read(string? json) => ReadAll(json).FirstOrDefault();
    public static IReadOnlyList<StudioCaptionAlignmentProvenance> ReadAll(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            var values = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.Deserialize<StudioCaptionAlignmentProvenance[]>() ?? []
                : new[] { document.RootElement.Deserialize<StudioCaptionAlignmentProvenance>()! };
            return values.Length <= 64 && values.All(value => value is { SchemaVersion: 1, Words.Count: > 0 and <= 120 } &&
                IsHash(value.ModelSha256) && IsHash(value.ExtractedPcmSha256) && IsHash(value.TextSha256) &&
                value.SourceStart >= TimeSpan.Zero && value.SourceEnd > value.SourceStart &&
                value.Words.All(word => word is not null && !string.IsNullOrWhiteSpace(word.Text) &&
                    word.RelativeStart >= TimeSpan.Zero && word.RelativeEnd > word.RelativeStart &&
                    word.RelativeEnd <= value.SourceEnd - value.SourceStart &&
                    double.IsFinite(word.AcousticScore) && word.AcousticScore is >= 0 and <= 1)) ? values : [];
        }
        catch (JsonException) { return []; }
    }
    internal static string? Merge(string? left, string? right)
    {
        if (left is null) return right;
        if (right is null || left == right) return left;
        return JsonSerializer.Serialize(ReadAll(left).Concat(ReadAll(right)).DistinctBy(value => JsonSerializer.Serialize(value)).ToArray());
    }

    public double? ScoreFor(string text, TimeSpan absoluteStart, TimeSpan absoluteEnd) => Words.FirstOrDefault(word =>
        word.Text == text && SourceStart + word.RelativeStart == absoluteStart && SourceStart + word.RelativeEnd == absoluteEnd)?.AcousticScore;

    public string Summary => $"English audio alignment · {Words.Count(word => word.AcousticScore < .15)} weak acoustic matches / {Words.Count} words. " +
        "Review by listening before Save. Scores are acoustic fit, not correctness probabilities.";
    public string Details => $"{ModelIdentity}\nModel SHA256: {ModelSha256}\nExtracted PCM SHA256: {ExtractedPcmSha256}\n" +
        $"Source identity SHA256: {SourceIdentitySha256}\nSource snapshot: {SourceLengthBytes} bytes, {SourceLastWriteUtc:O}\n" +
        $"Audio stream {AudioStreamIndex}, source {SourceStart.TotalSeconds:0.###}–{SourceEnd.TotalSeconds:0.###} s\nCorrected text SHA256: {TextSha256}";
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
