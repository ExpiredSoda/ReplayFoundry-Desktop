using System.Reflection;

namespace ReplayFoundry.Desktop.Platform.Updates;

internal sealed record ApplicationUpdateConfiguration(
    string FeedUrl, string PublicKey, string DisplayVersion, string BuildVersion, string Channel)
{
    public static ApplicationUpdateConfiguration? FromAssembly()
    {
        Assembly assembly = typeof(ApplicationUpdateConfiguration).Assembly;
        var values = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(value => value.Key, value => value.Value);
        if (values.GetValueOrDefault("ReplayFoundry.UpdatesEnabled") != "true") return null;
        string key = values.GetValueOrDefault("ReplayFoundry.UpdatePublicKey") ?? "";
        string channel = values.GetValueOrDefault("ReplayFoundry.UpdateChannel") ?? "";
        if (channel is not ("beta" or "stable") || Convert.FromBase64String(key).Length != 32)
            throw new InvalidOperationException("The installed update configuration is incomplete.");
        string display = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "";
        string build = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "";
        if (!Version.TryParse(build, out _)) throw new InvalidOperationException("The update build version is invalid.");
        return new(
            $"https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/app-updates/{channel}.xml",
            key, display, build, channel);
    }
}
