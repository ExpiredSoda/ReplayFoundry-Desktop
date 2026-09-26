using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using ReplayFoundry.Desktop.Features.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Diagnostics;

public sealed class ReplayFoundryDiagnosticCollector :
    IUserReportDiagnosticCollector
{
    public UserReportAttachment Collect(Exception? exception = null)
    {
        var text = new StringBuilder();
        text.AppendLine("Replay Foundry sanitized diagnostics 1.0");
        text.AppendLine($"Captured UTC: {DateTimeOffset.UtcNow:O}");
        text.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        text.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        text.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        if (exception is not null)
        {
            text.AppendLine("Exception chain:");
            Exception? current = exception;
            for (int index = 0; current is not null && index < 8; index++)
            {
                text.AppendLine(
                    $"  {index}: {current.GetType().FullName} · HResult 0x{current.HResult:X8}");
                current = current.InnerException;
            }
            text.AppendLine("Method-only stack (no messages, arguments, or file paths):");
            StackFrame[] frames = new StackTrace(exception, fNeedFileInfo: false)
                .GetFrames() ?? [];
            foreach (StackFrame frame in frames.Take(96))
            {
                System.Reflection.MethodBase? method = frame.GetMethod();
                if (method is null) continue;
                string type = method.DeclaringType?.FullName ?? "(unknown type)";
                text.AppendLine($"  {type}.{method.Name}");
            }
        }
        else
        {
            text.AppendLine("Exception details: none (manual feedback)");
        }
        return new UserReportAttachment(
            "diagnostics.txt",
            "text/plain; charset=utf-8",
            UserReportSanitizer.SanitizeDiagnostic(text.ToString()));
    }
}
