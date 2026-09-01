using System.Diagnostics.CodeAnalysis;

namespace ReplayFoundry.Desktop.Features.Publish;

public interface IPublishHistoryDialogService
{
    void Show(PublishHistoryViewModel viewModel);
}

public interface IPublishHistoryLinkLauncher
{
    void Open(Uri uri);
}

internal static class PublishHistoryLinkPolicy
{
    public static bool TryCreateTrustedYouTubeUri(
        string? value,
        [NotNullWhen(true)]
        out Uri? uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps)
        {
            uri = null;
            return false;
        }

        string host = candidate.IdnHost;
        bool isYouTube =
            host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
        uri = isYouTube ? candidate : null;
        return isYouTube;
    }
}
