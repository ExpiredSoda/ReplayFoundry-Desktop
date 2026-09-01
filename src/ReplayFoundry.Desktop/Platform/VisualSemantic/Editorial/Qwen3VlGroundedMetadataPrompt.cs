using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataPrompt
{
    private const string FileName =
        "replayfoundry-editorial-metadata-prompt-1.40.txt";

    internal static string Load(
        string hostScriptPath,
        string parameterName)
    {
        string promptPath = Path.Combine(
            Path.GetDirectoryName(hostScriptPath)!,
            FileName);
        if (!File.Exists(promptPath))
        {
            throw new ArgumentException(
                $"The grounded Qwen metadata prompt is missing beside the explicit host script: '{promptPath}'.",
                parameterName);
        }

        string promptText = File.ReadAllText(promptPath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(promptText)))
            .ToLowerInvariant();
        if (!hash.Equals(
                Qwen3VlGroundedMetadataGenerator.PromptSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The grounded Qwen metadata prompt hash changed.");
        }

        return promptText;
    }
}
