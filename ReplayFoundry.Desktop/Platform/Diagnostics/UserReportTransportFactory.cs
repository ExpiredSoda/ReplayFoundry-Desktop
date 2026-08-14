using System.Reflection;
using ReplayFoundry.Desktop.Features.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Diagnostics;

public static class UserReportTransportFactory
{
    public const string EndpointMetadataKey =
        "ReplayFoundry.UserReportEndpoint";
    public const string DestinationMetadataKey =
        "ReplayFoundry.UserReportDestinationName";

    public static IUserReportTransport CreateFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        IReadOnlyDictionary<string, string> metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static value => !string.IsNullOrWhiteSpace(value.Key))
            .GroupBy(static value => value.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value ?? string.Empty,
                StringComparer.Ordinal);
        if (!metadata.TryGetValue(EndpointMetadataKey, out string? endpoint) ||
            string.IsNullOrWhiteSpace(endpoint))
        {
            return new UnavailableUserReportTransport();
        }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException(
                $"Assembly metadata '{EndpointMetadataKey}' is not a valid absolute URI.");
        }
        string destination = metadata.GetValueOrDefault(
            DestinationMetadataKey,
            "Replay Foundry support");
        return new HttpsUserReportTransport(uri, destination);
    }
}
