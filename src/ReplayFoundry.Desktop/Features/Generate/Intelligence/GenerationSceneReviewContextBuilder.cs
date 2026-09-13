using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationSceneReviewContextBuilder
{
    internal static SceneReviewContext Build(GenerationSetupOptions setup, GenerationSourceFileSnapshot source,
        IEnumerable<int> availableStreams, TimeSpan start, TimeSpan end,
        GenerationSourceTranscript? transcript, IEnumerable<TimeSpan> anchors)
    {
        string path = source.FullPath;
        var selection = setup.CaptionSettings.FindForSource(path);
        var tracks = availableStreams.Distinct().OrderBy(index => index != selection?.AbsoluteAudioStreamIndex)
            .ThenBy(index => index).Take(4).Select(index => new SceneAudioTrack(index,
                Role(selection is not null && selection.AbsoluteAudioStreamIndex == index ? selection : null),
                GenerationVisualTranscriptContextBuilder.Build(transcript?.Tracks.SingleOrDefault(track => track.AudioStreamIndex == index),
                    start, end).Spans)).ToArray();
        var game = setup.GameContextSettings.Find(path);
        return new(path, source.FileLength, source.LastWriteTimeUtc.UtcTicks, start, end,
            setup.DiscoveryIntent.MomentType.ToString(),
            game?.Origin == GenerationGameContextOrigin.UserConfirmed ? game.GameName : null,
            Array.AsReadOnly(tracks), Array.AsReadOnly(anchors.Where(time => time >= start && time < end)
                .Select(time => time - start).Distinct().Take(32).ToArray()));
    }

    internal static AudioContentRoleAssignment Role(GenerationCaptionSourceSelection? selection) => selection?.ContentRole switch
    {
        CaptionAudioContentRole.CreatorCommentary => new(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
        CaptionAudioContentRole.GameDialogue => new(AudioContentRole.GameDialogue, AudioContentRoleSource.UserConfirmed),
        CaptionAudioContentRole.MixedSpeech => new(AudioContentRole.MixedSpeech, AudioContentRoleSource.UserConfirmed),
        _ => AudioContentRoleAssignment.Unknown,
    };
}
