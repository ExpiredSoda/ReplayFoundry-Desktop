using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public sealed record GenerationCaptureFrameDiagnostic(double SourceSeconds, string? FrameClassification,
    IReadOnlyList<string> Lines, IReadOnlyList<string> NormalizedLines);

public sealed record GenerationCaptureContextDiagnostic(string CandidateId, double StartSeconds, double EndSeconds,
    NormalizedRectangle Region, string? RepeatedClassification, IReadOnlyList<GenerationCaptureFrameDiagnostic> Frames,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<double>? AdditionalRequestedSampleSeconds = null,
    double? FirstSpeechStartSeconds = null,
    bool ApplicationStartupLeadIn = false)
{
    internal static GenerationCaptureContextDiagnostic Create(string candidateId, TimeSpan start, TimeSpan end,
        ClipVisualTextContext visualText) => new(candidateId, start.TotalSeconds, end.TotalSeconds, visualText.ContentRegion,
        GenerationCaptureContextPolicy.Assess(visualText.Frames)?.Kind.ToString(),
        visualText.Frames.Take(8).Select(frame => new GenerationCaptureFrameDiagnostic(frame.Request.Frame.RequestedTimestamp.TotalSeconds,
            GenerationCaptureContextPolicy.ClassifyFrame(frame.Lines.Select(static line => line.Text).ToArray())?.ToString(),
            frame.Lines.Take(128).Select(static line => line.Text.Length > 500 ? line.Text[..500] : line.Text).ToArray(),
            frame.Lines.Take(128).Select(static line => GenerationCaptureContextPolicy.NormalizeLine(line.Text)).ToArray())).ToArray(),
        visualText.Warnings.Take(16).Select(static warning => warning.Message).ToArray());
}
