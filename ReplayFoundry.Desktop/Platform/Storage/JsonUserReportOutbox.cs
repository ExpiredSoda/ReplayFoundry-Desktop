using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonUserReportOutbox : IUserReportOutbox
{
    private const string ManifestFileName = "report.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _root;

    public JsonUserReportOutbox(string? rootDirectory = null)
    {
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
            "Diagnostics",
            "Outbox"));
        if (!Path.IsPathFullyQualified(_root))
        {
            throw new ArgumentException(
                "The user-report outbox root must be fully qualified.",
                nameof(rootDirectory));
        }
    }

    public IReadOnlyList<StoredUserReport> Current
    {
        get
        {
            lock (_gate)
            {
                if (!Directory.Exists(_root)) return [];
                return Array.AsReadOnly(SafeEnumerateDirectories(_root)
                    .Where(path => !string.Equals(
                        Path.GetFileName(path),
                        ".staging",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(TryLoad)
                    .Where(static report => report is not null)
                    .Select(static report => report!)
                    .OrderByDescending(static report => report.Draft.CreatedAtUtc)
                    .ToArray());
            }
        }
    }

    private static StoredUserReport? TryLoad(string directory)
    {
        try
        {
            return Load(directory);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(
                root,
                "*",
                SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Upsert(StoredUserReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_gate)
        {
            string final = ReportDirectory(report.Draft.ReportId);
            if (!Directory.Exists(final))
            {
                WriteNew(report, final);
                return;
            }

            StoredUserReport existing = Load(final);
            if (!EquivalentDraft(existing.Draft, report.Draft))
            {
                throw new InvalidDataException(
                    "An outbox report ID cannot be reused for different report content.");
            }
            AtomicJsonFile.Write(
                Path.Combine(final, ManifestFileName),
                ToDocument(report),
                JsonOptions);
        }
    }

    public void Remove(string reportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        lock (_gate)
        {
            string directory = ReportDirectory(reportId);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteNew(StoredUserReport report, string final)
    {
        string stagingRoot = Path.Combine(_root, ".staging");
        Directory.CreateDirectory(stagingRoot);
        string staging = Path.Combine(
            stagingRoot,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (UserReportAttachment attachment in report.Draft.Attachments)
            {
                File.WriteAllText(
                    Path.Combine(staging, attachment.FileName),
                    attachment.Content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            File.WriteAllText(
                Path.Combine(staging, ManifestFileName),
                JsonSerializer.Serialize(ToDocument(report), JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _ = Load(staging);
            Directory.CreateDirectory(_root);
            Directory.Move(staging, final);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static StoredUserReport Load(string directory)
    {
        try
        {
            string manifest = Path.Combine(directory, ManifestFileName);
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(manifest), JsonOptions);
            if (document?.SchemaVersion != UserReportDraft.SchemaVersion ||
                document.Attachments is null)
            {
                throw new InvalidDataException(
                    "The local user-report schema is unsupported.");
            }
            UserReportAttachment[] attachments = document.Attachments
                .Select(item => LoadAttachment(directory, item))
                .ToArray();
            var draft = new UserReportDraft(
                document.ReportId,
                Enum.Parse<UserReportKind>(document.Kind),
                document.Summary,
                document.Details,
                document.ApplicationVersion,
                document.CreatedAtUtc,
                attachments);
            return new StoredUserReport(
                draft,
                Enum.Parse<UserReportDisposition>(document.Disposition),
                document.UpdatedAtUtc,
                document.FailureCode);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or ArgumentException or
            InvalidOperationException)
        {
            throw new InvalidDataException(
                "A local user report is invalid or incomplete.",
                exception);
        }
    }

    private static UserReportAttachment LoadAttachment(
        string directory,
        AttachmentDocument item)
    {
        if (string.IsNullOrWhiteSpace(item.FileName) ||
            Path.GetFileName(item.FileName) != item.FileName)
        {
            throw new InvalidDataException(
                "A diagnostic attachment path is invalid.");
        }
        string content = File.ReadAllText(Path.Combine(directory, item.FileName));
        var attachment = new UserReportAttachment(
            item.FileName,
            item.MediaType,
            content);
        if (attachment.Size != item.Size ||
            !string.Equals(
                attachment.Sha256,
                item.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A diagnostic attachment failed its size or SHA-256 check.");
        }
        return attachment;
    }

    private string ReportDirectory(string reportId)
    {
        if (reportId.Length != 32 ||
            reportId.Any(static value => !Uri.IsHexDigit(value)))
        {
            throw new ArgumentException("A report ID must be 32 hexadecimal characters.");
        }
        string path = Path.GetFullPath(Path.Combine(_root, reportId.ToUpperInvariant()));
        string prefix = _root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A report ID escaped the outbox root.");
        }
        return path;
    }

    private static bool EquivalentDraft(UserReportDraft left, UserReportDraft right) =>
        left.ReportId == right.ReportId &&
        left.Kind == right.Kind &&
        left.Summary == right.Summary &&
        left.Details == right.Details &&
        left.ApplicationVersion == right.ApplicationVersion &&
        left.CreatedAtUtc == right.CreatedAtUtc &&
        left.Attachments.Select(static item => item.Sha256)
            .SequenceEqual(right.Attachments.Select(static item => item.Sha256));

    private static Document ToDocument(StoredUserReport report) => new()
    {
        SchemaVersion = UserReportDraft.SchemaVersion,
        ReportId = report.Draft.ReportId,
        Kind = report.Draft.Kind.ToString(),
        Summary = report.Draft.Summary,
        Details = report.Draft.Details,
        ApplicationVersion = report.Draft.ApplicationVersion,
        CreatedAtUtc = report.Draft.CreatedAtUtc,
        Disposition = report.Disposition.ToString(),
        UpdatedAtUtc = report.UpdatedAtUtc,
        FailureCode = report.FailureCode,
        Attachments = report.Draft.Attachments.Select(static attachment =>
            new AttachmentDocument
            {
                FileName = attachment.FileName,
                MediaType = attachment.MediaType,
                Size = attachment.Size,
                Sha256 = attachment.Sha256,
            }).ToArray(),
    };

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string ReportId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string ApplicationVersion { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string Disposition { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string? FailureCode { get; set; }
        public AttachmentDocument[]? Attachments { get; set; }
    }

    private sealed class AttachmentDocument
    {
        public string FileName { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
