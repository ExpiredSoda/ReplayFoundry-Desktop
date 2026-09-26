using System.IO;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Media;

/// <summary>Repairs missing Pop word timing against the selected audio before any video is encoded.</summary>
internal static class StudioPopCaptionPreparation
{
    internal static async Task<GenerationOutputAsset> PrepareAsync(GenerationOutputAsset asset,
        ICorrectedCaptionAlignmentService? alignment, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!asset.RenderSettings.BurnCaptions || asset.Appearance.CaptionStyle != GenerationCaptionStylePreset.Pop ||
            asset.Captions is not { } track) return asset;
        var segments = track.Segments.ToArray();
        bool changed = false;
        for (int index = 0; index < segments.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioTranscriptionSegment segment = segments[index];
            if (segment.AbsoluteSourceEnd <= asset.SourceStart || segment.AbsoluteSourceStart >= asset.SourceEnd ||
                StudioCaptionPresentationPolicy.HasPopWordCoverage(segment)) continue;
            string location = $"Clip {asset.Rank}, caption at {Math.Max(0, (segment.AbsoluteSourceStart - asset.SourceStart).TotalSeconds):0.##} seconds";
            // Legacy English transcripts did not retain a detected language. An
            // undetected track can try this acoustic model, but known other
            // languages and translated text must not be aligned as English.
            bool canTryEnglish = track.SourceSelection.LanguagePolicy is GenerationCaptionLanguagePolicy.Auto or GenerationCaptionLanguagePolicy.English &&
                segment.Language?.Code is null or "en";
            if (alignment is null || !canTryEnglish || segment.AbsoluteSourceEnd - segment.AbsoluteSourceStart > TimeSpan.FromSeconds(30))
                throw new StudioCaptionTimingException(asset.Id, segment.Id, location + ": Pop needs timing for every word. Open Caption review before rendering.");
            progress?.Report(location + ": matching Pop words to speech.");
            // Standalone punctuation has no spoken word interval. It remains in
            // segment.Text and the shared display mapper attaches it to a word.
            string alignmentText = string.Join(" ", Regex.Matches(segment.Text, @"\S+").Select(match => match.Value)
                .Where(token => token.Any(char.IsLetterOrDigit)));
            var request = new CorrectedCaptionAlignmentRequest(asset.SourceFullPath, asset.SourceDuration,
                track.SourceSelection.AbsoluteAudioStreamIndex, segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd, alignmentText, "en");
            var source = new FileInfo(asset.SourceFullPath);
            long length = source.Length;
            DateTime modified = source.LastWriteTimeUtc;
            CorrectedCaptionAlignmentResult result;
            try { result = await alignment.AlignAsync(request, progress, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { throw new StudioCaptionTimingException(asset.Id, segment.Id, location + ": word timing could not be repaired. " + exception.Message, exception); }
            cancellationToken.ThrowIfCancellationRequested();
            source.Refresh();
            if (!source.Exists || source.Length != length || source.LastWriteTimeUtc != modified)
                throw new IOException("The source recording changed while Pop captions were being aligned.");
            IReadOnlyList<AudioTranscriptionWord> words;
            CorrectedCaptionAlignmentResult usedAlignment;
            try { (words, usedAlignment) = ResolveWords(segment, result, request); }
            catch (InvalidDataException exception)
            { throw new StudioCaptionTimingException(asset.Id, segment.Id, location + ": " + exception.Message, exception); }
            string provenance = StudioCaptionAlignmentProvenance.Create(request, usedAlignment, length, modified);
            var warnings = segment.Warnings.Where(warning => warning.Code != AudioTranscriptionWarningCode.CorrectedTextAlignment)
                .Append(new AudioTranscriptionWarning(AudioTranscriptionWarningCode.CorrectedTextAlignment, provenance, segment.Id));
            segments[index] = new AudioTranscriptionSegment(segment.Id, segment.NeighborhoodId, segment.Text,
                segment.RelativeStart, segment.RelativeEnd, segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd,
                words, segment.ProviderReportedConfidence, segment.Language, warnings, segment.Speaker, segment.SecondaryText);
            if (!StudioCaptionPresentationPolicy.HasPopWordCoverage(segments[index]))
                throw new StudioCaptionTimingException(asset.Id, segment.Id, location + ": repaired words still need timing review before Pop can render.");
            changed = true;
        }
        if (!changed) return asset;
        return asset.WithCaptionTrack(GenerationCandidateCaptionTrack.RestoreStudioHandoff(track.CandidateId, track.NeighborhoodId,
            track.SourceSelection, track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration, track.SourceDuration,
            segments, track.IsUserEdited, track.SuppressionReason));
    }

    private static (IReadOnlyList<AudioTranscriptionWord>, CorrectedCaptionAlignmentResult) ResolveWords(
        AudioTranscriptionSegment segment, CorrectedCaptionAlignmentResult result, CorrectedCaptionAlignmentRequest request)
    {
        string[] tokens = Regex.Matches(request.CorrectedText, @"\S+").Select(match => match.Value).ToArray();
        if (result.Words is null || result.Words.Count != tokens.Length || tokens.Length is < 1 or > 120)
            throw new InvalidDataException("Pop alignment must preserve every caption word.");
        TimeSpan previousEnd = TimeSpan.Zero;
        foreach (var (word, index) in result.Words.Select((word, index) => (word, index)))
        {
            if (word is null || word.Text != tokens[index] || word.RelativeStart < previousEnd ||
                word.RelativeEnd <= word.RelativeStart || word.RelativeEnd > request.SourceEnd - request.SourceStart ||
                !double.IsFinite(word.AcousticScore) || word.AcousticScore is < 0 or > 1)
                throw new InvalidDataException("Pop alignment returned weak or invalid word timing; review this phrase in Captions.");
            previousEnd = word.RelativeEnd;
        }
        // The provider can omit opening words while retaining a uniquely
        // matched suffix. Preserve those measured clocks where they still fit;
        // a weak replacement is not a reason to discard an existing timestamp.
        // RestoreAnchors supplies zero-duration placeholders, never guessed times.
        var anchors = StudioCaptionPartialWordMapping.RestoreAnchors(segment);
        var words = new List<AudioTranscriptionWord>();
        var adopted = new List<CorrectedCaptionAlignedWord>();
        previousEnd = segment.AbsoluteSourceStart;
        foreach (var (aligned, index) in result.Words.Select((word, index) => (word, index)))
        {
            AudioTranscriptionWord? known = anchors.Count == tokens.Length ? anchors[index] : null;
            if (known is not null && known.AbsoluteSourceStart >= previousEnd &&
                known.AbsoluteSourceEnd > known.AbsoluteSourceStart && known.AbsoluteSourceEnd <= segment.AbsoluteSourceEnd &&
                !known.Text.Trim().Any(char.IsWhiteSpace))
            {
                words.Add(known);
                previousEnd = known.AbsoluteSourceEnd;
                continue;
            }
            // Every newly adopted clock must pass the same acoustic threshold
            // as full-phrase repair and must not collide with retained words.
            if (aligned.AcousticScore < .15 || segment.AbsoluteSourceStart + aligned.RelativeStart < previousEnd)
                throw new InvalidDataException("Pop alignment returned weak or conflicting word timing; review this phrase in Captions.");
            var replacement = new AudioTranscriptionWord(aligned.Text,
                segment.RelativeStart + aligned.RelativeStart, segment.RelativeStart + aligned.RelativeEnd,
                segment.AbsoluteSourceStart + aligned.RelativeStart, segment.AbsoluteSourceStart + aligned.RelativeEnd,
                isEmphasized: segment.Words.Any(previous => previous.IsEmphasized &&
                    StudioCaptionTrackEditing.Lexical(previous.Text) == StudioCaptionTrackEditing.Lexical(aligned.Text)));
            words.Add(replacement);
            adopted.Add(aligned);
            previousEnd = replacement.AbsoluteSourceEnd;
        }
        return (words, result with { Words = adopted.ToArray() });
    }
}
