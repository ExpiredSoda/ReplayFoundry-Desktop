using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

internal static class RetainedCaptionEditorialTranscriptProjector
{
    public static ClipEditorialTranscriptContext[] Project(
        GenerationCandidateCaptionTrack track,
        TimeSpan sourceStart,
        TimeSpan sourceEnd)
    {
        ArgumentNullException.ThrowIfNull(track);
        TimeSpan trackEnd =
            track.SourceWindowStart + track.SourceWindowDuration;
        if (sourceStart < TimeSpan.Zero ||
            sourceEnd > track.SourceDuration ||
            sourceEnd <= sourceStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceEnd),
                "Editorial transcript projection requires a valid cut inside the retained source.");
        }
        TimeSpan projectionStart = sourceStart < track.SourceWindowStart
            ? track.SourceWindowStart
            : sourceStart;
        TimeSpan projectionEnd = sourceEnd > trackEnd
            ? trackEnd
            : sourceEnd;
        if (projectionEnd <= projectionStart)
        {
            // Studio can extend a cut beyond the original transcription
            // window. With no retained caption overlap there is no truthful
            // transcript context to project.
            return [];
        }

        var textParts = new List<string>();
        var spans = new List<ClipEditorialTranscriptSpan>();
        int retainedTextLength = 0;
        foreach (AudioTranscriptionSegment segment in track.Segments)
        {
            if (HasApplicableForeignStreamAlignment(track, segment))
            {
                // A per-phrase alignment can use a different voice track from
                // the original transcript. Do not label that text with the
                // original stream's confirmed speech role.
                continue;
            }
            SegmentProjection? projection = ProjectSegment(
                segment,
                projectionStart,
                projectionEnd);
            if (projection is null)
            {
                continue;
            }

            if (projection.SourceEnd > projection.SourceStart)
            {
                if (spans.Count <
                    ClipEditorialTranscriptContext.MaximumSpanCount)
                {
                    spans.Add(new ClipEditorialTranscriptSpan(
                        projection.SourceStart,
                        projection.SourceEnd,
                        BoundText(
                            projection.Text,
                            ClipEditorialTranscriptSpan.MaximumTextLength)));
                }
            }
            textParts.Add(projection.Text);
            retainedTextLength = checked(
                retainedTextLength +
                projection.Text.Length +
                (textParts.Count > 1 ? 1 : 0));
            if (retainedTextLength >=
                ClipEditorialTranscriptContext.MaximumTextLength)
            {
                break;
            }
        }

        string text = JoinBounded(
            textParts,
            ClipEditorialTranscriptContext.MaximumTextLength);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        return
        [
            new ClipEditorialTranscriptContext(
                track.SourceSelection.AbsoluteAudioStreamIndex,
                MapRole(track.SourceSelection.ContentRole),
                text,
                track.IsUserEdited
                    ? ClipEditorialTranscriptAuthority.UserCorrected
                    : ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
                spans),
        ];
    }

    private static bool HasApplicableForeignStreamAlignment(GenerationCandidateCaptionTrack track, AudioTranscriptionSegment segment)
    {
        string sourceIdentity = StudioCaptionAlignmentProvenance.TextIdentity(
            System.IO.Path.GetFullPath(track.SourceSelection.SourceFullPath).ToUpperInvariant());
        string textIdentity = StudioCaptionAlignmentProvenance.TextIdentity(segment.Text);
        return segment.Warnings.Where(warning => warning.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment)
            .SelectMany(warning => StudioCaptionAlignmentProvenance.ReadAll(warning.Message))
            .Any(alignment => alignment.AudioStreamIndex != track.SourceSelection.AbsoluteAudioStreamIndex &&
                alignment.SourceIdentitySha256 == sourceIdentity &&
                alignment.SourceStart < segment.AbsoluteSourceEnd && alignment.SourceEnd > segment.AbsoluteSourceStart &&
                (alignment.TextSha256 == textIdentity || segment.Words.Any(word =>
                    alignment.ScoreFor(word.Text, word.AbsoluteSourceStart, word.AbsoluteSourceEnd).HasValue)));
    }

    private static SegmentProjection? ProjectSegment(
        AudioTranscriptionSegment segment,
        TimeSpan sourceStart,
        TimeSpan sourceEnd)
    {
        if (segment.AbsoluteSourceEnd <= sourceStart ||
            segment.AbsoluteSourceStart >= sourceEnd)
        {
            return null;
        }

        if (segment.Words.Count == 0)
        {
            // An untimed correction cannot be split truthfully. Keep it only
            // when the exact Studio cut retains its complete phrase interval.
            return segment.AbsoluteSourceStart >= sourceStart &&
                   segment.AbsoluteSourceEnd <= sourceEnd &&
                   segment.AbsoluteSourceEnd > segment.AbsoluteSourceStart
                ? new SegmentProjection(
                    segment.Text,
                    segment.AbsoluteSourceStart,
                    segment.AbsoluteSourceEnd)
                : null;
        }

        int firstIndex = -1;
        int lastIndex = -1;
        for (int index = 0; index < segment.Words.Count; index++)
        {
            AudioTranscriptionWord word = segment.Words[index];
            if (word.AbsoluteSourceEnd > sourceStart &&
                word.AbsoluteSourceStart < sourceEnd)
            {
                firstIndex = firstIndex < 0 ? index : firstIndex;
                lastIndex = index;
            }
        }
        if (firstIndex < 0)
        {
            return null;
        }

        string text = SliceWords(
            segment,
            firstIndex,
            lastIndex + 1,
            sourceStart,
            sourceEnd);
        AudioTranscriptionWord first = segment.Words[firstIndex];
        AudioTranscriptionWord last = segment.Words[lastIndex];
        TimeSpan spanStart = first.AbsoluteSourceStart < sourceStart
            ? sourceStart
            : first.AbsoluteSourceStart;
        TimeSpan spanEnd = last.AbsoluteSourceEnd > sourceEnd
            ? sourceEnd
            : last.AbsoluteSourceEnd;
        return new SegmentProjection(text, spanStart, spanEnd);
    }

    private static string SliceWords(
        AudioTranscriptionSegment segment,
        int firstIndex,
        int nextIndex,
        TimeSpan sourceStart,
        TimeSpan sourceEnd)
    {
        if (firstIndex == 0 &&
            nextIndex == segment.Words.Count &&
            segment.AbsoluteSourceStart >= sourceStart &&
            segment.AbsoluteSourceEnd <= sourceEnd)
        {
            // Preserve user punctuation and corrected phrase text even when
            // its lexical text no longer maps one-to-one onto provider words.
            return segment.Text;
        }

        if (TryMapWordStarts(
                segment.Text,
                segment.Words,
                out int[] starts))
        {
            int textStart = firstIndex == 0
                ? 0
                : starts[firstIndex];
            int textEnd = nextIndex < segment.Words.Count
                ? starts[nextIndex]
                : segment.Text.Length;
            string slice = segment.Text[textStart..textEnd].Trim();
            if (!string.IsNullOrWhiteSpace(slice))
            {
                return slice;
            }
        }

        // Provider word text remains the only truthful partial-cut source if
        // edited phrase text cannot be aligned back to its retained timings.
        return string.Join(
            " ",
            segment.Words
                .Skip(firstIndex)
                .Take(nextIndex - firstIndex)
                .Select(static word => word.Text));
    }

    private static bool TryMapWordStarts(
        string text,
        IReadOnlyList<AudioTranscriptionWord> words,
        out int[] starts)
    {
        starts = [];
        IReadOnlyList<(char Value, int SourceIndex)> source =
            BuildLexicalCharacters(text);
        if (source.Count == 0)
        {
            return false;
        }

        var lexicalWords = new List<char[]>(words.Count);
        foreach (AudioTranscriptionWord word in words)
        {
            char[] lexical = BuildLexicalCharacters(word.Text)
                .Select(static value => value.Value)
                .ToArray();
            if (lexical.Length == 0)
            {
                return false;
            }
            lexicalWords.Add(lexical);
        }

        char[] expected = lexicalWords
            .SelectMany(static word => word)
            .ToArray();
        if (expected.Length != source.Count)
        {
            return false;
        }
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != source[index].Value)
            {
                return false;
            }
        }

        starts = new int[words.Count];
        int lexicalOffset = 0;
        for (int index = 0; index < words.Count; index++)
        {
            starts[index] = source[lexicalOffset].SourceIndex;
            lexicalOffset += lexicalWords[index].Length;
        }
        return true;
    }

    private static IReadOnlyList<(char Value, int SourceIndex)>
        BuildLexicalCharacters(string value)
    {
        var result = new List<(char Value, int SourceIndex)>();
        for (int sourceIndex = 0;
             sourceIndex < value.Length;
             sourceIndex++)
        {
            string normalized = value[sourceIndex]
                .ToString()
                .Normalize(NormalizationForm.FormKC);
            foreach (char character in normalized)
            {
                if (char.IsLetterOrDigit(character))
                {
                    result.Add((
                        char.ToUpperInvariant(character),
                        sourceIndex));
                }
            }
        }
        return result.AsReadOnly();
    }

    private static string JoinBounded(
        IReadOnlyList<string> parts,
        int maximumLength)
    {
        var builder = new StringBuilder(
            Math.Min(maximumLength, parts.Sum(static part => part.Length)));
        foreach (string part in parts)
        {
            if (builder.Length > 0)
            {
                if (builder.Length == maximumLength)
                {
                    break;
                }
                builder.Append(' ');
            }
            int remaining = maximumLength - builder.Length;
            if (remaining <= 0)
            {
                break;
            }
            builder.Append(part.AsSpan(0, Math.Min(part.Length, remaining)));
        }
        return builder.ToString().TrimEnd();
    }

    private static string BoundText(string value, int maximumLength)
    {
        string text = value.Trim();
        return text.Length <= maximumLength
            ? text
            : text[..maximumLength].TrimEnd();
    }

    private static AudioContentRoleAssignment MapRole(
        CaptionAudioContentRole role) =>
        role switch
        {
            CaptionAudioContentRole.CreatorCommentary =>
                new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.GameDialogue =>
                new AudioContentRoleAssignment(
                    AudioContentRole.GameDialogue,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.MixedSpeech =>
                new AudioContentRoleAssignment(
                    AudioContentRole.MixedSpeech,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.OtherKnownSpeech =>
                AudioContentRoleAssignment.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private sealed record SegmentProjection(
        string Text,
        TimeSpan SourceStart,
        TimeSpan SourceEnd);
}
