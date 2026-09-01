using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Security;

namespace ReplayFoundry.Desktop.Platform.Diagnostics;

public sealed partial class UserReportSanitizer : IUserReportTextSanitizer
{
    public const int MaximumDiagnosticTextLength = 48 * 1024;

    public string Sanitize(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }
        string sanitized = SanitizeDiagnostic(value);
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }

    internal static string SanitizeDiagnostic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(not available)";
        string sanitized = value.Replace('\0', ' ');
        string profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            sanitized = sanitized.Replace(
                profile,
                "[user-profile]",
                StringComparison.OrdinalIgnoreCase);
        }
        sanitized = WindowsPath().Replace(sanitized, "[local-path]");
        sanitized = SecretAssignment().Replace(
            sanitized,
            match => match.Groups[1].Value + "=[redacted]");
        sanitized = BearerToken().Replace(sanitized, "Bearer [redacted]");
        sanitized = JsonWebToken().Replace(sanitized, "[redacted-token]");
        sanitized = EmailAddress().Replace(sanitized, "[redacted-email]");
        sanitized = UrlParameters().Replace(
            sanitized,
            match => match.Groups[1].Value + "[redacted-url-parameters]");
        sanitized = UnixPath().Replace(sanitized, "[local-path]");
        sanitized = ControlCharacters().Replace(sanitized, " ");
        return sanitized.Length <= MaximumDiagnosticTextLength
            ? sanitized
            : sanitized[..MaximumDiagnosticTextLength] +
              Environment.NewLine + "[diagnostics truncated]";
    }

    internal static UserReportDraft SanitizeOutboundDraft(
        UserReportDraft report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string summary = SanitizeOutbound(report.Summary, 160);
        string details = SanitizeOutbound(report.Details, 4_000);
        string version = ExternalTextSecurity.SingleLine(
            report.ApplicationVersion,
            128);
        UserReportAttachment[] attachments = report.Attachments
            .Select(static attachment => new UserReportAttachment(
                attachment.FileName,
                attachment.MediaType,
                TruncateUtf8(
                    SanitizeOutbound(
                        attachment.Content,
                        UserReportAttachment.MaximumContentLength),
                    UserReportAttachment.MaximumContentLength)))
            .ToArray();
        return new UserReportDraft(
            report.ReportId,
            report.Kind,
            summary,
            details,
            version,
            report.CreatedAtUtc,
            attachments);
    }

    private static string SanitizeOutbound(string value, int maximumLength)
    {
        string secured = SanitizeDiagnostic(value);
        secured = PromptInjection().Replace(
            secured,
            "[untrusted instruction-like text removed]");
        secured = ExternalTextSecurity.SingleLine(secured, maximumLength);
        return string.IsNullOrWhiteSpace(secured) ? "(not available)" : secured;
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }
        var result = new System.Text.StringBuilder(value.Length);
        int bytes = 0;
        foreach (System.Text.Rune rune in value.EnumerateRunes())
        {
            int next = rune.Utf8SequenceLength;
            if (bytes + next > maximumBytes) break;
            result.Append(rune.ToString());
            bytes += next;
        }
        return result.ToString();
    }

    [GeneratedRegex(
        "(?i)(?<![A-Za-z0-9])(?:[A-Z]:\\\\|\\\\\\\\)[^\\r\\n\\t<>|\"']+")]
    private static partial Regex WindowsPath();

    [GeneratedRegex(
        "(?i)\\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|password|authorization|api[_-]?key|cookie|session[_-]?id)\\b[\\s\"']*[:=][\\s\"']*[^\\s,;\"']+")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?i)(https?://[^\s?#]+)[?#][^\s]+")]
    private static partial Regex UrlParameters();

    [GeneratedRegex("(?i)(?<![A-Za-z0-9:])/(?:Users|home|tmp|var/tmp|mnt|media|Volumes)/[^\\s<>|\\\"']+")]
    private static partial Regex UnixPath();

    [GeneratedRegex(
        @"(?i)\b(?:ignore|disregard|override|forget)\s+(?:all\s+)?(?:previous|prior|above|system|developer)\s+(?:instructions?|messages?|prompts?)\b|\b(?:reveal|print|repeat|return|show)\s+(?:the\s+)?(?:system|developer)\s+(?:prompt|message|instructions?)\b|\b(?:follow|obey|execute)\s+(?:these|the\s+following|my)\s+(?:instructions?|commands?)\b|<\|(?:im_start|im_end|system|assistant|user|developer|tool)[^>]*\|>|\[/?INST\]|<<\/?SYS>>")]
    private static partial Regex PromptInjection();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex ControlCharacters();
}
