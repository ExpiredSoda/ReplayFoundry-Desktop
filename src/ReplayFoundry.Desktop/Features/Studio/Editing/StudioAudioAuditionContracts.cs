using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public interface IStudioAudioAuditionSession : IDisposable
{
    event EventHandler? Changed;
    string Status { get; }
    bool CanListen { get; }
    bool IsActive { get; }
    void SetHostBusy(bool busy);
    void Stop();
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IStudioMixAudioAuditionSession : IStudioAudioAuditionSession
{
    event EventHandler? PlaybackStarting;
    void Bind(GenerationOutputAsset? asset);
    Task StartAsync(bool processed);
}

public interface IStudioCaptionAudioAuditionSession : IStudioAudioAuditionSession
{
    void Bind(GenerationOutputAsset? asset, int streamIndex);
    Task ListenAsync();
}

internal static class StudioAudioAuditionFactory
{
    internal static IStudioMixAudioAuditionSession CreateMix() => new WpfStudioMixAudioAuditionSession();
    internal static IStudioCaptionAudioAuditionSession CreateCaption() => new WpfStudioCaptionAudioAuditionSession();
}
