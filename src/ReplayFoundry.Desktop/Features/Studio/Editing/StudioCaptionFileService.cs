using System.IO;
using Microsoft.Win32;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal sealed record StudioCaptionImport(string Text, SubtitleSidecarFormat Format);

/// <summary>Owns caption file dialogs and local I/O, separate from draft state.</summary>
internal sealed class StudioCaptionFileService
{
    private readonly JsonCaptionVocabularyStore _vocabulary = new();
    internal IReadOnlyList<string> LoadVocabulary() => _vocabulary.Load();
    internal void SaveVocabulary(IEnumerable<string> terms) => _vocabulary.Save(terms);

    internal static string? ChooseImport()
    {
        var dialog = new OpenFileDialog { Filter = "Subtitles|*.srt;*.vtt", CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    internal static StudioCaptionImport ReadImport(string path)
    {
        if (new FileInfo(path).Length > 10_000_000)
            throw new ArgumentException("Subtitle files must be smaller than 10 MB.");
        return new(File.ReadAllText(path), FormatForPath(path));
    }

    internal static IReadOnlyList<StudioCaptionSegmentEdit> Parse(string text, SubtitleSidecarFormat format, TimeSpan duration)
    {
        var cues = SubtitleSidecarSerializer.Parse(text, format);
        if (cues.Any(cue => cue.End > duration)) throw new ArgumentException("Imported timestamps extend beyond this cut.");
        return cues.Select(cue => new StudioCaptionSegmentEdit("manual-" + Guid.NewGuid().ToString("N"),
            cue.Text, cue.Start.TotalSeconds, cue.End.TotalSeconds, [], cue.Speaker)).ToArray();
    }

    internal static string? ChooseExport()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SubRip captions|*.srt|WebVTT captions|*.vtt",
            AddExtension = true,
            DefaultExt = ".srt",
            FileName = "captions",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    internal static void Export(string path, GenerationOutputAsset asset) => File.WriteAllText(path,
        SubtitleSidecarSerializer.Build(asset.Captions!, asset.SourceStart, asset.Duration, FormatForPath(path)));

    private static SubtitleSidecarFormat FormatForPath(string path) =>
        Path.GetExtension(path).Equals(".vtt", StringComparison.OrdinalIgnoreCase) ? SubtitleSidecarFormat.WebVtt : SubtitleSidecarFormat.Srt;
}
