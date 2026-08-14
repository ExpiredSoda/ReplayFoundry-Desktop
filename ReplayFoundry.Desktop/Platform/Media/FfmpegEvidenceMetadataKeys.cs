namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceMetadataKeys
{
    internal const string SceneTimeKey =
        "lavfi.scd.time";

    internal const string SceneScoreKey =
        "lavfi.scd.score";

    internal const string SceneMafdKey =
        "lavfi.scd.mafd";

    internal const string BlackStartKey =
        "lavfi.black_start";

    internal const string BlackEndKey =
        "lavfi.black_end";

    internal const string FreezeStartKey =
        "lavfi.freezedetect.freeze_start";

    internal const string FreezeEndKey =
        "lavfi.freezedetect.freeze_end";

    internal const string VisualMeanLumaKey =
        "lavfi.signalstats.YAVG";

    internal const string VisualLowLumaKey =
        "lavfi.signalstats.YLOW";

    internal const string VisualHighLumaKey =
        "lavfi.signalstats.YHIGH";

    internal const string VisualSaturationKey =
        "lavfi.signalstats.SATAVG";

    internal const string VisualActivityKey =
        "lavfi.signalstats.YDIF";

    internal const string AudioRmsKey =
        "lavfi.astats.Overall.RMS_level";

    internal const string AudioPeakKey =
        "lavfi.astats.Overall.Peak_level";

    internal const string AudioSampleCountKey =
        "lavfi.astats.Overall.Number_of_samples";
}
