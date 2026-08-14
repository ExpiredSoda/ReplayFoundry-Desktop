using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Clip render profile is bounded and preserves orientation", ProfileIsBounded),
        new("Clip command mixes exact audio streams and keeps paths atomic", SegmentCommandMapsExactStreams),
        new("Clip command supplies silence when source has no audio", SegmentCommandSuppliesSilence),
        new("Captioned clip command uses ASS and preserves the full audio mix", CaptionedCommandPreservesAudioMix),
        new("ASS subtitle builder supports five deterministic styles", AssBuilderSupportsFiveEffects),
        new("ASS subtitle position uses a bounded vertical percentage", AssBuilderPositionsCaptions),
        new("Caption presentation defaults preserve legacy rendering", CaptionPresentationDefaultsRemainCompatible),
        new("Caption presentation matches Studio preview and final ASS", CaptionPresentationMatchesPreviewAndRender),
        new("Timed captions appear around speech and clear during silence", TimedCaptionsClearDuringSilence),
        new("Caption presentation persists through Studio and caption edits", CaptionPresentationPersistsThroughStudioEdits),
        new("Caption corrections replace text without fabricating word timing", CaptionCorrectionsPreserveTruthfulTiming),
        new("Zero-length Whisper word timing preserves the full caption segment", ZeroLengthWhisperWordsPreserveSegmentTiming),
        new("Caption source language is explicit and options remain immutable", CaptionLanguagePolicyIsExplicit),
        new("Repetitive low-information captions are suppressed without losing provenance", RepetitiveCaptionsAreSuppressed),
        new("Bracketed non-speech tokens stay in provenance but never render", NonSpeechTokensDoNotRender),
        new("Low-probability sparse whisper segments stay in provenance but never render", LowConfidenceSparseSegmentsDoNotRender),
        new("Whisper aggregate captions defer to their tighter fragments without losing provenance", AggregateWhisperCaptionDefersToFragments),
        new("Suppressed captions never enter the Studio render handoff", SuppressedCaptionsDoNotReachStudio),
        new("Studio video treatments map to bounded FFmpeg filters", VideoTreatmentsMapToFilters),
        new("Studio graphic overlays are immutable and validated", GraphicOverlayContractsAreValidated),
        new("Studio graphic overlays enter one bounded FFmpeg graph", GraphicOverlaysEnterRenderGraph),
        new("Studio graphic editor persists through the output boundary", GraphicEditorPersistsOverlay),
        new("Studio final render writes one ASS script per selected clip and cleans it", CaptionRenderingUsesOwnedScripts),
        new("Studio clip boundaries retain exactly one minute of edit context", StudioBoundaryContextIsBounded),
        new("Studio preview context covers the complete one-minute trim envelope", StudioPreviewContextCoversEdits),
        new("Studio preview cache ignores live-only caption styling", StudioPreviewCacheSeparatesLiveCaptionStyling),
        new("Studio CC toggle hides the actual caption overlay", StudioCaptionToggleHidesActualOverlay),
        new("Studio project switching protects every manual-save draft", StudioProjectSwitchProtectsManualSaveDrafts),
        new("Studio project switching preserves simultaneous manual drafts", StudioProjectSwitchPreservesSimultaneousDrafts),
        new("Studio applies caption layout to every captioned clip atomically", StudioCaptionPositionAppliesToAll),
        new("Studio output replacement preserves project identity", StudioReplacementPreservesProject),
        new("Repeated identical generation results receive distinct project identities", RepeatedGenerationResultsHaveDistinctProjectIdentity),
        new("Montage concatenation does not re-encode video", ConcatenationCopiesStreams),
        new("Library thumbnail command samples one bounded rendered frame", ThumbnailCommandIsBounded),
        new("Individual rendering commits complete files atomically", IndividualRenderingCommitsAtomically),
        new("Individual output filenames use the saved audience title", IndividualOutputFilenameUsesSavedTitle),
        new("Accepted completed rendering releases ownership and preserves output", CompletedRenderAcceptReleasesOwnership),
        new("Discarded completed rendering removes its owned output and permits retry", CompletedRenderDiscardEnablesRetry),
        new("Montage renders each segment once then joins once", MontageRendersOncePerSegment),
        new("Rendering failure removes the owned staging directory", FailureRemovesStaging),
        new("Rendering cancellation removes the owned staging directory", CancellationRemovesStaging),
        new("Generation pipeline finds moments without rendering clips", PipelineFindsThenRenders),
        new("Generation pipeline publishes one validated workspace handoff", PipelinePublishesWorkspaceHandoff),
        new("Generation pipeline does not render an empty moment result", PipelineRejectsNoMoments),
    ];

}
