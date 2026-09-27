using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonClipEditorialProfileStore(string? path = null) : IClipEditorialProfileStore
{
    private const string Schema = "foundry-writing-profile-1";
    private readonly string _path = ReplayFoundryLocalDataPaths.Resolve(path, "writing-profile.json");
    private readonly object _gate = new();
    public ClipEditorialProfile Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return ClipEditorialProfile.Default;
            try
            {
                if (new FileInfo(_path).Length > 16_384) throw new InvalidDataException("Writing preferences exceed their size limit.");
                var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), ReplayFoundryLocalJsonPolicy.IndentedCamelCase);
                if (document?.Schema != Schema) throw new InvalidDataException("Unknown writing preference version.");
                return new(document.Audience, document.Naming, document.Signature, document.Tags, document.Voice, document.Objective, document.Tone);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                SafeDiagnosticTrace.Write("Saved writing preferences could not be read; the file was preserved", error);
                return ClipEditorialProfile.Default;
            }
        }
    }

    public void Save(ClipEditorialProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate) AtomicJsonFile.Write(_path, new Document(Schema, profile.AudienceAddress, profile.NamingGuidance,
            profile.ReusableDescriptionSignature, profile.DefaultTags.ToArray(), profile.VoicePerspective, profile.CopyObjective, profile.DefaultTone),
            ReplayFoundryLocalJsonPolicy.IndentedCamelCase);
    }

    private sealed record Document(string Schema, string Audience, string? Naming, string? Signature, string[] Tags,
        ClipEditorialVoicePerspective Voice, ClipEditorialCopyObjective Objective, string Tone = "Natural");
}
