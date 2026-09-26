using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static partial class YouTubePublishingTests
{
    private static async Task PublishLegacyRerollPreservesContextBoundary()
    {
        using var fixture = new PublishAssetFixture(32);
        using var directory = new TemporaryDirectory();
        var media = TestMediaFactory.Create(fixture.SourcePath, TimeSpan.FromSeconds(30));
        var context = new ClipEditorialContext("candidate-1", fixture.SourcePath, "Test Game", TimeSpan.Zero,
            TimeSpan.FromSeconds(30), media.Duration, 82, "Retained bounded source evidence.");
        var metadata = new ClipEditorialMetadataDraft("An unverified older title", "Older saved wording.", [],
            ClipEditorialMetadataOrigin.UserEdited, new ClipEditorialMetadataGeneratorIdentity("legacy", "1"), 4,
            priorAcceptedTitles: ["An unverified earlier title"]);
        var source = new GenerationOutputAsset("candidate-1", 1, media, fixture.Asset.OutputFullPath,
            context.SourceStart, context.SourceEnd, 82, 70, GenerationCandidateSelectionReason.QualityQualified,
            context.DeterministicReason, editorialContext: context, editorialMetadata: metadata);
        TestAssert.True(source.EditorialAuthoredContextRevision is null, "This regression must begin with genuinely unknown old copy provenance.");
        TestAssert.False(source.IsEditorialMetadataCurrentForCut, "A valid source context does not prove old wording was authored for it.");

        GenerationOutputProject Project(GenerationOutputAsset output, bool finalized = true) => new(
            "project-1", GenerationMode.IndividualClips, Path.GetDirectoryName(fixture.Asset.OutputFullPath)!,
            1, ClipFulfillmentPreference.QualityFirst, GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [output], DateTimeOffset.UnixEpoch, finalized ? DateTimeOffset.UnixEpoch.AddMinutes(1) : null);
        LibraryMediaAsset Library(string projectId, string candidateId = "candidate-1") => new(
            "legacy-library", projectId, fixture.Asset.Mode, fixture.Asset.Rank, fixture.Asset.OutputFullPath,
            fixture.Asset.ThumbnailFullPath, fixture.Asset.Duration, fixture.Asset.OutputWidth, fixture.Asset.OutputHeight,
            fixture.Asset.Title, fixture.Asset.Description, fixture.Asset.Tags, fixture.Asset.AddedAtUtc,
            sourceCandidateIds: [candidateId]);

        GenerationOutputProject project = Project(source);
        var store = new JsonStudioProjectStore(directory.Path); store.Save(project, 1);
        var session = new GenerationOutputSession(); session.Publish(project);
        var generator = new RecordingEditorialGenerationService();
        foreach (var service in new[]
        {
            new PublishEditorialMetadataService(session, generator, new ClipEditorialProfileSession()),
            new PublishEditorialMetadataService(new GenerationOutputSession(), generator, new ClipEditorialProfileSession(), store),
        })
        {
            TestAssert.True(service.CanReroll(fixture.Asset), "Unknown old wording must not prevent fresh authoring from an exact finalized cut.");
            await service.RerollAsync(fixture.Asset, "Viewers", "", "", null, metadata.Title,
                ["Another old publish headline"], true, CancellationToken.None);
            TestAssert.Equal(0, generator.LastRequest!.PriorAcceptedTitleExclusions.Count,
                "Neither retained nor UI-supplied unknown title history may be assigned to the resolved current context.");
            TestAssert.Equal(context.SourceStart, generator.LastRequest.Context.SourceStart, "Fresh generation must retain the exact cut start.");
            TestAssert.Equal(context.SourceEnd, generator.LastRequest.Context.SourceEnd, "Fresh generation must retain the exact cut end.");
            TestAssert.Equal(fixture.SourcePath, generator.LastRequest.SourceMedia!.FullPath, "Fresh generation must retain its verified original source.");
        }

        var durable = new PublishEditorialMetadataService(new GenerationOutputSession(), generator, new ClipEditorialProfileSession(), store);
        LibraryMediaAsset batch = Library("project-1-render-abcdef12");
        TestAssert.True(durable.CanReroll(batch), "A finalized source project's exact rendered binding remains usable after restart.");
        TestAssert.False(durable.CanReroll(Library(batch.ProjectId, "foreign-candidate")), "A render alias must not borrow another candidate's context.");

        var active = new PublishEditorialMetadataService(session, generator, new ClipEditorialProfileSession());
        session.Publish(Project(source.WithStudioEdits(TimeSpan.FromSeconds(1), source.SourceEnd, source.Appearance)
            .WithRenderedOutput(fixture.Asset.OutputFullPath)));
        TestAssert.False(active.CanReroll(fixture.Asset), "Old source-range context must not describe a different rendered cut.");
        string unrelatedOutput = Path.Combine(directory.Path, "unrelated.mp4"); File.WriteAllBytes(unrelatedOutput, [1]);
        session.Publish(Project(source.WithRenderedOutput(unrelatedOutput)));
        TestAssert.False(active.CanReroll(fixture.Asset), "A different output path must not resolve solely through candidate identity.");
        var mutable = Project(source.WithStudioEdits(source.SourceStart, source.SourceEnd, source.Appearance), finalized: false);
        TestAssert.True(!mutable.IsFinalized && !mutable.Assets.Single().IsRendered,
            "A genuine editable project retains its source context while clearing its finalized output binding.");
        TestAssert.Same(context, mutable.Assets.Single().EditorialContext!, "Draft rejection must not depend on losing the otherwise valid source context.");
        session.Publish(mutable);
        TestAssert.False(active.CanReroll(fixture.Asset), "A mutable draft is not the retained finalized render context.");
        store.Save(mutable, 2);
        TestAssert.False(durable.CanReroll(batch), "The durable render-alias fallback must preserve the same finalized guard as direct resolution.");
        session.Publish(project);
        File.Delete(fixture.SourcePath);
        TestAssert.False(active.CanReroll(fixture.Asset), "A missing original source must still block regeneration.");
    }
}
