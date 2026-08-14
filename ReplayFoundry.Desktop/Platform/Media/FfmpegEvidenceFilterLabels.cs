using System.Globalization;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceFilterLabels
{
    internal static string Input(
        string targetKey)
    {
        return $"[rf_in_{targetKey}]";
    }

    internal static string SceneOutput(
        string targetKey)
    {
        return $"[rf_scene_{targetKey}]";
    }

    internal static string SceneDetectorInput(
        string targetKey)
    {
        return $"[rf_scene_detector_{targetKey}]";
    }

    internal static string VisualSignalInput(
        string targetKey)
    {
        return $"[rf_signal_{targetKey}]";
    }

    internal static string SceneEventInput(
        string targetKey)
    {
        return $"[rf_scene_event_{targetKey}]";
    }

    internal static string SceneSinkInput(
        string targetKey)
    {
        return $"[rf_scene_sink_{targetKey}]";
    }

    internal static string VisualEventInput(
        string targetKey,
        int eventIndex)
    {
        return $"[rf_event_{targetKey}_{eventIndex}]";
    }

    internal static string VisualSinkInput(
        string targetKey)
    {
        return $"[rf_visual_sink_{targetKey}]";
    }

    internal static string VisualOutput(
        string targetKey)
    {
        return $"[rf_visual_{targetKey}]";
    }

    internal static string Format(
        double value)
    {
        return value.ToString(
            "0.########",
            CultureInfo.InvariantCulture);
    }
}
