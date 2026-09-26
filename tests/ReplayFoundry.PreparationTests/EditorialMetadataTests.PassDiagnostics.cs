using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task GroundedExecutorPassDiagnosticsAreOptIn()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        var request = fixture.CreateRequest();
        var observations = new List<QwenEditorialPassDiagnostic>();
        var environments = new List<IReadOnlyDictionary<string, string>>();
        var runner = new FailureArtifactProcessRunner(process =>
        {
            environments.Add(process.EnvironmentVariables);
            return new ProcessRunResult(2, string.Empty,
                QwenEditorialPassDiagnosticsTests.Line(request.Context.CandidateId, request.Attempt,
                    preparationFailure: true) + "\n" +
                "{\"errorCode\":\"UsageOrInputError\",\"message\":\"model-free stop\"}",
                TimeSpan.FromMilliseconds(20));
        });
        using var generator = new Qwen3VlGroundedMetadataGenerator(fixture.Runtime, runner, new SystemQwen3VlBatchWorkspaceFactory());
        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(() => generator.GenerateAsync(request, CancellationToken.None),
            "Uncaptured process failure retains existing provider semantics.");
        using (QwenEditorialPassDiagnostics.Capture(observations.Add))
            await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(() => generator.GenerateAsync(request, CancellationToken.None),
                "Pass timing cannot turn a failed process into an accepted draft.");
        TestAssert.Equal(2, environments.Count, "Diagnostics never add another provider invocation.");
        TestAssert.True(!environments[0].ContainsKey(QwenEditorialPassDiagnostics.EnvironmentFlag), "Default invocation remains uninstrumented.");
        TestAssert.Equal("1", environments[1][QwenEditorialPassDiagnostics.EnvironmentFlag], "Captured invocation opts in on the owned child only.");
        TestAssert.True(!fixture.Runtime.Host.EnvironmentVariables.ContainsKey(QwenEditorialPassDiagnostics.EnvironmentFlag),
            "The qualified runtime environment snapshot stays unchanged.");
        TestAssert.Equal(1, observations.Count, "The executor reports a completed diagnostic even when provider output is rejected.");
        TestAssert.Equal(request.Context.CandidateId, observations[0].CandidateId, "The observation belongs to the actual submitted candidate.");
    }
}
