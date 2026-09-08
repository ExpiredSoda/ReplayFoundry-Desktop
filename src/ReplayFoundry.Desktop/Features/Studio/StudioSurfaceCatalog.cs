using System.IO;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio;

internal static class StudioSurfaceCatalog
{
    public static IReadOnlyList<StudioInspectorItem> InspectorSections { get; } =
        Array.AsReadOnly<StudioInspectorItem>(
        [
            new(StudioInspectorSection.Clip, "Clip", "Icon.Media", "Choose where the clip starts and ends"),
            new(StudioInspectorSection.Audio, "Audio", "Icon.Audio", "Choose what you hear"),
            new(StudioInspectorSection.Captions, "Captions", "Icon.Caption", "Choose a look and edit the words"),
            new(StudioInspectorSection.Effects, "Effects", "Icon.Effects", "Adjust a look and compare it with the original"),
            new(StudioInspectorSection.Graphics, "Graphics", "Icon.Graphics", "Add pictures and logos"),
            new(StudioInspectorSection.Metadata, "Title & description", "Icon.Info", "Title, description, and tags"),
        ]);

    public static IReadOnlyList<SelectionOption<GenerationCaptionStylePreset>> CaptionStyles { get; } =
        Array.AsReadOnly<SelectionOption<GenerationCaptionStylePreset>>(
        [
            new(GenerationCaptionStylePreset.Clean, "Clean", "Shows short white phrases with a crisp edge and soft shadow."),
            new(GenerationCaptionStylePreset.WordFocus, "Word focus", "Keeps phrase context visible while the spoken word lifts in gold."),
            new(GenerationCaptionStylePreset.KaraokeSweep, "Karaoke", "Sweeps gold through each spoken word while past words resolve white."),
            new(GenerationCaptionStylePreset.Pop, "Pop", "Shows one spoken word at a time with a quick elastic bounce."),
            new(GenerationCaptionStylePreset.HighContrast, "High contrast", "Places white phrases on an opaque dark panel for busy footage."),
        ]);

    public static IReadOnlyList<SelectionOption<StudioCaptionWordLimitPreset>> CaptionWordLimits { get; } =
        Array.AsReadOnly<SelectionOption<StudioCaptionWordLimitPreset>>(
        [
            new(StudioCaptionWordLimitPreset.Punchy, "3 words", "Quick phrases, up to three words. Pauses start a new phrase."),
            new(StudioCaptionWordLimitPreset.Streamlined, "5 words", "Short phrases, up to five words. Pauses start a new phrase."),
            new(StudioCaptionWordLimitPreset.Balanced, "8 words", "More context, up to eight words. Pauses still break the phrase."),
            new(StudioCaptionWordLimitPreset.FullSegment, "Full phrase", "Longer phrases, with breaks at pauses and punctuation."),
        ]);

    public static IReadOnlyList<SelectionOption<StudioVideoEffectPreset>> VideoEffects { get; } =
        Array.AsReadOnly<SelectionOption<StudioVideoEffectPreset>>(
        [
            new(StudioVideoEffectPreset.None, "None", "Keep the source color unchanged."),
            new(StudioVideoEffectPreset.Noir, "Noir", "Reduce color and deepen contrast."),
            new(StudioVideoEffectPreset.Chromatic, "Chromatic", "Offset red and blue channels for a subtle digital edge."),
            new(StudioVideoEffectPreset.SoftBloom, "Soft focus", "Soften the picture and gently lift its brightness."),
            new(StudioVideoEffectPreset.Vivid, "Vivid", "Bring out muted colors."),
        ]);

    public static StudioInspectorItem GetInspector(StudioInspectorSection section) =>
        InspectorSections.SingleOrDefault(item => item.Key == section) ??
        throw new InvalidOperationException(
            "The selected Studio inspector is not defined.");

    public static IReadOnlyList<StudioBrowserPreviewItem> BuildBrowserPreviewItems(
        GenerationOutputProject? project,
        string? selectedAssetId,
        IReadOnlySet<string>? queuedAssetIds = null) =>
        BuildMomentItems(project, selectedAssetId, queuedAssetIds);

    private static IReadOnlyList<StudioBrowserPreviewItem> BuildMomentItems(
        GenerationOutputProject? project,
        string? selectedAssetId,
        IReadOnlySet<string>? queuedAssetIds)
    {
        if (project is null)
        {
            return Items(
                new StudioBrowserPreviewItem(
                    "No generated clips yet",
                    "Finish Generate to choose and edit clips here.",
                    "EMPTY",
                    "Icon.Spark"));
        }

        return Array.AsReadOnly(
            project.Assets
                .Select(
                    asset =>
                    {
                        StudioClipSelectionPresentation presentation =
                            PresentClipSelection(asset);
                        return new StudioBrowserPreviewItem(
                            asset.EditorialMetadata?.Title ??
                                $"Clip {asset.Rank:00}",
                            $"{StudioTimeFormatter.FormatDuration(asset.Duration)} · " +
                            Path.GetFileName(asset.SourceFullPath),
                            presentation.Label,
                            "Icon.Media",
                            asset.Id,
                            asset.Id.Equals(
                                selectedAssetId,
                                StringComparison.Ordinal),
                            asset.IsIncludedInFinalRender,
                            queuedAssetIds?.Contains(asset.Id) == true,
                            $"Source {StudioTimeFormatter.FormatTime(asset.SourceStart)}–" +
                            StudioTimeFormatter.FormatTime(asset.SourceEnd),
                            $"Pick {asset.Rank} of {project.Assets.Count}",
                            presentation.Explanation);
                    })
                .ToArray());
    }

    internal static StudioClipSelectionPresentation PresentClipSelection(
        GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.EditorialContext?.DeterministicReason.StartsWith("Check the game name:", StringComparison.Ordinal) == true)
            return new("Check game name", asset.EditorialContext.DeterministicReason);
        string label = asset.SelectionReason switch
        {
            GenerationCandidateSelectionReason.UserReservedRange =>
                "Requested",
            GenerationCandidateSelectionReason.UserPriority =>
                "Matches your request",
            GenerationCandidateSelectionReason.QualityQualified =>
                asset.PreferenceFeatures?.DetectedContent?.VisualReviewCompleted == true ? "Strong match" : "Suggested · review",
            GenerationCandidateSelectionReason.QualityQualifiedGameplayEventCoverage =>
                "Gameplay moment",
            GenerationCandidateSelectionReason.CountFillBelowQualityTarget =>
                "More variety",
            GenerationCandidateSelectionReason.CountFillRelaxedDiversity =>
                "Similar option",
            GenerationCandidateSelectionReason.HiddenMomentRecovery =>
                "Found later",
            GenerationCandidateSelectionReason.ManualSourceCut => "Your cut",
            _ => "Selected clip",
        };
        return new StudioClipSelectionPresentation(
            label,
            PresentClipExplanation(asset.Explanation, asset.SelectionReason));
    }

    private static string PresentClipExplanation(
        string explanation,
        GenerationCandidateSelectionReason reason)
    {
        string normalized = explanation.ToUpperInvariant();
        if (normalized.Contains("AGREEMENT", StringComparison.Ordinal) ||
            normalized.Contains("INDEPENDENT", StringComparison.Ordinal))
        {
            return "Several picture and sound cues lined up around this moment.";
        }
        if (normalized.Contains("PRESENTER", StringComparison.Ordinal))
        {
            return "A clear on-camera reaction helped this moment stand out.";
        }
        if (normalized.Contains("AUDIO", StringComparison.Ordinal) ||
            normalized.Contains("SILENCE", StringComparison.Ordinal))
        {
            return "A noticeable sound change helped this moment stand out.";
        }
        if (normalized.Contains("OUTCOME", StringComparison.Ordinal) ||
            normalized.Contains("RECOVERY", StringComparison.Ordinal))
        {
            return "The cut keeps the action and what happened next.";
        }
        if (normalized.Contains("DURATION", StringComparison.Ordinal) ||
            normalized.Contains(
                "REPRESENTATIVE-SEGMENT",
                StringComparison.Ordinal))
        {
            return "This moment fit the clip length you asked for.";
        }
        if (normalized.Contains("GAMEPLAY", StringComparison.Ordinal) ||
            normalized.Contains("SCENE", StringComparison.Ordinal) ||
            normalized.Contains("ACTIVATION", StringComparison.Ordinal) ||
            normalized.Contains("EPISODE", StringComparison.Ordinal))
        {
            return "A clear change in the action made this moment stand out.";
        }

        return reason switch
        {
            GenerationCandidateSelectionReason.UserReservedRange =>
                "You marked this part of the video to include.",
            GenerationCandidateSelectionReason.UserPriority =>
                "This moment best matched what you asked Replay Foundry to find.",
            GenerationCandidateSelectionReason.QualityQualified =>
                "This was one of the strongest complete moments in the video.",
            GenerationCandidateSelectionReason.QualityQualifiedGameplayEventCoverage =>
                "This strong gameplay event keeps the set from leaning too heavily on conversation.",
            GenerationCandidateSelectionReason.CountFillBelowQualityTarget =>
                "This usable option adds variety to the set you asked for.",
            GenerationCandidateSelectionReason.CountFillRelaxedDiversity =>
                "This option completed your requested clip count and may feel similar to another pick.",
            GenerationCandidateSelectionReason.HiddenMomentRecovery =>
                "You brought this alternate into the Studio.",
            GenerationCandidateSelectionReason.ManualSourceCut =>
                "You chose this range from the full source recording.",
            _ => "Replay Foundry selected this as a useful editing option.",
        };
    }

    private static IReadOnlyList<StudioBrowserPreviewItem> Items(
        params StudioBrowserPreviewItem[] items) => Array.AsReadOnly(items);

}
