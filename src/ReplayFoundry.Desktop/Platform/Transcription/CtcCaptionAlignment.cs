using System.Text;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal sealed record CtcAlignedCaptionWord(string Text, double StartSeconds, double EndSeconds, double AcousticScore);

/// <summary>
/// Monotonic CTC alignment of a supplied English transcript to acoustic emissions.
/// The score describes the chosen acoustic path; it is not a correctness probability.
/// No word timing is created from text length or from a new transcription.
/// </summary>
internal static class CtcCaptionAlignment
{
    private const string Labels = "     ETAONIHSRDLUMWCFGYPBVK'XJQZ";
    private const int LabelCount = 32;

    internal static IReadOnlyList<CtcAlignedCaptionWord> Align(float[] logits, int frameCount, int sampleCount,
        string correctedText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logits);
        ArgumentNullException.ThrowIfNull(correctedText);
        if (frameCount < 1 || frameCount > 1500 || sampleCount < 400 || sampleCount > 480000 ||
            frameCount != (sampleCount - 400) / 320 + 1 ||
            logits.Length != frameCount * LabelCount || logits.Any(static value => !float.IsFinite(value)))
            throw new ArgumentException("Alignment requires finite 32-label emissions for at most 30 seconds of 16 kHz audio.");
        string[] words = Regex.Matches(correctedText.Trim(), @"\S+").Select(static match => match.Value).ToArray();
        if (words.Length is < 1 or > 120 || correctedText.Length > 1000)
            throw new ArgumentException("Align between one and 120 words in a phrase of at most 1,000 characters.", nameof(correctedText));
        var tokens = new List<int>(); var tokenWords = new List<int>();
        for (int word = 0; word < words.Length; word++)
        {
            string normalized = Normalize(words[word]);
            if (!normalized.Any(static letter => letter is >= 'A' and <= 'Z'))
                throw new ArgumentException("Each alignment word must include English letters. Spell numbers as spoken words.", nameof(correctedText));
            if (word > 0) { tokens.Add(4); tokenWords.Add(-1); }
            foreach (char letter in normalized)
            {
                tokens.Add(Labels.IndexOf(letter)); tokenWords.Add(word);
            }
        }
        int repeats = tokens.Zip(tokens.Skip(1)).Count(static pair => pair.First == pair.Second);
        if (tokens.Count > 750 || frameCount < tokens.Count + repeats)
            throw new ArgumentException("This audio window is too short to align all supplied speech.");

        int stateCount = tokens.Count * 2 + 1;
        float[] previous = Enumerable.Repeat(float.NegativeInfinity, stateCount).ToArray();
        float[] current = new float[stateCount]; previous[0] = 0;
        byte[] steps = new byte[frameCount * stateCount];
        float[] logProbabilities = new float[logits.Length];
        for (int frame = 0; frame < frameCount; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = frame * LabelCount;
            float maximum = float.NegativeInfinity;
            for (int label = 0; label < LabelCount; label++) maximum = Math.Max(maximum, logits[offset + label]);
            double sum = 0;
            for (int label = 0; label < LabelCount; label++) sum += Math.Exp(logits[offset + label] - maximum);
            double normalizer = maximum + Math.Log(sum);
            for (int label = 0; label < LabelCount; label++) logProbabilities[offset + label] = (float)(logits[offset + label] - normalizer);
            for (int state = 0; state < stateCount; state++)
            {
                int label = (state & 1) == 0 ? 0 : tokens[state / 2];
                float best = previous[state]; byte step = 0;
                if (state > 0 && previous[state - 1] > best) { best = previous[state - 1]; step = 1; }
                if (state > 1 && (state & 1) == 1 && label != tokens[state / 2 - 1] && previous[state - 2] > best)
                { best = previous[state - 2]; step = 2; }
                current[state] = best + logProbabilities[offset + label];
                steps[frame * stateCount + state] = step;
            }
            (previous, current) = (current, previous);
        }
        int pathState = previous[^1] > previous[^2] ? stateCount - 1 : stateCount - 2;
        if (!float.IsFinite(previous[pathState])) throw new InvalidOperationException("No complete acoustic alignment was found.");
        int[] starts = Enumerable.Repeat(int.MaxValue, words.Length).ToArray();
        int[] ends = new int[words.Length], counts = new int[words.Length]; double[] scores = new double[words.Length];
        for (int frame = frameCount - 1; frame >= 0; frame--)
        {
            if ((pathState & 1) == 1)
            {
                int token = pathState / 2, word = tokenWords[token];
                if (word >= 0)
                {
                    starts[word] = Math.Min(starts[word], frame); ends[word] = Math.Max(ends[word], frame + 1);
                    scores[word] += logProbabilities[frame * LabelCount + tokens[token]]; counts[word]++;
                }
            }
            pathState -= steps[frame * stateCount + pathState];
        }
        if (counts.Any(static count => count == 0)) throw new InvalidOperationException("A supplied word has no acoustic alignment.");
        // Wav2vec2's seven convolution layers have a 320-sample stride and
        // 400-sample receptive field. Attribute each token the central 320
        // samples of its receptive field instead of stretching to file length.
        double duration = sampleCount / 16000d;
        return words.Select((word, index) => new CtcAlignedCaptionWord(word,
            Math.Min(duration, (starts[index] * 320d + 40) / 16000),
            Math.Min(duration, (ends[index] * 320d + 40) / 16000),
            Math.Exp(scores[index] / counts[index]))).ToArray();
    }

    private static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (char raw in text)
        {
            char letter = char.ToUpperInvariant(raw);
            if (letter is >= 'A' and <= 'Z') result.Append(letter);
            else if (letter is '\'' or '\u2019') result.Append('\'');
            else if (!char.IsPunctuation(letter))
                throw new ArgumentException("This alignment model supports English letters and apostrophes. Spell numbers as spoken words.", nameof(text));
        }
        return result.ToString();
    }
}
