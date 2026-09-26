using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialPackagingTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Packaging guidance distinguishes a redundant description from added context", DescriptionAddsContext),
        new("Packaging flags an unfinished title modifier without rejecting complete endings", UnfinishedModifierNeedsReview),
        new("Earlier editorial versions survive edits and JSON without becoming grounding", CopyVersionsSurvive),
        new("Editorial version history stays bounded without mutable tag aliases", CopyHistoryIsBounded),
        new("Reused earlier wording stays the most recent recoverable version", ReusedCopyRemainsRecent),
    ];

    private static Task DescriptionAddsContext()
    {
        var repeated = ClipAudiencePackagingAssessment.Evaluate("The bridge collapsed", "The bridge collapsed.");
        var expanded = ClipAudiencePackagingAssessment.Evaluate("The bridge collapsed", "We reached the stone arch after the bridge collapsed behind us.");
        TestAssert.True(repeated.Penalty > expanded.Penalty, "A useful additional detail should improve packaging guidance.");
        TestAssert.True(ClipAudiencePackagingAssessment.Evaluate("You won't believe this", "A bridge collapsed behind us.").Penalty > expanded.Penalty,
            "An empty curiosity hook should receive actionable feedback.");
        TestAssert.True(ClipAudiencePackagingAssessment.Evaluate("A Complete Gameplay Sequence #REANIMAL", "A complete, uninterrupted REANIMAL gameplay sequence.").Penalty > expanded.Penalty,
            "A readable generic working label must not be presented as strong packaging.");
        foreach (string workingLabel in new[] { "A Continuous Part of the Run #REANIMAL", "A Complete Part of the Run #REANIMAL" })
            TestAssert.True(ClipAudiencePackagingAssessment.Evaluate(workingLabel, "A continuous part of a playthrough of REANIMAL.").Penalty > expanded.Penalty,
                "Generic run labels observed in real validation need the same concrete-detail guidance.");
        TestAssert.Equal(expanded.Penalty, ClipAudiencePackagingAssessment.Evaluate("The bridge collapsed #REANIMAL", "We reached the stone arch after the bridge collapsed behind us.").Penalty,
            "Game tags must not distort title-body guidance.");
        return Task.CompletedTask;
    }

    private static ClipEditorialMetadataDraft Draft(string title) => new(title, "We reached the arch before the bridge fell.", ["REANIMAL"],
        ClipEditorialMetadataOrigin.Heuristic, new ClipEditorialMetadataGeneratorIdentity("test", "1"), 0);

    private static Task UnfinishedModifierNeedsReview()
    {
        const string observed = "Figure swims through dark underwater tunnel with headlamp beam cutting #REANIMAL";
        const string description = "A figure swims through a dark underwater tunnel, headlamp cutting through murky water and floating debris.";
        ClipAudiencePackagingAssessment assessment = ClipAudiencePackagingAssessment.Evaluate(observed, description);
        TestAssert.True(assessment.Penalty > 0 && assessment.Suggestions.Any(static value =>
                value.Contains("modifier clause", StringComparison.Ordinal)),
            "The observed capped clause must no longer receive zero mechanical issues.");
        TestAssert.True(ClipEditorialTitleCompletenessPolicy.HasUnfinishedModifier(
                "The door opened with a spotlight illuminating #REANIMAL", "A spotlight was illuminating the passage."),
            "An unfinished complement must be recognized without relying on the exact observed wording or length.");
        foreach (string complete in new[]
        {
            "We escaped with lights fading #REANIMAL",
            "The boat left with engines roaring #REANIMAL",
            "The tunnel opened with headlamp beam cutting through darkness #REANIMAL",
            "I kept cutting #REANIMAL",
            "The workshop opened with workers cutting #REANIMAL",
            "The opening faded in #REANIMAL",
            "The checkpoint guard checked in #REANIMAL",
            "Boat heads for red light, fading into darkness after arrival #REANIMAL",
        })
        {
            TestAssert.False(ClipEditorialTitleCompletenessPolicy.HasUnfinishedModifier(complete, description),
                "Complete/intransitive endings and grammatical chronology need factual review, not a fabricated completeness failure: " + complete);
        }
        TestAssert.False(ClipEditorialTitleCompletenessPolicy.HasUnfinishedModifier(observed, "The water was dark."),
            "Without a matching complement, the narrow check must leave completeness uncertain.");
        return Task.CompletedTask;
    }

    private static Task CopyVersionsSurvive()
    {
        var previous = Draft("The bridge collapsed");
        var current = Draft("The arch was still standing").RememberPreviousCopy(previous, "cut-a");
        var edited = current.WithUserEdits("We reached the arch", current.Description, current.Tags).MarkReviewed();
        var versions = JsonSerializer.Deserialize<ClipEditorialCopyVersion[]>(JsonSerializer.Serialize(edited.CopyVersions))!;
        TestAssert.Equal(previous.Title, versions.Single().Title, "Earlier title must remain recoverable after edit, review, and persistence.");
        TestAssert.Equal("cut-a", versions.Single().ContextFingerprint, "Restoration must retain its exact context boundary.");
        TestAssert.Equal(0, edited.Evidence.Count, "Earlier packages must not be promoted into factual evidence.");
        return Task.CompletedTask;
    }

    private static Task CopyHistoryIsBounded()
    {
        var current = Draft("Title 0");
        for (int index = 1; index <= 25; index++) current = Draft($"Title {index}").RememberPreviousCopy(current, "cut-a");
        TestAssert.Equal(20, current.CopyVersions.Count, "Long reroll sessions must keep a bounded history.");
        TestAssert.Equal("Title 24", current.CopyVersions.Last().Title, "The newest earlier version must survive the cap.");
        var mutable = new List<string> { "REANIMAL" };
        var version = new ClipEditorialCopyVersion("Bridge", "The bridge fell.", mutable, "cut-a", DateTimeOffset.UtcNow);
        mutable.Clear();
        TestAssert.Equal(1, version.Tags.Count, "A caller cannot mutate saved tags after capture.");
        return Task.CompletedTask;
    }

    private static Task ReusedCopyRemainsRecent()
    {
        var current = Draft("Title 0");
        for (int index = 1; index <= 20; index++) current = Draft($"Title {index}").RememberPreviousCopy(current, "cut-a");
        var restored = current.WithUserEdits("Title 0", current.Description, current.Tags);
        var rerolled = Draft("A new title").RememberPreviousCopy(restored, "cut-a");
        TestAssert.Equal(20, rerolled.CopyVersions.Count, "Reused wording must keep the history bounded.");
        TestAssert.Equal("Title 0", rerolled.CopyVersions.Last().Title, "The immediately previous wording must survive as newest even when it was restored from the oldest entry.");
        TestAssert.True(rerolled.CopyVersions.Last().SavedAtUtc >= current.CopyVersions.First().SavedAtUtc,
            "Recapturing previous wording must preserve its latest save time.");
        return Task.CompletedTask;
    }
}
