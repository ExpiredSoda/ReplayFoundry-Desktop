using ReplayFoundry.Desktop.Features.Studio.Preview;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task StudioPreviewLoadsExactCutBeforeTrimContext()
    {
        var project = CreateStudioQueueProject(2);
        var asset = project.PrimaryAsset;
        using var media = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(media);
        preview.Bind(true, project, asset);
        TestAssert.Equal(StudioPreviewRangeMode.ExactSelection, media.LastRequest!.RangeMode,
            "Opening a clip must not first encode the unused two-minute trim envelope.");
        TestAssert.Equal(asset.Duration, media.LastRequest.Duration, "The initial proxy should cover exactly the selected cut.");
        preview.UpdateRange(asset.SourceStart + TimeSpan.FromSeconds(1), asset.SourceEnd - TimeSpan.FromSeconds(1));
        TestAssert.Equal(1, media.MaterializeCount, "An inward trim needs no new media.");
        preview.UpdateRange(asset.SourceStart - TimeSpan.FromSeconds(2), asset.SourceEnd + TimeSpan.FromSeconds(3));
        TestAssert.Equal(2, media.MaterializeCount, "An outward trim should lazily load its available context.");
        TestAssert.Equal(StudioPreviewRangeMode.EditableEnvelope, media.LastRequest!.RangeMode,
            "Once context is requested, subsequent boundary edits should reuse the wider proxy.");
        TestAssert.True(preview.PreviewSourceOffsetSeconds <= preview.PreviewPositionMinimumSeconds,
            "The loaded proxy must be able to seek to the current source-clock range.");
        preview.UpdateRange(asset.SourceStart - TimeSpan.FromSeconds(3), asset.SourceEnd + TimeSpan.FromSeconds(4));
        TestAssert.Equal(2, media.MaterializeCount, "Further trims within loaded context must not re-encode.");
        preview.Bind(true, project, project.Assets[1]);
        TestAssert.Equal(StudioPreviewRangeMode.ExactSelection, media.LastRequest!.RangeMode,
            "Selecting another source starts with its exact cut rather than inheriting prior trim context.");
        return Task.CompletedTask;
    }

    private static async Task StudioPreviewHandlesTrimDuringInitialLoad()
    {
        var project = CreateStudioQueueProject(1);
        var asset = project.PrimaryAsset;
        using var media = new DeferredExactPreviewService();
        using var preview = new StudioPreviewViewModel(media);
        preview.Bind(true, project, asset);
        preview.UpdateRange(asset.SourceStart - TimeSpan.FromSeconds(2), asset.SourceEnd + TimeSpan.FromSeconds(3));
        TestAssert.True(preview.IsPreviewLoading && !preview.IsPreviewAvailable,
            "A pending exact proxy cannot be used as though it already covers an outward trim.");
        media.CompleteFirst.TrySetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (preview.IsPreviewLoading || !preview.IsPreviewAvailable) await Task.Delay(10, deadline.Token);
        TestAssert.Equal(1, media.ReleasedFirst, "The insufficient late exact proxy must release its lease.");
        TestAssert.Equal(2, media.Requests.Count, "One context request should supersede the now-insufficient initial proxy.");
        TestAssert.Equal(StudioPreviewRangeMode.EditableEnvelope, media.Requests[1].RangeMode,
            "A trim that moves during loading still obtains the needed context.");
        TestAssert.True(preview.PreviewSourceOffsetSeconds <= preview.PreviewPositionMinimumSeconds,
            "The visible result must cover the current trim instead of a stale initial selection.");
    }

    private sealed class DeferredExactPreviewService : IStudioPreviewMediaService, IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReplayFoundry-exact-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
        internal TaskCompletionSource CompleteFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<StudioPreviewMediaRequest> Requests { get; } = [];
        internal int ReleasedFirst { get; private set; }
        internal DeferredExactPreviewService() => System.IO.File.WriteAllBytes(_path, [0]);
        public async Task<StudioPreviewMediaLease> MaterializeAsync(StudioPreviewMediaRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            bool first = Requests.Count == 1;
            if (first) await CompleteFirst.Task.WaitAsync(cancellationToken);
            return new(_path, request.SourceStart, request.Duration, () => { if (first) ReleasedFirst++; });
        }
        public void Dispose() => System.IO.File.Delete(_path);
    }

    private static Task StudioPreviewRejectsInsufficientProviderCoverage()
    {
        var project = CreateStudioQueueProject(1);
        using var media = new ShortCoveragePreviewService();
        using var preview = new StudioPreviewViewModel(media);
        preview.Bind(true, project, project.PrimaryAsset);
        TestAssert.Equal(1, media.Calls, "An incomplete provider lease must fail once, not recursively retry the same covered request.");
        TestAssert.Equal(1, media.Releases, "The unusable lease must be released before reporting failure.");
        TestAssert.True(preview.HasPreviewError && !preview.IsPreviewLoading && !preview.IsPreviewAvailable,
            "Incomplete source coverage should leave an actionable stopped preview state.");
        return Task.CompletedTask;
    }

    private sealed class ShortCoveragePreviewService : IStudioPreviewMediaService, IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReplayFoundry-short-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
        internal int Calls { get; private set; }
        internal int Releases { get; private set; }
        internal ShortCoveragePreviewService() => System.IO.File.WriteAllBytes(_path, [0]);
        public Task<StudioPreviewMediaLease> MaterializeAsync(StudioPreviewMediaRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++Calls > 2) throw new InvalidOperationException("Controlled retry ceiling reached.");
            return Task.FromResult(new StudioPreviewMediaLease(_path, request.SourceStart,
                TimeSpan.FromTicks(request.Duration.Ticks / 2), () => Releases++));
        }
        public void Dispose() => System.IO.File.Delete(_path);
    }
}
