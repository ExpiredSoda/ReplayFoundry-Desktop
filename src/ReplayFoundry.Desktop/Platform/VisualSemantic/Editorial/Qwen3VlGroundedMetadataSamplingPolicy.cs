using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataSamplingPolicy
{
    internal const string Version =
        "grounded-editorial-adaptive-sampling-1.2";
    internal const string PreviousVersion =
        "grounded-editorial-adaptive-sampling-1.1";
    internal const string InitialVersion =
        "grounded-editorial-adaptive-sampling-1.0";
    internal const string CandidateCoreTier = "CandidateCore";
    internal const string SparseContextTier = "SparseContext";

    internal const double CoreFramesPerSecond = 0.5;
    internal const int CoreMinimumFrames = 4;
    internal const int CoreMaximumFrames = 6;
    internal const int CoreMaximumPixelsPerFrame = 512 * 288;
    internal const int CoreMaximumTotalVideoPixels =
        CoreMaximumFrames * CoreMaximumPixelsPerFrame;
    internal const double CoreMaximumDurationSeconds = 16.0;
    internal const double CoreWindowOverlapSeconds = 2.0;

    internal const int PreviousCoreMaximumFrames = 8;
    internal const int PreviousCoreMaximumPixelsPerFrame = 640 * 360;
    internal const int PreviousCoreMaximumTotalVideoPixels =
        PreviousCoreMaximumFrames * PreviousCoreMaximumPixelsPerFrame;
    internal const int InitialCoreMaximumFrames = 16;
    internal const int InitialCoreMaximumTotalVideoPixels =
        InitialCoreMaximumFrames * PreviousCoreMaximumPixelsPerFrame;

    internal const double ContextFramesPerSecond = 0.2;
    internal const int ContextMinimumFrames = 4;
    internal const int ContextMaximumFrames = 6;
    internal const int PreviousContextMaximumFrames = 8;
    internal const int ContextMaximumPixelsPerFrame = 131_072;
    internal const int ContextMaximumTotalVideoPixels =
        ContextMaximumFrames * ContextMaximumPixelsPerFrame;

    internal const double LegacyFramesPerSecond = 0.2;
    internal const int LegacyMinimumFrames = 4;
    internal const int LegacyMaximumFrames = 16;
    internal const int LegacyMaximumPixelsPerFrame = 131_072;
    internal const int LegacyMaximumTotalVideoPixels =
        LegacyMaximumFrames * LegacyMaximumPixelsPerFrame;

    internal static void ValidateSummary(
        JsonElement generation,
        bool adaptive,
        bool peakBoundedSampling,
        bool lowPeakSampling = false)
    {
        double expectedFramesPerSecond = adaptive
            ? CoreFramesPerSecond
            : LegacyFramesPerSecond;
        int expectedMinimumFrames = adaptive
            ? CoreMinimumFrames
            : LegacyMinimumFrames;
        int expectedMaximumFrames = adaptive
            ? lowPeakSampling
                ? CoreMaximumFrames
                : peakBoundedSampling
                    ? PreviousCoreMaximumFrames
                    : InitialCoreMaximumFrames
            : LegacyMaximumFrames;
        int expectedMaximumPixels = adaptive
            ? lowPeakSampling
                ? CoreMaximumPixelsPerFrame
                : PreviousCoreMaximumPixelsPerFrame
            : LegacyMaximumPixelsPerFrame;
        int expectedMaximumTotalPixels = adaptive
            ? lowPeakSampling
                ? CoreMaximumTotalVideoPixels
                : peakBoundedSampling
                    ? PreviousCoreMaximumTotalVideoPixels
                    : InitialCoreMaximumTotalVideoPixels
            : LegacyMaximumTotalVideoPixels;

        if (adaptive)
        {
            Qwen3VlGroundedMetadataJson.RequireText(
                generation,
                "samplingPolicyVersion",
                lowPeakSampling
                    ? Version
                    : peakBoundedSampling
                        ? PreviousVersion
                        : InitialVersion);
        }
        if (Math.Abs(Qwen3VlEditorialJson.Finite(
                generation,
                "videoFramesPerSecond") - expectedFramesPerSecond) > 0.000001 ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "minimumVideoFrames") != expectedMinimumFrames ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "maximumVideoFrames") != expectedMaximumFrames ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "maximumPixelsPerFrame") != expectedMaximumPixels ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "maximumTotalVideoPixels") != expectedMaximumTotalPixels)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata sampling provenance is invalid.");
        }
    }

    internal static string ValidateDraft(
        JsonElement sampling,
        bool peakBoundedSampling,
        bool lowPeakSampling = false)
    {
        Qwen3VlEditorialJson.Exact(
            sampling,
            "policyVersion",
            "tier",
            "framesPerSecond",
            "minimumFrames",
            "maximumFrames",
            "maximumPixelsPerFrame",
            "maximumTotalVideoPixels",
            "actualFrameCount",
            "actualFrameWidth",
            "actualFrameHeight",
            "actualPixelsPerFrame",
            "actualTotalVideoPixels");
        Qwen3VlGroundedMetadataJson.RequireText(
            sampling,
            "policyVersion",
            lowPeakSampling
                ? Version
                : peakBoundedSampling
                    ? PreviousVersion
                    : InitialVersion);
        string tier = Qwen3VlEditorialJson.Text(sampling, "tier");
        bool core = tier.Equals(CandidateCoreTier, StringComparison.Ordinal);
        bool context = tier.Equals(SparseContextTier, StringComparison.Ordinal);
        if (!core && !context)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata sampling tier is invalid.");
        }

        double expectedFps = core
            ? CoreFramesPerSecond
            : ContextFramesPerSecond;
        int minimumFrames = core
            ? CoreMinimumFrames
            : ContextMinimumFrames;
        int maximumFrames = core
            ? lowPeakSampling
                ? CoreMaximumFrames
                : peakBoundedSampling
                    ? PreviousCoreMaximumFrames
                    : InitialCoreMaximumFrames
            : lowPeakSampling
                ? ContextMaximumFrames
                : PreviousContextMaximumFrames;
        int maximumPixels = core
            ? lowPeakSampling
                ? CoreMaximumPixelsPerFrame
                : PreviousCoreMaximumPixelsPerFrame
            : ContextMaximumPixelsPerFrame;
        int maximumTotalPixels = core
            ? lowPeakSampling
                ? CoreMaximumTotalVideoPixels
                : peakBoundedSampling
                    ? PreviousCoreMaximumTotalVideoPixels
                    : InitialCoreMaximumTotalVideoPixels
            : (lowPeakSampling
                ? ContextMaximumFrames
                : PreviousContextMaximumFrames) *
                ContextMaximumPixelsPerFrame;
        if (Math.Abs(Qwen3VlEditorialJson.Finite(
                sampling,
                "framesPerSecond") - expectedFps) > 0.000001 ||
            Qwen3VlEditorialJson.Integer(sampling, "minimumFrames") !=
                minimumFrames ||
            Qwen3VlEditorialJson.Integer(sampling, "maximumFrames") !=
                maximumFrames ||
            Qwen3VlEditorialJson.Integer(
                sampling,
                "maximumPixelsPerFrame") != maximumPixels ||
            Qwen3VlEditorialJson.Integer(
                sampling,
                "maximumTotalVideoPixels") != maximumTotalPixels)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata sampling tier policy changed.");
        }

        int frameCount = Qwen3VlEditorialJson.Integer(
            sampling,
            "actualFrameCount");
        int width = Qwen3VlEditorialJson.Integer(
            sampling,
            "actualFrameWidth");
        int height = Qwen3VlEditorialJson.Integer(
            sampling,
            "actualFrameHeight");
        int pixels = Qwen3VlEditorialJson.Integer(
            sampling,
            "actualPixelsPerFrame");
        int totalPixels = Qwen3VlEditorialJson.Integer(
            sampling,
            "actualTotalVideoPixels");
        if (frameCount < minimumFrames ||
            frameCount > maximumFrames ||
            width <= 0 ||
            width > 640 ||
            height <= 0 ||
            height > 640 ||
            pixels != width * height ||
            pixels > maximumPixels ||
            totalPixels != frameCount * pixels ||
            totalPixels > maximumTotalPixels)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata actual sampling exceeded its declared policy.");
        }
        return tier;
    }

    internal static void ValidateWindowTimeline(
        double previousEnd,
        string? previousTier,
        double start,
        double end,
        string? tier,
        bool peakBoundedSampling)
    {
        double expectedOverlap =
            peakBoundedSampling &&
            previousTier == CandidateCoreTier &&
            tier == CandidateCoreTier
                ? CoreWindowOverlapSeconds
                : 0.0;
        if (Math.Abs(previousEnd - start - expectedOverlap) > 0.000001 ||
            end <= start ||
            (peakBoundedSampling &&
                tier == CandidateCoreTier &&
                end - start > CoreMaximumDurationSeconds + 0.000001))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen visual-draft sampling timeline is invalid.");
        }
    }
}
