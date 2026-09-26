namespace ReplayFoundry.Desktop.Platform.YouTube;

internal sealed record YouTubeStoredCredential(
    string RefreshToken,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> Scopes);

internal sealed record YouTubeAccessCredential(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string? RefreshToken,
    IReadOnlyList<string> Scopes);

internal interface IYouTubeCredentialStore
{
    YouTubeStoredCredential? Read();
    void Write(YouTubeStoredCredential credential);
    void Delete();
}
