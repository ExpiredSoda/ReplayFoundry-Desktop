using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class QwenModelLoadDiagnosticsTests
{
    public static IEnumerable<TestCase> GetTests()
    {
        yield return new("Model-load diagnostics accept bounded timing without changing the inference protocol", CaptureIsBoundedAndScoped);
    }

    private static Task CaptureIsBoundedAndScoped()
    {
        var results = new List<QwenModelLoadDiagnostic>();
        const string line = "replayfoundry-model-load: {\"schemaVersion\":\"model-load-diagnostic-1.0\",\"modelLoadSeconds\":1.25}";
        using (QwenModelLoadDiagnostics.Capture(results.Add))
        {
            QwenModelLoadDiagnostics.Report("normal checkpoint logging\nreplayfoundry-model-load: invalid\n" + line + "\n" + line);
            QwenModelLoadDiagnostics.Report(line.Replace("1.25", "-1", StringComparison.Ordinal));
            QwenModelLoadDiagnostics.Report(line.Replace("1.25", "999999", StringComparison.Ordinal));
        }
        QwenModelLoadDiagnostics.Report(line);
        TestAssert.Equal(1, results.Count, "Malformed data, excess entries, and disposed capture scopes cannot produce diagnostics.");
        TestAssert.Equal(1.25, results.Single().ModelLoadSeconds, "The observed duration is retained exactly.");
        return Task.CompletedTask;
    }
}
