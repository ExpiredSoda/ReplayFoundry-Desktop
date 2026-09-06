namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record CorrectedCaptionAlignmentRequest(
    string SourceFullPath, TimeSpan SourceDuration, int AbsoluteAudioStreamIndex,
    TimeSpan SourceStart, TimeSpan SourceEnd, string CorrectedText, string LanguageCode);

public sealed record CorrectedCaptionAlignedWord(
    string Text, TimeSpan RelativeStart, TimeSpan RelativeEnd, double AcousticScore);

public sealed record CorrectedCaptionAlignmentResult(
    IReadOnlyList<CorrectedCaptionAlignedWord> Words, string ModelIdentity, string ModelSha256,
    TimeSpan Elapsed, string ReviewNote, string? ExtractedPcmSha256 = null);

public interface ICorrectedCaptionAlignmentService
{
    Task<CorrectedCaptionAlignmentResult> AlignAsync(CorrectedCaptionAlignmentRequest request,
        IProgress<string>? progress, CancellationToken cancellationToken);
}
