using System.Reflection;

namespace ReplayFoundry.Desktop.Platform.YouTube;

public sealed class YouTubeOAuthClientConfiguration
{
    public const string ManageYouTubeScope =
        "https://www.googleapis.com/auth/youtube";

    public YouTubeOAuthClientConfiguration(
        string clientId,
        string clientSecret,
        string applicationName = "Replay Foundry")
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            !clientId.Trim().EndsWith(
                ".apps.googleusercontent.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "YouTube publishing requires a Google Desktop OAuth client ID.",
                nameof(clientId));
        }
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            throw new ArgumentException(
                "YouTube OAuth requires an application name.",
                nameof(applicationName));
        }
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException(
                "YouTube publishing requires the Google Desktop OAuth client secret paired with its client ID.",
                nameof(clientSecret));
        }

        ClientId = clientId.Trim();
        ClientSecret = clientSecret.Trim();
        ApplicationName = applicationName.Trim();
        Scopes = Array.AsReadOnly([ManageYouTubeScope]);
    }

    public string ClientId { get; }
    public string ClientSecret { get; }
    public string ApplicationName { get; }
    public IReadOnlyList<string> Scopes { get; }
}

public static class YouTubeOAuthConfigurationLoader
{
    private const string ClientIdEnvironmentVariable =
        "REPLAYFOUNDRY_YOUTUBE_CLIENT_ID";
    private const string ClientSecretEnvironmentVariable =
        "REPLAYFOUNDRY_YOUTUBE_CLIENT_SECRET";
    private const string ClientIdMetadataKey =
        "ReplayFoundry.YouTubeClientId";
    private const string ClientSecretMetadataKey =
        "ReplayFoundry.YouTubeClientSecret";

    public static YouTubeOAuthClientConfiguration? TryLoad()
    {
        string? clientId = Environment.GetEnvironmentVariable(
            ClientIdEnvironmentVariable,
            EnvironmentVariableTarget.Process);
        string? clientSecret = Environment.GetEnvironmentVariable(
            ClientSecretEnvironmentVariable,
            EnvironmentVariableTarget.Process);
        clientId = string.IsNullOrWhiteSpace(clientId)
            ? ReadAssemblyMetadata(ClientIdMetadataKey)
            : clientId;
        clientSecret = string.IsNullOrWhiteSpace(clientSecret)
            ? ReadAssemblyMetadata(ClientSecretMetadataKey)
            : clientSecret;
        return string.IsNullOrWhiteSpace(clientId) ||
               string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new YouTubeOAuthClientConfiguration(
                clientId,
                clientSecret);
    }

    private static string? ReadAssemblyMetadata(string key) =>
        typeof(YouTubeOAuthConfigurationLoader)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                attribute.Key.Equals(
                    key,
                    StringComparison.Ordinal))
            ?.Value;
}
