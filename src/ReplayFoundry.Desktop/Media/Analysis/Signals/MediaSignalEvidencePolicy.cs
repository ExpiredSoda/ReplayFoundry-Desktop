namespace ReplayFoundry.Desktop.Media.Analysis.Signals;

public static class MediaSignalEvidencePolicy
{
    public const string CurrentSchemaVersion = "1.0";

    public const string VisualAnalysisPixelFormat =
        "yuv444p16le";

    public const int VisualAnalysisBitDepth = 16;

    public const int MaximumVisualOutputCharacters =
        128 * 1024 * 1024;

    public const int MaximumAudioOutputCharacters =
        64 * 1024 * 1024;
}
