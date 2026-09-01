using System.Diagnostics;
using ReplayFoundry.Desktop.Security;

namespace ReplayFoundry.Desktop.Platform.Diagnostics;

internal static class SafeDiagnosticTrace
{
    internal static void Write(string category, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(exception);
        string safeCategory = ExternalTextSecurity.SingleLine(category, 160);
        string safeMessage = ExternalTextSecurity.SingleLine(
            UserReportSanitizer.SanitizeDiagnostic(exception.Message),
            1_000);
        Debug.WriteLine(
            $"{safeCategory}: {exception.GetType().Name}: {safeMessage}");
    }

    internal static void Write(string category, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        string safeCategory = ExternalTextSecurity.SingleLine(category, 160);
        string safeDetail = ExternalTextSecurity.SingleLine(
            UserReportSanitizer.SanitizeDiagnostic(detail),
            1_000);
        Debug.WriteLine($"{safeCategory}: {safeDetail}");
    }
}
