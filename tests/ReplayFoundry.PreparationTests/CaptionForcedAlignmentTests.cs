using ReplayFoundry.Desktop.Platform.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static class CaptionForcedAlignmentTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Corrected-text CTC alignment follows acoustic frames and repeated letters", AlignsMeasuredFramesAndRepeatedLetters),
        new("CTC alignment rejects unsupported text, impossible paths and invalid emissions", RejectsUnusableInputs),
        new("CTC silence remains a weak acoustic match and alignment is cancellable", SilenceAndCancellationRemainExplicit),
    ];

    private static Task AlignsMeasuredFramesAndRepeatedLetters()
    {
        // Independent idealized acoustic observation: RO(blank)OK, then GO.
        // Repeated O must consume two distinct observations separated by blank.
        int[] observed = [0, 0, 13, 13, 0, 8, 8, 0, 8, 0, 26, 26, 0, 4, 0, 21, 0, 8, 0, 0, 0, 0];
        var words = CtcCaptionAlignment.Align(Emissions(observed), observed.Length, Samples(observed.Length), "Rook, go!");
        TestAssert.Equal(2, words.Count, "Word identity must come from the corrected text, including punctuation.");
        TestAssert.Equal("Rook,", words[0].Text, "The aligner cannot replace the corrected name with newly decoded text.");
        TestAssert.NearlyEqual(.0425, words[0].StartSeconds, 1e-9, "The name must start at its first observed consonant.");
        TestAssert.NearlyEqual(.2425, words[0].EndSeconds, 1e-9, "The name must cover both observed O sounds and final K.");
        TestAssert.NearlyEqual(.3025, words[1].StartSeconds, 1e-9, "The next word must follow the actual acoustic pause.");
        TestAssert.NearlyEqual(.3625, words[1].EndSeconds, 1e-9, "Trailing silence cannot stretch the final word.");
        TestAssert.True(words.All(static word => word.AcousticScore > .99), "The idealized acoustic path should retain strong emission scores.");
        return Task.CompletedTask;
    }

    private static Task RejectsUnusableInputs()
    {
        int[] observed = [0, 8, 0, 8, 0];
        foreach (string text in new[] { "42", "こんにちは", "'", "two 🎮", "" })
            TestAssert.Throws<ArgumentException>(() => CtcCaptionAlignment.Align(Emissions(observed), observed.Length, Samples(observed.Length), text),
                "Unsupported text must not be silently dropped to create misleading word timing.");
        TestAssert.Throws<ArgumentException>(() => CtcCaptionAlignment.Align(Emissions([8, 8]), 2, Samples(2), "OO"),
            "Repeated CTC labels need a separate blank frame; a too-short window must be rejected.");
        float[] invalid = Emissions(observed); invalid[2] = float.NaN;
        TestAssert.Throws<ArgumentException>(() => CtcCaptionAlignment.Align(invalid, observed.Length, Samples(observed.Length), "O"),
            "Nonfinite model output cannot become timing.");
        TestAssert.Throws<ArgumentException>(() => CtcCaptionAlignment.Align(Emissions(observed), observed.Length, 16000, "O"),
            "The acoustic stride must match the measured audio sample count.");
        return Task.CompletedTask;
    }

    private static Task SilenceAndCancellationRemainExplicit()
    {
        int[] silence = new int[30];
        var words = CtcCaptionAlignment.Align(Emissions(silence), silence.Length, Samples(silence.Length), "Rook");
        TestAssert.True(words.Single().AcousticScore < .001, "Forced alignment against silence must not acquire invented high confidence.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        TestAssert.Throws<OperationCanceledException>(() => CtcCaptionAlignment.Align(Emissions(silence), silence.Length,
            Samples(silence.Length), "Rook", cancellation.Token), "Cancelled alignment must stop before running the acoustic path search.");
        return Task.CompletedTask;
    }

    private static int Samples(int frames) => 400 + (frames - 1) * 320;
    private static float[] Emissions(int[] observed)
    {
        float[] values = Enumerable.Repeat(-12f, observed.Length * 32).ToArray();
        for (int frame = 0; frame < observed.Length; frame++) values[frame * 32 + observed[frame]] = 12;
        return values;
    }
}
