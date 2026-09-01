using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Features.Settings;

public enum ReplayFoundryLocalDataKind
{
    DerivedCaches,
    DiagnosticsAndReports,
    PreferencesAndHistory,
    LibraryCatalog,
    StudioProjects,
}

public sealed class ReplayFoundryLocalDataResetRequest
{
    private readonly ReadOnlyCollection<ReplayFoundryLocalDataKind> _kinds;

    public ReplayFoundryLocalDataResetRequest(
        IEnumerable<ReplayFoundryLocalDataKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ReplayFoundryLocalDataKind[] snapshot = kinds.Distinct().ToArray();
        if (snapshot.Length == 0 || snapshot.Any(static kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentException(
                "At least one defined local-data category is required.",
                nameof(kinds));
        }
        _kinds = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<ReplayFoundryLocalDataKind> Kinds => _kinds;
    public bool Includes(ReplayFoundryLocalDataKind kind) => _kinds.Contains(kind);
}

public sealed record ReplayFoundryLocalDataUsage(
    ReplayFoundryLocalDataKind Kind,
    long Bytes,
    int FileCount);

public sealed record ReplayFoundryLocalDataCleanupResult(
    long DeletedBytes,
    int DeletedFiles,
    IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Warnings.Count == 0;
}

public interface IReplayFoundryLocalDataMaintenance
{
    IReadOnlyList<ReplayFoundryLocalDataUsage> Inspect();

    Task<ReplayFoundryLocalDataCleanupResult> ClearDerivedCachesAsync(
        CancellationToken cancellationToken = default);

    void ScheduleReset(ReplayFoundryLocalDataResetRequest request);

    Task<ReplayFoundryLocalDataCleanupResult> ApplyScheduledResetAsync(
        CancellationToken cancellationToken = default);
}

public interface ILocalDataCleanupConfirmation
{
    bool Confirm(ReplayFoundryLocalDataResetRequest request);
}

public sealed class UnavailableReplayFoundryLocalDataMaintenance :
    IReplayFoundryLocalDataMaintenance
{
    public IReadOnlyList<ReplayFoundryLocalDataUsage> Inspect() => [];

    public Task<ReplayFoundryLocalDataCleanupResult> ClearDerivedCachesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReplayFoundryLocalDataCleanupResult(
            0,
            0,
            ["Local-data maintenance is unavailable in this preview."]));

    public void ScheduleReset(ReplayFoundryLocalDataResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new InvalidOperationException(
            "Local-data maintenance is unavailable in this preview.");
    }

    public Task<ReplayFoundryLocalDataCleanupResult> ApplyScheduledResetAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReplayFoundryLocalDataCleanupResult(
            0,
            0,
            ["Local-data maintenance is unavailable in this preview."]));
}
