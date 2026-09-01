using System.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal interface ISystemBrowser
{
    void Open(Uri uri);
}

internal sealed class WindowsSystemBrowser : ISystemBrowser
{
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "External authorization pages must use HTTPS.",
                nameof(uri));
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
    }
}
