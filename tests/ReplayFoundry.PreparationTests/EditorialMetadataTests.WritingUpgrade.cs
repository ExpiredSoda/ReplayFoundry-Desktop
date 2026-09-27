using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Features.Studio.Projects;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task WritingIntentSurvivesRequestClones()
    {
        var writing = new ClipEditorialWritingRequest(ClipEditorialWritingAction.Rewrite, "The closed door", "The handle did not move.", KeepTitle:true);
        var request = new ClipEditorialMetadataRequest(CreateContext(), ClipEditorialProfile.Default, 1).WithWriting(writing);
        var cloned = request.WithAttempt(2).WithTone("Playful").WithVariantIntent(ClipEditorialVariantIntent.ConcreteDetail)
            .WithPriorAcceptedTitleExclusions([]);
        TestAssert.True(ReferenceEquals(writing, cloned.Writing), "All request transforms retain current wording, action and locks.");
        TestAssert.False(cloned.RequiresNewAngle, "A rewrite does not require a new semantic angle.");
        return Task.CompletedTask;
    }

    private static async Task StudioWritingActionsHaveDifferentIntent()
    {
        var (asset, _) = await CreateAssetAsync();
        var project = WritingProject(asset);
        var session = new GenerationOutputSession(); session.Publish(project);
        var generator = new RecordingRequestMetadataGenerator();
        using var studio = new StudioViewModel(session, session, new UnusedProjectRenderer(), generator, new ClipEditorialProfileSession());
        var editor = studio.Inspector.Editorial;
        await ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();
        TestAssert.Equal(ClipEditorialWritingAction.Rewrite, generator.LastRequest!.Writing!.Action, "Rewrite retains the idea.");
        TestAssert.Equal(asset.EditorialMetadata!.Title, generator.LastRequest.Writing.CurrentTitle, "Current wording reaches the model as editing context.");
        await ((AsyncDelegateCommand)editor.NewAngleCommand).ExecuteAsync();
        TestAssert.Equal(ClipEditorialWritingAction.NewAngle, generator.LastRequest!.Writing!.Action, "New angle asks for different meaning.");
    }

    private static async Task WritingCannotOverwriteNewerSavedCopy()
    {
        var (asset, _) = await CreateAssetAsync();
        var project = WritingProject(asset);
        var session = new GenerationOutputSession(); session.Publish(project);
        var generator = new DeferredMetadataGenerator();
        var service = new StudioEditorialMetadataService(session,generator,new ClipEditorialProfileSession());
        var pending = service.RerollAsync(project,asset,"Chat","","",false,CancellationToken.None);
        await generator.Started;
        service.Save(project,asset,"My newer wording","This description was saved during generation.","game");
        generator.Complete();
        try { await pending; throw new InvalidOperationException("Expected a stale writing result to be rejected."); }
        catch (InvalidOperationException error) when (error.Message.Contains("newer wording",StringComparison.Ordinal)) { }
        TestAssert.Equal("My newer wording",session.Current!.PrimaryAsset.EditorialMetadata!.Title,"Newer saved copy survives.");
    }

    private static async Task TypingCancelsPendingWritingWithoutLosingDraft()
    {
        var (asset, _) = await CreateAssetAsync(); var project = WritingProject(asset);
        var session = new GenerationOutputSession(); session.Publish(project);
        var generator = new DeferredMetadataGenerator();
        var preference = new EditorialRerollPreferenceState(new InMemoryEditorialRerollPreferenceStore());
        preference.SetUseLocalAi(false);
        using var editor = new StudioEditorialMetadataViewModel(session,generator,new ClipEditorialProfileSession(),preference);
        editor.Bind(project,asset);
        var pending = ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();
        await generator.Started;
        editor.Description = "I am still editing this description.";
        generator.Complete(); await pending;
        TestAssert.Equal("I am still editing this description.",editor.Description,"In-flight output cannot erase typing.");
        TestAssert.Equal(asset.EditorialMetadata!.Description,session.Current!.PrimaryAsset.EditorialMetadata!.Description,"Cancellation retains saved wording.");
        TestAssert.True(editor.HasUnsavedChanges,"Typing remains available to save.");
    }

    private static async Task AlternativesPreserveHistoryAndExpireOnManualEdits()
    {
        var (_, draft) = await CreateAssetAsync();
        var choice = new ClipEditorialAlternative("Another supported angle","A complementary description.",["game"],"A different detail",
            ClipEditorialAlternative.ProfileKey(ClipEditorialProfile.Default,"Natural"),new string('a',64));
        var restored = JsonSerializer.Deserialize<ClipEditorialAlternative>(JsonSerializer.Serialize(choice))!;
        TestAssert.Equal(choice.Description,restored.Description,"Alternative descriptions survive serialization.");
        TestAssert.Throws<ArgumentException>(() => new ClipEditorialAlternative("Title","Description",[null!],"Action",
            choice.ProfileFingerprint,choice.FactHash),"Malformed saved tags are rejected when loading alternatives.");
        var withChoices = new ClipEditorialMetadataDraft(draft.Title,draft.Description,draft.Tags,draft.Origin,draft.Generator,
            draft.Attempt,draft.Evidence,alternatives:[restored]);
        var selected = withChoices.SelectAlternative(restored,"context");
        TestAssert.Equal(choice.Description,selected.Description,"Selecting an alternative uses its exact description.");
        TestAssert.Equal(draft.Title,selected.CopyVersions.Last().Title,"The previous title remains available for undo.");
        TestAssert.True(selected.PriorAcceptedTitles.Contains(draft.Title),"The previous idea remains in reroll exclusion history.");
        TestAssert.Equal(0,selected.Alternatives.Count,"Selecting an alternative removes it from the remaining choices.");
        TestAssert.Equal(0,withChoices.WithUserEdits("Manual title","Manual description",draft.Tags).Alternatives.Count,"Manual changes invalidate the old batch.");
    }

    private static GenerationOutputProject WritingProject(GenerationOutputAsset asset) => new("writing-upgrade",
        GenerationMode.IndividualClips,Path.GetFullPath("writing-upgrade-output"),1,ClipFulfillmentPreference.FillRequestedCount,
        GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,[asset],DateTimeOffset.UtcNow);

    private static Task WritingDefaultsSurviveRestart()
    {
        string folder = Path.Combine(Path.GetTempPath(), "writing-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder,"profile.json");
            var session = new ClipEditorialProfileSession(new JsonClipEditorialProfileStore(path));
            session.Update(new("Friends","Keep it conversational","See you next time.",["horror"],defaultTone:"Playful"));
            var restored = new ClipEditorialProfileSession(new JsonClipEditorialProfileStore(path));
            TestAssert.True(restored.IsPersistent,"Installed writing defaults use disk storage.");
            TestAssert.Equal("Friends",restored.Current.AudienceAddress,"Audience survives restart.");
            TestAssert.Equal("Playful",new ClipEditorialMetadataRequest(CreateContext(),restored.Current,0).Tone,"Generation uses the saved tone.");
            TestAssert.Equal("See you next time.",restored.Current.ReusableDescriptionSignature,"Description ending survives restart.");
            File.WriteAllText(path,"{broken preferences");
            TestAssert.Equal("Chat",new JsonClipEditorialProfileStore(path).Load().AudienceAddress,"Corrupt preferences use safe defaults.");
            TestAssert.Equal("{broken preferences",File.ReadAllText(path),"Corrupt file remains recoverable.");
        }
        finally { Directory.Delete(folder,true); }
        return Task.CompletedTask;
    }

    private sealed class RejectingProfileStore : IClipEditorialProfileStore
    {
        public ClipEditorialProfile Load() => ClipEditorialProfile.Default;
        public void Save(ClipEditorialProfile profile) => throw new IOException("Test disk failure");
    }

    private static Task WritingDefaultsKeepPreviousOnFailure()
    {
        var session = new ClipEditorialProfileSession(new RejectingProfileStore());
        var before = session.Current;
        try { session.Update(new("Friends")); throw new InvalidOperationException("Expected save failure."); }
        catch (IOException) { }
        TestAssert.True(ReferenceEquals(before,session.Current),"Failed durable writes cannot masquerade as saved defaults.");
        return Task.CompletedTask;
    }

    private static async Task StudioToneSurvivesRebind()
    {
        var (asset,_) = await CreateAssetAsync();
        var session = new GenerationOutputSession(); var project = WritingProject(asset); session.Publish(project);
        var profile = new ClipEditorialProfileSession();
        using var editor = new StudioEditorialMetadataViewModel(session,new RecordingRequestMetadataGenerator(),profile);
        editor.Bind(project,asset); editor.Tone = "Playful"; editor.Bind(project,asset);
        TestAssert.Equal("Playful",editor.Tone,"A rewrite rebind does not reset the active tone or hide matching alternatives.");
        editor.SaveProfileCommand.Execute(null);
        TestAssert.Equal("Playful",profile.Current.DefaultTone,"Tone can become the default for future generation.");
        TestAssert.False(editor.HasUnsavedProfileChanges,"Saving tone clears the dirty state.");
    }

    private static async Task WritingAlternativesSurviveProjectSave()
    {
        var (asset,draft) = await CreateAssetAsync();
        var choice = new ClipEditorialAlternative("Another title","The exact alternative description.",["game"],"Commentary",
            ClipEditorialAlternative.ProfileKey(ClipEditorialProfile.Default,"Natural"),new string('a',64));
        var saved = new ClipEditorialMetadataDraft(draft.Title,draft.Description,draft.Tags,draft.Origin,draft.Generator,draft.Attempt,
            draft.Evidence,alternatives:[choice]);
        string folder = Path.Combine(Path.GetTempPath(),"writing-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder,"source.mkv"); File.WriteAllBytes(source,[1,2,3]);
            var file = new FileInfo(source);
            var context = new ClipEditorialContext(asset.Id,source,"ExampleGame",asset.SourceStart,asset.SourceEnd,
                asset.SourceDuration,82.5,"Persistence fixture",evidence:[new("scene-review-source-binding",ClipEditorialEvidenceKind.SourceIdentity,
                    JsonSerializer.Serialize(new { start=asset.SourceStart.Ticks,end=asset.SourceEnd.Ticks,length=file.Length,
                        modified=file.LastWriteTimeUtc.Ticks,source=file.FullName }))]);
            var media = TestMediaFactory.Create(source,asset.SourceDuration);
            asset = new GenerationOutputAsset(asset.Id,1,media,null,asset.SourceStart,asset.SourceEnd,82.5,70,
                GenerationCandidateSelectionReason.QualityQualified,"Persistence fixture",editorialContext:context,editorialMetadata:saved);
            asset = asset.WithCurrentCutEditorialMetadata(context,saved);
            var document = StudioProjectDocumentMapper.Capture(WritingProject(asset),1,DateTimeOffset.UtcNow);
            var roundtrip = JsonSerializer.Deserialize<StudioProjectDocument>(JsonSerializer.Serialize(document))!;
            var reopened = StudioProjectDocumentMapper.Restore(roundtrip);
            var restored = reopened.PrimaryAsset.EditorialMetadata!;
            TestAssert.Equal(choice.Description,restored.Alternatives.Single().Description,"Project reopen preserves the full alternative description.");
            TestAssert.Equal(saved.Description,restored.Description,"Saving alternatives cannot overwrite the selected description.");
            var output = new GenerationOutputSession(); output.Publish(reopened);
            var service = new StudioEditorialMetadataService(output,null,new ClipEditorialProfileSession());
            var alternative = restored.Alternatives.Single();
            service.SelectAlternative(reopened,reopened.PrimaryAsset,alternative,"Chat",ClipEditorialProfile.DefaultNamingGuidance,"","Natural",false,false);
            var selected = output.Current!.PrimaryAsset.EditorialMetadata!;
            TestAssert.Equal(choice.Description,selected.Description,"Choosing saved wording works with no AI generator.");
            TestAssert.Equal(saved.Description,selected.CopyVersions.Last().Description,"Selection preserves the previous full description.");
            output.Publish(reopened); File.AppendAllText(source,"changed source");
            var error = TestAssert.Throws<InvalidOperationException>(() => service.SelectAlternative(reopened,reopened.PrimaryAsset,
                alternative,"Chat",ClipEditorialProfile.DefaultNamingGuidance,"","Natural",false,false),"A changed source invalidates saved alternatives.");
            TestAssert.True(error.Message.Contains("recording",StringComparison.Ordinal),"Stale source rejection explains why another review is required.");
        }
        finally { Directory.Delete(folder,true); }
    }

    private static Task ProviderCopyHonorsLocks()
    {
        var request = new ClipEditorialMetadataRequest(CreateContext(),new ClipEditorialProfile(reusableDescriptionSignature:"New signature"),1)
            .WithWriting(new(ClipEditorialWritingAction.Rewrite,"Keep this title","Keep this description",KeepDescription:true));
        Qwen3VlSceneCopyGenerator.ValidateLocks("A different title","Keep this description",request);
        TestAssert.Equal("Keep this description",Qwen3VlSceneCopyGenerator.FinishDescription("Keep this description",request),
            "A new default signature must not modify a locked description.");
        try { Qwen3VlSceneCopyGenerator.ValidateLocks("A different title","Changed description",request); throw new InvalidOperationException("Expected locked field rejection."); }
        catch (InvalidDataException) { }
        return Task.CompletedTask;
    }

    private static Task WritingAlternativesRequireQualityAgreement()
    {
        var request = new ClipEditorialMetadataRequest(CreateContext(),ClipEditorialProfile.Default,0);
        object Judgment(double first, double second) => new
        {
            version="copy-judgment-2", calibrated=false, margins=new[] { first, second },
            value=(1/(1+Math.Exp(-first))+1/(1+Math.Exp(-second)))/2,
        };
        JsonElement Row(double qualityMargin) => JsonSerializer.SerializeToElement(new
        {
            alternatives=new[] { new
            {
                copy=new { titleBody="A supported title", description="A complementary detail." },
                angle="Action", factHash=new string('a',64),
                neuralGrounding=Judgment(2,2), neuralQuality=Judgment(20,qualityMargin),
            } },
        });
        TestAssert.Equal(1,QwenSceneCopyAlternatives.Read(Row(2),request).Count,"Agreed writing remains selectable.");
        foreach (double margin in new[] { -8.0, 0.0 })
            TestAssert.Throws<InvalidDataException>(() => QwenSceneCopyAlternatives.Read(Row(margin),request),
                "A positive average cannot hide disputed alternative quality.");
        return Task.CompletedTask;
    }
}
