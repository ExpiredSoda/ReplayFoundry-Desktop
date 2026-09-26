using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Personalization;

namespace ReplayFoundry.Desktop.Platform.Storage;

public static class WriterLearningOverviewReader
{
    public static WriterLearningOverview Read(string? root = null)
    {
        string directory = ReplayFoundryLocalDataPaths.Resolve(root, Path.Combine("Personalization", "Writer"));
        string examples = Path.Combine(directory, "examples");
        var groups = new HashSet<string>(StringComparer.Ordinal);
        int count = 0, pending = 0;
        if (Directory.Exists(examples))
            foreach (string path in Directory.EnumerateFiles(examples, "*.json").Take(10_001))
            {
                if (++count > 10_000 || new FileInfo(path).Length > 65_536) throw new InvalidDataException("Feedback exceeds its supported bounds.");
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var row = document.RootElement;
                if (row.GetProperty("sourceGroup").GetString() is { Length: 64 } group) groups.Add(group);
                if (row.TryGetProperty("feedback", out var feedback) &&
                    feedback.TryGetProperty("reason", out var reason) && reason.GetString() is "WrongSpeaker" or "WrongEvent" or "InventedOutcome" or "WrongScope" &&
                    (!feedback.TryGetProperty("factsReviewed", out var reviewed) || reviewed.ValueKind != JsonValueKind.True)) pending++;
            }
        // File presence is not proof of qualified/loaded weights. The runtime performs that check.
        return new(count, groups.Count, pending, File.Exists(Path.Combine(directory, "active.json")));
    }
}
