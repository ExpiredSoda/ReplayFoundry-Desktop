using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialRerollPreferenceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Editorial reroll provider choice persists without optional fallback",
            ProviderChoicePersists),
        new(
            "Studio single reroll obeys the shared provider choice",
            StudioSingleRerollObeysProviderChoice),
    ];

    private static Task ProviderChoicePersists()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(
            directory.Path,
            "editorial-reroll-preference.json");
        var initial = new EditorialRerollPreferenceState(
            new JsonEditorialRerollPreferenceStore(path));

        TestAssert.False(
            initial.UseLocalAi,
            "A missing preference must default to the fast deterministic generator rather than silently starting expensive AI.");

        initial.SetUseLocalAi(true);
        var reloaded = new EditorialRerollPreferenceState(
            new JsonEditorialRerollPreferenceStore(path));

        TestAssert.True(
            reloaded.UseLocalAi,
            "The local-AI choice must survive a new store instance.");

        reloaded.SetUseLocalAi(false);
        var disabledAgain = new EditorialRerollPreferenceState(
            new JsonEditorialRerollPreferenceStore(path));
        TestAssert.False(
            disabledAgain.UseLocalAi,
            "Turning the preference off must persist the deterministic provider choice.");
        return Task.CompletedTask;
    }

    private static async Task StudioSingleRerollObeysProviderChoice()
    {
        (GenerationOutputProject project,
            GenerationOutputAsset asset,
            GenerationOutputSession session) = CreateStudioProject();
        var preference = new EditorialRerollPreferenceState(
            new InMemoryEditorialRerollPreferenceStore(
                new EditorialRerollPreferenceSnapshot(
                    UseLocalAi: true)));
        var generator = new RecordingUnavailableAiGenerator();
        using var editor = new StudioEditorialMetadataViewModel(
            session,
            generator,
            new ClipEditorialProfileSession(),
            preference);
        editor.Bind(project, asset);

        TestAssert.True(
            editor.RerollCommand.CanExecute(null),
            "A selected editable clip must keep one reroll action even when its required AI provider is unavailable.");
        await ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();

        TestAssert.Equal(
            ClipEditorialGenerationPreference.AiRequired,
            generator.Requests[0].Preference,
            "The on preference must reach the existing Studio service as AiRequired.");
        TestAssert.True(
            editor.Status.Contains(
                "unavailable",
                StringComparison.OrdinalIgnoreCase),
            "A missing required provider must fail visibly in Studio.");
        TestAssert.Same(
            asset.EditorialMetadata!,
            session.Current!.Assets[0].EditorialMetadata!,
            "A failed required-AI reroll must not replace the retained draft with heuristics.");

        preference.SetUseLocalAi(false);
        await ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();

        TestAssert.Equal(
            2,
            generator.Requests.Count,
            "The same primary action must invoke the shared generator once per request.");
        TestAssert.Equal(
            ClipEditorialGenerationPreference.HeuristicOnly,
            generator.Requests[1].Preference,
            "The off preference must reach the existing Studio service as HeuristicOnly.");
        TestAssert.Equal(
            ClipEditorialMetadataOrigin.Heuristic,
            session.Current!.Assets[0].EditorialMetadata!.Origin,
            "The successful off-state reroll must retain the deterministic result.");
    }

    private static (
        GenerationOutputProject Project,
        GenerationOutputAsset Asset,
        GenerationOutputSession Session) CreateStudioProject()
    {
        string sourcePath = Path.GetFullPath(
            Path.Combine("reroll-preference", "source.mkv"));
        var context = new ClipEditorialContext(
            "candidate-reroll-preference",
            sourcePath,
            "ExampleGame",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(35),
            TimeSpan.FromMinutes(2),
            82,
            "Deterministic scene and audio evidence aligned.");
        var metadata = new ClipEditorialMetadataDraft(
            "Retained metadata title",
            "Retained grounded description.",
            ["examplegame"],
            ClipEditorialMetadataOrigin.UserEdited,
            new ClipEditorialMetadataGeneratorIdentity(
                "reroll-preference-fixture",
                "1.0"),
            attempt: 1);
        var asset = new GenerationOutputAsset(
            context.CandidateId,
            1,
            TestMediaFactory.Create(
                sourcePath,
                context.SourceDuration),
            outputFullPath: null,
            context.SourceStart,
            context.SourceEnd,
            context.DeterministicScore,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            context.DeterministicReason,
            editorialContext: context,
            editorialMetadata: metadata);
        var project = new GenerationOutputProject(
            "project-reroll-preference",
            GenerationMode.IndividualClips,
            Path.GetFullPath("reroll-preference-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        return (project, asset, session);
    }

    private sealed class RecordingUnavailableAiGenerator :
        IClipEditorialMetadataGenerationService
    {
        public bool IsAiAvailable => false;

        public List<ClipEditorialMetadataRequest> Requests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (request.Preference ==
                ClipEditorialGenerationPreference.AiRequired)
            {
                throw new InvalidOperationException(
                    "Qualified local AI is unavailable for this test.");
            }

            return Task.FromResult(
                new ClipEditorialMetadataDraft(
                    "Deterministic reroll title",
                    "Deterministic reroll description.",
                    ["examplegame"],
                    ClipEditorialMetadataOrigin.Heuristic,
                    new ClipEditorialMetadataGeneratorIdentity(
                        "recording-heuristic",
                        "1.0"),
                    request.Attempt));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ReplayFoundryEditorialRerollTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
