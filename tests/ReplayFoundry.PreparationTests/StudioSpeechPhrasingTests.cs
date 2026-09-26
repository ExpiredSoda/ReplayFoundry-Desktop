using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static class StudioSpeechPhrasingTests
{
    internal static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Captions clear during a short speech pause and keep measured clocks", PausesClearText),
        new("Missing opening word timing no longer turns a long sentence into a caption block", MissingOpeningWordsStayLocal),
        new("Missing question timing remains readable instead of flashing at the word limit", MissingQuestionDoesNotFlash),
        new("Ambiguous partial words cannot borrow a repeated word's timestamp", AmbiguousWordsStayUntimed),
        new("Caption phrases break at punctuation even when the word limit fits", PunctuationBreaks),
    ];

    private static Task PausesClearText()
    {
        var words = new[] { Word("That", 0, .3), Word("worked", .3, .65), Word("Now", .95, 1.2), Word("go", 1.2, 1.6) };
        var cues = StudioCaptionPresentationPolicy.ProjectCues(Segment("That worked Now go", words), StudioCaptionWordLimitPreset.Streamlined);
        TestAssert.Equal(2, cues.Count, "A 300 ms speech pause must create separate captions.");
        TestAssert.Equal(TimeSpan.FromSeconds(.65), cues[0].RelativeEnd, "Text must disappear during the real pause.");
        TestAssert.Equal(TimeSpan.FromSeconds(.95), cues[1].RelativeStart, "The next phrase must begin with its spoken words.");
        TestAssert.True(words.SequenceEqual(cues.SelectMany(cue => cue.Words)), "Provider clocks must be retained exactly.");
        return Task.CompletedTask;
    }
    private static Task MissingOpeningWordsStayLocal()
    {
        string text = "I think it was probably a hat that I found somewhere at some point.";
        var words = text.Split(' ').Skip(1).Select((word, index) => Word(word, index * .25, (index + 1) * .25)).ToArray();
        var segment = Segment(text, words);
        var cues = StudioCaptionPresentationPolicy.ProjectCues(segment, StudioCaptionWordLimitPreset.Streamlined);
        TestAssert.True(cues.Count >= 3, "An untimed opening I cannot collapse fourteen words into one page.");
        TestAssert.Equal(text, string.Join(" ", cues.Select(cue => cue.Text)), "No missing or timed words may disappear.");
        TestAssert.True(cues.All(cue => cue.Text.Split(' ').Length <= 5), "The five-word limit must remain useful with a partial transcript.");
        TestAssert.Equal(0, cues[0].Words.Count, "Missing words receive a local phrase, never invented karaoke times.");
        TestAssert.True(cues.Skip(1).All(cue => cue.Words.Count > 0), "Later words must keep their measured animation.");
        TestAssert.Equal(13, segment.Words.Count, "Presentation cannot mutate or persist fabricated words.");
        return Task.CompletedTask;
    }
    private static Task AmbiguousWordsStayUntimed()
    {
        var segment = Segment("go go now", [Word("go", 0, .5), Word("now", .5, 1)]);
        var cues = StudioCaptionPresentationPolicy.ProjectCues(segment, StudioCaptionWordLimitPreset.Punchy);
        TestAssert.Equal(0, cues.Single().Words.Count, "Two possible matches must remain a phrase for explicit alignment.");
        return Task.CompletedTask;
    }
    private static Task MissingQuestionDoesNotFlash()
    {
        var words = new[] { Word("hat", 0, .09), Word("am", .09, .12), Word("I", .12, .2), Word("wearing?", .2, 1.34) };
        var segment = Segment("What kind of hat am I wearing?", words);
        var cues = StudioCaptionPresentationPolicy.ProjectCues(segment, StudioCaptionWordLimitPreset.Streamlined);
        TestAssert.Equal(1, cues.Count, "A short untimed question must not flash five words for 120 ms.");
        TestAssert.Equal(segment.Text, cues[0].Text, "The readable fallback must retain the whole question.");
        TestAssert.Equal(TimeSpan.FromSeconds(1.34), cues[0].RelativeEnd, "Fallback must use the actual speech envelope.");
        TestAssert.Equal(0, cues[0].Words.Count, "A phrase fallback must not invent word timings.");
        TestAssert.True(words.SequenceEqual(segment.Words), "Provider word clocks must remain unchanged.");
        return Task.CompletedTask;
    }
    private static Task PunctuationBreaks()
    {
        var words = new[] { Word("Wait.", 0, .4), Word("We", .4, .7), Word("won!", .7, 1) };
        var cues = StudioCaptionPresentationPolicy.ProjectCues(Segment("Wait. We won!", words), StudioCaptionWordLimitPreset.Streamlined);
        TestAssert.Equal(2, cues.Count, "Separate sentences should not share a caption merely because five words fit.");
        TestAssert.Equal("Wait.", cues[0].Text, "Sentence punctuation stays attached to the correct phrase.");
        return Task.CompletedTask;
    }
    private static AudioTranscriptionWord Word(string text, double start, double end) => new(text,
        TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), TimeSpan.FromSeconds(100 + start), TimeSpan.FromSeconds(100 + end));
    private static AudioTranscriptionSegment Segment(string text, AudioTranscriptionWord[] words) => new("phrase", "clip", text,
        TimeSpan.Zero, words[^1].RelativeEnd, TimeSpan.FromSeconds(100), words[^1].AbsoluteSourceEnd, words);
}
